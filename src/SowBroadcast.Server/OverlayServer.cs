using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SowBroadcast.Server;

public sealed class OverlayServerOptions
{
    public int Port { get; init; } = 8324;

    /// <summary>How many consecutive ports to try when the preferred one is taken (port 0 = any free port).</summary>
    public int PortAttempts { get; init; } = 20;

    /// <summary>URL prefix -> folders served under it, first match wins (user data before shipped files).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> StaticFolders { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Required in the <c>X-SOWB-Token</c> header of <c>POST /notify</c>.</summary>
    public string NotifyToken { get; init; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>The body of <c>GET /api/state</c> (the overlay state JSON); called for every request, so it should return a cached string.</summary>
    public Func<string>? StateProvider { get; init; }

    public IReadOnlyList<string> FullRefreshKeys { get; init; } = Array.Empty<string>();
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(15);
    public int MaxNotifyBodyBytes { get; init; } = 64 * 1024;

    /// <summary>The local control interface (<c>/api/control</c>); null leaves it switched off.</summary>
    public ControlGateway? Control { get; init; }
}

/// <summary>What a control call got back: an HTTP status and a JSON body.</summary>
public sealed record ControlReply(int Status, string Json);

/// <summary>
/// The door of the local control interface: a secret token and the handler that runs commands. The handler is set once the program
/// has a window (the server starts first), and until then the interface answers 503.
/// </summary>
public sealed class ControlGateway
{
    /// <summary>Sent as <c>X-SOWB-Token</c> or <c>?token=</c>. May be replaced while the server runs.</summary>
    public volatile string Token = "";

    /// <summary>Runs <c>command</c> (empty = list the commands) with the query values; called on a server thread.</summary>
    public volatile Func<string, IReadOnlyDictionary<string, string>, Task<ControlReply>>? Handler;
}

/// <summary>
/// The local web server for OBS browser sources: static overlay files (only the configured folders),
/// a Server-Sent-Events stream at <c>/events</c> and <c>POST /notify</c> for legacy clients.
/// </summary>
public sealed class OverlayServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private OverlayServer(WebApplication app, EventHub hub, int port, OverlayServerOptions options)
    {
        _app = app;
        Hub = hub;
        Port = port;
        Options = options;
    }

    public EventHub Hub { get; }
    public int Port { get; }
    public OverlayServerOptions Options { get; }
    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public static async Task<OverlayServer> StartAsync(OverlayServerOptions options)
    {
        Exception? last = null;
        var attempts = options.Port == 0 ? 1 : Math.Max(1, options.PortAttempts);
        for (var i = 0; i < attempts; i++)
        {
            var port = options.Port == 0 ? 0 : options.Port + i;
            var hub = new EventHub(options.FullRefreshKeys);
            var app = Build(options, hub, port);
            try
            {
                await app.StartAsync();
                var actual = new Uri(app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                    .Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
                return new OverlayServer(app, hub, actual, options);
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                last = ex;
                await app.DisposeAsync();
            }
        }
        throw new IOException($"No free port found starting at {options.Port}.", last);
    }

    private static WebApplication Build(OverlayServerOptions options, EventHub hub, int port)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();

        // The legacy server sent these on every response; OBS browser sources must never serve stale overlay data.
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.CacheControl = "no-cache, no-store, must-revalidate";
                headers.Pragma = "no-cache";
                headers.Expires = "0";
                return Task.CompletedTask;
            });
            await next();
        });

        app.MapGet("/events", (HttpContext context) => StreamEvents(context, hub, options));
        app.MapGet("/api/state", async (HttpContext context) =>
        {
            var json = options.StateProvider?.Invoke();
            if (string.IsNullOrEmpty(json))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(json);
        });
        app.MapPost("/notify", (HttpContext context) => Notify(context, hub, options));
        if (options.Control is { } control)
        {
            // GET as well as POST: Stream Deck and Companion buttons are easiest to set up with a plain URL. The token keeps
            // other web pages out, because they cannot know it.
            app.MapMethods("/api/control", new[] { "GET", "POST" }, (HttpContext context) => Control(context, control, ""));
            app.MapMethods("/api/control/{command}", new[] { "GET", "POST" },
                (HttpContext context, string command) => Control(context, control, command));
        }

        foreach (var (prefix, folders) in options.StaticFolders)
        {
            var providers = folders.Where(Directory.Exists).Select(f => (IFileProvider)new PhysicalFileProvider(f)).ToArray();
            if (providers.Length == 0) continue;
            app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = prefix,
                FileProvider = new CompositeFileProvider(providers),
                ServeUnknownFileTypes = false,
                DefaultContentType = "application/octet-stream",
            });
        }

        return app;
    }

    private static async Task StreamEvents(HttpContext context, EventHub hub, OverlayServerOptions options)
    {
        long? lastId = null;
        var header = context.Request.Headers["Last-Event-ID"].FirstOrDefault() ?? context.Request.Query["lastEventId"].FirstOrDefault();
        if (long.TryParse(header, out var parsed)) lastId = parsed;

        var response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.Connection = "keep-alive";
        await response.WriteAsync(": connected\n\n", context.RequestAborted);
        await response.Body.FlushAsync(context.RequestAborted);

        var (reader, subscription) = hub.Subscribe(lastId);
        using var _ = subscription;
        try
        {
            while (!context.RequestAborted.IsCancellationRequested)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                wait.CancelAfter(options.KeepAliveInterval);
                try
                {
                    if (!await reader.WaitToReadAsync(wait.Token)) break;
                    while (reader.TryRead(out var ev))
                        await response.WriteAsync($"id: {ev.Id}\ndata: {ev.Payload}\n\n", context.RequestAborted);
                }
                catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
                {
                    await response.WriteAsync(": keepalive\n\n", context.RequestAborted);
                }
                await response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
    }

    // Writes the status itself: a lambda returning Task<IResult> would bind to RequestDelegate and the result would be dropped.
    private static async Task Notify(HttpContext context, EventHub hub, OverlayServerOptions options)
    {
        var response = context.Response;
        var token = context.Request.Headers["X-SOWB-Token"].FirstOrDefault();
        if (token is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(options.NotifyToken)))
        {
            response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (context.Request.ContentLength is { } length && length > options.MaxNotifyBodyBytes)
        {
            response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = await context.Request.Body.ReadAsync(chunk)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > options.MaxNotifyBodyBytes)
                {
                    response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
            }

            using var doc = JsonDocument.Parse(buffer.ToArray());
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("changed", out var changed) ||
                changed.ValueKind != JsonValueKind.Array)
            {
                await BadRequest(response, "Body must be {\"changed\": [..]}.");
                return;
            }

            hub.Publish(changed.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));
            response.StatusCode = StatusCodes.Status204NoContent;
        }
        catch (JsonException)
        {
            await BadRequest(response, "Body is not valid JSON.");
        }
    }

    private static async Task Control(HttpContext context, ControlGateway control, string command)
    {
        var response = context.Response;
        var token = context.Request.Headers["X-SOWB-Token"].FirstOrDefault() ?? context.Request.Query["token"].FirstOrDefault();
        var expected = control.Token;
        if (string.IsNullOrEmpty(expected) || token is null ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected)))
        {
            response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var handler = control.Handler;
        if (handler is null)
        {
            response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var arguments = context.Request.Query
            .Where(q => !string.Equals(q.Key, "token", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(q => q.Key, q => q.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var reply = await handler(command, arguments);
        response.StatusCode = reply.Status;
        response.ContentType = "application/json; charset=utf-8";
        await response.WriteAsync(reply.Json);
    }

    private static async Task BadRequest(HttpResponse response, string message)
    {
        response.StatusCode = StatusCodes.Status400BadRequest;
        response.ContentType = "text/plain; charset=utf-8";
        await response.WriteAsync(message);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

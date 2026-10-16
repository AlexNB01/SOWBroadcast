using System.Text.Json;

namespace SowBroadcast.Integrations.Link;

/// <summary>A response of the HTTP fetcher. <c>Status</c> 0 means the request itself failed (see <c>Error</c>).</summary>
public sealed record HttpResult(int Status, byte[] Body, string? ContentType = null, string? Error = null)
{
    public string Text => System.Text.Encoding.UTF8.GetString(Body);
}

/// <summary>The HTTP access the link needs, behind an interface so the service can be tested without a network.</summary>
public interface IHttpFetcher
{
    /// <param name="maxBytes">Stop and report an error when the body is larger than this.</param>
    Task<HttpResult> GetAsync(Uri url, int maxBytes, CancellationToken cancellation);
}

/// <summary>One Socket.IO connection to a SOWDraft server. <c>Connected</c> fires on every (re)connect.</summary>
public interface ISocketConnection : IAsyncDisposable
{
    event Action? Connected;
    event Action? Disconnected;
    event Action<string>? Error;

    /// <summary>Handles an event; the handler gets the first argument of the event as JSON (<c>default</c> when there is none).</summary>
    void On(string eventName, Action<JsonElement> handler);

    Task ConnectAsync(string origin);
    Task EmitAsync(string eventName, object payload);
}

/// <summary><see cref="IHttpFetcher"/> over <see cref="HttpClient"/> with the program's own User-Agent (a library default is refused by the CDN).</summary>
public sealed class HttpFetcher : IHttpFetcher, IDisposable
{
    private readonly HttpClient _client;

    public HttpFetcher(string version)
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"SOWBroadcast/{version} (+https://suomiow.fi)");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/json, image/*;q=0.8");
    }

    public async Task<HttpResult> GetAsync(Uri url, int maxBytes, CancellationToken cancellation)
    {
        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } length && length > maxBytes)
                return new HttpResult((int)response.StatusCode, Array.Empty<byte>(), null, "The response is too large.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellation).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > maxBytes) return new HttpResult((int)response.StatusCode, Array.Empty<byte>(), null, "The response is too large.");
            }
            return new HttpResult((int)response.StatusCode, buffer.ToArray(), response.Content.Headers.ContentType?.MediaType);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return new HttpResult(0, Array.Empty<byte>(), null, "The request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return new HttpResult(0, Array.Empty<byte>(), null, ex.Message);
        }
    }

    public void Dispose() => _client.Dispose();
}

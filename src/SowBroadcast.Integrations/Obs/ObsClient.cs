using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SowBroadcast.Integrations.Obs;

/// <summary>OBS refused or failed a request, or the connection to it could not be made.</summary>
public sealed class ObsException : Exception
{
    public ObsException(string message, int code = 0, Exception? inner = null) : base(message, inner) => Code = code;

    /// <summary>OBS WebSocket's status code (for example 600 = not found, 604 = output not running); 0 when it is not a request failure.</summary>
    public int Code { get; }
}

/// <summary>One WebSocket connection carrying text messages, behind an interface so the protocol can be tested without OBS.</summary>
public interface IObsSocket : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellation);
    Task SendAsync(string text, CancellationToken cancellation);

    /// <summary>The next text message; null when the connection was closed.</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellation);
}

public sealed class WebSocketObsSocket : IObsSocket
{
    private readonly ClientWebSocket _socket = new();

    public Task ConnectAsync(Uri uri, CancellationToken cancellation) => _socket.ConnectAsync(uri, cancellation);

    public Task SendAsync(string text, CancellationToken cancellation) =>
        _socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellation);

    public async Task<string?> ReceiveAsync(CancellationToken cancellation)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result;
            try { result = await _socket.ReceiveAsync(buffer, cancellation).ConfigureAwait(false); }
            catch (WebSocketException) { return null; }
            if (result.MessageType == WebSocketMessageType.Close) return null;
            message.Write(buffer, 0, result.Count);
            if (message.Length > 4 * 1024 * 1024) return null;   // nothing OBS sends is this big
            if (result.EndOfMessage) return Encoding.UTF8.GetString(message.ToArray());
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A client of OBS Studio's WebSocket server (protocol v5, built into OBS 28 and later): connects, signs in with the password
/// challenge, sends requests and matches their answers, and passes on the events OBS sends (scene changes, replay buffer state).
/// </summary>
public sealed class ObsClient : IAsyncDisposable
{
    /// <summary>Events we ask OBS for: Scenes (4) and Outputs (64). Everything else would only be noise.</summary>
    public const int EventSubscriptions = 4 | 64;

    private readonly Func<IObsSocket> _socketFactory;
    private readonly TimeSpan _requestTimeout;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private IObsSocket? _socket;
    private CancellationTokenSource? _lifetime;
    private Task? _reader;

    public ObsClient(Func<IObsSocket>? socketFactory = null, TimeSpan? requestTimeout = null)
    {
        _socketFactory = socketFactory ?? (() => new WebSocketObsSocket());
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(5);
    }

    public bool IsConnected { get; private set; }

    /// <summary>OBS Studio's version, once connected.</summary>
    public string ObsVersion { get; private set; } = "";

    /// <summary>An event of OBS: its type (<c>CurrentProgramSceneChanged</c>...) and data. Raised on a background thread.</summary>
    public event Action<string, JsonElement>? EventReceived;

    /// <summary>The connection ended (OBS closed, or it was lost); the text says why when it is known.</summary>
    public event Action<string?>? Disconnected;

    public async Task ConnectAsync(string host, int port, string? password, CancellationToken cancellation = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
        var socket = _socketFactory();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(_requestTimeout);
            await socket.ConnectAsync(new Uri($"ws://{host}:{port}"), timeout.Token).ConfigureAwait(false);

            var hello = await socket.ReceiveAsync(timeout.Token).ConfigureAwait(false) ?? throw new ObsException("OBS closed the connection.");
            using var helloDoc = JsonDocument.Parse(hello);
            if (helloDoc.RootElement.GetProperty("op").GetInt32() != 0) throw new ObsException("OBS did not answer like OBS WebSocket.");
            var data = helloDoc.RootElement.GetProperty("d");
            ObsVersion = data.TryGetProperty("obsStudioVersion", out var v) ? v.GetString() ?? "" : "";

            string? authentication = null;
            if (data.TryGetProperty("authentication", out var auth))
            {
                if (string.IsNullOrEmpty(password)) throw new ObsException("OBS asks for a password.");
                authentication = Authenticate(password, auth.GetProperty("salt").GetString() ?? "", auth.GetProperty("challenge").GetString() ?? "");
            }

            var identify = new Dictionary<string, object?> { ["rpcVersion"] = 1, ["eventSubscriptions"] = EventSubscriptions };
            if (authentication is not null) identify["authentication"] = authentication;
            await socket.SendAsync(JsonSerializer.Serialize(new { op = 1, d = identify }), timeout.Token).ConfigureAwait(false);

            var identified = await socket.ReceiveAsync(timeout.Token).ConfigureAwait(false)
                             ?? throw new ObsException(authentication is null ? "OBS closed the connection." : "OBS refused the password.");
            using var identifiedDoc = JsonDocument.Parse(identified);
            if (identifiedDoc.RootElement.GetProperty("op").GetInt32() != 2) throw new ObsException("OBS did not accept the connection.");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            await socket.DisposeAsync().ConfigureAwait(false);
            throw new ObsException($"OBS did not answer at {host}:{port}. Is OBS running with the WebSocket server switched on (Tools > WebSocket Server Settings)?");
        }
        catch (Exception ex) when (ex is not ObsException)
        {
            await socket.DisposeAsync().ConfigureAwait(false);
            throw new ObsException(ex is WebSocketException or System.Net.Http.HttpRequestException
                ? $"Could not reach OBS at {host}:{port}. Is OBS running with the WebSocket server switched on (Tools > WebSocket Server Settings)?"
                : ex.Message, 0, ex);
        }
        catch
        {
            await socket.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _socket = socket;
        _lifetime = new CancellationTokenSource();
        IsConnected = true;
        _reader = Task.Run(() => ReadLoopAsync(socket, _lifetime.Token));
    }

    /// <summary>OBS WebSocket 5 sign-in: base64(sha256(base64(sha256(password + salt)) + challenge)).</summary>
    public static string Authenticate(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    /// <summary>Sends a request and returns its response data (an empty object when OBS sends none).</summary>
    /// <exception cref="ObsException">OBS answered with a failure, did not answer in time, or the connection is gone.</exception>
    public async Task<JsonElement> RequestAsync(string requestType, object? requestData = null, CancellationToken cancellation = default)
    {
        var socket = _socket;
        if (!IsConnected || socket is null) throw new ObsException("Not connected to OBS.");

        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            var message = JsonSerializer.Serialize(new { op = 6, d = new { requestType, requestId = id, requestData } });
            await _sendLock.WaitAsync(cancellation).ConfigureAwait(false);
            try { await socket.SendAsync(message, cancellation).ConfigureAwait(false); }
            finally { _sendLock.Release(); }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(_requestTimeout);
            using var _ = timeout.Token.Register(() => completion.TrySetException(new ObsException($"OBS did not answer {requestType} in time.")));
            return await completion.Task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
        {
            throw new ObsException("The connection to OBS was lost.", 0, ex);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ReadLoopAsync(IObsSocket socket, CancellationToken cancellation)
    {
        string? reason = null;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var text = await socket.ReceiveAsync(cancellation).ConfigureAwait(false);
                if (text is null) { reason = "OBS closed the connection."; break; }
                Handle(text);
            }
        }
        catch (OperationCanceledException) { /* disconnecting */ }
        catch (Exception ex) when (ex is WebSocketException or IOException or JsonException)
        {
            reason = "The connection to OBS was lost.";
        }

        var wasConnected = IsConnected;
        IsConnected = false;
        foreach (var pending in _pending.Values) pending.TrySetException(new ObsException("The connection to OBS was lost."));
        if (wasConnected && !cancellation.IsCancellationRequested) Disconnected?.Invoke(reason);
    }

    private void Handle(string text)
    {
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        switch (root.GetProperty("op").GetInt32())
        {
            case 7:
            {
                var d = root.GetProperty("d");
                if (!_pending.TryGetValue(d.GetProperty("requestId").GetString() ?? "", out var completion)) return;
                var status = d.GetProperty("requestStatus");
                if (status.GetProperty("result").GetBoolean())
                {
                    completion.TrySetResult(d.TryGetProperty("responseData", out var data) && data.ValueKind == JsonValueKind.Object ? data.Clone() : EmptyObject());
                }
                else
                {
                    var code = status.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
                    var comment = status.TryGetProperty("comment", out var m) ? m.GetString() : null;
                    completion.TrySetException(new ObsException(string.IsNullOrEmpty(comment) ? $"OBS refused the request (code {code})." : comment!, code));
                }
                break;
            }
            case 5:
            {
                var d = root.GetProperty("d");
                var type = d.GetProperty("eventType").GetString() ?? "";
                var data = d.TryGetProperty("eventData", out var e) ? e.Clone() : EmptyObject();
                try { EventReceived?.Invoke(type, data); }
                catch (Exception) { /* a handler's problem must not stop the connection */ }
                break;
            }
        }
    }

    private static JsonElement EmptyObject()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;
        _lifetime?.Cancel();
        var socket = _socket;
        _socket = null;
        if (socket is not null) await socket.DisposeAsync().ConfigureAwait(false);
        if (_reader is not null)
        {
            try { await _reader.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { /* it ends on its own */ }
        }
        _reader = null;
        _lifetime?.Dispose();
        _lifetime = null;
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}

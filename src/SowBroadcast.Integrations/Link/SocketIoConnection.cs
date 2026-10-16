using System.Text.Json;
using SocketIOClient;
using SocketIOClient.Common;

namespace SowBroadcast.Integrations.Link;

/// <summary><see cref="ISocketConnection"/> over the SocketIOClient package (Socket.IO v4), with automatic reconnection.</summary>
public sealed class SocketIoConnection : ISocketConnection
{
    private SocketIO? _client;
    private readonly List<(string Name, Action<JsonElement> Handler)> _handlers = new();

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<string>? Error;

    public void On(string eventName, Action<JsonElement> handler) => _handlers.Add((eventName, handler));

    public async Task ConnectAsync(string origin)
    {
        var client = new SocketIO(new Uri(origin), new SocketIOOptions
        {
            Reconnection = true,
            ReconnectionAttempts = int.MaxValue,
            ReconnectionDelayMax = 5000,
            ConnectionTimeout = TimeSpan.FromSeconds(10),
            Transport = TransportProtocol.WebSocket,
        });
        _client = client;

        client.OnConnected += (_, _) => Connected?.Invoke();
        client.OnDisconnected += (_, _) => Disconnected?.Invoke();
        client.OnReconnectError += (_, ex) => Error?.Invoke(ex.Message);

        foreach (var (name, handler) in _handlers)
        {
            var capture = handler;
            client.On(name, context =>
            {
                JsonElement value;
                try { value = context.GetValue<JsonElement>(0); }
                catch (Exception) { value = default; }
                capture(value);
                return Task.CompletedTask;
            });
        }

        await client.ConnectAsync().ConfigureAwait(false);
    }

    public Task EmitAsync(string eventName, object payload) =>
        _client is { Connected: true } client ? client.EmitAsync(eventName, new[] { payload }) : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        var client = _client;
        _client = null;
        if (client is null) return;
        try { await client.DisconnectAsync().ConfigureAwait(false); }
        catch (Exception) { /* closing anyway */ }
        client.Dispose();
    }
}

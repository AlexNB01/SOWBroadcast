using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SowBroadcast.Integrations.Obs;

namespace SowBroadcast.Tests;

/// <summary>The real WebSocket transport against a tiny OBS look-alike on a local port (the protocol itself is tested with fakes).</summary>
public sealed class ObsWebSocketTransportTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        _app = builder.Build();
        _app.UseWebSockets();
        _app.Map("/", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await Send(socket, new { op = 0, d = new { obsStudioVersion = "30.9.9", obsWebSocketVersion = "5.5.2", rpcVersion = 1 } });
            while (socket.State == WebSocketState.Open)
            {
                var text = await Receive(socket);
                if (text is null) break;
                using var doc = JsonDocument.Parse(text);
                var d = doc.RootElement.GetProperty("d");
                switch (doc.RootElement.GetProperty("op").GetInt32())
                {
                    case 1:
                        await Send(socket, new { op = 2, d = new { negotiatedRpcVersion = 1 } });
                        break;
                    case 6:
                        var id = d.GetProperty("requestId").GetString();
                        var type = d.GetProperty("requestType").GetString();
                        await Send(socket, new { op = 7, d = new { requestType = type, requestId = id, requestStatus = new { result = true, code = 100 }, responseData = new { echoed = type } } });
                        await Send(socket, new { op = 5, d = new { eventType = "CurrentProgramSceneChanged", eventIntent = 4, eventData = new { sceneName = "After " + type } } });
                        break;
                }
            }
        });
        await _app.StartAsync();
        _port = new Uri(_app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private static Task Send(WebSocket socket, object message) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<string?> Receive(WebSocket socket)
    {
        var buffer = new byte[8192];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        return result.MessageType == WebSocketMessageType.Close ? null : Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    [Fact]
    public async Task The_client_connects_over_a_real_websocket_sends_requests_and_receives_events()
    {
        await using var client = new ObsClient();
        var events = new List<string>();
        client.EventReceived += (type, data) => events.Add(data.GetProperty("sceneName").GetString()!);

        await client.ConnectAsync("127.0.0.1", _port, null);
        var answer = await client.RequestAsync("GetVersion");
        await Task.Delay(200);

        Assert.Equal("30.9.9", client.ObsVersion);
        Assert.Equal("GetVersion", answer.GetProperty("echoed").GetString());
        Assert.Equal("After GetVersion", Assert.Single(events));
    }

    [Fact]
    public async Task Nothing_listening_gives_a_message_that_says_what_to_check_in_obs()
    {
        await using var client = new ObsClient();

        var failure = await Assert.ThrowsAsync<ObsException>(() => client.ConnectAsync("127.0.0.1", 1, null));

        Assert.Contains("WebSocket server", failure.Message);
        Assert.False(client.IsConnected);
    }
}

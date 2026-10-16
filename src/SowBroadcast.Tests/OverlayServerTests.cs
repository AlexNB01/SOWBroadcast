using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SowBroadcast.Server;

namespace SowBroadcast.Tests;

public sealed class OverlayServerTests : IAsyncLifetime
{
    private static readonly string[] FullRefresh = { "maps", "standings" };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-server-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private OverlayServer _server = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "install", "HTML"));
        Directory.CreateDirectory(Path.Combine(_dir, "install", "Scoreboard", "Heroes"));
        Directory.CreateDirectory(Path.Combine(_dir, "data", "Scoreboard", "Match"));
        File.WriteAllText(Path.Combine(_dir, "install", "HTML", "scoreboard.html"), "<html>overlay</html>");
        File.WriteAllText(Path.Combine(_dir, "install", "Scoreboard", "Heroes", "ana.txt"), "shipped");
        File.WriteAllText(Path.Combine(_dir, "install", "Scoreboard", "Match.txt"), "shadowed-by-nothing");
        File.WriteAllText(Path.Combine(_dir, "data", "Scoreboard", "Match", "T1Name.txt"), "Alpha");
        File.WriteAllText(Path.Combine(_dir, "data", "autosave.json"), "{\"secret\":true}");

        _server = await StartAsync(0);
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        _http.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private Task<OverlayServer> StartAsync(int port, TimeSpan? keepAlive = null) => OverlayServer.StartAsync(new OverlayServerOptions
    {
        Port = port,
        NotifyToken = "secret-token",
        FullRefreshKeys = FullRefresh,
        KeepAliveInterval = keepAlive ?? TimeSpan.FromSeconds(15),
        StaticFolders = new Dictionary<string, IReadOnlyList<string>>
        {
            ["/HTML"] = new[] { Path.Combine(_dir, "install", "HTML") },
            // Generated files live in the data folder; shipped assets in the install folder. Data wins.
            ["/Scoreboard"] = new[] { Path.Combine(_dir, "data", "Scoreboard"), Path.Combine(_dir, "install", "Scoreboard") },
        },
    });

    private string Url(string path) => _server.BaseUrl + path;

    private async Task<HttpResponseMessage> NotifyAsync(string body, string? token = "secret-token")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url("/notify")) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (token is not null) request.Headers.Add("X-SOWB-Token", token);
        return await _http.SendAsync(request);
    }

    // ---- static files ------------------------------------------------------------------------------------

    [Fact]
    public async Task Serves_overlay_files_from_the_configured_folders_only()
    {
        Assert.Equal("<html>overlay</html>", await _http.GetStringAsync(Url("/HTML/scoreboard.html")));
        Assert.Equal("Alpha", await _http.GetStringAsync(Url("/Scoreboard/Match/T1Name.txt")));
    }

    [Fact]
    public async Task Generated_data_and_shipped_assets_are_served_under_one_prefix()
    {
        Assert.Equal("shipped", await _http.GetStringAsync(Url("/Scoreboard/Heroes/ana.txt")));
    }

    [Theory]
    [InlineData("/autosave.json")]
    [InlineData("/match.json")]
    [InlineData("/")]
    [InlineData("/HTML/../autosave.json")]
    [InlineData("/Scoreboard/../autosave.json")]
    [InlineData("/%2e%2e/autosave.json")]
    [InlineData("/HTML/missing.html")]
    public async Task Everything_else_is_not_found(string path)
    {
        var response = await _http.GetAsync(Url(path));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Every_response_forbids_caching()
    {
        var response = await _http.GetAsync(Url("/HTML/scoreboard.html"));

        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Contains("no-cache", string.Join(",", response.Headers.Pragma.Select(p => p.Name)));
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("0", response.Content.Headers.NonValidated["Expires"].ToString());
    }

    // ---- /notify -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Notify_requires_the_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await NotifyAsync("{\"changed\":[\"maps\"]}", token: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await NotifyAsync("{\"changed\":[\"maps\"]}", token: "wrong")).StatusCode);
    }

    [Fact]
    public async Task Notify_accepts_a_valid_payload_with_204_and_publishes_it()
    {
        var response = await NotifyAsync("{\"changed\":[\"maps\",\"t1.score\"]}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, _server.Hub.LastId);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"changed\":\"maps\"}")]
    public async Task Notify_rejects_malformed_bodies_with_400(string body)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await NotifyAsync(body)).StatusCode);
    }

    [Fact]
    public async Task Notify_rejects_oversized_bodies()
    {
        var big = "{\"changed\":[\"" + new string('x', 100_000) + "\"]}";

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await NotifyAsync(big)).StatusCode);
    }

    // ---- /events -----------------------------------------------------------------------------------------

    private async Task<StreamReader> OpenEventsAsync(string? lastEventId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Url("/events"));
        if (lastEventId is not null) request.Headers.Add("Last-Event-ID", lastEventId);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        return new StreamReader(await response.Content.ReadAsStreamAsync());
    }

    private static async Task<List<string>> ReadBlocksAsync(StreamReader reader, int count)
    {
        var blocks = new List<string>();
        var current = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (blocks.Count < count)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line is null) break;
            if (line.Length == 0)
            {
                if (current.Length > 0) blocks.Add(current.ToString().TrimEnd('\n'));
                current.Clear();
            }
            else current.Append(line).Append('\n');
        }
        return blocks;
    }

    [Fact]
    public async Task Events_start_with_a_connected_comment_and_do_not_replay_old_events_to_new_clients()
    {
        _server.Hub.Publish(new[] { "maps" });

        using var reader = await OpenEventsAsync();
        var first = await ReadBlocksAsync(reader, 1);

        Assert.Equal(": connected", first[0]);
        Assert.Equal(1, _server.Hub.LastId);
    }

    [Fact]
    public async Task Events_deliver_every_published_key_in_order_with_ids()
    {
        using var reader = await OpenEventsAsync();
        await ReadBlocksAsync(reader, 1);

        for (var i = 0; i < 100; i++) _server.Hub.Publish(new[] { $"k{i}" });
        var blocks = await ReadBlocksAsync(reader, 100);

        Assert.Equal(100, blocks.Count);
        for (var i = 0; i < 100; i++)
        {
            Assert.StartsWith($"id: {i + 1}\n", blocks[i]);
            using var doc = JsonDocument.Parse(blocks[i].Split('\n')[1]["data: ".Length..]);
            Assert.Equal($"k{i}", doc.RootElement.GetProperty("changed")[0].GetString());
        }
    }

    [Fact]
    public async Task Reconnecting_with_last_event_id_replays_only_the_missed_events()
    {
        for (var i = 0; i < 5; i++) _server.Hub.Publish(new[] { $"k{i}" });

        using var reader = await OpenEventsAsync(lastEventId: "3");
        var blocks = await ReadBlocksAsync(reader, 3);

        Assert.Equal(": connected", blocks[0]);
        Assert.StartsWith("id: 4\n", blocks[1]);
        Assert.StartsWith("id: 5\n", blocks[2]);
    }

    [Fact]
    public async Task A_client_from_a_previous_server_run_gets_one_full_refresh()
    {
        _server.Hub.Publish(new[] { "maps" });

        using var reader = await OpenEventsAsync(lastEventId: "9999");
        var blocks = await ReadBlocksAsync(reader, 2);

        Assert.Contains("\"maps\"", blocks[1]);
        Assert.Contains("\"standings\"", blocks[1]);
    }

    [Fact]
    public async Task Notify_reaches_connected_event_clients_with_the_changed_keys_and_the_version()
    {
        using var reader = await OpenEventsAsync();
        await ReadBlocksAsync(reader, 1);

        await NotifyAsync("{\"changed\":[\"waiting.timer\"]}");
        var blocks = await ReadBlocksAsync(reader, 1);

        Assert.Equal("id: 1\ndata: {\"changed\":[\"waiting.timer\"],\"version\":1}", blocks[0]);
    }

    // ---- /api/state ----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_state_is_served_from_the_provider_as_json_and_never_cached()
    {
        var json = "{\"version\":7,\"match\":{}}";
        await using var server = await OverlayServer.StartAsync(new OverlayServerOptions { Port = 0, StateProvider = () => json });

        var response = await _http.GetAsync(server.BaseUrl + "/api/state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(json, await response.Content.ReadAsStringAsync());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task The_provider_is_asked_for_every_request_so_the_latest_state_is_served()
    {
        var version = 0;
        await using var server = await OverlayServer.StartAsync(new OverlayServerOptions { Port = 0, StateProvider = () => $"{{\"version\":{++version}}}" });

        var first = await _http.GetStringAsync(server.BaseUrl + "/api/state");
        var second = await _http.GetStringAsync(server.BaseUrl + "/api/state");

        Assert.Equal(("{\"version\":1}", "{\"version\":2}"), (first, second));
    }

    [Fact]
    public async Task Without_a_state_there_is_nothing_to_serve_yet()
    {
        var response = await _http.GetAsync(Url("/api/state")); // this fixture's server has no provider

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Idle_connections_receive_keepalive_comments()
    {
        await using var fast = await StartAsync(0, keepAlive: TimeSpan.FromMilliseconds(200));
        var request = new HttpRequestMessage(HttpMethod.Get, fast.BaseUrl + "/events");
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());

        var blocks = await ReadBlocksAsync(reader, 3);

        Assert.Equal(": connected", blocks[0]);
        Assert.Equal(": keepalive", blocks[1]);
        Assert.Equal(": keepalive", blocks[2]);
    }

    [Fact]
    public async Task Disconnected_clients_are_removed_from_the_hub()
    {
        using (var reader = await OpenEventsAsync())
            await ReadBlocksAsync(reader, 1);

        for (var i = 0; i < 50 && _server.Hub.ClientCount > 0; i++) await Task.Delay(100);

        Assert.Equal(0, _server.Hub.ClientCount);
    }

    // ---- ports -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Falls_back_to_the_next_free_port_when_the_preferred_one_is_taken()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var taken = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            await using var second = await StartAsync(taken);

            Assert.NotEqual(taken, second.Port);
            Assert.True(second.Port > taken);
            Assert.Equal("<html>overlay</html>", await _http.GetStringAsync(second.BaseUrl + "/HTML/scoreboard.html"));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void Only_listens_on_the_loopback_interface()
    {
        Assert.StartsWith("http://127.0.0.1:", _server.BaseUrl);
    }
}

using System.Text.Json;
using SowBroadcast.Core.Link;
using SowBroadcast.Integrations.Link;

namespace SowBroadcast.Tests;

internal sealed class FakeHttp : IHttpFetcher
{
    private readonly Dictionary<string, HttpResult> _routes = new();
    public List<string> Requests { get; } = new();

    public void Set(string url, int status, string body = "", string? error = null) =>
        _routes[url] = new HttpResult(status, System.Text.Encoding.UTF8.GetBytes(body), null, error);

    public void SetBytes(string url, byte[] body, int status = 200) => _routes[url] = new HttpResult(status, body);

    public Task<HttpResult> GetAsync(Uri url, int maxBytes, CancellationToken cancellation)
    {
        lock (Requests) Requests.Add(url.ToString());
        return Task.FromResult(_routes.TryGetValue(url.ToString(), out var r) ? r : new HttpResult(404, Array.Empty<byte>()));
    }

    public int Count(string url) { lock (Requests) return Requests.Count(r => r == url); }
}

internal sealed class FakeSocket : ISocketConnection
{
    private readonly Dictionary<string, Action<JsonElement>> _handlers = new();
    public List<(string Name, string Json)> Emitted { get; } = new();
    public string? Origin { get; private set; }
    public bool Disposed { get; private set; }

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<string>? Error;

    public void On(string eventName, Action<JsonElement> handler) => _handlers[eventName] = handler;

    public Task ConnectAsync(string origin)
    {
        Origin = origin;
        Connected?.Invoke();
        return Task.CompletedTask;
    }

    public Task EmitAsync(string eventName, object payload)
    {
        lock (Emitted) Emitted.Add((eventName, JsonSerializer.Serialize(payload)));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    public void Fire(string eventName, string json)
    {
        using var doc = JsonDocument.Parse(json);
        _handlers[eventName](doc.RootElement.Clone());
    }

    public void RaiseConnected() => Connected?.Invoke();
    public void RaiseDisconnected() => Disconnected?.Invoke();
    public void RaiseError(string message) => Error?.Invoke(message);

    public int Joined(string eventName) { lock (Emitted) return Emitted.Count(e => e.Name == eventName); }
}

/// <summary>Delays that only end when the test says so (or the wait is cancelled).</summary>
internal sealed class ManualDelays
{
    private readonly List<(TimeSpan Span, TaskCompletionSource Done)> _pending = new();

    public Task Delay(TimeSpan span, CancellationToken token)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pending) _pending.Add((span, done));
        token.Register(() => done.TrySetCanceled());
        return done.Task;
    }

    public bool Has(TimeSpan span) { lock (_pending) return _pending.Any(p => p.Span == span && !p.Done.Task.IsCompleted); }

    public void Release(TimeSpan span)
    {
        List<TaskCompletionSource> hit;
        lock (_pending)
        {
            hit = _pending.Where(p => p.Span == span).Select(p => p.Done).ToList();
            _pending.RemoveAll(p => p.Span == span);
        }
        foreach (var d in hit) d.TrySetResult();
    }
}

public sealed class LinkServiceTests : IAsyncLifetime
{
    private const string Origin = "https://sowdraft.test";
    private const string TournamentUrl = Origin + "/tournament/synth";
    private const string Root = Origin + "/api/tournament-system/synth";

    private static readonly TimeSpan Resync = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retry = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly FakeHttp _http = new();
    private readonly ManualDelays _delays = new();
    private readonly List<FakeSocket> _sockets = new();
    private readonly List<LinkUpdate> _updates = new();
    private readonly List<LinkStatus> _statuses = new();
    private LinkService _service = null!;

    public Task InitializeAsync()
    {
        _service = new LinkService(_http, () => { var s = new FakeSocket(); _sockets.Add(s); return s; },
            new LinkServiceOptions { Delay = _delays.Delay });
        _service.Updated += u => { lock (_updates) _updates.Add(u); };
        _service.StatusChanged += s => { lock (_statuses) _statuses.Add(s); };
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _service.DisposeAsync();

    private FakeSocket Socket => _sockets.Last();

    private static async Task Eventually(Func<bool> condition, string because = "")
    {
        for (var i = 0; i < 300; i++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.True(condition(), "Timed out waiting for: " + because);
    }

    /// <summary>Lets the pending wait of that length end, once the service has started it.</summary>
    private async Task ReleaseAsync(TimeSpan span)
    {
        await Eventually(() => _delays.Has(span), $"a pending {span} wait");
        _delays.Release(span);
    }

    private int UpdateCount() { lock (_updates) return _updates.Count; }
    private LinkUpdate LastUpdate() { lock (_updates) return _updates[^1]; }

    private void LegacyServer() => _http.Set(Root, 200, LinkFixtures.LegacyTournament());

    // ---- legacy route ------------------------------------------------------------------------------------

    [Fact]
    public async Task Linking_without_the_new_api_falls_back_to_the_public_response_and_chooses_no_match()
    {
        LegacyServer();

        await _service.LinkTournamentAsync(TournamentUrl);

        Assert.Equal(1, _http.Count(Root + "/broadcast"));  // tried the new API first
        Assert.Equal(1, _http.Count(Root));
        Assert.Equal(Origin, Socket.Origin);
        Assert.Equal(new LinkStatus(LinkState.NoMatchSelected, "No match selected"), _service.Status);
        var update = LastUpdate();
        Assert.Equal("", update.SelectedMatchId);          // never picked automatically, although m1 has a draft room
        Assert.Equal(10, update.Snapshot.Matches.Count);
        Assert.Equal("t-uuid", update.Snapshot.Id);
        Assert.Null(update.Snapshot.Live);
        Assert.Equal(0, Socket.Joined("join-match"));
    }

    [Fact]
    public async Task Choosing_a_match_joins_its_draft_room_and_the_rooms_state_becomes_the_live_draft()
    {
        LegacyServer();
        await _service.LinkTournamentAsync(TournamentUrl);

        await _service.SelectMatchAsync("m1");

        Assert.Equal(1, Socket.Joined("join-match"));
        Assert.Contains(Socket.Emitted, e => e.Name == "join-match" && e.Json == """{"matchId":"room-1","role":"stream"}""");
        Assert.Equal(new LinkStatus(LinkState.WaitingForDraft, "Waiting for draft to start…"), _service.Status); // room state not here yet

        var before = UpdateCount();
        Socket.Fire("update-state", LinkFixtures.DraftRoom);
        await Eventually(() => UpdateCount() > before, "the update after the room state");

        var live = LastUpdate().Snapshot.Live!;
        Assert.Equal(3, live.Maps.Count);
        Assert.Equal(("a", "b"), (live.Team1Id, live.Team2Id));
        Assert.Equal(new LinkStatus(LinkState.Linked, "Linked – Synthetic Cup – Alpha vs Bravo"), _service.Status);
    }

    [Fact]
    public async Task A_room_state_of_another_draft_is_ignored()
    {
        LegacyServer();
        await _service.LinkTournamentAsync(TournamentUrl, "m1");
        var before = UpdateCount();

        Socket.Fire("update-state", LinkFixtures.DraftRoom.Replace("\"room-1\"", "\"other-room\""));
        await Task.Delay(100);

        Assert.Equal(before, UpdateCount());
    }

    [Fact]
    public async Task A_match_without_a_draft_room_waits_and_a_finished_draft_says_so()
    {
        LegacyServer();
        await _service.LinkTournamentAsync(TournamentUrl);

        await _service.SelectMatchAsync("m2"); // no draft room
        Assert.Equal(LinkState.WaitingForDraft, _service.Status.State);
        Assert.Equal(0, Socket.Joined("join-match"));

        await _service.SelectMatchAsync("m1");
        var before = UpdateCount();
        Socket.Fire("update-state", LinkFixtures.DraftRoom.Replace("\"hero-ban\"", "\"complete\""));
        await Eventually(() => UpdateCount() > before, "the complete room");
        Assert.Equal(new LinkStatus(LinkState.MatchComplete, "Match complete"), _service.Status);
    }

    [Fact]
    public async Task The_saved_choice_of_an_earlier_session_is_taken_up_again_on_link()
    {
        LegacyServer();

        await _service.LinkTournamentAsync(TournamentUrl, "m1");

        Assert.Equal(1, Socket.Joined("join-match"));
        Assert.Equal("m1", LastUpdate().SelectedMatchId);
    }

    [Fact]
    public async Task A_tournament_update_for_this_tournament_refetches_once_for_a_burst_and_other_tournaments_are_ignored()
    {
        LegacyServer();
        await _service.LinkTournamentAsync(TournamentUrl);
        var baseline = _http.Count(Root);

        Socket.Fire("tournament-update", "\"some-other-uuid\"");
        Socket.Fire("tournament-update", "\"t-uuid\"");     // the server sends the UUID although we linked by slug
        Socket.Fire("tournament-update", "\"t-uuid\"");
        Socket.Fire("tournament-update", "\"synth\"");
        await ReleaseAsync(Debounce);

        await Eventually(() => _http.Count(Root) == baseline + 1, "one refetch");
        await Task.Delay(100);
        Assert.Equal(baseline + 1, _http.Count(Root));
    }

    // ---- new route ---------------------------------------------------------------------------------------

    private const string BroadcastJson = """
    {
      "schemaVersion": 1, "version": 10, "updatedAt": 1,
      "tournament": { "id": "t-uuid", "slug": "synth", "name": "Synthetic Cup", "format": "single_elimination", "status": "live", "bestOf": 3 },
      "stages": [], "standings": null,
      "teams": [ { "id": "a", "name": "Alpha", "players": [] }, { "id": "b", "name": "Bravo", "players": [] } ],
      "matches": [ { "id": "m1", "bracket": "main", "round": 1, "matchNumber": 1, "team1Id": "a", "team2Id": "b", "status": "in_progress", "draftMatchId": "room-1", "bestOf": 3 } ],
      "live": null
    }
    """;

    private static string BroadcastWithLive(long version) => BroadcastJson
        .Replace("\"version\": 10", $"\"version\": {version}")
        .Replace("\"live\": null", """
        "live": { "tournamentMatchId": "m1", "draftMatchId": "room-1", "team1Id": "a", "team2Id": "b", "phase": "map", "mapCount": 2,
                  "score": { "team1": 0, "team2": 0 }, "maps": [ { "order": 1, "map": "Busan", "pickedBy": "team1", "auto": false, "bans": [], "status": "current", "result": null } ] }
        """);

    [Fact]
    public async Task The_new_api_is_used_when_the_server_has_it_and_the_tournament_room_is_joined()
    {
        _http.Set(Root + "/broadcast", 200, BroadcastJson);

        await _service.LinkTournamentAsync(TournamentUrl);

        Assert.Equal(0, _http.Count(Root));                       // the legacy response is not needed
        Assert.Contains(Socket.Emitted, e => e.Name == "join-tournament" && e.Json == """{"tournamentId":"synth"}""");
        Assert.Equal(LinkState.NoMatchSelected, _service.Status.State);
    }

    [Fact]
    public async Task Choosing_a_match_asks_the_new_api_for_that_matchs_live_draft()
    {
        _http.Set(Root + "/broadcast", 200, BroadcastJson);
        _http.Set(Root + "/broadcast?matchId=m1", 200, BroadcastWithLive(11));
        await _service.LinkTournamentAsync(TournamentUrl);

        await _service.SelectMatchAsync("m1");

        Assert.Equal(1, _http.Count(Root + "/broadcast?matchId=m1"));
        Assert.Equal("Busan", LastUpdate().Snapshot.Live!.Maps[0].Map);
        Assert.Equal(LinkState.Linked, _service.Status.State);
        Assert.Equal(0, Socket.Joined("join-match")); // the draft room is not needed on this route
    }

    [Fact]
    public async Task A_live_only_event_that_follows_on_is_applied_without_a_refetch_and_a_gap_causes_one()
    {
        _http.Set(Root + "/broadcast", 200, BroadcastJson);
        _http.Set(Root + "/broadcast?matchId=m1", 200, BroadcastWithLive(11));
        await _service.LinkTournamentAsync(TournamentUrl, "m1");
        var fetches = _http.Count(Root + "/broadcast?matchId=m1");
        var before = UpdateCount();

        const string live = """{ "tournamentMatchId": "m1", "draftMatchId": "room-1", "team1Id": "a", "team2Id": "b", "phase": "hero-ban", "mapCount": 2, "score": { "team1": 1, "team2": 0 }, "maps": [] }""";
        Socket.Fire("tournament-event", $$"""{ "tournamentId": "t-uuid", "version": 12, "changed": ["live"], "live": {{live}} }""");

        await Eventually(() => UpdateCount() > before, "the pushed live state");
        Assert.Equal("hero-ban", LastUpdate().Snapshot.Live!.Phase);
        Assert.Equal(1, LastUpdate().Snapshot.Live!.Score1);
        Assert.Equal(fetches, _http.Count(Root + "/broadcast?matchId=m1"));

        Socket.Fire("tournament-event", $$"""{ "tournamentId": "t-uuid", "version": 15, "changed": ["live"], "live": {{live}} }""");
        await ReleaseAsync(Debounce);
        await Eventually(() => _http.Count(Root + "/broadcast?matchId=m1") == fetches + 1, "the refetch after a version gap");
    }

    [Fact]
    public async Task A_live_event_of_another_match_does_not_disturb_the_selected_one()
    {
        _http.Set(Root + "/broadcast", 200, BroadcastJson);
        _http.Set(Root + "/broadcast?matchId=m1", 200, BroadcastWithLive(11));
        await _service.LinkTournamentAsync(TournamentUrl, "m1");
        var before = UpdateCount();

        Socket.Fire("tournament-event", """{ "tournamentId": "t-uuid", "version": 12, "changed": ["live"], "live": { "tournamentMatchId": "m2", "phase": "map", "maps": [] } }""");
        await Task.Delay(100);

        Assert.Equal(before, UpdateCount());
    }

    [Fact]
    public async Task Other_changes_in_an_event_refetch_the_whole_snapshot()
    {
        _http.Set(Root + "/broadcast", 200, BroadcastJson);
        await _service.LinkTournamentAsync(TournamentUrl);
        var fetches = _http.Count(Root + "/broadcast");

        Socket.Fire("tournament-event", """{ "tournamentId": "t-uuid", "version": 11, "changed": ["matches", "standings"] }""");
        await ReleaseAsync(Debounce);

        await Eventually(() => _http.Count(Root + "/broadcast") == fetches + 1, "the refetch");
    }

    // ---- connection --------------------------------------------------------------------------------------

    [Fact]
    public async Task After_every_reconnect_everything_is_fetched_and_the_rooms_are_joined_again()
    {
        LegacyServer();
        await _service.LinkTournamentAsync(TournamentUrl, "m1");
        var fetches = _http.Count(Root);
        var joins = Socket.Joined("join-match");

        Socket.RaiseDisconnected();
        Assert.Equal(LinkState.Connecting, _service.Status.State);
        Socket.RaiseConnected();

        await Eventually(() => _http.Count(Root) == fetches + 1, "the refetch after reconnecting");
        await Eventually(() => Socket.Joined("join-match") == joins + 1, "the room is joined again");
    }

    [Fact]
    public async Task Everything_is_refetched_silently_every_thirty_seconds()
    {
        LegacyServer();
        await _service.LinkTournamentAsync(TournamentUrl);
        var fetches = _http.Count(Root);

        await ReleaseAsync(Resync);

        await Eventually(() => _http.Count(Root) == fetches + 1, "the periodic refetch");
    }

    [Fact]
    public async Task A_failed_fetch_shows_the_error_and_is_tried_once_more_after_a_few_seconds()
    {
        _http.Set(Root + "/broadcast", 404);
        _http.Set(Root, 0, "", "The server could not be reached.");

        await _service.LinkTournamentAsync(TournamentUrl);
        Assert.Equal(new LinkStatus(LinkState.Error, "Error: The server could not be reached."), _service.Status);

        LegacyServer();
        await ReleaseAsync(Retry);
        await Eventually(() => _service.Status.State == LinkState.NoMatchSelected, "recovery after the retry");
    }

    [Fact]
    public async Task An_unknown_tournament_is_reported_as_not_found()
    {
        await _service.LinkTournamentAsync(Origin + "/tournament/nope");

        Assert.Equal(new LinkStatus(LinkState.Error, "Error: Tournament not found."), _service.Status);
    }

    [Fact]
    public async Task A_socket_error_is_shown()
    {
        LegacyServer();
        await _service.LinkTournamentAsync(TournamentUrl);

        Socket.RaiseError("connection failed");

        Assert.Equal(new LinkStatus(LinkState.Error, "Error: connection failed"), _service.Status);
    }

    [Fact]
    public async Task Unlinking_closes_the_connection_and_late_events_do_nothing()
    {
        LegacyServer();
        await _service.LinkTournamentAsync(TournamentUrl, "m1");
        var socket = Socket;
        var before = UpdateCount();

        await _service.UnlinkAsync();
        socket.Fire("update-state", LinkFixtures.DraftRoom);
        socket.Fire("tournament-update", "\"t-uuid\"");
        await Task.Delay(50);

        Assert.True(socket.Disposed);
        Assert.Equal(new LinkStatus(LinkState.NotLinked, "Not linked"), _service.Status);
        Assert.False(_service.IsLinked);
        Assert.Null(_service.Snapshot);
        Assert.Equal(before, UpdateCount());
    }

    [Fact]
    public async Task Linking_a_second_time_replaces_the_first_link()
    {
        LegacyServer();
        await _service.LinkTournamentAsync(TournamentUrl);
        var first = Socket;

        await _service.LinkTournamentAsync(TournamentUrl);

        Assert.True(first.Disposed);
        Assert.Equal(2, _sockets.Count);
    }

    // ---- manual draft room -------------------------------------------------------------------------------

    [Fact]
    public async Task A_draft_room_linked_by_hand_joins_that_room_and_shows_its_teams_and_series()
    {
        await _service.LinkDraftRoomAsync(Origin + "/stream?matchId=room-1&role=stream");

        Assert.Contains(Socket.Emitted, e => e.Name == "join-match" && e.Json == """{"matchId":"room-1","role":"stream"}""");
        Assert.Equal(LinkState.WaitingForDraft, _service.Status.State);

        Socket.Fire("update-state", LinkFixtures.DraftRoom);
        await Eventually(() => UpdateCount() > 0, "the manual update");

        var update = LastUpdate();
        Assert.True(update.IsManualDraftRoom);
        Assert.Equal(LinkService.ManualMatchId, update.SelectedMatchId);
        Assert.Equal(new[] { "Alpha", "Bravo" }, update.Snapshot.Teams.Select(t => t.Name));
        Assert.Equal(3, update.Snapshot.Live!.Maps.Count);
        Assert.Equal("Linked – Alpha vs Bravo", _service.Status.Text);
        Assert.Empty(_http.Requests);                    // no REST needed
    }

    [Fact]
    public async Task Draft_room_addresses_and_tournament_addresses_are_checked()
    {
        var notUrl = await Assert.ThrowsAsync<LinkException>(() => _service.LinkTournamentAsync("hello"));
        var noKey = await Assert.ThrowsAsync<LinkException>(() => _service.LinkTournamentAsync(Origin + "/"));
        var noMatch = await Assert.ThrowsAsync<LinkException>(() => _service.LinkDraftRoomAsync(Origin + "/stream"));

        Assert.Equal("The link is not a valid URL.", notUrl.Message);
        Assert.Equal("The link has no tournament identifier.", noKey.Message);
        Assert.Equal("The link has no matchId parameter.", noMatch.Message);
        Assert.False(_service.IsLinked);
    }

    [Theory]
    [InlineData("https://sowdraft.fi/tournament/esm26", "https://sowdraft.fi", "esm26")]
    [InlineData("https://sowdraft.fi/tournament/esm26/", "https://sowdraft.fi", "esm26")]
    [InlineData("http://localhost:3000/tournament/abc-123?x=1", "http://localhost:3000", "abc-123")]
    [InlineData("https://sowdraft.fi/page?id=77", "https://sowdraft.fi", "77")]
    public void Tournament_addresses_give_the_origin_and_the_id_or_slug(string url, string origin, string key) =>
        Assert.Equal((origin, key), LinkUrl.ParseTournament(url));
}

public sealed class LogoCacheTests : IDisposable
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 1, 2, 3 };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-logos-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHttp _http = new();

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    [Fact]
    public async Task A_logo_is_downloaded_once_and_found_again_by_its_address()
    {
        _http.SetBytes("https://sowdraft.test/a.png", Png);
        var cache = new LogoCache(_dir, _http);
        Assert.Null(cache.PathOf("https://sowdraft.test/a.png"));

        Assert.Equal(1, await cache.EnsureAsync(new[] { "https://sowdraft.test/a.png", "https://sowdraft.test/a.png" }));
        var path = cache.PathOf("https://sowdraft.test/a.png");

        Assert.NotNull(path);
        Assert.EndsWith(".png", path);
        Assert.Equal(Png, await File.ReadAllBytesAsync(path!));
        Assert.Equal(0, await cache.EnsureAsync(new[] { "https://sowdraft.test/a.png" }));
        Assert.Equal(1, _http.Count("https://sowdraft.test/a.png"));
        Assert.Equal(path, new LogoCache(_dir, _http).PathOf("https://sowdraft.test/a.png")); // survives a restart
    }

    [Fact]
    public async Task Things_that_are_not_pictures_or_too_large_are_refused_and_not_retried()
    {
        _http.SetBytes("https://sowdraft.test/page.png", System.Text.Encoding.UTF8.GetBytes("<html><body>Forbidden</body></html>"));
        _http.Set("https://sowdraft.test/missing.png", 404);
        _http.SetBytes("https://sowdraft.test/big.png", Png.Concat(new byte[LogoCache.MaxBytes]).ToArray());
        var cache = new LogoCache(_dir, _http);
        var urls = new[] { "https://sowdraft.test/page.png", "https://sowdraft.test/missing.png", "https://sowdraft.test/big.png", "ftp://x/y.png", "not a url" };

        Assert.Equal(0, await cache.EnsureAsync(urls));
        Assert.Equal(0, await cache.EnsureAsync(urls));

        Assert.All(urls, u => Assert.Null(cache.PathOf(u)));
        Assert.Equal(1, _http.Count("https://sowdraft.test/page.png"));
        Assert.Equal(1, _http.Count("https://sowdraft.test/missing.png"));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }, ".jpg")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, ".webp")]
    public void Pictures_are_recognised_by_their_content(byte[] body, string extension) =>
        Assert.Equal(extension, LogoCache.ExtensionOf(body));

    [Fact]
    public void An_svg_is_accepted_but_html_and_scripts_are_not()
    {
        Assert.Equal(".svg", LogoCache.ExtensionOf(System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")));
        Assert.Equal(".svg", LogoCache.ExtensionOf(System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg></svg>")));
        Assert.Null(LogoCache.ExtensionOf(System.Text.Encoding.UTF8.GetBytes("<script>alert(1)</script>")));
        Assert.Null(LogoCache.ExtensionOf(new byte[] { 1, 2, 3 }));
    }
}

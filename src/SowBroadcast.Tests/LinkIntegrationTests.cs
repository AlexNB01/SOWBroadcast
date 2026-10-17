using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Link;
using SowBroadcast.Core.Model;
using SowBroadcast.Integrations.Link;

namespace SowBroadcast.Tests;

/// <summary>The link at the level of the whole app: what lands on the tabs and what goes to the overlays.</summary>
public sealed class LinkIntegrationTests : IAsyncLifetime, IDisposable
{
    private const string Origin = "https://sowdraft.test";
    private const string TournamentUrl = Origin + "/tournament/synth";
    private const string Root = Origin + "/api/tournament-system/synth";
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 1, 2, 3 };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-link-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHttp _http = new();
    private readonly ManualDelays _delays = new();
    private readonly List<FakeSocket> _sockets = new();
    private readonly Recorder _recorder = new();
    private LinkService _service = null!;
    private MainWindowViewModel _vm = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _http.Set(Root, 200, LinkFixtures.LegacyTournament());
        _http.SetBytes(Origin + "/uploads/logos/a.png", Png);

        var assets = new AssetService(Path.Combine(_dir, "Scoreboard"), Array.Empty<string>());
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "assets");
        foreach (var map in new[] { "Busan", "Midtown" })
            assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = map, Mode = "Control", ImageSourcePath = Path.Combine(fixtures, "busan.png") });

        _service = new LinkService(_http, () => { var s = new FakeSocket(); _sockets.Add(s); return s; }, new LinkServiceOptions { Delay = _delays.Delay });
        var logos = new LogoCache(Path.Combine(_dir, "TeamLogos"), _http);
        _vm = new MainWindowViewModel(_recorder, _recorder, _recorder, Path.Combine(_dir, "autosave.json"), assets,
            linkService: _service, logoCache: logos, uiPost: action => action());
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _service.DisposeAsync();

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private FakeSocket Socket => _sockets.Last();

    private static async Task Eventually(Func<bool> condition, string because)
    {
        for (var i = 0; i < 300; i++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.True(condition(), "Timed out waiting for: " + because);
    }

    private async Task LinkAndSelectAsync()
    {
        _vm.Link.TournamentUrl = TournamentUrl;
        await _vm.Link.LinkCommand.ExecuteAsync(null);
        _vm.Link.SelectedMatch = _vm.Link.Matches.Single(m => m.Id == "m1");
        await Eventually(() => _sockets.Count > 0 && Socket.Joined("join-match") == 1, "the draft room is joined");
        Socket.Fire("update-state", LinkFixtures.DraftRoom);
        await Eventually(() => _vm.Match.Maps[0].Map == "Busan", "the draft reaches the map cards");
    }

    /// <summary>Waits for the background logo download to land (it rewrites the autosave when it does).</summary>
    private async Task SettledAsync()
    {
        await Eventually(() => _vm.Match.Team1.LogoPath is not null, "the logo is applied");
        await Eventually(() => _vm.Standings.Groups[0].Rows.Any(r => r.LogoPath.Length > 0), "the standings logo is applied");
        await Task.Delay(200); // the update that applied them is still publishing and saving on its own thread
    }

    [Fact]
    public async Task Linking_fills_the_standings_and_bracket_locks_their_tabs_and_lists_the_matches_with_no_choice_made()
    {
        _vm.Link.TournamentUrl = TournamentUrl;

        await _vm.Link.LinkCommand.ExecuteAsync(null);

        Assert.True(_vm.Link.IsLinked);
        Assert.True(_vm.Link.IsTournamentLinked);
        Assert.Equal("SOWDraft linked", _vm.Link.PillText);
        Assert.Same(LinkViewModel.NoMatch, _vm.Link.SelectedMatch);
        Assert.StartsWith("No match selected", _vm.Link.StatusText);
        Assert.Equal("", _vm.Match.Team1.Name);                              // nothing is filled before the user chooses
        Assert.Equal("Synthetic Cup", _vm.Standings.Title);
        Assert.Equal("League", _vm.Standings.Groups[0].Name);
        Assert.Equal("Alpha", _vm.Standings.Groups[0].Rows[0].TeamName);
        Assert.Equal("Synthetic Cup", _vm.Bracket.Title);
        Assert.True(_vm.Bracket.HasRounds);
        Assert.Single(_recorder.Published);                                   // sent at once (the link fills and sends)
    }

    [Fact]
    public async Task The_match_list_is_sorted_with_live_matches_first_and_finished_ones_last()
    {
        _vm.Link.TournamentUrl = TournamentUrl;
        await _vm.Link.LinkCommand.ExecuteAsync(null);

        var labels = _vm.Link.Matches.Select(m => m.Label).ToArray();

        Assert.Equal(LinkViewModel.NoMatch.Label, labels[0]);
        Assert.DoesNotContain(labels, l => l.Contains("bye"));                    // byes are not matches to follow
        Assert.Equal("Alpha vs Bravo – round 1 / completed", labels.Single(l => l.StartsWith("Alpha vs Bravo")));
        var lastCompleted = Array.FindLastIndex(labels, l => l.EndsWith("completed"));
        var firstPending = Array.FindIndex(labels, l => l.EndsWith("pending"));
        Assert.True(firstPending < lastCompleted);
        Assert.Contains(labels, l => l.StartsWith("TBD vs TBD"));
    }

    [Fact]
    public async Task Without_a_match_in_play_the_next_matches_come_first_and_the_finished_ones_after_them_newest_first()
    {
        _vm.Link.TournamentUrl = TournamentUrl;
        await _vm.Link.LinkCommand.ExecuteAsync(null);

        var ids = _vm.Link.Matches.Select(m => m.Id).Where(id => id is not null).ToArray();

        var completed = ids.Where(id => id is "m1" or "m2" or "m3").ToArray();
        Assert.Equal(new[] { "m1", "m3", "m2" }, completed);                      // m1 has a time; then the later round first
        Assert.True(Array.IndexOf(ids, "m4") < Array.IndexOf(ids, "m1"));          // pending before finished
    }

    [Fact]
    public async Task While_a_match_is_in_play_the_finished_ones_are_hidden_and_the_match_in_play_is_first()
    {
        _http.Set(Root, 200, LinkFixtures.LegacyTournament().Replace("\"winner_team_id\": \"d\", \"status\": \"pending\"", "\"winner_team_id\": \"d\", \"status\": \"in_progress\""));
        _vm.Link.TournamentUrl = TournamentUrl;
        await _vm.Link.LinkCommand.ExecuteAsync(null);

        var matches = _vm.Link.Matches.Where(m => m.Id is not null).ToArray();

        Assert.Equal("m4", matches[0].Id);
        Assert.DoesNotContain(matches, m => m.Label.EndsWith("completed"));
        Assert.NotEmpty(matches.Skip(1));                                          // the pending ones are still there
    }

    private const string ListTournament = """
    {
      "schemaVersion": 1, "version": 10, "updatedAt": 1,
      "tournament": { "id": "t-uuid", "slug": "synth", "name": "Synthetic Cup", "format": "single_elimination", "status": "live", "bestOf": 3 },
      "stages": [], "standings": null,
      "teams": [ { "id": "a", "name": "Alpha", "players": [] }, { "id": "b", "name": "Bravo", "players": [] } ],
      "matches": [
        { "id": "old", "bracket": "main", "round": 1, "matchNumber": 1, "team1Id": "a", "team2Id": "b", "status": "completed", "completedAt": 1000, "roundName": "Quarterfinals" },
        { "id": "new", "bracket": "main", "round": 2, "matchNumber": 1, "team1Id": "a", "team2Id": "b", "status": "completed", "completedAt": 5000, "roundName": "Semifinals" },
        { "id": "early", "bracket": "main", "round": 3, "matchNumber": 1, "team1Id": "a", "team2Id": "b", "status": "in_progress", "startedAt": 2000, "draftMatchId": "r1", "roundName": "Final" },
        { "id": "late", "bracket": "main", "round": 3, "matchNumber": 2, "team1Id": "b", "team2Id": "a", "status": "in_progress", "startedAt": 9000, "draftMatchId": "r2", "roundName": "Final" }
      ],
      "live": null
    }
    """;

    [Fact]
    public async Task The_list_names_the_rounds_puts_the_latest_started_match_first_and_hides_finished_matches_while_one_is_in_play()
    {
        _http.Set(Root + "/broadcast", 200, ListTournament);
        _vm.Link.TournamentUrl = TournamentUrl;
        await _vm.Link.LinkCommand.ExecuteAsync(null);

        var matches = _vm.Link.Matches.Where(m => m.Id is not null).ToArray();

        Assert.Equal(new[] { "late", "early" }, matches.Select(m => m.Id));
        Assert.Equal("● Bravo vs Alpha – Final / in progress", matches[0].Label);
    }

    [Fact]
    public async Task Finished_matches_are_listed_newest_first_with_their_round_names_when_nothing_is_in_play()
    {
        _http.Set(Root + "/broadcast", 200, ListTournament.Replace("\"status\": \"in_progress\"", "\"status\": \"pending\""));
        _vm.Link.TournamentUrl = TournamentUrl;
        await _vm.Link.LinkCommand.ExecuteAsync(null);

        var matches = _vm.Link.Matches.Where(m => m.Id is not null).ToArray();

        Assert.Equal(new[] { "early", "late", "new", "old" }, matches.Select(m => m.Id));   // pending first, then the finished ones newest first
        Assert.Equal("Alpha vs Bravo – Semifinals / completed", matches.Single(m => m.Id == "new").Label);
    }

    [Fact]
    public async Task Choosing_a_match_fills_the_match_tab_and_sends_it_to_the_overlays_at_once()
    {
        _vm.Link.TournamentUrl = TournamentUrl;
        await _vm.Link.LinkCommand.ExecuteAsync(null);
        var before = _recorder.Published.Count;

        await LinkAndSelectAsync();

        var match = _vm.Match;
        Assert.Equal(("Alpha", "ALP", "#112233"), (match.Team1.Name, match.Team1.Abbr, match.Team1.ColourHex));
        Assert.Equal(("OnePlayer", "Tank"), (match.Team1.Players[0].Name, match.Team1.Players[0].Role));
        Assert.Equal(("Bravo", 0), (match.Team2.Name, match.Team2.Score));
        Assert.Equal(1, match.Team1.Score);
        Assert.Equal(("Busan", 2, 1, true), (match.Maps[0].Map, match.Maps[0].T1, match.Maps[0].T2, match.Maps[0].Completed));
        Assert.Equal(("Midtown", true), (match.Maps[1].Map, match.Maps[1].Completed));
        Assert.False(match.Maps[2].Completed);
        Assert.True(_recorder.Published.Count > before);
        Assert.Equal("Alpha", _recorder.Published[^1].Team1.Name);
        Assert.False(_vm.HasUnsentChanges);
        Assert.Contains("Linked – Synthetic Cup – Alpha vs Bravo", _vm.Link.StatusText);
        Assert.Contains("last update", _vm.Link.StatusText);
    }

    [Fact]
    public async Task The_link_is_saved_with_the_state_so_a_restart_can_connect_again()
    {
        await LinkAndSelectAsync();
        await SettledAsync();
        _vm.Autosave();

        var saved = SowBroadcast.Core.Storage.StateStore.Load(Path.Combine(_dir, "autosave.json")).Link;

        Assert.Equal((TournamentUrl, "t-uuid", "m1", false), (saved.TournamentUrl, saved.TournamentId, saved.SelectedMatchId, saved.SidesSwapped));
        Assert.True(saved.IsLinked);
        Assert.NotEmpty(saved.Received);
    }

    [Fact]
    public async Task A_restart_takes_up_the_saved_link_and_match_again()
    {
        await LinkAndSelectAsync();
        await SettledAsync();
        _vm.Autosave();
        await _service.UnlinkAsync();

        var again = new MainWindowViewModel(_recorder, _recorder, _recorder, Path.Combine(_dir, "autosave.json"), null,
            linkService: _service, uiPost: action => action());
        await again.LoadAutosaveAsync();
        await again.RestoreLinkAsync();

        Assert.True(again.Link.IsLinked);
        Assert.Equal(TournamentUrl, again.Link.TournamentUrl);
        Assert.Equal("m1", again.Link.SelectedMatch!.Id);
        await Eventually(() => Socket.Joined("join-match") == 1, "the saved match's draft room is joined again");
    }

    [Fact]
    public async Task Swap_sides_puts_SOWDrafts_teams_in_the_other_panels_and_sends_it()
    {
        await LinkAndSelectAsync();
        var before = _recorder.Published.Count;

        _vm.Link.SidesSwapped = true;

        Assert.Equal(("Bravo", "Alpha"), (_vm.Match.Team1.Name, _vm.Match.Team2.Name));
        Assert.Equal((0, 1), (_vm.Match.Team1.Score, _vm.Match.Team2.Score));
        Assert.Equal((1, 2), (_vm.Match.Maps[0].T1, _vm.Match.Maps[0].T2));
        Assert.True(_recorder.Published.Count > before);
        Assert.True(_vm.State.Link.SidesSwapped);
    }

    [Fact]
    public async Task The_swap_teams_button_while_linked_is_the_same_as_swap_sides_and_swaps_back()
    {
        await LinkAndSelectAsync();

        _vm.SwapTeamsCommand.Execute(null);
        Assert.True(_vm.Link.SidesSwapped);
        Assert.Equal("Bravo", _vm.Match.Team1.Name);

        _vm.SwapTeamsCommand.Execute(null);
        Assert.False(_vm.Link.SidesSwapped);
        Assert.Equal("Alpha", _vm.Match.Team1.Name);
    }

    [Fact]
    public async Task A_manual_correction_survives_a_refetch_until_SOWDraft_changes_that_value()
    {
        await LinkAndSelectAsync();
        _vm.Match.Team1.Name = "Alpha (typed)";
        _vm.Match.Maps[0].T1 = 7;

        await _service.RefreshAsync();
        Assert.Equal("Alpha (typed)", _vm.Match.Team1.Name);
        Assert.Equal(7, _vm.Match.Maps[0].T1);

        // SOWDraft now has another score for the first map: that value is written, the typed team name still stays.
        Socket.Fire("update-state", LinkFixtures.DraftRoom.Replace("\"team1Score\": 2", "\"team1Score\": 3"));
        await Eventually(() => _vm.Match.Maps[0].T1 == 3, "the changed score overwrites the typed one");
        Assert.Equal("Alpha (typed)", _vm.Match.Team1.Name);
    }

    [Fact]
    public async Task Reset_while_linked_is_filled_in_again_at_once()
    {
        await LinkAndSelectAsync();

        _vm.ResetMatchCommand.Execute(null);

        Assert.Equal("Alpha", _vm.Match.Team1.Name);
        Assert.Equal("Busan", _vm.Match.Maps[0].Map);
    }

    [Fact]
    public async Task Unlinking_clears_the_map_cards_unlocks_the_tabs_and_sends_the_result_but_keeps_the_tournament_data()
    {
        await LinkAndSelectAsync();

        await _vm.Link.UnlinkCommand.ExecuteAsync(null);

        Assert.False(_vm.Link.IsLinked);
        Assert.False(_vm.Link.IsTournamentLinked);
        Assert.Equal("Not linked", _vm.Link.StatusText);
        Assert.All(_vm.Match.Maps, m => Assert.Equal("", m.Map ?? ""));
        Assert.Equal("Alpha", _vm.Match.Team1.Name);                      // the teams stay
        Assert.Equal("Alpha", _vm.Standings.Groups[0].Rows[0].TeamName);  // and so does the standings data
        Assert.False(_vm.State.Link.IsLinked);
        Assert.Equal("", _recorder.Published[^1].Maps[0].Map);
        Assert.Single(_vm.Link.Matches);
    }

    [Fact]
    public async Task Linking_with_nothing_or_a_bad_address_shows_a_message_and_does_not_link()
    {
        await _vm.Link.LinkCommand.ExecuteAsync(null);
        _vm.Link.TournamentUrl = "hello";
        await _vm.Link.LinkCommand.ExecuteAsync(null);
        _vm.Link.ManualUrl = Origin + "/stream";
        await _vm.Link.LinkManualCommand.ExecuteAsync(null);

        Assert.Equal(new[]
        {
            "Link Tournament: Paste a SOWDraft link first.",
            "Link Tournament: The link is not a valid URL.",
            "Link Draft Room: The link has no matchId parameter.",
        }, _recorder.Errors);
        Assert.False(_vm.Link.IsLinked);
    }

    [Fact]
    public async Task A_draft_room_linked_by_hand_fills_the_match_but_leaves_the_standings_and_bracket_alone()
    {
        _vm.Standings.Title = "Mine";
        _vm.Link.ManualUrl = Origin + "/stream?matchId=room-1";

        await _vm.Link.LinkManualCommand.ExecuteAsync(null);
        Socket.Fire("update-state", LinkFixtures.DraftRoom);
        await Eventually(() => _vm.Match.Maps[0].Map == "Busan", "the manual draft fills the cards");

        Assert.True(_vm.Link.IsLinked);
        Assert.False(_vm.Link.IsTournamentLinked);   // the other tabs stay editable
        Assert.Equal(("Alpha", "Bravo"), (_vm.Match.Team1.Name, _vm.Match.Team2.Name));
        Assert.Equal("Mine", _vm.Standings.Title);
        Assert.Equal(Origin + "/stream?matchId=room-1", _vm.State.Link.ManualDraftUrl);
    }

    [Fact]
    public async Task A_map_that_is_not_in_the_list_is_kept_by_name_and_warned_about()
    {
        _vm.Link.TournamentUrl = TournamentUrl;
        await _vm.Link.LinkCommand.ExecuteAsync(null);
        _vm.Link.SelectedMatch = _vm.Link.Matches.Single(m => m.Id == "m1");
        await Eventually(() => _sockets.Count > 0 && Socket.Joined("join-match") == 1, "joined");

        Socket.Fire("update-state", LinkFixtures.DraftRoom.Replace("\"Midtown\"", "\"Atlantis\""));
        await Eventually(() => _vm.Match.Maps[1].Map == "Atlantis", "the unknown map is shown");

        Assert.Contains("Atlantis", _vm.Link.WarningText);
    }

    [Fact]
    public async Task Team_logos_are_downloaded_and_put_on_the_team_afterwards()
    {
        await LinkAndSelectAsync();

        await Eventually(() => _vm.Match.Team1.LogoPath is not null, "the logo is downloaded and applied");

        Assert.StartsWith(Path.Combine(_dir, "TeamLogos"), _vm.Match.Team1.LogoPath);
        Assert.True(File.Exists(_vm.Match.Team1.LogoPath));
        await Eventually(() => _vm.Standings.Groups[0].Rows.Any(r => r.LogoPath.Length > 0), "the standings get the logo too");
    }

    /// <summary>The app's outward side in one fake: overlays, dialogs and messages.</summary>
    private sealed class Recorder : IOverlayPublisher, IFileDialogs, IMessages
    {
        public List<AppState> Published { get; } = new();
        public List<string> Errors { get; } = new();

        public string? BaseUrl => null;

        public PublishResult Publish(AppState state)
        {
            Published.Add(state);
            return new PublishResult(Array.Empty<string>(), Array.Empty<string>());
        }

        public PublishResult PublishPart(AppState state, PublishScope scope) => new(Array.Empty<string>(), Array.Empty<string>());
        public void MarkAssetsChanged(AssetCategory category) { }

        public int ReplayAnnouncements { get; private set; }

        public void AnnounceReplay() => ReplayAnnouncements++;

        public Task<string?> PickOpenStateFileAsync(string? initialDirectory) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveStateFileAsync(string? initialDirectory, string suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> PickOpenTeamFileAsync(string? initialDirectory) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveTeamFileAsync(string? initialDirectory, string suggestedName) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title, string? initialDirectory) => Task.FromResult<string?>(null);

        public Task ShowErrorAsync(string title, string message)
        {
            Errors.Add($"{title}: {message}");
            return Task.CompletedTask;
        }

        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }
}

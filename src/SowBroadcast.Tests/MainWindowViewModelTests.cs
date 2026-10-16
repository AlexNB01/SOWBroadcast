using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-vm-" + Guid.NewGuid().ToString("N"));
    private readonly FakeDialogs _dialogs = new();
    private readonly string _autosave;

    public MainWindowViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        _autosave = Path.Combine(_dir, "autosave.json");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private readonly FakePublisher _publisher = new();

    private MainWindowViewModel Create() => new(_dialogs, _dialogs, _publisher, _autosave);

    [Fact]
    public async Task Startup_restores_the_autosave()
    {
        StateStore.Save(_autosave, AppState.CreateDefault() with { Team1 = new Team { Name = "Restored" } });
        var vm = Create();

        await vm.LoadAutosaveAsync();

        Assert.Equal("Restored", vm.State.Team1.Name);
    }

    [Fact]
    public async Task Startup_with_a_corrupt_autosave_and_no_backup_starts_empty_and_reports_it()
    {
        File.WriteAllText(_autosave, "{ broken");
        var vm = Create();

        await vm.LoadAutosaveAsync();

        Assert.Equal("", vm.State.Team1.Name);
        Assert.Single(_dialogs.Errors);
    }

    [Fact]
    public async Task Save_without_a_current_file_asks_where_to_save_and_also_autosaves()
    {
        var target = Path.Combine(_dir, "match.sowbroadcast.json");
        _dialogs.SaveResult = target;
        var vm = Create();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.True(File.Exists(target));
        Assert.True(File.Exists(_autosave));
        Assert.Equal(1, _dialogs.SavePrompts);
    }

    [Fact]
    public async Task Second_save_reuses_the_current_file_without_asking_again()
    {
        var target = Path.Combine(_dir, "match.sowbroadcast.json");
        _dialogs.SaveResult = target;
        var vm = Create();
        await vm.SaveCommand.ExecuteAsync(null);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogs.SavePrompts);
    }

    [Fact]
    public async Task Cancelling_the_save_dialog_writes_nothing()
    {
        _dialogs.SaveResult = null;
        var vm = Create();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(File.Exists(_autosave));
    }

    [Fact]
    public async Task Load_replaces_the_state_and_updates_the_autosave()
    {
        var source = Path.Combine(_dir, "other.json");
        StateStore.Save(source, AppState.CreateDefault() with { Team2 = new Team { Name = "Loaded" } });
        _dialogs.OpenResult = source;
        var vm = Create();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Loaded", vm.State.Team2.Name);
        Assert.Equal("Loaded", StateStore.Load(_autosave).Team2.Name);
    }

    [Fact]
    public async Task Loading_an_invalid_file_keeps_the_current_state_and_shows_an_error()
    {
        var bad = Path.Combine(_dir, "bad.json");
        File.WriteAllText(bad, "not json");
        _dialogs.OpenResult = bad;
        var vm = Create();
        vm.State = vm.State with { Team1 = new Team { Name = "Keep me" } };

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Keep me", vm.State.Team1.Name);
        Assert.Single(_dialogs.Errors);
    }

    [Fact]
    public void Startup_publishes_the_restored_state_without_touching_the_status_line()
    {
        var vm = Create();
        vm.StatusText = "before";

        vm.PublishStartup();

        Assert.Single(_publisher.Published);
        Assert.Equal("before", vm.StatusText);
    }

    [Fact]
    public void Publish_now_sends_the_state_and_clears_the_unsent_marker()
    {
        var vm = Create();
        vm.HasUnsentChanges = true;

        vm.PublishNowCommand.Execute(null);

        Assert.False(vm.HasUnsentChanges);
        Assert.Contains("Published: maps", vm.StatusText);
    }

    [Fact]
    public async Task Loading_a_file_marks_the_state_as_unsent_and_does_not_publish()
    {
        var source = Path.Combine(_dir, "other.json");
        StateStore.Save(source, AppState.CreateDefault());
        _dialogs.OpenResult = source;
        var vm = Create();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.HasUnsentChanges);
        Assert.Empty(_publisher.Published);
    }

    // ---- Match tab actions -------------------------------------------------------------------------------

    [Fact]
    public void Editing_the_match_marks_unsent_changes_without_publishing()
    {
        var vm = Create();

        vm.Match.Team1.Name = "Typed";

        Assert.True(vm.HasUnsentChanges);
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public void Update_publishes_what_is_on_the_tab_clears_the_marker_and_autosaves()
    {
        var vm = Create();
        vm.Match.Team1.Name = "Typed";
        vm.Match.Team1.Score = 2;

        vm.UpdateMatchCommand.Execute(null);

        Assert.Equal("Typed", _publisher.Published.Single().Team1.Name);
        Assert.Equal(2, _publisher.Published.Single().Team1.Score);
        Assert.False(vm.HasUnsentChanges);
        Assert.Equal("Typed", StateStore.Load(_autosave).Team1.Name);
    }

    [Fact]
    public void Swap_changes_the_tab_marks_unsent_and_publishes_nothing()
    {
        var vm = Create();
        vm.Match.Team1.Name = "Home";
        vm.Match.Team2.Name = "Away";
        vm.HasUnsentChanges = false;

        vm.SwapTeamsCommand.Execute(null);

        Assert.Equal("Away", vm.Match.Team1.Name);
        Assert.Equal("Home", vm.Match.Team2.Name);
        Assert.True(vm.HasUnsentChanges);
        Assert.Empty(_publisher.Published);
        Assert.Equal("Away", StateStore.Load(_autosave).Team1.Name);
    }

    [Fact]
    public void Reset_clears_the_tab_but_not_the_format_bar_and_publishes_nothing()
    {
        var vm = Create();
        vm.Match.Team1.Name = "Home";
        vm.Match.MapsCount = 3;
        vm.Match.Maps[0].T1 = 2;

        vm.ResetMatchCommand.Execute(null);

        Assert.Equal("", vm.Match.Team1.Name);
        Assert.Equal(0, vm.Match.Maps[0].T1);
        Assert.Equal(3, vm.Match.MapsCount);
        Assert.True(vm.HasUnsentChanges);
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task Saving_includes_edits_that_were_never_sent()
    {
        var target = Path.Combine(_dir, "match.sowbroadcast.json");
        _dialogs.SaveResult = target;
        var vm = Create();
        vm.Match.Team2.Name = "Unsent";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Unsent", StateStore.Load(target).Team2.Name);
    }

    [Fact]
    public async Task Loading_a_file_shows_it_on_the_tab()
    {
        var source = Path.Combine(_dir, "other.json");
        StateStore.Save(source, AppState.CreateDefault() with { Team2 = new Team { Name = "Loaded", Score = 4 } });
        _dialogs.OpenResult = source;
        var vm = Create();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Loaded", vm.Match.Team2.Name);
        Assert.Equal(4, vm.Match.Team2.Score);
    }

    [Fact]
    public async Task Exporting_a_team_writes_its_file_without_the_score()
    {
        var target = Path.Combine(_dir, "teams", "home.sowteam.json");
        _dialogs.TeamSaveResult = target;
        var vm = Create();
        vm.Match.Team1.Name = "Northern Lights";
        vm.Match.Team1.Score = 3;

        await vm.ExportHomeCommand.ExecuteAsync(null);

        Assert.Equal("northern-lights.sowteam.json", _dialogs.LastSuggestedTeamName);
        var json = File.ReadAllText(target);
        Assert.Contains("Northern Lights", json);
        Assert.DoesNotContain("score", json);
    }

    [Fact]
    public async Task Importing_a_team_fills_one_panel_and_keeps_its_score()
    {
        var file = Path.Combine(_dir, "away.sowteam.json");
        File.WriteAllText(file, """{ "version": 1, "name": "Imported", "abbr": "IMP", "color_hex": "#112233", "players": [ { "name": "A", "hero": "", "role": "Tank" } ] }""");
        _dialogs.TeamOpenResult = file;
        var vm = Create();
        vm.Match.Team2.Score = 7;
        vm.Match.Team1.Name = "Untouched";
        vm.HasUnsentChanges = false;

        await vm.ImportAwayCommand.ExecuteAsync(null);

        Assert.Equal("Imported", vm.Match.Team2.Name);
        Assert.Equal("#112233", vm.Match.Team2.ColourHex);
        Assert.Equal(7, vm.Match.Team2.Score);
        Assert.Equal("Tank", vm.Match.Team2.Players[0].Role);
        Assert.Equal("Untouched", vm.Match.Team1.Name);
        Assert.True(vm.HasUnsentChanges);
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task Importing_a_broken_team_file_changes_nothing_and_shows_an_error()
    {
        var file = Path.Combine(_dir, "broken.json");
        File.WriteAllText(file, "nope");
        _dialogs.TeamOpenResult = file;
        var vm = Create();
        vm.Match.Team1.Name = "Keep";

        await vm.ImportHomeCommand.ExecuteAsync(null);

        Assert.Equal("Keep", vm.Match.Team1.Name);
        Assert.Single(_dialogs.Errors);
    }

    // ---- General and Waiting tabs ------------------------------------------------------------------------

    private long _now;
    private long _epoch = 1_760_000_000_000;

    private MainWindowViewModel CreateWithClock() =>
        new(_dialogs, _dialogs, _publisher, _autosave, defaultVideosDir: Path.Combine(_dir, "Highlights"), clock: () => _now, epochMs: () => _epoch);

    [Fact]
    public void Update_general_publishes_only_that_part_and_leaves_other_unsent_changes_marked()
    {
        var vm = CreateWithClock();
        vm.Match.Team1.Name = "Typed";
        vm.General.Host = "  Host  ";
        vm.General.StatusText = "Cup";

        vm.UpdateGeneralCommand.Execute(null);

        var (state, scope) = Assert.Single(_publisher.Parts);
        Assert.Equal(PublishScope.General, scope);
        Assert.Equal("Host", state.General.Host);
        Assert.Equal("Cup", state.General.StatusText);
        Assert.Empty(_publisher.Published);
        Assert.True(vm.HasUnsentChanges); // the Match tab still has changes the overlays do not
        Assert.Equal("Host", StateStore.Load(_autosave).General.Host);

        vm.UpdateMatchCommand.Execute(null);
        Assert.False(vm.HasUnsentChanges);
    }

    [Fact]
    public void The_marker_clears_when_the_last_tab_with_changes_is_updated()
    {
        var vm = CreateWithClock();
        vm.General.Caster1 = "Caster";
        Assert.True(vm.HasUnsentChanges);

        vm.UpdateGeneralCommand.Execute(null);

        Assert.False(vm.HasUnsentChanges);
    }

    [Fact]
    public void Update_waiting_normalises_the_socials_and_fills_empty_texts_with_the_defaults()
    {
        var vm = CreateWithClock();
        vm.Waiting.Socials.Single(s => s.Kind == "twitch").Text = "https://www.twitch.tv/SuomiOW?x=1";
        vm.Waiting.Socials.Single(s => s.Kind == "discord").Text = "discord.gg/abc";
        vm.Waiting.TextBrb = "   ";
        vm.Waiting.TickerOverride = " custom ";
        vm.Waiting.TickerOverrideEnabled = true;

        vm.UpdateWaitingCommand.Execute(null);

        var (state, scope) = Assert.Single(_publisher.Parts);
        Assert.Equal(PublishScope.Waiting, scope);
        Assert.Equal(new Dictionary<string, string> { ["twitch"] = "SuomiOW", ["discord"] = "abc" }, state.Waiting.Socials);
        Assert.Equal("BE RIGHT BACK!", state.Waiting.TextBrb);
        Assert.Equal("custom", state.Waiting.TickerOverride);
        Assert.True(state.Waiting.TickerOverrideEnabled);
    }

    [Fact]
    public void Timer_commands_reach_the_overlays_at_once_and_are_not_unsent_changes()
    {
        var vm = CreateWithClock();

        vm.Waiting.AdjustTimerCommand.Execute("60");
        vm.Waiting.StartTimerCommand.Execute(null);

        Assert.False(vm.HasUnsentChanges);
        Assert.Equal(2, _publisher.Parts.Count);
        Assert.All(_publisher.Parts, p => Assert.Equal(PublishScope.Timer, p.Scope));
        var running = _publisher.Parts[1].State.Waiting;
        Assert.Equal(60, running.TimerSeconds);
        Assert.True(running.TimerRunning);
        Assert.Equal("01:00", vm.Waiting.TimerText);
    }

    [Fact]
    public void A_running_countdown_carries_its_end_time_and_sends_nothing_while_it_counts()
    {
        _epoch = 1_760_000_000_000;
        var vm = CreateWithClock();
        vm.Waiting.AdjustTimerCommand.Execute("30");
        vm.Waiting.AdjustTimerCommand.Execute("-25"); // 5 s
        vm.Waiting.StartTimerCommand.Execute(null);

        var started = _publisher.Parts[^1].State.Waiting;
        Assert.True(started.TimerRunning);
        Assert.Equal(1_760_000_005_000, started.TimerEndsAtMs);   // the overlays count down to this themselves
        _publisher.Parts.Clear();

        _now = 1_000;
        _epoch += 1_000;
        vm.Waiting.Tick();
        Assert.Empty(_publisher.Parts);                           // no write or message each second
        Assert.Equal("00:04", vm.Waiting.TimerText);
    }

    [Fact]
    public void When_the_countdown_runs_out_the_stop_is_announced()
    {
        var vm = CreateWithClock();
        vm.Waiting.AdjustTimerCommand.Execute("5");
        vm.Waiting.StartTimerCommand.Execute(null);
        _publisher.Parts.Clear();

        _now = 5_000;
        vm.Waiting.Tick();

        var end = Assert.Single(_publisher.Parts);
        Assert.Equal(PublishScope.Timer, end.Scope);
        Assert.Equal((0, false, 0L), (end.State.Waiting.TimerSeconds, end.State.Waiting.TimerRunning, end.State.Waiting.TimerEndsAtMs));
        Assert.False(vm.Waiting.IsRunning);
    }

    [Fact]
    public void A_paused_or_stopped_countdown_has_no_end_time()
    {
        var vm = CreateWithClock();
        vm.Waiting.AdjustTimerCommand.Execute("60");
        vm.Waiting.StartTimerCommand.Execute(null);

        vm.Waiting.PauseTimerCommand.Execute(null);

        var paused = _publisher.Parts[^1].State.Waiting;
        Assert.Equal((false, 0L, 60), (paused.TimerRunning, paused.TimerEndsAtMs, paused.TimerSeconds));
    }

    [Fact]
    public void Pausing_a_stopped_timer_sends_nothing()
    {
        var vm = CreateWithClock();

        vm.Waiting.PauseTimerCommand.Execute(null);

        Assert.Empty(_publisher.Parts);
    }

    [Fact]
    public void Swapping_teams_does_not_disturb_a_running_countdown()
    {
        var vm = CreateWithClock();
        vm.Waiting.AdjustTimerCommand.Execute("120");
        vm.Waiting.StartTimerCommand.Execute(null);

        vm.SwapTeamsCommand.Execute(null);
        vm.ResetMatchCommand.Execute(null);

        Assert.True(vm.Waiting.IsRunning);
        Assert.True(vm.Waiting.Timer.IsRunning);
    }

    [Fact]
    public void Reset_general_keeps_the_match_format_marks_unsent_and_sends_nothing()
    {
        var vm = CreateWithClock();
        vm.Match.MapsCount = 3;
        vm.General.Host = "Host";
        vm.General.Colours[0].Hex = "#123456";
        vm.HasUnsentChanges = false;

        vm.ResetGeneralCommand.Execute(null);

        Assert.Equal("", vm.General.Host);
        Assert.Equal("#FFFFFF", vm.General.Colours[0].Hex);
        Assert.Equal(3, vm.Match.MapsCount);
        Assert.True(vm.HasUnsentChanges);
        Assert.Empty(_publisher.Parts);
        Assert.Equal(3, StateStore.Load(_autosave).General.MapsCount);
    }

    [Fact]
    public void Reset_waiting_restores_the_defaults_but_keeps_the_socials()
    {
        var vm = CreateWithClock();
        vm.Waiting.Socials.Single(s => s.Kind == "twitch").Text = "SuomiOW";
        vm.Waiting.TextStarting = "Soon";
        vm.Waiting.TickerOverride = "x";
        vm.Waiting.TickerOverrideEnabled = true;
        vm.Waiting.VideosDir = "C:\\Videos";
        vm.Waiting.UseDefaultVideos = false;
        vm.Waiting.AdjustTimerCommand.Execute("60");
        vm.Waiting.StartTimerCommand.Execute(null);
        _publisher.Parts.Clear();

        vm.ResetWaitingCommand.Execute(null);

        Assert.Equal("STARTING SOON!", vm.Waiting.TextStarting);
        Assert.Equal("THANK YOU FOR WATCHING", vm.Waiting.TextEnd);
        Assert.True(vm.Waiting.UseDefaultVideos);
        Assert.Equal("", vm.Waiting.VideosDir);
        Assert.False(vm.Waiting.TickerOverrideEnabled);
        Assert.Equal("SuomiOW", vm.Waiting.Socials.Single(s => s.Kind == "twitch").Text);
        Assert.False(vm.Waiting.IsRunning);
        Assert.Equal("01:00", vm.Waiting.TimerText);
        // The tab reset sends nothing, but a countdown it stopped is stopped for the overlays too.
        var part = Assert.Single(_publisher.Parts);
        Assert.Equal(PublishScope.Timer, part.Scope);
        Assert.False(part.State.Waiting.TimerRunning);
    }

    [Fact]
    public async Task Browsing_for_a_video_folder_switches_off_the_default_and_the_default_clears_the_path()
    {
        var vm = CreateWithClock();
        _dialogs.FolderResult = Path.Combine(_dir, "Clips");

        await vm.Waiting.BrowseVideosCommand.ExecuteAsync(null);

        Assert.False(vm.Waiting.UseDefaultVideos);
        Assert.Equal(Path.Combine(_dir, "Clips"), vm.Waiting.VideosFolderText);
        Assert.Equal(Path.Combine(_dir, "Clips"), vm.Waiting.ApplyTo(vm.State).Waiting.VideosDir);

        vm.Waiting.UseDefaultVideos = true;
        Assert.Equal("", vm.Waiting.VideosDir);
        Assert.Equal(Path.Combine(_dir, "Highlights"), vm.Waiting.VideosFolderText);
        Assert.Equal("", vm.Waiting.ApplyTo(vm.State).Waiting.VideosDir);
    }

    [Fact]
    public async Task The_tabs_survive_a_restart_and_a_saved_countdown_comes_back_stopped()
    {
        var vm = CreateWithClock();
        vm.General.Caster2 = "Caster";
        vm.General.Colours[2].Hex = "#abcdef";
        vm.Waiting.TextEnd = "Bye";
        vm.Waiting.Socials.Single(s => s.Kind == "web").Text = "https://suomiow.fi/x";
        vm.Waiting.AdjustTimerCommand.Execute("90");
        vm.Waiting.StartTimerCommand.Execute(null);
        vm.Autosave();

        var again = CreateWithClock();
        await again.LoadAutosaveAsync();

        Assert.Equal("Caster", again.General.Caster2);
        Assert.Equal("#abcdef", again.General.Colours[2].Hex);
        Assert.Equal("Bye", again.Waiting.TextEnd);
        Assert.Equal("suomiow.fi", again.Waiting.Socials.Single(s => s.Kind == "web").Text);
        Assert.Equal("01:30", again.Waiting.TimerText);
        Assert.False(again.Waiting.IsRunning);
    }

    [Fact]
    public async Task Loading_a_file_shows_the_general_and_waiting_settings_without_marking_them_edited()
    {
        var source = Path.Combine(_dir, "other.json");
        StateStore.Save(source, AppState.CreateDefault() with
        {
            General = new GeneralSettings { Host = "Loaded host" },
            Waiting = new WaitingSettings { TextStarting = "Loaded text" },
        });
        _dialogs.OpenResult = source;
        var vm = CreateWithClock();

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Loaded host", vm.General.Host);
        Assert.Equal("Loaded text", vm.Waiting.TextStarting);
        Assert.Empty(_publisher.Parts);
    }

    // ---- Standings and Bracket tabs ----------------------------------------------------------------------

    [Fact]
    public void Update_and_sort_sorts_the_tables_and_publishes_only_the_standings()
    {
        var vm = CreateWithClock();
        var rows = vm.Standings.Groups[0].Rows;
        rows[0].TeamName = "low";
        rows[0].Points = 1;
        vm.Standings.Groups[0].AddRowCommand.Execute(null);
        rows[1].TeamName = "high";
        rows[1].Points = 9;
        vm.Match.Team1.Name = "Typed";

        vm.UpdateStandingsCommand.Execute(null);

        var (state, scope) = Assert.Single(_publisher.Parts);
        Assert.Equal(PublishScope.Standings, scope);
        Assert.Equal(new[] { "high", "low" }, state.Standings.Groups[0].Rows.Select(r => r.TeamName));
        Assert.Equal(new[] { 1, 2 }, state.Standings.Groups[0].Rows.Select(r => r.Rank));
        Assert.Equal(new[] { "high", "low" }, vm.Standings.Groups[0].Rows.Select(r => r.TeamName)); // the table on screen is sorted too
        Assert.True(vm.HasUnsentChanges); // the Match tab is still unsent
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public void Update_bracket_publishes_only_the_bracket_and_clears_its_marker()
    {
        var vm = CreateWithClock();
        vm.Bracket.SelectedTemplate = "4 team single elimination";
        vm.Bracket.LoadTemplateCommand.Execute(null);
        vm.Bracket.Title = "Playoffs";
        Assert.True(vm.HasUnsentChanges);

        vm.UpdateBracketCommand.Execute(null);

        var (state, scope) = Assert.Single(_publisher.Parts);
        Assert.Equal(PublishScope.Bracket, scope);
        Assert.Equal("Playoffs", state.Bracket.Title);
        Assert.Equal(2, state.Bracket.Rounds.Count);
        Assert.False(vm.HasUnsentChanges);
    }

    [Fact]
    public void Resetting_the_standings_or_bracket_tab_sends_nothing_and_marks_unsent()
    {
        var vm = CreateWithClock();
        vm.Standings.Title = "Cup";
        vm.Bracket.Title = "Playoffs";
        vm.HasUnsentChanges = false;

        vm.ResetStandingsCommand.Execute(null);
        vm.ResetBracketCommand.Execute(null);

        Assert.Equal("", vm.Standings.Title);
        Assert.Equal("", vm.Bracket.Title);
        Assert.True(vm.HasUnsentChanges);
        Assert.Empty(_publisher.Parts);
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task The_bracket_can_import_the_teams_typed_into_the_standings()
    {
        var vm = CreateWithClock();
        vm.Standings.Groups[0].Rows[0].TeamName = "Gamma";
        vm.Standings.Groups[0].Rows[0].Points = 3;
        vm.Standings.Groups[0].AddRowCommand.Execute(null);
        vm.Standings.Groups[0].Rows[1].TeamName = "Alpha";
        vm.Standings.Groups[0].Rows[1].Points = 9;

        await vm.Bracket.ImportFromStandingsCommand.ExecuteAsync(null);

        // No picker in this view model: every team is taken, unranked ones by name.
        Assert.Equal(new[] { "Alpha", "Gamma" }, vm.Bracket.Teams.Take(2).Select(t => t.Name));
    }

    [Fact]
    public async Task Standings_and_bracket_survive_a_restart()
    {
        var vm = CreateWithClock();
        vm.Standings.Title = "Cup";
        vm.Standings.Groups[0].Rows[0].TeamName = "Alpha";
        vm.Standings.Groups[0].Rows[0].Points = 4;
        vm.Bracket.SelectedTemplate = "8 team double elimination";
        vm.Bracket.LoadTemplateCommand.Execute(null);
        vm.Bracket.Stage = "Finals";
        vm.Autosave();

        var again = CreateWithClock();
        await again.LoadAutosaveAsync();

        Assert.Equal("Cup", again.Standings.Title);
        Assert.Equal("Alpha", again.Standings.Groups[0].Rows[0].TeamName);
        Assert.Equal("Finals", again.Bracket.Stage);
        Assert.True(again.Bracket.ShowDoubleView);
        Assert.Equal(3, again.Bracket.Sections.Count);
        Assert.False(again.HasUnsentChanges); // restoring is not an edit
    }

    internal sealed class FakePublisher : IOverlayPublisher
    {
        public string? BaseUrl => "http://127.0.0.1:8324";
        public List<AppState> Published { get; } = new();

        public PublishResult Publish(AppState state)
        {
            Published.Add(state);
            return new PublishResult(new[] { "maps" }, Array.Empty<string>());
        }

        public List<(AppState State, PublishScope Scope)> Parts { get; } = new();

        public PublishResult PublishPart(AppState state, PublishScope scope)
        {
            Parts.Add((state, scope));
            return new PublishResult(new[] { "part" }, Array.Empty<string>());
        }

        public void MarkAssetsChanged(SowBroadcast.Core.Assets.AssetCategory category) { }

        public int ReplayAnnouncements { get; private set; }

        public void AnnounceReplay() => ReplayAnnouncements++;
    }

    internal sealed class FakeDialogs : IFileDialogs, IMessages
    {
        public string? OpenResult { get; set; }
        public string? SaveResult { get; set; }
        public int SavePrompts { get; private set; }
        public List<string> Errors { get; } = new();

        public Task<string?> PickOpenStateFileAsync(string? initialDirectory) => Task.FromResult(OpenResult);

        public Task<string?> PickSaveStateFileAsync(string? initialDirectory, string suggestedName)
        {
            SavePrompts++;
            return Task.FromResult(SaveResult);
        }

        public string? TeamOpenResult { get; set; }
        public string? TeamSaveResult { get; set; }
        public string? LastSuggestedTeamName { get; private set; }

        public string? FolderResult { get; set; }

        public Task<string?> PickFolderAsync(string title, string? initialDirectory) => Task.FromResult(FolderResult);

        public Task<string?> PickOpenTeamFileAsync(string? initialDirectory) => Task.FromResult(TeamOpenResult);

        public Task<string?> PickSaveTeamFileAsync(string? initialDirectory, string suggestedName)
        {
            LastSuggestedTeamName = suggestedName;
            return Task.FromResult(TeamSaveResult);
        }

        public Task ShowErrorAsync(string title, string message)
        {
            Errors.Add(title + ": " + message);
            return Task.CompletedTask;
        }

        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }
}

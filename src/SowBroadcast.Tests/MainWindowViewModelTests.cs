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

    private sealed class FakePublisher : IOverlayPublisher
    {
        public string? BaseUrl => "http://127.0.0.1:8324";
        public List<AppState> Published { get; } = new();

        public PublishResult Publish(AppState state)
        {
            Published.Add(state);
            return new PublishResult(new[] { "maps" }, Array.Empty<string>());
        }

        public void MarkAssetsChanged(SowBroadcast.Core.Assets.AssetCategory category) { }
    }

    private sealed class FakeDialogs : IFileDialogs, IMessages
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

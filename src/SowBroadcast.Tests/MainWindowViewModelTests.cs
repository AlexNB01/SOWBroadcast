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

        public Task ShowErrorAsync(string title, string message)
        {
            Errors.Add(title + ": " + message);
            return Task.CompletedTask;
        }

        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }
}

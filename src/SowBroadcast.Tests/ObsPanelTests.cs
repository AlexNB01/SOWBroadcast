using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Control;
using SowBroadcast.Core.Obs;
using SowBroadcast.Integrations.Obs;

namespace SowBroadcast.Tests;

public sealed class ObsSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-obs-" + Guid.NewGuid().ToString("N"));

    public ObsSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    // Not real protection: it only has to show that the password is never stored as it was typed.
    internal sealed class ReversingProtector : ISecretProtector
    {
        public string Protect(string plain) => "P:" + new string(plain.Reverse().ToArray());
        public string? Unprotect(string protectedText) => protectedText.StartsWith("P:") ? new string(protectedText[2..].Reverse().ToArray()) : null;
    }

    [Fact]
    public void Settings_survive_saving_and_the_password_is_stored_protected_only()
    {
        var path = Path.Combine(_dir, "obs.json");
        var settings = new ObsSettings { Host = "192.168.1.20", Port = 4466, AutoConnect = true };
        settings.SetPassword("s3cret", new ReversingProtector());
        settings.Save(path);

        var again = ObsSettings.Load(path);

        Assert.Equal("192.168.1.20", again.Host);
        Assert.Equal(4466, again.Port);
        Assert.True(again.AutoConnect);
        Assert.Equal("s3cret", again.GetPassword(new ReversingProtector()));
        Assert.DoesNotContain("s3cret", File.ReadAllText(path));
        again.SetPassword("", new ReversingProtector());
        Assert.Null(again.GetPassword(new ReversingProtector()));
    }

    [Fact]
    public void A_missing_or_damaged_file_gives_the_defaults_and_nonsense_values_are_repaired()
    {
        var path = Path.Combine(_dir, "obs.json");
        Assert.Equal(4455, ObsSettings.Load(path).Port);

        File.WriteAllText(path, "{ nope");
        Assert.Equal("localhost", ObsSettings.Load(path).Host);

        File.WriteAllText(path, "{ \"host\": \"  \", \"port\": 0 }");
        var repaired = ObsSettings.Load(path);
        Assert.Equal("localhost", repaired.Host);
        Assert.Equal(4455, repaired.Port);
    }

    [Fact]
    public void A_password_that_cannot_be_unprotected_is_no_password()
    {
        var settings = new ObsSettings { ProtectedPassword = "from another computer" };

        Assert.Null(settings.GetPassword(new ReversingProtector()));
    }
}

public sealed class ObsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-obsvm-" + Guid.NewGuid().ToString("N"));
    private readonly MainWindowViewModelTests.FakeDialogs _dialogs = new();
    private readonly MainWindowViewModelTests.FakePublisher _publisher = new();
    private FakeObsSocket _socket = new();
    private string? _saveTarget;

    public ObsViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private ObsViewModel Create(ObsSettings? settings = null)
    {
        _socket.Handlers["GetSceneList"] = _ => new
        {
            currentProgramSceneName = "In Game",
            scenes = new[] { new { sceneName = "In Game", sceneIndex = 0 }, new { sceneName = "Starting Soon", sceneIndex = 1 } },
        };
        _socket.Handlers["GetReplayBufferStatus"] = _ => new { outputActive = false };
        _socket.Handlers["SetCurrentProgramScene"] = data => data.GetProperty("sceneName").GetString() == "Nowhere" ? throw new ObsException("No scene was found by that name.", 600) : null;
        _socket.Handlers["StartReplayBuffer"] = _ => null;
        _socket.Handlers["StopReplayBuffer"] = _ => null;
        _socket.Handlers["SaveReplayBuffer"] = _ => null;

        var controller = new ObsController(new ObsClient(() => _socket));
        return new ObsViewModel(controller, settings ?? new ObsSettings(), Path.Combine(_dir, "obs.json"), new ObsSettingsTests.ReversingProtector(),
            () => new ChecklistInput("http://127.0.0.1:8324", new[] { "In Game" }, new[] { "scoreboard.html" }, "C:/Replay"),
            () => "http://127.0.0.1:8324", "C:/Test/Install", "C:/Test/Data", action => action(),
            (_, _) => Task.FromResult(_saveTarget));
    }

    [Fact]
    public async Task Connecting_shows_the_scenes_with_the_current_one_marked_and_keeps_the_settings_with_a_protected_password()
    {
        var vm = Create();
        vm.Host = "localhost";
        vm.Password = "s3cret";
        vm.AutoConnect = true;

        await vm.ConnectAsync();

        Assert.True(vm.IsConnected);
        Assert.Contains("OBS Studio 30.2.1", vm.Status);
        Assert.Equal(new[] { "Starting Soon", "In Game" }, vm.Scenes.Select(s => s.Name));
        Assert.True(vm.Scenes.Single(s => s.Name == "In Game").IsCurrent);
        Assert.Equal("Replay buffer stopped", vm.ReplayText);
        var saved = ObsSettings.Load(Path.Combine(_dir, "obs.json"));
        Assert.True(saved.AutoConnect);
        Assert.DoesNotContain("s3cret", File.ReadAllText(Path.Combine(_dir, "obs.json")));
        Assert.NotEqual("s3cret", vm.Password);                                           // the box does not show the password again
    }

    [Fact]
    public async Task A_failed_connection_says_why_and_changes_no_saved_settings()
    {
        _socket = new FakeObsSocket("right");
        var vm = Create();
        vm.Password = "wrong";

        await vm.ConnectAsync();

        Assert.False(vm.IsConnected);
        Assert.Contains("refused the password", vm.Message);
        Assert.False(File.Exists(Path.Combine(_dir, "obs.json")));
    }

    [Fact]
    public async Task The_stored_password_is_used_when_the_box_still_shows_the_placeholder()
    {
        _socket = new FakeObsSocket("right");
        var settings = new ObsSettings();
        settings.SetPassword("right", new ObsSettingsTests.ReversingProtector());
        var vm = Create(settings);
        Assert.NotEmpty(vm.Password);
        Assert.NotEqual("right", vm.Password);

        await vm.ConnectAsync();

        Assert.True(vm.IsConnected);
    }

    [Fact]
    public async Task Auto_connect_is_only_tried_when_asked_for()
    {
        var off = Create();
        await off.ConnectAtStartupAsync();
        Assert.False(off.IsConnected);

        _socket = new FakeObsSocket();
        var on = Create(new ObsSettings { AutoConnect = true });
        await on.ConnectAtStartupAsync();
        Assert.True(on.IsConnected);
    }

    [Fact]
    public async Task Buttons_do_their_actions_and_say_in_words_what_happened()
    {
        var vm = Create();
        await vm.ConnectAsync();

        Assert.True((await vm.SwitchToSceneAsync("Starting Soon")).Ok);
        Assert.Equal("OBS scene: Starting Soon", vm.Message);
        var refused = await vm.SwitchToSceneAsync("Nowhere");
        Assert.False(refused.Ok);
        Assert.Contains("No scene", vm.Message);

        var early = await vm.SaveReplayNowAsync();
        Assert.False(early.Ok);                                                            // the buffer is not running yet
        Assert.Contains("not running", early.Message);
        Assert.True((await vm.StartReplayAsync()).Ok);
        Assert.True(vm.ReplayRunning);
        Assert.True((await vm.SaveReplayNowAsync()).Ok);
        Assert.True((await vm.ToggleReplayBufferAsync()).Ok);                              // running: this stops it
        Assert.False(vm.ReplayRunning);
    }

    [Fact]
    public async Task Without_a_connection_actions_say_so_and_the_checklist_shows_marked_lines()
    {
        var vm = Create();
        var none = await vm.SwitchToSceneAsync("In Game");
        Assert.False(none.Ok);
        Assert.Equal("Not connected to OBS.", vm.Message);

        await vm.ConnectAsync();
        _socket.Handlers["GetInputList"] = _ => new { inputs = Array.Empty<object>() };
        _socket.Handlers["GetRecordDirectory"] = _ => new { recordDirectory = "D:/Other" };
        await ((IAsyncRelayCommand)vm.CheckCommand).ExecuteAsync(null);

        Assert.Contains(vm.CheckResults, c => c.Title == "OBS" && c.Glyph == "✔");
        Assert.Contains(vm.CheckResults, c => c.Title == "Replay folder" && c.Glyph == "!");
        Assert.StartsWith("Go-live check", vm.Message);
    }

    [Fact]
    public async Task The_scene_collection_is_written_for_this_installation_and_a_cancelled_dialog_writes_nothing()
    {
        var vm = Create();
        _saveTarget = Path.Combine(_dir, "collection.json");

        await ((IAsyncRelayCommand)vm.ExportCollectionCommand).ExecuteAsync(null);

        var json = File.ReadAllText(_saveTarget);
        Assert.Contains("http://127.0.0.1:8324/HTML/scoreboard.html", json);
        Assert.Contains("C:/Test/Data/Scoreboard/General/caster1.txt", json);
        Assert.Contains("Scene collection saved", vm.Message);

        File.Delete(_saveTarget);
        _saveTarget = null;   // the user cancels the dialog
        await ((IAsyncRelayCommand)vm.ExportCollectionCommand).ExecuteAsync(null);
        Assert.False(File.Exists(Path.Combine(_dir, "collection.json")));
    }

    [Fact]
    public async Task Obs_commands_run_through_the_control_interface_with_a_scene_name_and_report_failures()
    {
        var vm = Create();
        await vm.ConnectAsync();
        var main = new MainWindowViewModel(_dialogs, _dialogs, _publisher, Path.Combine(_dir, "autosave.json")) { Obs = vm };

        var switched = await main.RunControlAsync(ControlCatalog.ObsScene, new Dictionary<string, string> { ["name"] = "Starting Soon" });
        var noName = await main.RunControlAsync(ControlCatalog.ObsScene);
        var bad = await main.RunControlAsync(ControlCatalog.ObsScene, new Dictionary<string, string> { ["name"] = "Nowhere" });
        var saveEarly = await main.RunControlAsync(ControlCatalog.ObsReplaySave);
        var toggle = await main.RunControlAsync(ControlCatalog.ObsReplayToggle);
        var save = await main.RunControlAsync(ControlCatalog.ObsReplaySave);
        var plain = await main.RunControlAsync(ControlCatalog.Team1ScoreUp);

        Assert.True(switched.Ok);
        Assert.False(noName.Ok);
        Assert.False(bad.Ok);
        Assert.False(saveEarly.Ok);
        Assert.True(toggle.Ok);
        Assert.True(save.Ok);
        Assert.Equal(1, plain.Team1Score);                                                 // the other commands still work through the same door
        var without = new MainWindowViewModel(_dialogs, _dialogs, _publisher, Path.Combine(_dir, "other.json"));
        Assert.False((await without.RunControlAsync(ControlCatalog.ObsReplaySave)).Ok);
    }

    [Fact]
    public void The_obs_commands_are_in_the_catalogue_with_a_default_hotkey_for_saving_a_replay()
    {
        Assert.True(ControlCatalog.IsObs("obs.scene"));
        Assert.False(ControlCatalog.IsObs("swap"));
        Assert.Equal("Ctrl+Alt+R", ControlCatalog.Find(ControlCatalog.ObsReplaySave)!.DefaultGesture);
        Assert.Empty(new ControlSettings().Problems());
    }
}

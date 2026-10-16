using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.History;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Profiles;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-profiles-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 16, 20, 0, 0);

    public ProfileStoreTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private ProfileStore Store() => new(Path.Combine(_tmp, "profiles"), () => _now);

    private string File_(string name, byte fill = 1)
    {
        var path = Path.Combine(_tmp, name);
        File.WriteAllBytes(path, Enumerable.Repeat(fill, 16).ToArray());
        return path;
    }

    private AppState Setup(string status = "ESM Finals", string? overlayLogo = null) => AppState.CreateDefault() with
    {
        General = new GeneralSettings { StatusText = status, Host = "Host", MapsCount = 5, OverlayLogoPath = overlayLogo },
        Standings = new StandingsSettings { Title = "Group stage" },
        Team1 = new Team { Name = "Alpha", Score = 2 },
        Waiting = new WaitingSettings { TextStarting = "SOON", TimerSeconds = 120, TimerRunning = true, TimerEndsAtMs = 99 },
        Link = new LinkSettings { TournamentUrl = "https://sowdraft.fi/tournament/esm", TournamentId = "abc", SelectedMatchId = "m1", Received = new Dictionary<string, string> { ["x"] = "y" } },
    };

    [Fact]
    public void A_profile_keeps_the_setup_but_not_the_countdown_or_the_running_link()
    {
        var store = Store();

        var info = store.Save("ESM Finals", Setup());
        var loaded = store.Load(info.Id);

        Assert.Equal("ESM Finals", loaded.General.StatusText);
        Assert.Equal(5, loaded.General.MapsCount);
        Assert.Equal("Group stage", loaded.Standings.Title);
        Assert.Equal("Alpha", loaded.Team1.Name);
        Assert.Equal("SOON", loaded.Waiting.TextStarting);
        Assert.Equal(0, loaded.Waiting.TimerSeconds);
        Assert.False(loaded.Waiting.TimerRunning);
        Assert.Equal("https://sowdraft.fi/tournament/esm", loaded.Link.TournamentUrl);   // the address stays
        Assert.Equal("", loaded.Link.TournamentId);                                       // the connection does not
        Assert.Empty(loaded.Link.Received);
        Assert.Contains("ESM Finals", info.Summary);
        Assert.Contains("Alpha", info.Summary);
        Assert.Equal(store.ActiveId, info.Id);
    }

    [Fact]
    public void Logos_are_copied_into_the_profile_and_survive_the_originals()
    {
        var store = Store();
        var original = File_("logo.png", 7);
        var info = store.Save("Cup", Setup(overlayLogo: original) with { Team1 = new Team { Name = "A", LogoPath = File_("t1.png", 9) } });
        File.Delete(original);

        var loaded = store.Load(info.Id);

        Assert.StartsWith(Path.Combine(_tmp, "profiles", info.Id), loaded.General.OverlayLogoPath);
        Assert.Equal(Enumerable.Repeat((byte)7, 16).ToArray(), File.ReadAllBytes(loaded.General.OverlayLogoPath!));
        Assert.True(File.Exists(loaded.Team1.LogoPath));
        Assert.Null(loaded.General.TransitionLogoPath);
    }

    [Fact]
    public void Saving_again_under_the_same_name_replaces_the_profile_and_leaves_no_stale_logo()
    {
        var store = Store();
        var first = store.Save("Cup", Setup(overlayLogo: File_("a.png")));
        _now = _now.AddHours(1);

        var second = store.Save(" cup ", Setup("Changed", overlayLogo: null));

        Assert.Equal(first.Id, second.Id);
        Assert.Single(store.List());
        Assert.Equal("Changed", store.Load(second.Id).General.StatusText);
        Assert.Null(store.Load(second.Id).General.OverlayLogoPath);
        Assert.Empty(Directory.GetFiles(Path.Combine(store.Folder, second.Id, "logos")));
    }

    [Fact]
    public void Profiles_are_listed_by_name_and_deleting_one_removes_its_folder_and_clears_the_active_mark()
    {
        var store = Store();
        var b = store.Save("Beta cup", Setup());
        store.Save("Alpha cup", Setup());
        Assert.Equal(new[] { "Alpha cup", "Beta cup" }, store.List().Select(p => p.Name));
        store.SetActive(b.Id);

        Assert.True(store.Delete(b.Id));

        Assert.Equal(new[] { "Alpha cup" }, store.List().Select(p => p.Name));
        Assert.False(Directory.Exists(Path.Combine(store.Folder, b.Id)));
        Assert.Equal("", store.ActiveId);
        Assert.False(store.Delete(b.Id));
    }

    [Fact]
    public void A_name_is_required_a_missing_profile_cannot_be_loaded_and_a_damaged_one_is_skipped()
    {
        var store = Store();
        Assert.Throws<ArgumentException>(() => store.Save("  ", Setup()));
        Assert.Throws<StateLoadException>(() => store.Load("nope"));

        var good = store.Save("Good", Setup());
        Directory.CreateDirectory(Path.Combine(store.Folder, "broken"));
        File.WriteAllText(Path.Combine(store.Folder, "broken", "profile.json"), "{ nope");

        Assert.Equal(good.Id, Assert.Single(store.List()).Id);
    }

    [Fact]
    public void A_profile_without_a_match_says_it_is_settings_only()
    {
        var info = Store().Save("Just settings", AppState.CreateDefault());

        Assert.Equal("settings only", info.Summary);
    }
}

public sealed class ProfileViewModelTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-profvm-" + Guid.NewGuid().ToString("N"));
    private readonly MainWindowViewModelTests.FakeDialogs _dialogs = new();
    private readonly MainWindowViewModelTests.FakePublisher _publisher = new();
    private long _timerNow;

    public ProfileViewModelTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private MainWindowViewModel Create() => new(_dialogs, _dialogs, _publisher, Path.Combine(_tmp, "autosave.json"),
        defaultVideosDir: Path.Combine(_tmp, "Highlights"), clock: () => _timerNow, epochMs: () => 1_760_000_000_000 + _timerNow,
        archive: new MatchArchive(Path.Combine(_tmp, "History")), profiles: new ProfileStore(Path.Combine(_tmp, "Profiles")));

    private static void FillEvent(MainWindowViewModel vm, string status)
    {
        vm.General.StatusText = status;
        vm.General.Host = "Host " + status;
        vm.Waiting.TextStarting = "SOON " + status;
        vm.Standings.Title = "Standings " + status;
    }

    [Fact]
    public void Settings_only_changes_the_event_and_keeps_the_match_on_screen()
    {
        var vm = Create();
        FillEvent(vm, "Weekly");
        vm.Match.Team1.Name = "Alpha";
        vm.SaveProfile("Weekly cup");
        FillEvent(vm, "Finals");
        vm.Match.Team1.Name = "Beta";
        vm.Match.Team1.Score = 2;

        var text = vm.ApplyProfile(vm.Profiles!.List().Single().Id, wholeMatch: false);

        Assert.Equal("Weekly", vm.General.StatusText);
        Assert.Equal("Host Weekly", vm.General.Host);
        Assert.Equal("SOON Weekly", vm.Waiting.TextStarting);
        Assert.Equal("Standings Weekly", vm.Standings.Title);
        Assert.Equal("Beta", vm.Match.Team1.Name);                  // the match on screen stays
        Assert.Equal(2, vm.Match.Team1.Score);
        Assert.Equal("Weekly cup", vm.ActiveProfileName);
        Assert.True(vm.HasActiveProfile);
        Assert.True(vm.HasUnsentChanges);
        Assert.Empty(_publisher.Published);                         // only Update sends
        Assert.Contains("settings only", text);
        Assert.Empty(vm.ArchivedMatches());                         // nothing was replaced, nothing archived
    }

    [Fact]
    public void The_whole_match_brings_the_saved_teams_and_archives_the_one_on_screen()
    {
        var vm = Create();
        vm.Match.Team1.Name = "Alpha";
        vm.Match.Team1.Score = 1;
        vm.SaveProfile("Semi 1");
        vm.Match.Team1.Name = "Beta";
        vm.Match.Team1.Score = 3;

        vm.ApplyProfile(vm.Profiles!.List().Single().Id, wholeMatch: true);

        Assert.Equal("Alpha", vm.Match.Team1.Name);
        Assert.Equal(1, vm.Match.Team1.Score);
        Assert.Equal("Beta 3–0 Team 2", Assert.Single(vm.ArchivedMatches()).Title);
    }

    [Fact]
    public void Applying_is_an_undo_step_and_leaves_a_running_countdown_alone()
    {
        var vm = Create();
        FillEvent(vm, "Weekly");
        vm.SaveProfile("Weekly cup");
        FillEvent(vm, "Finals");
        vm.FlushHistory();
        vm.Waiting.Timer.Set(300);
        vm.Waiting.StartTimerCommand.Execute(null);

        vm.ApplyProfile(vm.Profiles!.List().Single().Id, wholeMatch: false);
        Assert.True(vm.Waiting.IsRunning);
        Assert.Equal("05:00", vm.Waiting.TimerText);

        vm.UndoCommand.Execute(null);
        Assert.Equal("Finals", vm.General.StatusText);
        Assert.True(vm.Waiting.IsRunning);
    }

    [Fact]
    public void The_tournament_address_is_filled_in_but_nothing_is_connected()
    {
        var vm = Create();
        vm.Link.TournamentUrl = "https://sowdraft.fi/tournament/esm";
        vm.SaveProfile("ESM");
        vm.Link.TournamentUrl = "";

        vm.ApplyProfile(vm.Profiles!.List().Single().Id, wholeMatch: false);

        Assert.Equal("https://sowdraft.fi/tournament/esm", vm.Link.TournamentUrl);
        Assert.False(vm.Link.IsLinked);
    }

    [Fact]
    public void Saving_needs_a_name_and_problems_are_reported_in_words()
    {
        var vm = Create();

        Assert.Contains("name", vm.SaveProfile("  "));
        Assert.Contains("no longer", vm.ApplyProfile("gone", wholeMatch: true));
        Assert.Contains("no longer", vm.DeleteProfile("gone"));
        Assert.Contains("not available", new MainWindowViewModel(_dialogs, _dialogs, _publisher, Path.Combine(_tmp, "other.json")).SaveProfile("X"));
    }

    [Fact]
    public void The_window_saves_selects_applies_and_deletes()
    {
        var vm = Create();
        FillEvent(vm, "One");
        var window = new ProfilesViewModel(vm);
        Assert.True(window.IsEmpty);

        window.Name = "Cup one";
        window.SaveCommand.Execute(null);
        FillEvent(vm, "Two");
        window.Name = "Cup two";
        window.SaveCommand.Execute(null);

        Assert.Equal(new[] { "Cup one", "Cup two" }, window.Items.Select(i => i.Name));
        Assert.Equal("Cup two", window.Selected!.Name);
        Assert.True(window.Selected.IsActive);

        window.Selected = window.Items[0];
        Assert.Equal("Cup one", window.Name);                      // choosing a profile fills the name box
        window.ApplySettingsCommand.Execute(null);
        Assert.Equal("One", vm.General.StatusText);
        Assert.Equal("Cup one", vm.ActiveProfileName);
        Assert.Contains("Cup one", window.Message);

        window.DeleteCommand.Execute(null);
        Assert.Equal(new[] { "Cup two" }, window.Items.Select(i => i.Name));
        Assert.Equal("", vm.ActiveProfileName);
    }
}

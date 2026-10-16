using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Control;
using SowBroadcast.Core.History;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Tests;

public sealed class UndoHistoryTests
{
    [Fact]
    public void Steps_go_back_and_forward_and_a_new_step_drops_the_redo_list()
    {
        var history = new UndoHistory();
        history.Reset("a");
        history.Record("b");
        history.Record("c");

        Assert.Equal("b", history.Undo());
        Assert.Equal("a", history.Undo());
        Assert.Null(history.Undo());
        Assert.Equal("b", history.Redo());
        Assert.True(history.CanRedo);

        history.Record("x");

        Assert.False(history.CanRedo);
        Assert.Null(history.Redo());
        Assert.Equal("b", history.Undo());
    }

    [Fact]
    public void Recording_the_same_snapshot_is_not_a_step()
    {
        var history = new UndoHistory();
        history.Reset("a");

        Assert.False(history.Record("a"));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void The_oldest_steps_fall_off_at_the_capacity()
    {
        var history = new UndoHistory(3);
        history.Reset("0");
        for (var i = 1; i <= 6; i++) history.Record(i.ToString());

        var seen = new List<string>();
        while (history.Undo() is { } snapshot) seen.Add(snapshot);

        Assert.Equal(new[] { "5", "4", "3" }, seen);
    }
}

public sealed class MatchArchiveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-archive-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private static AppState Match(string one, string two, int score1 = 0, int score2 = 0) => AppState.CreateDefault() with
    {
        Team1 = new Team { Name = one, Score = score1 },
        Team2 = new Team { Name = two, Score = score2 },
    };

    [Fact]
    public void A_blank_match_is_not_worth_keeping()
    {
        var archive = new MatchArchive(_dir);

        Assert.Null(archive.Archive(AppState.CreateDefault(), new DateTime(2026, 10, 16, 20, 0, 0)));
        Assert.Empty(archive.List());
    }

    [Fact]
    public void Matches_are_listed_newest_first_with_a_one_line_title()
    {
        var archive = new MatchArchive(_dir);
        archive.Archive(Match("Alpha", "Beta", 1, 0), new DateTime(2026, 10, 16, 20, 0, 0));
        archive.Archive(Match("Gamma", "", 2, 3), new DateTime(2026, 10, 16, 21, 0, 0));

        var list = archive.List();

        Assert.Equal(new[] { "Gamma 2–3 Team 2", "Alpha 1–0 Beta" }, list.Select(m => m.Title));
        Assert.Equal(new DateTime(2026, 10, 16, 21, 0, 0), list[0].Time);   // from the file name
    }

    [Fact]
    public void The_same_state_is_not_archived_twice_but_a_change_is()
    {
        var archive = new MatchArchive(_dir);
        var first = archive.Archive(Match("Alpha", "Beta"), new DateTime(2026, 10, 16, 20, 0, 0));
        var again = archive.Archive(Match("Alpha", "Beta") with { Waiting = new WaitingSettings { TimerSeconds = 90, TimerRunning = true } }, new DateTime(2026, 10, 16, 20, 5, 0));
        var changed = archive.Archive(Match("Alpha", "Beta", 1, 0), new DateTime(2026, 10, 16, 20, 10, 0));

        Assert.NotNull(first);
        Assert.Null(again);               // the countdown is not part of the match
        Assert.NotNull(changed);
        Assert.Equal(2, archive.List().Count);
    }

    [Fact]
    public void Two_archives_in_the_same_second_both_survive_and_only_the_newest_ones_are_kept()
    {
        var archive = new MatchArchive(_dir, keep: 3);
        var moment = new DateTime(2026, 10, 16, 20, 0, 0);
        for (var i = 1; i <= 5; i++) archive.Archive(Match("Alpha", "Beta", i, 0), moment);

        var list = archive.List();

        Assert.Equal(3, list.Count);
        Assert.Equal("Alpha 5–0 Beta", list[0].Title);
    }

    [Fact]
    public void A_damaged_file_is_skipped()
    {
        var archive = new MatchArchive(_dir);
        archive.Archive(Match("Alpha", "Beta"), new DateTime(2026, 10, 16, 20, 0, 0));
        File.WriteAllText(Path.Combine(_dir, "match-20261016-230000.json"), "{ broken");

        Assert.Single(archive.List());
    }
}

public sealed class UndoViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-undo-" + Guid.NewGuid().ToString("N"));
    private readonly MainWindowViewModelTests.FakeDialogs _dialogs = new();
    private readonly MainWindowViewModelTests.FakePublisher _publisher = new();
    private long _timerNow;
    private DateTime _now = new(2026, 10, 16, 20, 0, 0);

    public UndoViewModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private MainWindowViewModel Create(bool withArchive = true) => new(_dialogs, _dialogs, _publisher, Path.Combine(_dir, "autosave.json"),
        defaultVideosDir: Path.Combine(_dir, "Highlights"), clock: () => _timerNow, epochMs: () => 1_760_000_000_000 + _timerNow,
        archive: withArchive ? new MatchArchive(Path.Combine(_dir, "History")) : null, now: () => _now);

    private static MainWindowViewModel Edit(MainWindowViewModel vm, Action<MainWindowViewModel> change)
    {
        change(vm);
        vm.FlushHistory();
        return vm;
    }

    [Fact]
    public void Undo_and_redo_step_through_edits_made_in_between_pauses()
    {
        var vm = Create();
        Edit(vm, v => v.Match.Team1.Name = "Alpha");
        Edit(vm, v => v.Match.Team1.Score = 2);

        vm.UndoCommand.Execute(null);
        Assert.Equal(0, vm.Match.Team1.Score);
        Assert.Equal("Alpha", vm.Match.Team1.Name);

        vm.UndoCommand.Execute(null);
        Assert.Equal("", vm.Match.Team1.Name);
        Assert.False(vm.CanUndo);

        vm.RedoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        Assert.Equal("Alpha", vm.Match.Team1.Name);
        Assert.Equal(2, vm.Match.Team1.Score);
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void Edits_without_a_pause_in_between_are_one_step()
    {
        var vm = Create();
        vm.Match.Team1.Name = "A";
        vm.Match.Team1.Name = "Al";
        vm.Match.Team1.Name = "Alpha";
        vm.FlushHistory();

        vm.UndoCommand.Execute(null);

        Assert.Equal("", vm.Match.Team1.Name);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Undo_takes_pending_edits_into_account_and_an_action_is_a_step_of_its_own()
    {
        var vm = Create();
        vm.Match.Team1.Name = "Alpha";          // typed, no pause yet
        vm.SwapTeamsCommand.Execute(null);      // an action right after: typing and swap are two steps

        vm.UndoCommand.Execute(null);
        Assert.Equal("Alpha", vm.Match.Team1.Name);   // the swap is undone, the typing stays
        Assert.Equal("", vm.Match.Team2.Name);

        vm.UndoCommand.Execute(null);
        Assert.Equal("", vm.Match.Team1.Name);
    }

    [Fact]
    public void Undo_marks_the_match_unsent_and_sends_nothing()
    {
        var vm = Create();
        Edit(vm, v => v.Match.Team1.Score = 3);
        vm.UpdateMatchCommand.Execute(null);
        Assert.False(vm.HasUnsentChanges);
        var published = _publisher.Published.Count;

        vm.UndoCommand.Execute(null);

        Assert.True(vm.HasUnsentChanges);
        Assert.Equal(published, _publisher.Published.Count);
        Assert.Equal(0, vm.Match.Team1.Score);
    }

    [Fact]
    public void A_running_countdown_is_not_touched_by_undo_and_does_not_create_steps()
    {
        var vm = Create();
        vm.Waiting.Timer.Set(300);
        vm.Waiting.StartTimerCommand.Execute(null);
        Edit(vm, v => v.Match.Team1.Name = "Alpha");
        _timerNow += 10_000;
        vm.Waiting.Tick();
        vm.FlushHistory();
        Assert.False(vm.CanRedo);

        vm.UndoCommand.Execute(null);

        Assert.Equal("", vm.Match.Team1.Name);
        Assert.True(vm.Waiting.IsRunning);
        Assert.Equal("04:50", vm.Waiting.TimerText);
        Assert.False(vm.CanUndo);   // ticking made no step
    }

    [Fact]
    public void Undo_brings_back_other_tabs_too()
    {
        var vm = Create();
        Edit(vm, v => v.General.Caster1 = "Caster One");
        Edit(vm, v => v.Waiting.TextStarting = "SOON");

        vm.UndoCommand.Execute(null);
        Assert.Equal("STARTING SOON!", vm.Waiting.TextStarting);
        vm.UndoCommand.Execute(null);

        Assert.Equal("", vm.General.Caster1);
    }

    [Fact]
    public void Nothing_to_undo_says_so()
    {
        var vm = Create();

        vm.UndoCommand.Execute(null);
        Assert.Equal("Nothing to undo", vm.StatusText);
        vm.RedoCommand.Execute(null);
        Assert.Equal("Nothing to redo", vm.StatusText);
    }

    [Fact]
    public void The_hotkey_commands_undo_and_redo_and_report_when_there_is_nothing_to_do()
    {
        var vm = Create();
        vm.RunControl(ControlCatalog.Team1ScoreUp);
        vm.RunControl(ControlCatalog.Team1ScoreUp);

        var undone = vm.RunControl(ControlCatalog.Undo);
        Assert.True(undone.Ok);
        Assert.Equal(1, undone.Team1Score);
        vm.RunControl(ControlCatalog.Undo);
        var nothing = vm.RunControl(ControlCatalog.Undo);
        Assert.False(nothing.Ok);
        Assert.Equal(0, nothing.Team1Score);

        var redone = vm.RunControl(ControlCatalog.Redo);
        Assert.True(redone.Ok);
        Assert.Equal(1, redone.Team1Score);
    }

    [Fact]
    public async Task A_loaded_autosave_is_the_start_of_the_history()
    {
        StateStore.Save(Path.Combine(_dir, "autosave.json"), AppState.CreateDefault() with { Team1 = new Team { Name = "Restored" } });
        var vm = Create();
        await vm.LoadAutosaveAsync();

        Assert.False(vm.CanUndo);
        Edit(vm, v => v.Match.Team1.Name = "Changed");
        vm.UndoCommand.Execute(null);

        Assert.Equal("Restored", vm.Match.Team1.Name);
    }

    // ---- archive -----------------------------------------------------------------------------------------

    [Fact]
    public void Reset_archives_the_match_and_it_can_be_restored()
    {
        var vm = Create();
        Edit(vm, v => { v.Match.Team1.Name = "Alpha"; v.Match.Team2.Name = "Beta"; v.Match.Team1.Score = 2; });

        vm.ResetMatchCommand.Execute(null);
        Assert.Equal("", vm.Match.Team1.Name);

        var archived = Assert.Single(vm.ArchivedMatches());
        Assert.Equal("Alpha 2–0 Beta", archived.Title);

        vm.RestoreArchivedAsync(archived.Path).GetAwaiter().GetResult();

        Assert.Equal("Alpha", vm.Match.Team1.Name);
        Assert.Equal(2, vm.Match.Team1.Score);
        Assert.True(vm.HasUnsentChanges);
        // The reset is still behind it in the undo history.
        vm.UndoCommand.Execute(null);
        Assert.Equal("", vm.Match.Team1.Name);
    }

    [Fact]
    public void A_blank_reset_archives_nothing()
    {
        var vm = Create();

        vm.ResetMatchCommand.Execute(null);

        Assert.Empty(vm.ArchivedMatches());
    }

    [Fact]
    public void Restoring_keeps_the_running_countdown_and_the_link()
    {
        var vm = Create();
        Edit(vm, v => v.Match.Team1.Name = "Alpha");
        vm.ResetMatchCommand.Execute(null);
        vm.Waiting.Timer.Set(300);
        vm.Waiting.StartTimerCommand.Execute(null);

        vm.RestoreArchivedAsync(vm.ArchivedMatches()[0].Path).GetAwaiter().GetResult();

        Assert.True(vm.Waiting.IsRunning);
        Assert.Equal("05:00", vm.Waiting.TimerText);
    }

    [Fact]
    public void Loading_a_file_archives_the_match_it_replaces()
    {
        var other = Path.Combine(_dir, "other.sowbroadcast.json");
        StateStore.Save(other, AppState.CreateDefault() with { Team1 = new Team { Name = "Other" } });
        _dialogs.OpenResult = other;
        var vm = Create();
        Edit(vm, v => v.Match.Team1.Name = "Alpha");

        vm.LoadCommand.ExecuteAsync(null).GetAwaiter().GetResult();

        Assert.Equal("Other", vm.Match.Team1.Name);
        Assert.Equal("Alpha 0–0 Team 2", Assert.Single(vm.ArchivedMatches()).Title);
        vm.UndoCommand.Execute(null);
        Assert.Equal("Alpha", vm.Match.Team1.Name);
    }

    [Fact]
    public void Update_archives_at_most_once_in_ten_minutes_and_closing_archives_the_last_state()
    {
        var vm = Create();
        Edit(vm, v => v.Match.Team1.Name = "Alpha");
        vm.UpdateMatchCommand.Execute(null);
        Edit(vm, v => v.Match.Team1.Score = 1);
        _now += TimeSpan.FromMinutes(5);
        vm.UpdateMatchCommand.Execute(null);

        Assert.Single(vm.ArchivedMatches());   // the second Update came too soon

        _now += TimeSpan.FromMinutes(6);
        vm.UpdateMatchCommand.Execute(null);
        Assert.Equal(2, vm.ArchivedMatches().Count);

        Edit(vm, v => v.Match.Team1.Score = 2);
        vm.ArchiveOnClose();
        Assert.Equal("Alpha 2–0 Team 2", vm.ArchivedMatches()[0].Title);
    }

    [Fact]
    public void Without_an_archive_everything_still_works()
    {
        var vm = Create(withArchive: false);
        Edit(vm, v => v.Match.Team1.Name = "Alpha");

        vm.ResetMatchCommand.Execute(null);
        vm.ArchiveOnClose();

        Assert.Empty(vm.ArchivedMatches());
        vm.UndoCommand.Execute(null);
        Assert.Equal("Alpha", vm.Match.Team1.Name);
    }
}

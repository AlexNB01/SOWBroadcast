using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.Core.History;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.App.ViewModels;

// Undo / redo and "restore a previous match" (EHD-17).
//
// Undo steps are snapshots of what the producer edits: the match, the general settings, the waiting texts, standings and bracket.
// The countdown, the SOWDraft link bookkeeping and the overlays are not part of them: undoing changes what is on the screen only
// (like any other edit, it reaches the overlays with Update), and a running countdown is never touched.
public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan ArchiveInterval = TimeSpan.FromMinutes(10);

    private readonly UndoHistory _history = new();
    private readonly MatchArchive? _archive;
    private readonly Func<DateTime> _now;
    private bool _historyDirty;
    private DateTime _lastArchiveAt = DateTime.MinValue;

    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canRedo;

    // The state as text, without what undo must not touch.
    private static string Snapshot(AppState state) => StateStore.Serialize(state with
    {
        Waiting = state.Waiting with { TimerSeconds = 0, TimerRunning = false, TimerEndsAtMs = 0 },
        Link = new LinkSettings(),
        DraftLinkUrl = "",
        TournamentLinkUrl = "",
        CurrentMap = null,
    });

    private void UpdateUndoFlags()
    {
        CanUndo = _history.CanUndo;
        CanRedo = _history.CanRedo;
    }

    /// <summary>Starts the undo history over from what is on the screen now (a loaded or restored match).</summary>
    private void ResetHistory()
    {
        CollectTabs();
        _history.Reset(Snapshot(State));
        _historyDirty = false;
        UpdateUndoFlags();
    }

    /// <summary>
    /// Makes what has been edited since the last call one undo step. Called by the window a little after the user stops editing,
    /// and before every action (swap, reset, a hotkey command), so an action is always a step of its own.
    /// </summary>
    public void FlushHistory()
    {
        if (!_historyDirty) return;
        _historyDirty = false;
        CollectTabs();
        if (_history.Record(Snapshot(State))) UpdateUndoFlags();
    }

    [RelayCommand]
    private void Undo() => StatusText = StepHistory(undo: true).Message;

    [RelayCommand]
    private void Redo() => StatusText = StepHistory(undo: false).Message;

    private (bool Ok, string Message) StepHistory(bool undo)
    {
        FlushHistory();
        var snapshot = undo ? _history.Undo() : _history.Redo();
        if (snapshot is null) return (false, undo ? "Nothing to undo" : "Nothing to redo");

        RestoreSlice(StateStore.Deserialize(snapshot));
        _historyDirty = false;   // showing a snapshot is not a new step
        UpdateUndoFlags();
        Autosave();
        return (true, undo ? "Undone (not published yet)" : "Redone (not published yet)");
    }

    // Shows a snapshot on the tabs. The countdown, the link and the SOWDraft addresses stay as they are now.
    private void RestoreSlice(AppState snapshot)
    {
        CollectTabs();
        var merged = snapshot with
        {
            Waiting = snapshot.Waiting with
            {
                TimerSeconds = State.Waiting.TimerSeconds,
                TimerRunning = State.Waiting.TimerRunning,
                TimerEndsAtMs = State.Waiting.TimerEndsAtMs,
            },
            Link = State.Link,
            DraftLinkUrl = State.DraftLinkUrl,
            TournamentLinkUrl = State.TournamentLinkUrl,
        };
        State = merged;   // the Match tab shows it
        General.Load(merged);
        Waiting.LoadSettings(merged);
        Standings.Load(merged);
        Bracket.Load(merged);
        foreach (var source in new[] { "match", "general", "waiting", "standings", "bracket" }) MarkUnsent(source);
    }

    // ---- archive of earlier matches ----------------------------------------------------------------------

    /// <summary>Keeps the match on the screen in the archive (not when it is blank or already the newest one there).</summary>
    private void ArchiveCurrent(string reason)
    {
        if (_archive is null) return;
        CollectTabs();
        try
        {
            if (_archive.Archive(State, _now()) is { } path)
            {
                _lastArchiveAt = _now();
                Serilog.Log.Information("Archived the match ({Reason}): {File}", reason, Path.GetFileName(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "The match could not be archived");
        }
    }

    /// <summary>Called when the program closes: the match stays available under "Restore previous match".</summary>
    public void ArchiveOnClose() => ArchiveCurrent("closed");

    /// <summary>The archived matches, newest first.</summary>
    public IReadOnlyList<ArchivedMatch> ArchivedMatches() => _archive?.List() ?? Array.Empty<ArchivedMatch>();

    /// <summary>Shows an archived match instead of the current one (which is archived first). Nothing is sent until Update.</summary>
    public async Task RestoreArchivedAsync(string path)
    {
        AppState loaded;
        try
        {
            loaded = StateStore.Load(path);
        }
        catch (StateLoadException ex)
        {
            await _messages.ShowErrorAsync("Restore failed", ex.Message);
            return;
        }

        FlushHistory();
        ArchiveCurrent("restore");
        RestoreSlice(loaded);   // not ShowState: a running countdown and the SOWDraft link stay as they are
        MarkUnsent("file");
        Autosave();
        StatusText = $"Restored {MatchArchive.Describe(loaded)} (not published yet)";
    }
}

using SowBroadcast.Core.Control;

namespace SowBroadcast.App.ViewModels;

/// <summary>What a control command did, and how the match stands afterwards (the reply of the HTTP interface).</summary>
public sealed record ControlOutcome(bool Ok, string Message, int Team1Score, int Team2Score, int? CurrentMap, string Timer, bool TimerRunning, bool Unsent);

// The commands of the local control interface (EHD-7). Hotkeys and the HTTP interface both end up in RunControl, on the UI thread.
public sealed partial class MainWindowViewModel
{
    private const int MaxScore = 200;

    /// <summary>Hands the producer's automation switches to the match and the bracket (the same object stays in use when the settings change).</summary>
    public void UseAutomation(AutomationSettings automation)
    {
        Match.Automation = automation;
        Match.AutomationAllowed = () => !Link.IsLinked;               // linked: SOWDraft fills the scores and the maps
        Bracket.Automation = automation;
        Bracket.AutomationAllowed = () => !Link.IsTournamentLinked;   // linked: the tournament fills (and locks) the bracket
    }

    /// <summary>Runs the command <paramref name="id"/> (a <see cref="ControlCatalog"/> id). Must be called on the UI thread.</summary>
    public ControlOutcome RunControl(string id)
    {
        var known = ControlCatalog.Find(id);
        if (known is null) return Describe(false, $"Unknown command \"{id}\"");

        FlushHistory();   // what was typed before this command is a step of its own
        var (ok, message) = known.Id switch
        {
            ControlCatalog.Undo => StepHistory(undo: true),
            ControlCatalog.Redo => StepHistory(undo: false),
            ControlCatalog.Team1ScoreUp => Bump(Match.Team1, +1),
            ControlCatalog.Team1ScoreDown => Bump(Match.Team1, -1),
            ControlCatalog.Team2ScoreUp => Bump(Match.Team2, +1),
            ControlCatalog.Team2ScoreDown => Bump(Match.Team2, -1),
            ControlCatalog.MapTeam1Up => BumpMap(1, +1),
            ControlCatalog.MapTeam1Down => BumpMap(1, -1),
            ControlCatalog.MapTeam2Up => BumpMap(2, +1),
            ControlCatalog.MapTeam2Down => BumpMap(2, -1),
            ControlCatalog.MapNext => FinishMap(),
            ControlCatalog.Swap => Run(SwapTeamsCommand.Execute, "Teams swapped"),
            ControlCatalog.Update => Run(UpdateMatchCommand.Execute, "Updated"),
            ControlCatalog.TimerToggle => Run(Waiting.IsRunning ? Waiting.PauseTimerCommand.Execute : Waiting.StartTimerCommand.Execute,
                Waiting.IsRunning ? "Countdown paused" : "Countdown started"),
            ControlCatalog.TimerStart => Run(Waiting.StartTimerCommand.Execute, "Countdown started"),
            ControlCatalog.TimerPause => Run(Waiting.PauseTimerCommand.Execute, "Countdown paused"),
            ControlCatalog.TimerReset => Run(Waiting.ResetTimerCommand.Execute, "Countdown reset"),
            ControlCatalog.TimerPlusMinute => Run(o => Waiting.AdjustTimerCommand.Execute("60"), "Countdown +1 minute"),
            ControlCatalog.TimerMinusMinute => Run(o => Waiting.AdjustTimerCommand.Execute("-60"), "Countdown −1 minute"),
            _ => (false, $"Unknown command \"{id}\""),
        };

        // A command changes the match on the screen only; the overlays get it with Update. The autosave keeps it safe.
        if (ok && known.Id is not (ControlCatalog.Update or ControlCatalog.Undo or ControlCatalog.Redo)) Autosave();
        StatusText = ok ? message : $"Control: {message}";
        return Describe(ok, message);
    }

    /// <summary>How the match stands (the reply of <c>GET /api/control</c>).</summary>
    public ControlOutcome ControlSnapshot() => Describe(true, "OK");

    private ControlOutcome Describe(bool ok, string message) =>
        new(ok, message, Match.Team1.Score, Match.Team2.Score, CurrentMapCard()?.Index, Waiting.TimerText, Waiting.IsRunning, HasUnsentChanges);

    private (bool, string) Run(Action<object?> action, string done)
    {
        action(null);
        return (true, done);
    }

    private static (bool, string) Bump(TeamPanelViewModel team, int delta)
    {
        var next = Math.Clamp(team.Score + delta, 0, MaxScore);
        if (next == team.Score) return (false, $"Team {team.Number} score stays at {next}");
        team.Score = next;
        return (true, $"Team {team.Number} score {next}");
    }

    // The map being played: the first card that is shown and not finished (the same rule as current_map in the state).
    private MapCardViewModel? CurrentMapCard() => Match.Maps.FirstOrDefault(card => card.IsShown && !card.Completed);

    private (bool, string) BumpMap(int team, int delta)
    {
        var card = CurrentMapCard();
        if (card is null) return (false, "No open map");
        var current = team == 1 ? card.T1 : card.T2;
        var next = Math.Clamp(current + delta, 0, MaxScore);
        if (next == current) return (false, $"Map {card.Index}: team {team} stays at {next}");
        if (team == 1) card.T1 = next; else card.T2 = next;
        return (true, $"Map {card.Index}: {card.T1} – {card.T2}");
    }

    private (bool, string) FinishMap()
    {
        var card = CurrentMapCard();
        if (card is null) return (false, "No open map");
        card.Completed = true;
        var following = CurrentMapCard();
        return (true, following is null ? $"Map {card.Index} finished (last map)" : $"Map {card.Index} finished, map {following.Index} is next");
    }
}

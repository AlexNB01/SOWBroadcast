namespace SowBroadcast.Core.Control;

/// <summary>One thing an operator can trigger without the mouse: from a hotkey, or from Stream Deck / Companion over HTTP.</summary>
public sealed record ControlCommandInfo(string Id, string Label, string? DefaultGesture);

/// <summary>The commands of the local control interface (EHD-7). The ids are the public names used in URLs and in the settings file.</summary>
public static class ControlCatalog
{
    public const string Team1ScoreUp = "team1.score.up";
    public const string Team1ScoreDown = "team1.score.down";
    public const string Team2ScoreUp = "team2.score.up";
    public const string Team2ScoreDown = "team2.score.down";
    public const string MapTeam1Up = "map.team1.up";
    public const string MapTeam1Down = "map.team1.down";
    public const string MapTeam2Up = "map.team2.up";
    public const string MapTeam2Down = "map.team2.down";
    public const string MapNext = "map.next";
    public const string Swap = "swap";
    public const string TimerToggle = "timer.toggle";
    public const string TimerStart = "timer.start";
    public const string TimerPause = "timer.pause";
    public const string TimerReset = "timer.reset";
    public const string TimerPlusMinute = "timer.plus1m";
    public const string TimerMinusMinute = "timer.minus1m";
    public const string Update = "update";
    public const string Undo = "undo";
    public const string Redo = "redo";
    public const string ScheduleShow = "schedule.show";
    public const string ObsScene = "obs.scene";
    public const string ObsReplaySave = "obs.replay.save";
    public const string ObsReplayToggle = "obs.replay.toggle";

    /// <summary>The commands that talk to OBS (they answer after OBS did, so they run asynchronously).</summary>
    public static bool IsObs(string id) => id.StartsWith("obs.", StringComparison.OrdinalIgnoreCase);

    // Defaults are Ctrl+Alt+F-keys (and Ctrl+Alt+Z / Y for undo / redo): nothing else on Windows uses them, and they work while OBS or the game has the focus.
    public static IReadOnlyList<ControlCommandInfo> All { get; } = new ControlCommandInfo[]
    {
        new(Team1ScoreUp, "Team 1 score +1", "Ctrl+Alt+F1"),
        new(Team1ScoreDown, "Team 1 score −1", "Ctrl+Alt+F2"),
        new(Team2ScoreUp, "Team 2 score +1", "Ctrl+Alt+F3"),
        new(Team2ScoreDown, "Team 2 score −1", "Ctrl+Alt+F4"),
        new(MapNext, "Finish the current map (next map)", "Ctrl+Alt+F5"),
        new(Swap, "Swap teams", "Ctrl+Alt+F6"),
        new(TimerToggle, "Countdown start / pause", "Ctrl+Alt+F7"),
        new(TimerReset, "Countdown reset", "Ctrl+Alt+F8"),
        new(Update, "Update (send to the overlays)", "Ctrl+Alt+F9"),
        new(Undo, "Undo the last change", "Ctrl+Alt+Z"),
        new(Redo, "Redo", "Ctrl+Alt+Y"),
        new(ScheduleShow, "Waiting screens: show the match schedule now", "Ctrl+Alt+L"),
        new(ObsReplaySave, "OBS: save the replay now", "Ctrl+Alt+R"),
        new(ObsReplayToggle, "OBS: replay buffer start / stop", null),
        new(ObsScene, "OBS: switch scene (over HTTP: ?name=Scene name)", null),
        new(MapTeam1Up, "Current map: team 1 +1", null),
        new(MapTeam1Down, "Current map: team 1 −1", null),
        new(MapTeam2Up, "Current map: team 2 +1", null),
        new(MapTeam2Down, "Current map: team 2 −1", null),
        new(TimerStart, "Countdown start", null),
        new(TimerPause, "Countdown pause", null),
        new(TimerPlusMinute, "Countdown +1 minute", null),
        new(TimerMinusMinute, "Countdown −1 minute", null),
    };

    public static ControlCommandInfo? Find(string id) =>
        All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
}

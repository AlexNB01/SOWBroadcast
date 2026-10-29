using System.Text.Json.Nodes;
using SowBroadcast.Core.Model;

namespace SowBroadcast.Core.Waiting;

/// <summary>
/// What the waiting screens list: the matches still to come (those in play first) and the latest results, chosen from
/// <see cref="AppState.Schedule"/> by the waiting-tab settings.
/// </summary>
public static class ScheduleOverlay
{
    public const int MinRows = 1, MaxRows = 8;
    public const int MinSeconds = 5, MaxSeconds = 120;
    public const int MinVideoSeconds = 10, MaxVideoSeconds = 600;

    public static int RowsOf(WaitingSettings w) => Math.Clamp(w.ScheduleRows, MinRows, MaxRows);
    public static int SecondsOf(WaitingSettings w) => Math.Clamp(w.ScheduleSeconds, MinSeconds, MaxSeconds);
    public static int VideoSecondsOf(WaitingSettings w) => Math.Clamp(w.ScheduleVideoSeconds, MinVideoSeconds, MaxVideoSeconds);

    /// <summary>When a match is expected to start: its time, else the target time of an ASAP match, else never (listed last).</summary>
    public static long StartsAt(ScheduleEntry e) =>
        e.ScheduledAt > 0 ? e.ScheduledAt : e.TargetAt > 0 ? e.TargetAt : long.MaxValue;

    /// <summary>
    /// Matches in play (the one started last first), then the next ones: soonest first (an ASAP match counts at its target time),
    /// those without any time last, and among those the stage, round and match number decide (group matches before the playoffs).
    /// Only matches that have a stream link in SOWDraft count, and with a filter set the link must contain it (so a match streamed
    /// elsewhere stays out). A match in which neither team is known yet stays out unless the tab says otherwise.
    /// </summary>
    public static IReadOnlyList<ScheduleEntry> Upcoming(AppState state)
    {
        var filter = state.Waiting.ScheduleStreamFilter.Trim();
        bool Streamed(ScheduleEntry e) =>
            e.StreamUrl.Length > 0 && (filter.Length == 0 || e.StreamUrl.Contains(filter, StringComparison.OrdinalIgnoreCase));
        bool Shown(ScheduleEntry e) => state.Waiting.ScheduleShowUnknown || e.Team1.Length > 0 || e.Team2.Length > 0;

        var open = state.Schedule.Where(e => e.Status != ScheduleStatus.Completed && Streamed(e) && Shown(e)).ToArray();
        return open.Where(e => e.Status == ScheduleStatus.Live)
            .OrderByDescending(e => e.StartedAt > 0 ? e.StartedAt : e.ScheduledAt)
            .ThenBy(e => e.StageOrder).ThenBy(e => e.Round).ThenBy(e => e.MatchNumber)
            .Concat(open.Where(e => e.Status != ScheduleStatus.Live)
                .OrderBy(StartsAt)
                .ThenBy(e => e.Asap ? 0 : 1)   // among those without a time, an ASAP match is the one that is wanted soon
                .ThenBy(e => e.StageOrder).ThenBy(e => e.Round).ThenBy(e => e.MatchNumber))
            .Take(RowsOf(state.Waiting))
            .ToArray();
    }

    /// <summary>The finished matches, newest first (all of them, whether they were streamed or not).</summary>
    public static IReadOnlyList<ScheduleEntry> Results(AppState state) =>
        state.Schedule.Where(e => e.Status == ScheduleStatus.Completed && (e.Team1.Length > 0 || e.Team2.Length > 0))
            .OrderByDescending(e => e.CompletedAt)
            .ThenByDescending(e => e.Round).ThenByDescending(e => e.MatchNumber)
            .Take(RowsOf(state.Waiting))
            .ToArray();

    /// <summary>"Group A · Round 2" or just "Semifinals".</summary>
    public static string Label(ScheduleEntry e) => e.Group.Length > 0 ? $"Group {e.Group} · {e.RoundName}" : e.RoundName;

    /// <param name="logoPath">Turns a team logo file into the path the overlay loads it from (the exporter's normaliser); without one the path is kept as it is.</param>
    public static JsonObject BuildJson(AppState state, Func<string?, string?>? logoPath = null)
    {
        static string Name(string name) => name.Length > 0 ? name : "TBD";
        string? Logo(string? path) => logoPath is null ? path : logoPath(path);
        var w = state.Waiting;
        return new JsonObject
        {
            ["enabled"] = w.ScheduleEnabled,
            ["rows"] = RowsOf(w),
            ["seconds"] = SecondsOf(w),
            ["video_seconds"] = VideoSecondsOf(w),
            ["upcoming"] = new JsonArray(Upcoming(state).Select(e => (JsonNode)new JsonObject
            {
                ["team1"] = Name(e.Team1),
                ["team2"] = Name(e.Team2),
                ["team1_logo"] = Logo(e.Team1Logo),
                ["team2_logo"] = Logo(e.Team2Logo),
                ["live"] = e.Status == ScheduleStatus.Live,
                ["scheduled_at"] = e.ScheduledAt,
                ["asap"] = e.Asap && e.ScheduledAt == 0,
                ["target_at"] = e.Asap && e.ScheduledAt == 0 ? e.TargetAt : 0,
                ["label"] = Label(e),
            }).ToArray()),
            ["results"] = new JsonArray(Results(state).Select(e => (JsonNode)new JsonObject
            {
                ["team1"] = Name(e.Team1),
                ["team2"] = Name(e.Team2),
                ["team1_logo"] = Logo(e.Team1Logo),
                ["team2_logo"] = Logo(e.Team2Logo),
                ["score1"] = e.Score1,
                ["score2"] = e.Score2,
                ["label"] = Label(e),
            }).ToArray()),
        };
    }
}

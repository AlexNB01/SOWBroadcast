namespace SowBroadcast.Core.Control;

/// <summary>
/// The optional automations of EHD-4. All off by default: a producer turns on what they trust. They change what is on the screen
/// (and the autosave) only; the overlays still get it with Update (KYS-9).
/// </summary>
public sealed class AutomationSettings
{
    /// <summary>Points that win a map, by game mode; 0 = never decided by points alone (Escort and Hybrid depend on both attacks).</summary>
    public static readonly IReadOnlyDictionary<string, int> DefaultMapWinPoints = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Control"] = 2,
        ["Flashpoint"] = 3,
        ["Push"] = 1,
        ["Escort"] = 0,
        ["Hybrid"] = 0,
    };

    /// <summary>A team's score is the number of maps it has won (finished maps only).</summary>
    public bool TeamScoreFromMaps { get; set; }

    /// <summary>A map is finished as soon as one team has the points that win it (then the next map is the current one).</summary>
    public bool CompleteMapsAutomatically { get; set; }

    /// <summary>The winner of a bracket match moves to its place in the next round.</summary>
    public bool AdvanceBracketWinners { get; set; }

    /// <summary>Best-of for bracket matches that have no BO label (a bracket typed by hand): 3 means first to 2.</summary>
    public int DefaultBracketBestOf { get; set; } = 3;

    public Dictionary<string, int> MapWinPoints { get; set; } = new(DefaultMapWinPoints, StringComparer.OrdinalIgnoreCase);

    public bool AnyEnabled => TeamScoreFromMaps || CompleteMapsAutomatically || AdvanceBracketWinners;

    /// <summary>Points that win a map of <paramref name="mode"/>; 0 when the mode is unknown or not decided by points.</summary>
    public int PointsToWin(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return 0;
        if (MapWinPoints.TryGetValue(mode, out var points)) return Math.Max(0, points);
        return DefaultMapWinPoints.TryGetValue(mode, out var fallback) ? fallback : 0;
    }

    public void CopyFrom(AutomationSettings other)
    {
        TeamScoreFromMaps = other.TeamScoreFromMaps;
        CompleteMapsAutomatically = other.CompleteMapsAutomatically;
        AdvanceBracketWinners = other.AdvanceBracketWinners;
        DefaultBracketBestOf = Math.Clamp(other.DefaultBracketBestOf, 1, 9);
        MapWinPoints = new Dictionary<string, int>(other.MapWinPoints, StringComparer.OrdinalIgnoreCase);
    }
}

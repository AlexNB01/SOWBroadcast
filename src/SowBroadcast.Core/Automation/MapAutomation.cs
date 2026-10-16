using SowBroadcast.Core.Control;

namespace SowBroadcast.Core.Automation;

/// <summary>The map rules of EHD-4, as plain functions so they can be tested without a window.</summary>
public static class MapAutomation
{
    /// <summary>Whether a map of <paramref name="mode"/> scored <paramref name="t1"/>–<paramref name="t2"/> is decided: one team has the winning points.</summary>
    public static bool ShouldComplete(AutomationSettings settings, string? mode, int t1, int t2)
    {
        var needed = settings.PointsToWin(mode);
        return needed > 0 && t1 != t2 && Math.Max(t1, t2) >= needed;
    }

    /// <summary>Maps won by each team among the finished maps (a drawn map counts for nobody).</summary>
    public static (int Team1, int Team2) Wins(IEnumerable<(bool Completed, int T1, int T2)> maps)
    {
        int one = 0, two = 0;
        foreach (var (completed, t1, t2) in maps)
        {
            if (!completed) continue;
            if (t1 > t2) one++;
            else if (t2 > t1) two++;
        }
        return (one, two);
    }
}

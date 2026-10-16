namespace SowBroadcast.Core.Model;

/// <summary>Fields that are always computed from the editable ones (never trusted from a file).</summary>
public static class StateDerivations
{
    public static AppState Apply(AppState state)
    {
        var maps = state.Maps.Select(WithWinner).ToArray();
        return state with { Maps = maps, CurrentMap = CurrentMapOf(maps, state.General.MapsCount) };
    }

    public static MapEntry WithWinner(MapEntry m) =>
        m with { Winner = m.T1 > m.T2 ? "t1" : m.T2 > m.T1 ? "t2" : "" };

    /// <summary>First visible (index within the series length) map that is not completed, or null.</summary>
    public static int? CurrentMapOf(IReadOnlyList<MapEntry> maps, int visibleMaps)
    {
        foreach (var map in maps)
            if (map.Index >= 1 && map.Index <= visibleMaps && !map.Completed)
                return map.Index;
        return null;
    }
}

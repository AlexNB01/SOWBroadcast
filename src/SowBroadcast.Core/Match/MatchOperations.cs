using SowBroadcast.Core.Model;

namespace SowBroadcast.Core.Match;

/// <summary>The Match tab's whole-state actions, as pure functions (the UI only calls them).</summary>
public static class MatchOperations
{
    /// <summary>
    /// Reset: both teams back to blank with their slot colours, every map card cleared.
    /// The format bar, general settings, waiting screen, standings, bracket and links stay as they are.
    /// </summary>
    public static AppState Reset(AppState state) => StateDerivations.Apply(state with
    {
        Team1 = new Team { ColorHex = Defaults.Team1Color },
        Team2 = new Team { ColorHex = Defaults.Team2Color },
        Maps = state.Maps.Select(m => new MapEntry { Index = m.Index }).ToArray(),
    });

    /// <summary>
    /// Swap Teams: the panels trade places with everything in them (score included). A colour travels with its team only when it
    /// differs from the slot default; otherwise the slot keeps its own default. On the map cards the scores, picks and bans follow
    /// the teams, while <c>completed</c> stays where it is.
    /// </summary>
    public static AppState Swap(AppState state)
    {
        var t1 = state.Team1;
        var t2 = state.Team2;
        var t1Custom = !SameColour(t1.ColorHex, Defaults.Team1Color);
        var t2Custom = !SameColour(t2.ColorHex, Defaults.Team2Color);

        return StateDerivations.Apply(state with
        {
            Team1 = t2 with { ColorHex = t2Custom ? t2.ColorHex : Defaults.Team1Color },
            Team2 = t1 with { ColorHex = t1Custom ? t1.ColorHex : Defaults.Team2Color },
            Maps = state.Maps.Select(SwapMap).ToArray(),
        });
    }

    private static MapEntry SwapMap(MapEntry m) => m with
    {
        T1 = m.T2,
        T2 = m.T1,
        Pick = m.Pick switch { Picks.Team1 => Picks.Team2, Picks.Team2 => Picks.Team1, var other => other },
        T1Ban = m.T2Ban,
        T2Ban = m.T1Ban,
    };

    private static bool SameColour(string? a, string b) =>
        string.Equals((a ?? "").Trim(), b, StringComparison.OrdinalIgnoreCase);
}

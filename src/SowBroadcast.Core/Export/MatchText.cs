using System.Globalization;
using SowBroadcast.Core.Model;

namespace SowBroadcast.Core.Export;

public static class MatchText
{
    private const string Separator = "      •      ";
    private const string Padding = "                              "; // 30 spaces; the overlay ticker scrolls through it

    /// <summary>"Team1 (a - b) Team2" followed by "Map (x - y)" for every map that has a name.</summary>
    public static string Build(AppState state)
    {
        var parts = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"{state.Team1.Name.Trim()} ({state.Team1.Score} - {state.Team2.Score}) {state.Team2.Name.Trim()}"),
        };

        foreach (var map in state.Maps)
        {
            var name = map.Map.Trim();
            if (name.Length > 0)
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"{name} ({map.T1} - {map.T2})"));
        }

        return string.Join(Separator, parts).Trim() + Padding;
    }
}

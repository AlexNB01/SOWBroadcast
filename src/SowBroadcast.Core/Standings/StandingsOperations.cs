using SowBroadcast.Core.Model;

namespace SowBroadcast.Core.Standings;

/// <summary>Standings rules of the Standings tab, as pure functions.</summary>
public static class StandingsOperations
{
    public static readonly IReadOnlyList<string> Statuses = new[] { "", "qualified", "eliminated" };

    /// <summary>A row with no team, abbreviation or logo and no numbers is an empty table line and is left out.</summary>
    public static bool IsBlank(StandingsRow row) =>
        string.IsNullOrWhiteSpace(row.TeamName) && string.IsNullOrWhiteSpace(row.Abbr) && string.IsNullOrWhiteSpace(row.LogoPath) &&
        row.Wins == 0 && row.Losses == 0 && row.MapDiff == 0 && row.Points == 0;

    public static IReadOnlyList<StandingsRow> WithoutBlanks(IEnumerable<StandingsRow> rows) =>
        rows.Where(r => !IsBlank(r)).ToArray();

    /// <summary>
    /// Update + sort: empty lines go, the rest is ordered by points, then map difference, then team name (case-insensitive),
    /// and every row gets its place 1..n (replacing any rank typed by hand).
    /// </summary>
    public static IReadOnlyList<StandingsRow> SortAndRank(IEnumerable<StandingsRow> rows) =>
        WithoutBlanks(rows)
            .OrderByDescending(r => r.Points)
            .ThenByDescending(r => r.MapDiff)
            .ThenBy(r => r.TeamName.ToLowerInvariant(), StringComparer.Ordinal)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToArray();

    /// <summary>
    /// The teams of all groups for the bracket's team list, best rank first (an unranked row counts as 9999) then by name;
    /// rows without a team name are skipped.
    /// </summary>
    public static IReadOnlyList<TeamRef> TeamsFromStandings(StandingsSettings standings)
    {
        var rows = standings.Groups.Count > 0 ? standings.Groups.SelectMany(g => g.Rows) : standings.Rows;
        return rows
            .OrderBy(r => r.Rank > 0 ? r.Rank : 9999)
            .ThenBy(r => r.TeamName.ToLowerInvariant(), StringComparer.Ordinal)
            .Where(r => !string.IsNullOrWhiteSpace(r.TeamName))
            .Select(r => new TeamRef { Name = r.TeamName.Trim(), Abbr = r.Abbr, LogoPath = r.LogoPath })
            .ToArray();
    }
}

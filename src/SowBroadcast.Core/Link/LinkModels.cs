namespace SowBroadcast.Core.Link;

// The neutral picture of a SOWDraft tournament that both sources (the new /broadcast API and the legacy public REST + draft
// room) produce. Everything downstream (mapping to the match tab, standings and bracket) only knows these records.

public sealed record LinkPlayer(string? Name, string? Battletag, string? Discord, string? Role, IReadOnlyList<string> SecondaryRoles);

public sealed record LinkTeam(string Id, string Name, string? Abbr, string? Color, string? LogoUrl, IReadOnlyList<LinkPlayer> Players);

public sealed record LinkStage(string Id, string Name, string Format, int? BestOf, int PtsWin = 3, int PtsDraw = 1, int PtsLoss = 0);

public sealed record LinkMatch(
    string Id, string? StageId, string? Bracket, string? GroupName, int Round, int MatchNumber,
    string? Team1Id, string? Team2Id, int? Score1, int? Score2, string? WinnerTeamId, string Status,
    long? ScheduledAt, string? DraftMatchId, int? BestOf);

public sealed record LiveBan(string? Team, string Hero);

/// <summary><c>Winner</c> is "team1", "team2", "draw" or null.</summary>
public sealed record LiveResult(string? Winner, int? Team1Score, int? Team2Score);

/// <summary>One map of the series in the order played. <c>PickedBy</c> and ban teams are "team1", "team2" or null; <c>Status</c> is "current" or "decided".</summary>
public sealed record LiveMap(
    int Order, string Map, string? GameMode, string? PickedBy, bool Auto,
    IReadOnlyList<LiveBan> Bans, string Status, LiveResult? Result);

public sealed record LiveState(
    string? TournamentMatchId, string? DraftMatchId, string? Team1Id, string? Team2Id,
    string Phase, int MapCount, int Score1, int Score2, IReadOnlyList<LiveMap> Maps);

public sealed record ServerStandingsEntry(string Id, string Name, int Wins, int Losses, int MapDiff, int Points);

public sealed record ServerStandingsGroup(string Key, IReadOnlyList<ServerStandingsEntry> Entries);

/// <summary>A tournament as the link sees it. <c>Standings</c> is the server's own table (not staged tournaments); <c>Live</c> is only set for the selected match.</summary>
public sealed record TournamentSnapshot
{
    public string Id { get; init; } = "";
    public string? Slug { get; init; }
    public string Name { get; init; } = "";
    public string Format { get; init; } = "";
    public int? BestOf { get; init; }
    public long Version { get; init; }
    public string Origin { get; init; } = "";
    public IReadOnlyList<LinkStage> Stages { get; init; } = Array.Empty<LinkStage>();
    public IReadOnlyList<LinkTeam> Teams { get; init; } = Array.Empty<LinkTeam>();
    public IReadOnlyList<LinkMatch> Matches { get; init; } = Array.Empty<LinkMatch>();
    public IReadOnlyList<ServerStandingsGroup>? Standings { get; init; }
    public LiveState? Live { get; init; }

    public LinkTeam? TeamById(string? id) => string.IsNullOrEmpty(id) ? null : Teams.FirstOrDefault(t => t.Id == id);
    public LinkMatch? MatchById(string? id) => string.IsNullOrEmpty(id) ? null : Matches.FirstOrDefault(m => m.Id == id);
}

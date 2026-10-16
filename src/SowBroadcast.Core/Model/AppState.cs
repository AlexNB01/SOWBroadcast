namespace SowBroadcast.Core.Model;

// The persisted state. Property names serialise to the legacy snake_case keys
// (see StateStore.Options), so old autosave.json / *.sowbroadcast.json files load unchanged.

public sealed record Player
{
    public string Name { get; init; } = "";
    public string Hero { get; init; } = "";
    public string Role { get; init; } = "";
}

public sealed record Team
{
    public string Name { get; init; } = "";
    public string Abbr { get; init; } = "";
    public string? LogoPath { get; init; }
    public int Score { get; init; }
    public string ColorHex { get; init; } = "#FFFFFF";
    public IReadOnlyList<Player> Players { get; init; } = EmptyPlayers();
    public string BannedHero { get; init; } = "";

    public static IReadOnlyList<Player> EmptyPlayers() =>
        Enumerable.Range(0, Limits.PlayersPerTeam).Select(_ => new Player()).ToArray();
}

public sealed record MapEntry
{
    public int Index { get; init; }
    public string Map { get; init; } = "";
    public int T1 { get; init; }
    public int T2 { get; init; }
    public bool Completed { get; init; }
    /// <summary>"—" (em dash, TBA), "T1" or "T2".</summary>
    public string Pick { get; init; } = Picks.Tba;
    public string T1Ban { get; init; } = "";
    public string T2Ban { get; init; } = "";
    /// <summary>Derived from the scores: "t1", "t2" or "".</summary>
    public string Winner { get; init; } = "";
}

public sealed record GeneralSettings
{
    public int FirstTo { get; init; } = 2;
    public int MapsCount { get; init; } = Limits.MapsPerSeries;
    public int NumPlayers { get; init; } = 5;
    public string Host { get; init; } = "";
    public string Caster1 { get; init; } = "";
    public string Caster2 { get; init; } = "";
    public string StatusText { get; init; } = "";
    public string? OverlayLogoPath { get; init; }
    public string? TransitionLogoPath { get; init; }
    public IReadOnlyDictionary<string, string> Colors { get; init; } = DefaultColors();

    public static IReadOnlyDictionary<string, string> DefaultColors() => new Dictionary<string, string>
    {
        ["primary"] = "#FFFFFF",
        ["secondary"] = "#000000",
        ["tertiary"] = "#55aaff",
        ["quaternary"] = "#006ea1",
        ["quinary"] = "#FFFFFF",
        ["senary"] = "#FFFFFF",
        ["septenary"] = "#FFFFFF",
        ["octonary"] = "#006ea1",
    };
}

public sealed record WaitingSettings
{
    public string VideosDir { get; init; } = "";
    /// <summary>Remaining seconds.</summary>
    public int TimerSeconds { get; init; }
    public string TextStarting { get; init; } = "STARTING SOON!";
    public string TextBrb { get; init; } = "BE RIGHT BACK!";
    public string TextEnd { get; init; } = "THANK YOU FOR WATCHING";
    public bool TimerRunning { get; init; }
    /// <summary>While the countdown runs: when it reaches zero, in epoch milliseconds (the overlays count down to this themselves).</summary>
    public long TimerEndsAtMs { get; init; }
    public IReadOnlyDictionary<string, string> Socials { get; init; } = new Dictionary<string, string>();
    public string TickerOverride { get; init; } = "";
    public bool TickerOverrideEnabled { get; init; }
}

public sealed record StandingsRow
{
    public string TeamName { get; init; } = "";
    public string Abbr { get; init; } = "";
    public string? LogoPath { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int MapDiff { get; init; }
    public int Points { get; init; }
    /// <summary>"", "qualified" or "eliminated".</summary>
    public string Status { get; init; } = "";
    public int Rank { get; init; }
    public int? Played { get; init; }
    public int? Draws { get; init; }
    public int? Sf { get; init; }
    public int? Sa { get; init; }
}

public sealed record StandingsGroup
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public IReadOnlyList<StandingsRow> Rows { get; init; } = Array.Empty<StandingsRow>();
}

public sealed record StandingsColumns
{
    public string Mode { get; init; } = "map_diff";
}

public sealed record StandingsSettings
{
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public StandingsColumns Columns { get; init; } = new();
    public IReadOnlyList<StandingsRow> Rows { get; init; } = Array.Empty<StandingsRow>();
    public IReadOnlyList<StandingsGroup> Groups { get; init; } = Array.Empty<StandingsGroup>();
    /// <summary>"__all__" or a group key.</summary>
    public string DisplayGroup { get; init; } = "__all__";
}

public sealed record TeamRef
{
    public string Name { get; init; } = "";
    public string Abbr { get; init; } = "";
    public string? LogoPath { get; init; }
}

public sealed record BracketMatch
{
    public string Id { get; init; } = "";
    public string BoLabel { get; init; } = "";
    public TeamRef Team1 { get; init; } = new();
    public TeamRef Team2 { get; init; } = new();
    public int Score1 { get; init; }
    public int Score2 { get; init; }
    public string Status { get; init; } = "";
    /// <summary>Epoch milliseconds, 0 = not scheduled.</summary>
    public long ScheduledAt { get; init; }
}

public sealed record BracketRound
{
    public string Name { get; init; } = "";
    /// <summary>"upper", "lower" or "grand".</summary>
    public string Side { get; init; } = "upper";
    public IReadOnlyList<BracketMatch> Matches { get; init; } = Array.Empty<BracketMatch>();
}

public sealed record BracketSettings
{
    public string Title { get; init; } = "";
    public string Stage { get; init; } = "";
    public IReadOnlyList<BracketRound> Rounds { get; init; } = Array.Empty<BracketRound>();
    /// <summary>"", "upper" or "lower".</summary>
    public string DoubleElimView { get; init; } = "";
    public IReadOnlyList<TeamRef> Teams { get; init; } = Enumerable.Range(0, Limits.BracketTeams).Select(_ => new TeamRef()).ToArray();
}

public sealed record AppState
{
    /// <summary>0 = legacy file without a version, 1 = first SOW Broadcast 3.x format.</summary>
    public int SchemaVersion { get; init; } = StateSchema.Current;

    public Team Team1 { get; init; } = new() { ColorHex = Defaults.Team1Color };
    public Team Team2 { get; init; } = new() { ColorHex = Defaults.Team2Color };
    public IReadOnlyList<MapEntry> Maps { get; init; } = DefaultMaps();
    /// <summary>1-based index of the first visible, not completed map; derived.</summary>
    public int? CurrentMap { get; init; }
    public GeneralSettings General { get; init; } = new();
    public WaitingSettings Waiting { get; init; } = new();
    public StandingsSettings Standings { get; init; } = new();
    public BracketSettings Bracket { get; init; } = new();
    public string DraftLinkUrl { get; init; } = "";
    public string TournamentLinkUrl { get; init; } = "";
    public LinkSettings Link { get; init; } = new();

    public static AppState CreateDefault() => new();

    public static IReadOnlyList<MapEntry> DefaultMaps() =>
        Enumerable.Range(1, Limits.MapsPerSeries).Select(i => new MapEntry { Index = i }).ToArray();
}

/// <summary>
/// The SOWDraft link as it is saved, so a restart can connect again: the tournament, the match the user chose, which side is
/// which, and the last value received for every linked field (so a manual correction is only overwritten when SOWDraft itself changes).
/// </summary>
public sealed record LinkSettings
{
    public string TournamentUrl { get; init; } = "";
    public string Origin { get; init; } = "";
    public string TournamentId { get; init; } = "";
    public string SelectedMatchId { get; init; } = "";
    public bool SidesSwapped { get; init; }
    /// <summary>A draft room address linked by hand ("Advanced"), instead of a tournament.</summary>
    public string ManualDraftUrl { get; init; } = "";
    public IReadOnlyDictionary<string, string> Received { get; init; } = new Dictionary<string, string>();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLinked => TournamentId.Length > 0 || ManualDraftUrl.Length > 0;
}

public static class StateSchema
{
    public const int Current = 1;
}

public static class Limits
{
    public const int PlayersPerTeam = 8;
    public const int MapsPerSeries = 7;
    public const int BracketTeams = 16;
}

public static class Picks
{
    public const string Tba = "—";
    public const string Team1 = "T1";
    public const string Team2 = "T2";
}

public static class Defaults
{
    public const string Team1Color = "#55aaff";
    public const string Team2Color = "#ff557f";
}

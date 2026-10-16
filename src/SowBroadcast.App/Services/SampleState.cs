#if DEBUG
using SowBroadcast.Core.Model;

namespace SowBroadcast.App.Services;

/// <summary>Synthetic match used from the Debug menu to look at the overlays without typing data in.</summary>
public static class SampleState
{
    public static AppState Create()
    {
        static Team TeamOf(string name, string abbr, string color, int score, params (string Name, string Hero, string Role)[] players) => new()
        {
            Name = name, Abbr = abbr, ColorHex = color, Score = score,
            Players = players.Select(p => new Player { Name = p.Name, Hero = p.Hero, Role = p.Role })
                .Concat(Enumerable.Range(0, Limits.PlayersPerTeam).Select(_ => new Player())).Take(Limits.PlayersPerTeam).ToArray(),
        };

        // Pictures from the test fixtures when they are there (a development checkout), so the overlays show logos too.
        string? Fixture(string name)
        {
            var path = Path.Combine(SowBroadcast.Core.Storage.AppPaths.ContentRoot, "src", "SowBroadcast.Tests", "Fixtures", "logos", name);
            return File.Exists(path) ? path : null;
        }

        var maps = AppState.DefaultMaps().ToArray();
        maps[0] = maps[0] with { Map = "Busan", T1 = 2, T2 = 1, Completed = true, Pick = Picks.Team1, T1Ban = "Ana", T2Ban = "Ashe" };
        maps[1] = maps[1] with { Map = "Circuit Royal", Pick = Picks.Team2, T1Ban = "Echo", T2Ban = "Genji" };
        maps[2] = maps[2] with { Map = "Ilios" };

        static StandingsRow Row(string team, string abbr, int w, int l, int diff, int pts, string status = "") =>
            new() { TeamName = team, Abbr = abbr, Wins = w, Losses = l, MapDiff = diff, Points = pts, Status = status };

        var teams = new[] { "Northern Lights", "Midnight Sun", "Aurora Guard", "Polar Wolves" };
        var bracketIds = new SowBroadcast.Core.Bracket.MatchIdSource(() => 1_760_000_000);
        var rounds = SowBroadcast.Core.Bracket.BracketTemplates.Build(SowBroadcast.Core.Bracket.BracketTemplate.Double8, bracketIds)
            .Select((r, i) => i == 0
                ? r with { Matches = r.Matches.Select((m, j) => m with
                {
                    Team1 = new TeamRef { Name = teams[j % 4] },
                    Team2 = new TeamRef { Name = teams[(j + 1) % 4] },
                    Score1 = 2, Score2 = j,
                }).ToArray() }
                : r)
            .ToArray();

        var sample = AppState.CreateDefault() with
        {
            Standings = new StandingsSettings
            {
                Title = "Group Stage", Subtitle = "Synthetic Cup",
                Groups = new[]
                {
                    new StandingsGroup { Key = "group_1", Name = "Group A", Rows = new[]
                    {
                        Row("Midnight Sun", "MS", 2, 1, 1, 6), Row("Northern Lights", "NL", 3, 0, 5, 9, "qualified"), Row("Aurora Guard", "AG", 0, 3, -6, 0, "eliminated"),
                    } },
                    new StandingsGroup { Key = "group_2", Name = "Group B", Rows = new[] { Row("Polar Wolves", "PW", 1, 1, 0, 3) } },
                },
                DisplayGroup = "__all__",
            },
            Bracket = new BracketSettings
            {
                Title = "Playoffs", Stage = "Quarterfinals", Rounds = rounds, DoubleElimView = "upper",
                Teams = teams.Select(t => new TeamRef { Name = t }).Concat(Enumerable.Range(0, 12).Select(_ => new TeamRef())).ToArray(),
            },
            Team1 = TeamOf("Northern Lights", "NL", "#27aae1", 1,
                ("Aurora", "Doomfist", "Tank"), ("Borealis", "Ashe", "Damage"), ("Cirrus", "Genji", "Damage"),
                ("Dawn", "Ana", "Support"), ("Ember", "Baptiste", "Support")),
            Team2 = TeamOf("Midnight Sun", "MS", "#c80013", 0,
                ("Zenith", "Doomfist", "Tank"), ("Yarrow", "Echo", "Damage"), ("Xenon", "Cassidy", "Damage"),
                ("Willow", "Brigitte", "Support"), ("Vesper", "Ana", "Support")),
            Maps = maps,
            General = new GeneralSettings
            {
                OverlayLogoPath = Fixture("overlay.png"), TransitionLogoPath = Fixture("overlay.png"),
                MapsCount = 3, Host = "Host H", Caster1 = "Caster One @casterone", Caster2 = "Caster Two",
                StatusText = "Synthetic Cup – Match 1",
            },
            Waiting = new WaitingSettings { TimerSeconds = 300, Socials = new Dictionary<string, string> { ["twitch"] = "SuomiOW" } },
        };
        return sample with
        {
            Team1 = sample.Team1 with { LogoPath = Fixture("team1.png") },
            Team2 = sample.Team2 with { LogoPath = Fixture("team2.png") },
        };
    }
}
#endif

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

        var maps = AppState.DefaultMaps().ToArray();
        maps[0] = maps[0] with { Map = "Busan", T1 = 2, T2 = 1, Completed = true, Pick = Picks.Team1, T1Ban = "Ana", T2Ban = "Ashe" };
        maps[1] = maps[1] with { Map = "Circuit Royal", Pick = Picks.Team2, T1Ban = "Echo", T2Ban = "Genji" };
        maps[2] = maps[2] with { Map = "Ilios" };

        return AppState.CreateDefault() with
        {
            Team1 = TeamOf("Northern Lights", "NL", "#27aae1", 1,
                ("Aurora", "Doomfist", "Tank"), ("Borealis", "Ashe", "Damage"), ("Cirrus", "Genji", "Damage"),
                ("Dawn", "Ana", "Support"), ("Ember", "Baptiste", "Support")),
            Team2 = TeamOf("Midnight Sun", "MS", "#c80013", 0,
                ("Zenith", "Doomfist", "Tank"), ("Yarrow", "Echo", "Damage"), ("Xenon", "Cassidy", "Damage"),
                ("Willow", "Brigitte", "Support"), ("Vesper", "Ana", "Support")),
            Maps = maps,
            General = new GeneralSettings
            {
                MapsCount = 3, Host = "Host H", Caster1 = "Caster One @casterone", Caster2 = "Caster Two",
                StatusText = "Synthetic Cup – Match 1",
            },
            Waiting = new WaitingSettings { TimerSeconds = 300, Socials = new Dictionary<string, string> { ["twitch"] = "SuomiOW" } },
        };
    }
}
#endif

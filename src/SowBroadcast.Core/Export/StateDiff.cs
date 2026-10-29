using System.Text.Json;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;
using SowBroadcast.Core.Waiting;

namespace SowBroadcast.Core.Export;

/// <summary>
/// Which overlay data changed between two states. The key vocabulary is what the overlays listen for
/// (CLAUDE.md 5.2). With no previous state every key is reported.
/// </summary>
public static class StateDiff
{
    public static readonly IReadOnlyList<string> AllKeys = new[]
    {
        "assets.heroes", "assets.maps", "assets.modes",
        "general.colors",
        "t1.name", "t1.score", "t1.color", "t1.logo", "t1.ban", "t1.abbr", "t1.players",
        "t2.name", "t2.score", "t2.color", "t2.logo", "t2.ban", "t2.abbr", "t2.players",
        "general.caster1", "general.caster2", "general.host", "general.logo", "general.first_to", "general.status",
        "waiting.texts", "waiting.timer", "waiting.videos", "waiting.socials",
        "maps", "standings", "bracket", "matchtext", "schedule",
    };

    private static readonly HashSet<string> MatchTextTriggers =
        new() { "maps", "t1.name", "t1.score", "t2.name", "t2.score", "t1.ban", "t2.ban" };

    public static IReadOnlyList<string> ChangedKeys(AppState? previous, AppState current)
    {
        if (previous is null) return AllKeys;

        var keys = new List<string>();
        void Add(string key) { if (!keys.Contains(key)) keys.Add(key); }

        var og = previous.General;
        var ng = current.General;
        if (!SameDictionary(og.Colors, ng.Colors)) Add("general.colors");
        if (og.MapsCount != ng.MapsCount) Add("general.first_to");
        if ((og.OverlayLogoPath ?? "") != (ng.OverlayLogoPath ?? "") ||
            (og.TransitionLogoPath ?? "") != (ng.TransitionLogoPath ?? "")) Add("general.logo");
        if (og.StatusText.Trim() != ng.StatusText.Trim()) Add("general.status");

        var ow = previous.Waiting;
        var nw = current.Waiting;
        if (ow.TextStarting.Trim() != nw.TextStarting.Trim() ||
            ow.TextBrb.Trim() != nw.TextBrb.Trim() ||
            ow.TextEnd.Trim() != nw.TextEnd.Trim()) Add("waiting.texts");
        if (ow.TimerSeconds != nw.TimerSeconds || ow.TimerRunning != nw.TimerRunning) Add("waiting.timer");
        if (ow.VideosDir.Trim() != nw.VideosDir.Trim()) Add("waiting.videos");
        if (!SameDictionary(ow.Socials, nw.Socials)) Add("waiting.socials");
        if (ow.TickerOverride.Trim() != nw.TickerOverride.Trim() || ow.TickerOverrideEnabled != nw.TickerOverrideEnabled)
            Add("matchtext");

        CompareTeam("t1", previous.Team1, current.Team1, Add);
        CompareTeam("t2", previous.Team2, current.Team2, Add);
        if (PlayersChanged(previous.Team1, current.Team1)) Add("t1.players");
        if (PlayersChanged(previous.Team2, current.Team2)) Add("t2.players");

        if (og.Caster1.Trim() != ng.Caster1.Trim()) Add("general.caster1");
        if (og.Caster2.Trim() != ng.Caster2.Trim()) Add("general.caster2");
        if (og.Host.Trim() != ng.Host.Trim()) Add("general.host");

        if (previous.CurrentMap != current.CurrentMap || MapsChanged(previous.Maps, current.Maps)) Add("maps");
        if (Section(previous.Standings) != Section(current.Standings)) Add("standings");
        if (Section(previous.Bracket) != Section(current.Bracket)) Add("bracket");
        // What the waiting screens list (not the whole tournament: a match far in the future changing is no news).
        if (ScheduleOverlay.BuildJson(previous).ToJsonString() != ScheduleOverlay.BuildJson(current).ToJsonString()) Add("schedule");

        if (keys.Any(MatchTextTriggers.Contains)) Add("matchtext");
        return keys;
    }

    private static void CompareTeam(string prefix, Team o, Team n, Action<string> add)
    {
        if (o.Name != n.Name) add($"{prefix}.name");
        if (o.Score != n.Score) add($"{prefix}.score");
        if (o.ColorHex != n.ColorHex) add($"{prefix}.color");
        if (o.LogoPath != n.LogoPath) add($"{prefix}.logo");
        if (o.BannedHero != n.BannedHero) add($"{prefix}.ban");
        if (o.Abbr != n.Abbr) add($"{prefix}.abbr");
    }

    private static bool PlayersChanged(Team o, Team n)
    {
        if (o.Players.Count != n.Players.Count) return true;
        for (var i = 0; i < o.Players.Count; i++)
        {
            var a = o.Players[i];
            var b = n.Players[i];
            if (a.Name.Trim() != b.Name.Trim() || a.Role.Trim() != b.Role.Trim() || a.Hero.Trim() != b.Hero.Trim())
                return true;
        }
        return false;
    }

    private static bool MapsChanged(IReadOnlyList<MapEntry> o, IReadOnlyList<MapEntry> n)
    {
        if (o.Count != n.Count) return true;
        for (var i = 0; i < o.Count; i++)
        {
            var a = o[i];
            var b = n[i];
            if (a.Map != b.Map || a.T1 != b.T1 || a.T2 != b.T2 || a.Completed != b.Completed ||
                a.Pick != b.Pick || a.T1Ban != b.T1Ban || a.T2Ban != b.T2Ban) return true;
        }
        return false;
    }

    private static bool SameDictionary(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    private static string Section<T>(T value) => JsonSerializer.Serialize(value, StateStore.Options);
}

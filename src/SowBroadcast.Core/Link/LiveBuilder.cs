using System.Text.RegularExpressions;
using System.Text.Json;
using static SowBroadcast.Core.Link.SnapshotParser;

namespace SowBroadcast.Core.Link;

/// <summary>
/// Builds the slot-resolved live state of a series from a draft room's <c>update-state</c> payload (a port of SOWDraft's
/// <c>lib/liveState.js</c>). The draft stores team <i>names</i> in its summary; they are resolved here to the stable
/// "team1" / "team2" slots so nothing downstream compares names. Used by the legacy source; the new API sends it ready-made.
/// </summary>
public static class LiveBuilder
{
    private static readonly Regex ScorePattern = new(@"^\s*(-?\d+)\s*-\s*(-?\d+)\s*$", RegexOptions.Compiled);

    public static LiveState Build(JsonElement room, string? team1Id, string? team2Id)
    {
        var name1 = Str(room, "team1Name");
        var name2 = Str(room, "team2Name");
        string? SlotOf(string? name)
        {
            if (name is null) return null;
            if (name == name1 && name != name2) return "team1";
            if (name == name2 && name != name1) return "team2";
            return null;
        }

        var mapEntries = Array(room, "summary").Where(s => Str(s, "type") == "map").ToArray();
        var maps = new List<LiveMap>();
        for (var i = 0; i < mapEntries.Length; i++)
        {
            var s = mapEntries[i];
            var entries = Array(s, "entries").ToArray();
            var resultEntry = entries.Cast<JsonElement?>().FirstOrDefault(e => Str(e!.Value, "type") == "result");

            var bans = new List<LiveBan>();
            void AddBan(string? team, string? hero)
            {
                if (hero is null) return;
                var slot = SlotOf(team);
                if (!bans.Any(b => b.Team == slot && b.Hero == hero)) bans.Add(new LiveBan(slot, hero));
            }
            foreach (var e in entries.Where(e => Str(e, "type") == "ban")) AddBan(Str(e, "team"), Str(e, "value"));
            if (i == mapEntries.Length - 1 && resultEntry is null)
                foreach (var b in Array(room, "heroBans")) AddBan(Str(b, "team"), Str(b, "hero"));

            var auto = Bool(s, "auto");
            maps.Add(new LiveMap(
                i + 1, Str(s, "value") ?? "", Str(s, "gamemode"), auto ? null : SlotOf(Str(s, "pickedBy")), auto,
                bans, resultEntry is null ? "current" : "decided", resultEntry is { } r ? BuildResult(r, SlotOf) : null));
        }

        var score = Prop(room, "score");
        return new LiveState(
            Str(room, "tournamentMatchId"), Str(room, "matchId"), team1Id, team2Id,
            Str(room, "phase") ?? "", Int(room, "mapCount") ?? 0,
            score is { } a ? Int(a, "team1") ?? 0 : 0, score is { } b2 ? Int(b2, "team2") ?? 0 : 0, maps);
    }

    private static LiveResult BuildResult(JsonElement entry, Func<string?, string?> slotOf)
    {
        var winner = Str(entry, "winnerSlot");
        if (winner is null)
        {
            var text = Str(entry, "winner");
            winner = text is not null && text.Equals("draw", StringComparison.OrdinalIgnoreCase) ? "draw" : slotOf(text);
        }

        var t1 = Int(entry, "team1Score");
        var t2 = Int(entry, "team2Score");
        if (t1 is null && t2 is null && Str(entry, "score") is { } text2 && ScorePattern.Match(text2) is { Success: true } m)
        {
            t1 = int.Parse(m.Groups[1].Value);
            t2 = int.Parse(m.Groups[2].Value);
        }
        return new LiveResult(winner, t1, t2);
    }
}

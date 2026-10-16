using System.Text.RegularExpressions;
using SowBroadcast.Core.Model;

namespace SowBroadcast.Core.Link;

/// <param name="SidesSwapped">SOWDraft's team 1 goes to the Team 2 panel (the user's "Swap sides" switch).</param>
/// <param name="MapNames">The map catalogue the names are resolved against.</param>
/// <param name="LogoPathOf">The local file of a logo address, or null while it has not been downloaded yet.</param>
public sealed record LinkApplyOptions(bool SidesSwapped, IReadOnlyList<string> MapNames, Func<string, string?> LogoPathOf);

/// <param name="State">The state with the linked values filled in.</param>
/// <param name="Received">The last value received per field, to be stored with the link.</param>
/// <param name="Warnings">Things the user should look at (unknown map, more maps than shown...).</param>
/// <param name="LogosToFetch">Logo addresses that are not on disk yet; the caller downloads them and applies again.</param>
public sealed record LinkApplyResult(AppState State, IReadOnlyDictionary<string, string> Received, IReadOnlyList<string> Warnings, IReadOnlyList<string> LogosToFetch);

/// <summary>
/// Fills the Match tab from the selected tournament match and its live draft (CLAUDE.md 8.4): teams (name, abbreviation, colour, logo,
/// players), the score and the map cards. A field is only written when SOWDraft's value for it changed since the last time, so a
/// correction typed by the operator stays until SOWDraft itself changes that value.
/// </summary>
public static class MatchLinkMapper
{
    private static readonly Regex HexColour = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static LinkApplyResult Apply(AppState state, TournamentSnapshot snapshot, string matchId, LinkApplyOptions options,
        IReadOnlyDictionary<string, string> received)
    {
        var rec = new Dictionary<string, string>(received);
        var warnings = new List<string>();
        var logos = new List<string>();

        var match = snapshot.MatchById(matchId);
        if (match is null)
        {
            warnings.Add("The selected match is no longer in the tournament.");
            return new LinkApplyResult(state, rec, warnings, logos);
        }

        // Writes `value` for `key` when it differs from what was received last time. Returns true when the field should be written.
        // An empty value only counts once something non-empty was received before (so empty rows never wipe manual entries).
        bool Changed(string key, string value, out string? previous)
        {
            var had = rec.TryGetValue(key, out previous);
            rec[key] = value;
            if (had && previous == value) return false;
            return had || value.Length > 0;
        }

        // SOWDraft slot -> panel.
        string PanelOf(string? slot) => slot switch
        {
            "team1" => options.SidesSwapped ? "t2" : "t1",
            "team2" => options.SidesSwapped ? "t1" : "t2",
            _ => "",
        };

        var team1 = state.Team1;
        var team2 = state.Team2;
        void Put(string panel, Func<Team, Team> change)
        {
            if (panel == "t1") team1 = change(team1);
            else if (panel == "t2") team2 = change(team2);
        }

        foreach (var (slot, source) in new[] { ("team1", snapshot.TeamById(match.Team1Id)), ("team2", snapshot.TeamById(match.Team2Id)) })
        {
            if (source is null) continue;
            var panel = PanelOf(slot);
            var current = panel == "t1" ? team1 : team2;

            if (Changed($"{panel}.name", source.Name, out _)) current = current with { Name = source.Name };

            var abbr = source.Abbr ?? LinkNaming.SuggestAbbreviation(source.Name);
            if (abbr.Length > 0 && Changed($"{panel}.abbr", abbr, out _)) current = current with { Abbr = abbr };

            if (source.Color is { } colour && HexColour.IsMatch(colour) && Changed($"{panel}.color", colour, out _))
                current = current with { ColorHex = colour.ToLowerInvariant() };

            if (source.LogoUrl is { } url)
            {
                if (options.LogoPathOf(url) is { } local)
                {
                    if (Changed($"{panel}.logo", url, out _)) current = current with { LogoPath = local };
                }
                else logos.Add(url);
            }

            var players = current.Players.ToArray();
            for (var i = 0; i < players.Length; i++)
            {
                var p = players[i];
                var hasSource = i < source.Players.Count;
                var name = hasSource ? LinkNaming.PlayerName(source.Players[i]) : "";
                var role = hasSource ? LinkNaming.RoleOf(source.Players[i].Role, source.Players[i].SecondaryRoles) : "";

                if (Changed($"{panel}.p{i}.name", name, out var previousName))
                {
                    // A different player in this row: the hero of the previous one no longer applies.
                    p = p with { Name = name, Hero = previousName is not null && previousName != name ? "" : p.Hero };
                }
                if (Changed($"{panel}.p{i}.role", role, out _)) p = p with { Role = role };
                players[i] = p;
            }
            current = current with { Players = players };

            if (panel == "t1") team1 = current; else team2 = current;
        }

        var maps = state.Maps.ToArray();
        if (snapshot.Live is { } live)
        {
            // Match score, per panel.
            Put(PanelOf("team1"), t => Changed($"{PanelOf("team1")}.score", live.Score1.ToString(), out _) ? t with { Score = live.Score1 } : t);
            Put(PanelOf("team2"), t => Changed($"{PanelOf("team2")}.score", live.Score2.ToString(), out _) ? t with { Score = live.Score2 } : t);

            if (live.Maps.Count > state.General.MapsCount)
                warnings.Add($"The series has {live.Maps.Count} maps but the format bar shows {state.General.MapsCount}.");

            foreach (var lm in live.Maps)
            {
                if (lm.Order < 1 || lm.Order > maps.Length) continue;
                var card = maps[lm.Order - 1];
                var key = $"m{lm.Order}";

                var resolved = LinkNaming.ResolveName(lm.Map, options.MapNames);
                if (resolved is null && lm.Map.Length > 0) warnings.Add($"Map \"{lm.Map}\" is not in the map list.");
                var mapName = resolved ?? lm.Map;
                if (mapName.Length > 0 && Changed($"{key}.map", mapName, out _)) card = card with { Map = mapName };

                var pick = PanelOf(lm.PickedBy) switch { "t1" => Picks.Team1, "t2" => Picks.Team2, _ => Picks.Tba };
                if (Changed($"{key}.pick", pick, out _)) card = card with { Pick = pick };

                // One ban per team per map on the card.
                var ban1 = lm.Bans.FirstOrDefault(b => PanelOf(b.Team) == "t1")?.Hero ?? "";
                var ban2 = lm.Bans.FirstOrDefault(b => PanelOf(b.Team) == "t2")?.Hero ?? "";
                if (Changed($"{key}.t1ban", ban1, out _)) card = card with { T1Ban = ban1 };
                if (Changed($"{key}.t2ban", ban2, out _)) card = card with { T2Ban = ban2 };

                var decided = lm.Status == "decided";
                if (decided && lm.Result is { } result)
                {
                    var (s1, s2) = ScoresOf(result);
                    // Scores are given per SOWDraft slot; the card's T1 is the Team 1 panel.
                    var (t1Score, t2Score) = options.SidesSwapped ? (s2, s1) : (s1, s2);
                    if (Changed($"{key}.t1", t1Score.ToString(), out _)) card = card with { T1 = t1Score };
                    if (Changed($"{key}.t2", t2Score.ToString(), out _)) card = card with { T2 = t2Score };
                }
                var completed = decided ? "1" : "0";
                if (Changed($"{key}.completed", completed, out _)) card = card with { Completed = decided };

                maps[lm.Order - 1] = card;
            }
        }

        var result2 = StateDerivations.Apply(state with { Team1 = team1, Team2 = team2, Maps = maps });
        return new LinkApplyResult(result2, rec, warnings, logos.Distinct().ToArray());
    }

    /// <summary>The scores of a finished map: as given; else a win counts 1–0; a draw without scores 0–0.</summary>
    internal static (int, int) ScoresOf(LiveResult result)
    {
        if (result.Team1Score is { } a && result.Team2Score is { } b) return (a, b);
        return result.Winner switch { "team1" => (1, 0), "team2" => (0, 1), _ => (result.Team1Score ?? 0, result.Team2Score ?? 0) };
    }
}

using System.Text.Json;

namespace SowBroadcast.Core.Link;

/// <summary>Reads the two kinds of SOWDraft responses into a <see cref="TournamentSnapshot"/>. Tolerant of missing and unknown fields.</summary>
public static class SnapshotParser
{
    // ---- tolerant JSON access ----------------------------------------------------------------------------

    public static JsonElement? Prop(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    public static string? Str(JsonElement el, string name) =>
        Prop(el, name) is { } v ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null;

    public static int? Int(JsonElement el, string name)
    {
        if (Prop(el, name) is not { } v) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return (int)d;
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var parsed) ? parsed : null;
    }

    public static long? Long(JsonElement el, string name)
    {
        if (Prop(el, name) is not { } v) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l;
        return v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var parsed) ? parsed : null;
    }

    public static bool Bool(JsonElement el, string name) =>
        Prop(el, name) is { } v && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.GetDouble() != 0));

    public static IEnumerable<JsonElement> Array(JsonElement el, string name) =>
        Prop(el, name) is { ValueKind: JsonValueKind.Array } v ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    /// <summary>A value that is a JSON array either inline or as a JSON-encoded string (the legacy API does both).</summary>
    public static IEnumerable<JsonElement> ArrayOrEncoded(JsonElement el, string name)
    {
        if (Prop(el, name) is not { } v) return Enumerable.Empty<JsonElement>();
        if (v.ValueKind == JsonValueKind.Array) return v.EnumerateArray().ToArray();
        if (v.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString())) return Enumerable.Empty<JsonElement>();
        try
        {
            using var doc = JsonDocument.Parse(v.GetString()!);
            return doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.Clone().EnumerateArray().ToArray() : Enumerable.Empty<JsonElement>();
        }
        catch (JsonException) { return Enumerable.Empty<JsonElement>(); }
    }

    public static string Absolute(string origin, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        if (Uri.TryCreate(url, UriKind.Absolute, out var abs) && (abs.Scheme == "http" || abs.Scheme == "https")) return abs.ToString();
        return new Uri(new Uri(origin.TrimEnd('/') + "/"), url.TrimStart('/')).ToString();
    }

    // ---- /api/tournament-system/:id/broadcast ------------------------------------------------------------

    public static TournamentSnapshot FromBroadcast(string json, string origin)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var t = Prop(root, "tournament") ?? throw new FormatException("The response has no tournament.");

        var stages = Array(root, "stages").Select(s => new LinkStage(
            Str(s, "id") ?? "", Str(s, "name") ?? "", Str(s, "format") ?? "", Int(s, "bestOf"),
            Int(s, "ptsWin") ?? 3, Int(s, "ptsDraw") ?? 1, Int(s, "ptsLoss") ?? 0)).ToArray();

        var teams = Array(root, "teams").Select(tm => new LinkTeam(
            Str(tm, "id") ?? "", Str(tm, "name") ?? "", NullIfBlank(Str(tm, "abbr")), NullIfBlank(Str(tm, "color")),
            NullIfBlank(Str(tm, "logoUrl")),
            Array(tm, "players").Select(p => new LinkPlayer(
                Str(p, "name"), Str(p, "battletag"), Str(p, "discord"), Str(p, "role"),
                Array(p, "secondaryRoles").Select(r => r.GetString() ?? "").Where(r => r.Length > 0).ToArray(),
                NullIfBlank(Str(p, "hero")))).ToArray())).ToArray();

        var matches = Array(root, "matches").Select(m => new LinkMatch(
            Str(m, "id") ?? "", Str(m, "stageId"), Str(m, "bracket"), Str(m, "groupName"), Int(m, "round") ?? 0, Int(m, "matchNumber") ?? 0,
            Str(m, "team1Id"), Str(m, "team2Id"), Int(m, "score1"), Int(m, "score2"), Str(m, "winnerTeamId"), Str(m, "status") ?? "",
            Long(m, "scheduledAt"), Str(m, "draftMatchId"), Int(m, "bestOf"),
            Long(m, "startedAt"), Long(m, "completedAt"), NullIfBlank(Str(m, "roundName")), NullIfBlank(Str(m, "statusText")))).ToArray();

        var advancement = Array(root, "advancement").Select(a => new AdvancementEntry(
            Str(a, "stageId"), Str(a, "group"), Str(a, "teamId") ?? "", Str(a, "status") ?? ""))
            .Where(a => a.TeamId.Length > 0 && a.Status.Length > 0).ToArray();

        return new TournamentSnapshot
        {
            Id = Str(t, "id") ?? "",
            Slug = Str(t, "slug"),
            Name = Str(t, "name") ?? "",
            Format = Str(t, "format") ?? "",
            BestOf = Int(t, "bestOf"),
            Version = Long(root, "version") ?? 0,
            Origin = origin,
            Stages = stages,
            Teams = teams,
            Matches = matches,
            Standings = ParseStandings(Prop(root, "standings")),
            Advancement = advancement,
            Live = Prop(root, "live") is { ValueKind: JsonValueKind.Object } live ? ParseLive(live) : null,
        };
    }

    /// <summary>The <c>live</c> object of the broadcast API (also carried by <c>tournament-event</c>).</summary>
    public static LiveState ParseLive(JsonElement live)
    {
        var score = Prop(live, "score");
        return new LiveState(
            Str(live, "tournamentMatchId"), Str(live, "draftMatchId"), Str(live, "team1Id"), Str(live, "team2Id"),
            Str(live, "phase") ?? "", Int(live, "mapCount") ?? 0,
            score is { } s ? Int(s, "team1") ?? 0 : 0, score is { } s2 ? Int(s2, "team2") ?? 0 : 0,
            Array(live, "maps").Select(m => new LiveMap(
                Int(m, "order") ?? 0, Str(m, "map") ?? "", Str(m, "gamemode"), Str(m, "pickedBy"), Bool(m, "auto"),
                Array(m, "bans").Select(b => new LiveBan(Str(b, "team"), Str(b, "hero") ?? "")).ToArray(),
                Str(m, "status") ?? "",
                Prop(m, "result") is { ValueKind: JsonValueKind.Object } r
                    ? new LiveResult(Str(r, "winner"), Int(r, "team1Score"), Int(r, "team2Score"))
                    : null)).ToArray());
    }

    // ---- /api/tournament-system/:id (the legacy public response) -----------------------------------------

    public static TournamentSnapshot FromLegacy(string json, string origin)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var t = Prop(root, "tournament") ?? throw new FormatException("Tournament not found.");
        var tournamentBestOf = Int(t, "best_of");

        var stages = ArrayOrEncoded(t, "stages").Select(s => new LinkStage(
            Str(s, "id") ?? "", Str(s, "name") ?? "", Str(s, "format") ?? "", Int(s, "best_of"),
            Int(s, "pts_win") ?? 3, Int(s, "pts_draw") ?? 1, Int(s, "pts_loss") ?? 0)).ToArray();
        var bestOfByStage = stages.Where(s => s.BestOf is not null).ToDictionary(s => s.Id, s => s.BestOf);

        var teams = Array(root, "teams").Select(tm => new LinkTeam(
            Str(tm, "id") ?? "", Str(tm, "name") ?? "", NullIfBlank(Str(tm, "abbr")), NullIfBlank(Str(tm, "color")),
            NullIfBlank(Str(tm, "logo_url")) is { } logo ? Absolute(origin, logo) : null,
            Array(tm, "players").Select(p => new LinkPlayer(
                Str(p, "name"), Str(p, "battletag"), Str(p, "discord"), Str(p, "role"),
                Array(p, "secondary_roles").Select(r => r.GetString() ?? "").Where(r => r.Length > 0).ToArray())).ToArray())).ToArray();

        var matches = Array(root, "matches").Select(m =>
        {
            var stageId = Str(m, "stage_id");
            var bestOf = stageId is not null && bestOfByStage.TryGetValue(stageId, out var sb) ? sb : tournamentBestOf;
            return new LinkMatch(
                Str(m, "id") ?? "", stageId, Str(m, "bracket"), Str(m, "group_name"), Int(m, "round") ?? 0, Int(m, "match_number") ?? 0,
                Str(m, "team1_id"), Str(m, "team2_id"), Int(m, "score1"), Int(m, "score2"), Str(m, "winner_team_id"), Str(m, "status") ?? "",
                Long(m, "scheduled_at"), Str(m, "draft_match_id"), bestOf);
        }).ToArray();

        return new TournamentSnapshot
        {
            Id = Str(t, "id") ?? "",
            Slug = Str(t, "slug"),
            Name = Str(t, "name") ?? "",
            Format = Str(t, "format") ?? "",
            BestOf = tournamentBestOf,
            Origin = origin,
            Stages = stages,
            Teams = teams,
            Matches = matches,
            Standings = ParseStandings(Prop(root, "standings")),
        };
    }

    /// <summary>The server's table: an object of groups to entry lists, or a plain list (both appear).</summary>
    private static IReadOnlyList<ServerStandingsGroup>? ParseStandings(JsonElement? element)
    {
        static ServerStandingsEntry Entry(JsonElement e) => new(
            Str(e, "id") ?? "", Str(e, "name") ?? "", Int(e, "wins") ?? 0, Int(e, "losses") ?? 0, Int(e, "mapDiff") ?? 0, Int(e, "points") ?? 0);

        if (element is not { } el) return null;
        if (el.ValueKind == JsonValueKind.Array)
        {
            var entries = el.EnumerateArray().Select(Entry).ToArray();
            return entries.Length == 0 ? null : new[] { new ServerStandingsGroup("group_1", entries) };
        }
        if (el.ValueKind != JsonValueKind.Object) return null;
        var groups = el.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.Array)
            .Select(p => new ServerStandingsGroup(p.Name, p.Value.EnumerateArray().Select(Entry).ToArray()))
            .ToArray();
        return groups.Length == 0 ? null : groups;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

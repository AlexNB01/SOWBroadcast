using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Waiting;

namespace SowBroadcast.Core.Export;

/// <summary>
/// The one thing overlays read from the program: <c>GET /api/state</c> (CLAUDE.md EHD-2). The same JSON is rebuilt after every publish;
/// the SSE stream then only says which parts changed and which <c>version</c> to fetch.
/// </summary>
/// <remarks>
/// <c>match</c> has the shape of the legacy <c>match.json</c> (snake_case state without assets); the other parts are what the old
/// text and JSON files held, ready made: <c>matchtext</c>, the waiting <c>videos</c>, the <c>timer</c> with its end time, and the
/// <c>standings</c> and <c>bracket</c> objects as the overlays draw them. Local file paths are never part of it.
/// </remarks>
public static class OverlayStateJson
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Build(AppState state, ScoreboardExporter exporter, long version, long serverNowMs, JsonObject? replay = null)
    {
        var derived = StateDerivations.Apply(state);
        var match = LegacyMatchJson.BuildObject(derived);

        // Logo pictures are served as files (Match/T1Logo.png ...); the paths on this machine stay here.
        foreach (var team in new[] { "team1", "team2" })
            if (match[team] is JsonObject t) t["logo_path"] = null;
        if (match["general"] is JsonObject general)
        {
            general["overlay_logo_path"] = null;
            general["transition_logo_path"] = null;
        }
        if (match["standings"] is JsonObject) match.Remove("standings");
        if (match["bracket"] is JsonObject) match.Remove("bracket");

        var w = derived.Waiting;
        var root = new JsonObject
        {
            ["schema_version"] = SchemaVersion,
            ["version"] = version,
            ["server_time"] = serverNowMs,
            ["match"] = match,
            ["matchtext"] = MatchText.Build(derived),
            ["videos"] = new JsonArray(exporter.Videos.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
            ["timer"] = new JsonObject
            {
                ["seconds"] = w.TimerSeconds,
                ["running"] = w.TimerRunning,
                ["ends_at"] = w.TimerRunning ? w.TimerEndsAtMs : 0,
            },
            ["standings"] = exporter.BuildStandingsJson(derived),
            ["bracket"] = exporter.BuildBracketJson(derived),
            ["schedule"] = ScheduleOverlay.BuildJson(derived, exporter.NormalizeLogoPath),
            ["replay"] = replay ?? new JsonObject { ["latest"] = null, ["play"] = null },
        };
        return root.ToJsonString(Options);
    }
}

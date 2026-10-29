using System.Text.Json;
using System.Text.Json.Nodes;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Core.Export;

/// <summary>
/// The <c>match.json</c> the overlays read (MapDraft). Same shape the legacy app wrote: the state without
/// assets, plus the dead <c>map_pool</c>, <c>format_type</c> and <c>division</c> keys some overlays still look at.
/// </summary>
public static class LegacyMatchJson
{
    /// <summary>The <c>match.json</c> file. The short status text is left out: it exists only in the overlay state (<c>/api/state</c>).</summary>
    public static string Build(AppState state)
    {
        var root = BuildObject(state);
        root["general"]!.AsObject().Remove("short_status_text");
        return FileWriter.Crlf(root.ToJsonString(StateStore.Options));
    }

    public static JsonObject BuildObject(AppState state)
    {
        var derived = StateDerivations.Apply(state);
        var root = JsonSerializer.SerializeToNode(derived, StateStore.Options)!.AsObject();
        root.Remove("schema_version");
        root.Remove("link"); // the link settings are the app's own (they hold no overlay data)
        root["map_pool"] = new JsonArray();
        root.Remove("schedule"); // the waiting screens get the schedule from the overlay state
        if (root["waiting"] is JsonObject waiting)
        {
            waiting.Remove("timer_ends_at_ms"); // not part of the legacy shape (the overlay state has its own timer)
            foreach (var key in new[] { "schedule_enabled", "schedule_rows", "schedule_seconds", "schedule_video_seconds","schedule_stream_filter", "schedule_show_unknown" }) waiting.Remove(key);
        }

        var general = root["general"]!.AsObject();
        general["first_to"] = derived.General.MapsCount;
        general["format_type"] = "First to";
        general["division"] = "";

        return root;
    }
}

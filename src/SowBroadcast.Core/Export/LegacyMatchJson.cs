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
    public static string Build(AppState state)
    {
        var derived = StateDerivations.Apply(state);
        var root = JsonSerializer.SerializeToNode(derived, StateStore.Options)!.AsObject();
        root.Remove("schema_version");
        root["map_pool"] = new JsonArray();

        var general = root["general"]!.AsObject();
        general["first_to"] = derived.General.MapsCount;
        general["format_type"] = "First to";
        general["division"] = "";

        return FileWriter.Crlf(root.ToJsonString(StateStore.Options));
    }
}

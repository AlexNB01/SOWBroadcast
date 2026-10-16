using System.Text.Json;
using System.Text.Json.Nodes;

namespace SowBroadcast.Core.Assets;

/// <summary>One line of the manifest: the asset, and where its pictures can be downloaded when they are not installed yet.</summary>
public sealed record ManifestEntry(Asset Asset, string? ImageUrl, string? BodyImageUrl);

/// <summary>
/// The single list of every hero, map and game mode (EHD-10): name, slug, role or game mode, picture files and aliases.
/// It replaces the tables that used to be spread over the code (hero roles, full-body file names, spelling variants), and a new
/// hero or map is added by publishing a newer manifest. Heroes and maps in it show up in the app by themselves.
/// <code>
/// { "schema": 1, "revision": 3, "updated": "2026-10-16",
///   "heroes": [ { "name": "Lúcio", "slug": "lucio", "role": "Support", "image": "Heroes/lucio.png",
///                 "body_image": "HeroesBody/Lucio.png", "aliases": ["Lucio"] } ],
///   "maps":   [ { "name": "King's Row", "slug": "kings-row", "mode": "Hybrid", "image": "Maps/kings-row.png", "aliases": ["Kings Row"] } ],
///   "modes":  [ { "name": "Control", "slug": "control", "image": "Gametypes/control.png" } ] }
/// </code>
/// Optional <c>image_url</c> / <c>body_image_url</c> (https) tell the updater where to fetch pictures that are not installed yet.
/// </summary>
public sealed class AssetManifest
{
    public const int CurrentSchema = 1;

    public int Schema { get; init; } = CurrentSchema;

    /// <summary>Grows with every published manifest; the newest one wins.</summary>
    public int Revision { get; init; }

    public string Updated { get; init; } = "";

    public IReadOnlyDictionary<AssetCategory, IReadOnlyList<ManifestEntry>> Entries { get; init; } =
        AssetCategories.All.ToDictionary(c => c, _ => (IReadOnlyList<ManifestEntry>)Array.Empty<ManifestEntry>());

    public IReadOnlyList<ManifestEntry> Of(AssetCategory category) =>
        Entries.TryGetValue(category, out var list) ? list : Array.Empty<ManifestEntry>();

    /// <summary>Reads a manifest; throws <see cref="FormatException"/> with a readable message when it is not one.</summary>
    public static AssetManifest Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new FormatException("The manifest is not valid JSON.", ex); }
        if (root is not JsonObject obj) throw new FormatException("The manifest must be a JSON object.");

        var schema = obj["schema"]?.GetValue<int>() ?? CurrentSchema;
        if (schema > CurrentSchema) throw new FormatException($"The manifest is for a newer version of SOW Broadcast (schema {schema}).");

        var entries = new Dictionary<AssetCategory, IReadOnlyList<ManifestEntry>>();
        foreach (var category in AssetCategories.All)
        {
            var list = new List<ManifestEntry>();
            if (obj[category.Key()] is JsonArray array)
            {
                foreach (var node in array)
                {
                    var entry = ParseEntry(node, category);
                    if (entry is null) continue;
                    if (list.Any(e => string.Equals(e.Asset.Name, entry.Asset.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new FormatException($"{category.Title()}: \"{entry.Asset.Name}\" is listed twice.");
                    list.Add(entry);
                }
            }
            entries[category] = list;
        }

        return new AssetManifest
        {
            Schema = schema,
            Revision = obj["revision"]?.GetValue<int>() ?? 0,
            Updated = obj["updated"]?.GetValue<string>() ?? "",
            Entries = entries,
        };
    }

    public static AssetManifest? TryLoad(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path, System.Text.Encoding.UTF8)) : null; }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or InvalidOperationException) { return null; }
    }

    private static ManifestEntry? ParseEntry(JsonNode? node, AssetCategory category)
    {
        var name = Text(node?["name"]);
        if (string.IsNullOrEmpty(name)) return null;

        var aliases = (node?["aliases"] as JsonArray)?
            .Select(a => Text(a)).Where(a => !string.IsNullOrEmpty(a) && !string.Equals(a, name, StringComparison.Ordinal))
            .Select(a => a!).Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();

        var slug = Text(node?["slug"]) is { Length: > 0 } given ? given : Slug.Make(name);
        var image = Normalise(Text(node?["image"])) ?? $"{category.Folder()}/{slug}.png";
        var asset = new Asset
        {
            Name = name,
            ImagePath = image,
            Aliases = aliases,
            Mode = category == AssetCategory.Maps ? Text(node?["mode"]) : null,
            Role = category == AssetCategory.Heroes ? Text(node?["role"]) : null,
            BodyImagePath = category == AssetCategory.Heroes ? Normalise(Text(node?["body_image"])) : null,
        };
        return new ManifestEntry(asset, Url(Text(node?["image_url"])), category == AssetCategory.Heroes ? Url(Text(node?["body_image_url"])) : null);
    }

    private static string? Text(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text)) return null;
        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    private static string? Normalise(string? path) => path?.Replace('\\', '/');

    private static string? Url(string? text) =>
        text is not null && Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? text : null;

    /// <summary>The manifest as JSON text (the shipped file is produced this way, and tests compare against it).</summary>
    public string ToJson()
    {
        var root = new JsonObject { ["schema"] = Schema, ["revision"] = Revision, ["updated"] = Updated };
        foreach (var category in AssetCategories.All)
        {
            var array = new JsonArray();
            foreach (var entry in Of(category))
            {
                var a = entry.Asset;
                var node = new JsonObject { ["name"] = a.Name, ["slug"] = Slug.Make(a.Name) };
                if (category == AssetCategory.Heroes) node["role"] = a.Role ?? "";
                if (category == AssetCategory.Maps) node["mode"] = a.Mode ?? "";
                if (a.ImagePath is not null) node["image"] = a.ImagePath;
                if (category == AssetCategory.Heroes && a.BodyImagePath is not null) node["body_image"] = a.BodyImagePath;
                if (a.Aliases.Count > 0) node["aliases"] = new JsonArray(a.Aliases.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
                if (entry.ImageUrl is not null) node["image_url"] = entry.ImageUrl;
                if (entry.BodyImageUrl is not null) node["body_image_url"] = entry.BodyImageUrl;
                array.Add(node);
            }
            root[category.Key()] = array;
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}

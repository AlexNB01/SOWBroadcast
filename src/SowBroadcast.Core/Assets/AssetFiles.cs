using System.Text.Json;
using System.Text.Json.Nodes;
using SowBroadcast.Core.Export;

namespace SowBroadcast.Core.Assets;

/// <summary>Reads and writes the per-category <c>index.json</c> / <c>index.txt</c> files overlays use to find heroes, maps and modes.</summary>
public static class AssetFiles
{
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp" };

    /// <summary>
    /// Reads an index file. Image paths stay relative; <see cref="Asset.SourcePath"/> is resolved against the given
    /// Scoreboard roots (first existing file wins) so the app can show previews and copy the image later.
    /// </summary>
    public static List<Asset>? ReadIndex(string indexJsonPath, AssetCategory category, IReadOnlyList<string> scoreboardRoots)
    {
        if (!File.Exists(indexJsonPath)) return null;
        JsonNode? root;
        try { root = JsonNode.Parse(File.ReadAllText(indexJsonPath, System.Text.Encoding.UTF8)); }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
        if (root?[category.Key()] is not JsonArray items) return null;

        var result = new List<Asset>();
        foreach (var node in items)
        {
            var name = node?["name"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrEmpty(name)) continue;

            var image = Normalise(node?["image"]?.GetValue<string>());
            string? body = null;
            string? bodySource = null;
            if (category == AssetCategory.Heroes)
            {
                body = Normalise(node?["body_image"]?.GetValue<string>());
                bodySource = Resolve(body, scoreboardRoots);
                if (bodySource is null)
                {
                    // Entries from before body images were tracked: use the conventional file name if one exists.
                    var conventional = $"HeroesBody/{Slug.HeroBodyFile(name)}.png";
                    var found = Resolve(conventional, scoreboardRoots);
                    if (found is not null) (body, bodySource) = (conventional, found);
                }
            }

            result.Add(new Asset
            {
                Name = name,
                ImagePath = image,
                SourcePath = Resolve(image, scoreboardRoots),
                Mode = category == AssetCategory.Maps ? node?["mode"]?.GetValue<string>() : null,
                Role = category == AssetCategory.Heroes ? Blank(node?["role"]?.GetValue<string>()) : null,
                Aliases = (node?["aliases"] as JsonArray)?.Select(a => Blank(a?.GetValue<string>())).Where(a => a is not null).Select(a => a!).ToArray()
                          ?? Array.Empty<string>(),
                BodyImagePath = body,
                BodySourcePath = bodySource,
            });
        }
        return result;
    }

    /// <summary>Image files in a folder as assets, named after their file names ("d-va.png" -> "D Va").</summary>
    public static List<Asset> ScanFolder(string folder, AssetCategory category)
    {
        var result = new List<Asset>();
        if (!Directory.Exists(folder)) return result;
        foreach (var file in Directory.GetFiles(folder).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            if (!ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
            var name = Slug.NameFromFileName(file);
            if (name.Length == 0) continue;
            result.Add(new Asset
            {
                Name = name,
                SourcePath = file,
                ImagePath = $"{category.Folder()}/{Slug.Make(name)}.png",
            });
        }
        return result;
    }

    /// <summary>The <c>index.json</c> text in the legacy format (CRLF, two-space indent, UTF-8 without escaping).</summary>
    public static string BuildIndexJson(AssetCategory category, IReadOnlyList<Asset> assets, Func<string, bool> imageExists)
    {
        var items = new JsonArray();
        foreach (var asset in assets)
        {
            var slug = Slug.Make(asset.Name);
            var item = new JsonObject
            {
                ["name"] = asset.Name,
                ["slug"] = slug,
                ["image"] = $"{category.Folder()}/{slug}.png",
            };
            if (category == AssetCategory.Maps) item["mode"] = asset.Mode ?? "";
            if (category == AssetCategory.Heroes)
            {
                item["role"] = asset.Role ?? "";
                var body = asset.BodyImagePath ?? $"HeroesBody/{Slug.HeroBodyFile(asset.Name)}.png";
                if (imageExists(body)) item["body_image"] = body;
            }
            if (asset.Aliases.Count > 0) item["aliases"] = new JsonArray(asset.Aliases.Select(a => (JsonNode)JsonValue.Create(a)!).ToArray());
            items.Add(item);
        }

        var root = new JsonObject { [category.Key()] = items };
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return FileWriter.Crlf(root.ToJsonString(options));
    }

    /// <summary>One name per line, sorted by code point, each line ended with CRLF.</summary>
    public static string BuildIndexText(IReadOnlyList<Asset> assets) =>
        string.Concat(assets.Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).Select(n => n + "\r\n"));

    public static string? Resolve(string? relative, IReadOnlyList<string> scoreboardRoots)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        foreach (var root in scoreboardRoots)
        {
            var candidate = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? Normalise(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim().Replace('\\', '/');

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

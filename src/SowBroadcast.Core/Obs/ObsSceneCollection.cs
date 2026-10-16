using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SowBroadcast.Core.Obs;

/// <summary>
/// The OBS scene collection for the program's overlays (EHD-6): the 18 scenes of the old collection with every browser source pointed
/// at the current pages and everything that belonged to one machine removed (NDI sender names, the webcam, absolute paths, dead
/// addresses). Built from the template shipped inside the program, with the server address and the program's folders filled in.
/// Import it in OBS with Scene Collection > Import; pick the NDI senders and the camera afterwards.
/// </summary>
public static class ObsSceneCollection
{
    private const string TemplateResource = "SowBroadcast.Core.Obs.scene-template.json";
    private const string SceneIdNamespace = "https://suomiow.fi/sowbroadcast/obs/";

    // Old page -> new page (the waiting screens and the rosters are one page each now; mapdraft.html was a case-sensitivity accident).
    private static readonly Dictionary<string, string> Pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["berightback.html"] = "waiting.html?mode=brb",
        ["startingsoon.html"] = "waiting.html?mode=start",
        ["thankyouforwatching.html"] = "waiting.html?mode=end",
        ["team1.html"] = "team.html?side=1",
        ["team2.html"] = "team.html?side=2",
        ["mapdraft.html"] = "MapDraft.html",
    };

    private static readonly Dictionary<string, string> Renames = new() { ["Musiikki Highlights"] = "Highlights Music" };

    private static JsonObject LoadTemplate()
    {
        using var stream = typeof(ObsSceneCollection).Assembly.GetManifestResourceStream(TemplateResource)
                           ?? throw new InvalidOperationException("The OBS scene template is missing from the program.");
        return JsonNode.Parse(stream)!.AsObject();
    }

    /// <summary>The names of the scenes in the collection, in the order of OBS's scene list.</summary>
    public static IReadOnlyList<string> SceneNames()
    {
        var template = LoadTemplate();
        return template["scene_order"]!.AsArray().Select(o => Rename(o!["name"]!.GetValue<string>())).ToArray();
    }

    /// <param name="baseUrl">The overlay server, e.g. <c>http://127.0.0.1:8324</c>.</param>
    /// <param name="installDir">Where the program is installed (music and the camera mask are read from there).</param>
    /// <param name="dataDir">The program's data folder (the caster and host text files).</param>
    public static string Build(string baseUrl, string installDir, string dataDir)
    {
        baseUrl = baseUrl.TrimEnd('/');
        var template = LoadTemplate();
        var sources = template["sources"]!.AsArray();
        var groups = template["groups"]!.AsArray();
        var general = Path.Combine(dataDir, "Scoreboard", "General");

        // The webcam belongs to one machine: it goes, together with the scene items that show it.
        var dropped = sources.Where(s => s!["id"]!.GetValue<string>() == "dshow_input").Select(s => s!["name"]!.GetValue<string>()).ToHashSet();

        foreach (var source in sources.Select(s => s!.AsObject()))
        {
            var settings = source["settings"]!.AsObject();
            switch (source["id"]!.GetValue<string>())
            {
                case "browser_source":
                    settings["url"] = $"{baseUrl}/HTML/{PageOf(settings["url"]!.GetValue<string>())}";
                    settings["width"] = 1920;
                    settings["height"] = 1080;
                    break;
                case "text_gdiplus":
                    settings["file"] = Slash(Path.Combine(general, Path.GetFileName(settings["file"]!.GetValue<string>().Replace('\\', '/'))));
                    break;
                case "ndi_source":
                    settings["ndi_source_name"] = "";
                    break;
                case "vlc_source":
                    foreach (var entry in settings["playlist"]!.AsArray()) entry!["value"] = WindowsPath(installDir) + "\\Music";
                    break;
                case "image_source":
                    settings["file"] = Slash(Path.Combine(installDir, "HTML", "assets", "imagemask", Path.GetFileName(settings["file"]!.GetValue<string>().Replace('\\', '/'))));
                    break;
            }
        }

        for (var i = sources.Count - 1; i >= 0; i--)
            if (dropped.Contains(sources[i]!["name"]!.GetValue<string>())) sources.RemoveAt(i);

        var known = sources.Concat(groups).Select(s => s!["name"]!.GetValue<string>()).ToHashSet();
        foreach (var container in sources.Concat(groups).Select(c => c!.AsObject()))
        {
            if (container["settings"]!["items"] is JsonArray items)
            {
                for (var i = items.Count - 1; i >= 0; i--)
                    if (!known.Contains(items[i]!["name"]!.GetValue<string>())) items.RemoveAt(i);
            }
        }
        foreach (var container in sources.Concat(groups).Select(c => c!.AsObject()))
        {
            if (container["settings"]!["items"] is JsonArray items)
                foreach (var item in items) item!["name"] = Rename(item["name"]!.GetValue<string>());
            container["name"] = Rename(container["name"]!.GetValue<string>());
        }

        // Fresh, stable ids so importing this next to the old collection does not collide with it.
        var text = template.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        foreach (var old in sources.Concat(groups).Select(s => s!["uuid"]!.GetValue<string>()).OrderBy(u => u, StringComparer.Ordinal).Distinct())
            text = text.Replace(old, NewId(old), StringComparison.Ordinal);

        var result = JsonNode.Parse(text)!.AsObject();
        result["name"] = "SOW Broadcast";
        result["current_scene"] = "In Game";
        result["current_program_scene"] = "In Game";
        result["modules"] = new JsonObject();
        foreach (var entry in result["scene_order"]!.AsArray()) entry!["name"] = Rename(entry["name"]!.GetValue<string>());
        return result.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Every browser source must point at a page that exists; nothing may carry an old absolute path or a device id. Empty when all is well.</summary>
    public static IReadOnlyList<string> Check(string collectionJson, IReadOnlyCollection<string> overlayPages)
    {
        var problems = new List<string>();
        var root = JsonNode.Parse(collectionJson)!.AsObject();
        var names = root["sources"]!.AsArray().Concat(root["groups"]!.AsArray()).Select(s => s!["name"]!.GetValue<string>()).ToHashSet();
        foreach (var source in root["sources"]!.AsArray().Select(s => s!.AsObject()))
        {
            var name = source["name"]!.GetValue<string>();
            switch (source["id"]!.GetValue<string>())
            {
                case "browser_source":
                    var url = source["settings"]!["url"]!.GetValue<string>();
                    var match = Regex.Match(url, @"/HTML/([^?]+)");
                    if (!match.Success || !overlayPages.Contains(match.Groups[1].Value, StringComparer.Ordinal))
                        problems.Add($"{name}: {url} is not a page in HTML/");
                    break;
                case "scene":
                    foreach (var item in source["settings"]!["items"]!.AsArray())
                        if (!names.Contains(item!["name"]!.GetValue<string>())) problems.Add($"{name}: item {item["name"]} has no source");
                    break;
            }
        }
        foreach (var leftover in new[] { "C:/SOWBroadcast", "HEXEON", "BRIO", "localhost:899" })
            if (collectionJson.Contains(leftover, StringComparison.Ordinal)) problems.Add($"leftover {leftover}");
        return problems;
    }

    // "http://127.0.0.1:8324/HTML/startingsoon.html" -> "waiting.html?mode=start". The blank-screen source had a dead address and a lower-case path.
    private static string PageOf(string url)
    {
        var at = url.IndexOf("/html/", StringComparison.OrdinalIgnoreCase);
        var name = at >= 0 ? url[(at + 6)..] : url;
        var bare = name.Split('?')[0];
        return Pages.TryGetValue(bare, out var page) ? page : name;
    }

    private static string Rename(string name)
    {
        if (name.StartsWith('v') && name.EndsWith("ritausta", StringComparison.Ordinal)) return "Background Colour";   // "väritausta", which the old file had lost its ä in
        return Renames.TryGetValue(name, out var renamed) ? renamed : name;
    }

    private static string Slash(string path) => path.Replace('\\', '/');

    // VLC wants a Windows path: backslashes, no trailing separator.
    private static string WindowsPath(string path) => path.TrimEnd('/', '\\').Replace('/', '\\');

    // A version 5 (name-based) UUID, so the same collection always gets the same ids.
    private static string NewId(string old)
    {
        var ns = Guid.Parse("6ba7b811-9dad-11d1-80b4-00c04fd430c8");   // the URL namespace
        var nsBytes = ns.ToByteArray();
        SwapGuidByteOrder(nsBytes);
        var hash = SHA1.HashData(nsBytes.Concat(Encoding.UTF8.GetBytes(SceneIdNamespace + old)).ToArray());
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        SwapGuidByteOrder(bytes);
        return new Guid(bytes).ToString();
    }

    private static void SwapGuidByteOrder(byte[] b)
    {
        (b[0], b[3]) = (b[3], b[0]);
        (b[1], b[2]) = (b[2], b[1]);
        (b[4], b[5]) = (b[5], b[4]);
        (b[6], b[7]) = (b[7], b[6]);
    }
}

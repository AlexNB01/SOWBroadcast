using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SowBroadcast.Core.Obs;

/// <summary>
/// The OBS scene collection for the program's overlays (EHD-6): the 18 scenes of the old collection (and one more for the replay clips) with every browser source pointed
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
        var template = JsonNode.Parse(stream)!.AsObject();
        AddReplayClipsScene(template);
        return template;
    }

    // ---- replay ---------------------------------------------------------------------------------------------
    // The old collection had one replay scene. The replay page has two jobs now (replay.html?mode=instant loops the newest clip, ?mode=clips
    // plays what the operator asks for in the Replay tab), so the collection has a scene for each: the old one becomes the instant replay and
    // a copy of it, with its own sources, shows the clips.

    /// <summary>The scene that plays the clips asked for in the Replay tab.</summary>
    public const string ReplayClipsScene = "Replay Clips";

    private const string InstantReplayScene = "InstantReplay";
    private const string InstantReplayPage = "replay.html?mode=instant";
    private const string ReplayClipsPage = "replay.html?mode=clips";

    // Ids of the copies in the template (the final ids are made from them like all the others).
    private const string ReplayClipsSceneUuid = "b1c2d3e4-0001-4a5b-8c6d-7e8f90a1b2c3";
    private const string ReplayClipsPageUuid = "b1c2d3e4-0002-4a5b-8c6d-7e8f90a1b2c3";
    private const string ReplayClipsTransitionUuid = "b1c2d3e4-0003-4a5b-8c6d-7e8f90a1b2c3";

    private static void AddReplayClipsScene(JsonObject template)
    {
        var sources = template["sources"]!.AsArray();
        JsonObject Find(string name) => sources.Select(s => s!.AsObject()).First(s => s["name"]!.GetValue<string>() == name);

        var scene = Find(InstantReplayScene);
        var items = scene["settings"]!["items"]!.AsArray().Select(i => i!.AsObject()).ToArray();
        var pageItem = items.First(i => i["name"]!.GetValue<string>().StartsWith("HTML Instant Replay", StringComparison.Ordinal));
        var transitionItem = items.First(i => i["name"]!.GetValue<string>().StartsWith("HTML Transition", StringComparison.Ordinal));
        var page = Find(pageItem["name"]!.GetValue<string>());
        var transition = Find(transitionItem["name"]!.GetValue<string>());
        page["settings"]!["url"] = "http://127.0.0.1:8324/HTML/" + InstantReplayPage;

        var pageCopy = page.DeepClone().AsObject();
        pageCopy["name"] = "HTML Replay Clips";
        pageCopy["uuid"] = ReplayClipsPageUuid;
        pageCopy["settings"]!["url"] = "http://127.0.0.1:8324/HTML/" + ReplayClipsPage;

        var transitionCopy = transition.DeepClone().AsObject();
        transitionCopy["name"] = "HTML Transition Replay Clips";
        transitionCopy["uuid"] = ReplayClipsTransitionUuid;

        var sceneCopy = scene.DeepClone().AsObject();
        sceneCopy["name"] = ReplayClipsScene;
        sceneCopy["uuid"] = ReplayClipsSceneUuid;
        foreach (var item in sceneCopy["settings"]!["items"]!.AsArray().Select(i => i!.AsObject()))
        {
            var isPage = item["name"]!.GetValue<string>().StartsWith("HTML Instant Replay", StringComparison.Ordinal);
            item["name"] = isPage ? pageCopy["name"]!.GetValue<string>() : transitionCopy["name"]!.GetValue<string>();
            item["source_uuid"] = isPage ? ReplayClipsPageUuid : ReplayClipsTransitionUuid;
        }

        sources.Add(pageCopy);
        sources.Add(transitionCopy);
        sources.Add(sceneCopy);

        var order = template["scene_order"]!.AsArray();
        var at = order.ToList().FindIndex(o => o!["name"]!.GetValue<string>() == InstantReplayScene);
        order.Insert(at + 1, new JsonObject { ["name"] = ReplayClipsScene });
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
                case "ndi_source":
                    settings["ndi_source_name"] = "";
                    break;
                case "vlc_source":
                    foreach (var entry in settings["playlist"]!.AsArray()) entry!["value"] = WindowsPath(installDir) + "\\Music";
                    break;
            }
        }

        for (var i = sources.Count - 1; i >= 0; i--)
            if (dropped.Contains(sources[i]!["name"]!.GetValue<string>())) sources.RemoveAt(i);

        ApplyCameraLayout(sources, groups);

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

    // ---- cameras ------------------------------------------------------------------------------------------
    // The camera scenes ("=> Caster 1 NDI Framing", ...) hold nothing but the NDI source, stretched over the whole canvas, so a camera is a
    // full 16:9 picture wherever it is used. The scenes that use them (single camera, duo row, trio row) place and crop that picture
    // into the camera windows of their overlay pages. The pages are 1920x1080 CSS pixels shown at 4/3 on the 2560x1440 canvas the collection
    // is made for (the base resolution of the producers' OBS profiles; the browser sources are scaled by 4/3 for the same reason).

    private const double CanvasWidth = 2560, CanvasHeight = 1440;
    private const double PageScale = CanvasWidth / 1920.0;

    // The camera scene and the NDI source it shows. The old collection had no source for caster 2 at all: it is made like caster 1's.
    private static readonly (string Scene, string Source)[] FramingScenes =
    {
        ("=> Host NDI Framing", "HostNDI"), ("=> Caster 1 NDI Framing", "Caster1NDI"), ("=> Caster 2 NDI Framing", "Caster2NDI"),
    };

    private const string NewNdiSourceId = "7f3c2a10-5d1e-4c6b-9a8e-2b4d6f8a0c13";

    /// <summary>One camera window of an overlay page, in the page's CSS pixels (the part of a frame above its name plate).</summary>
    internal readonly record struct Window(double X, double Y, double Width, double Height);

    // duorow.html: .duo (top 220, height 750) centres two .frame boxes of 848x549 with a 48 gap; the .plate (72) at the bottom of each frame
    // is opaque, so the camera is the 848x477 above it (exactly 16:9). Left to right: caster 1, caster 2.
    internal static IReadOnlyList<Window> DuoWindows() => Row(2, 848, 549, 72, 48, 220, 750);

    // triorow.html: .trio (top 220, height 770) centres three .frame boxes of 596x505 with a 36 gap and a 64 plate. Left to right in the
    // page: caster 1, host, caster 2.
    internal static IReadOnlyList<Window> TrioWindows() => Row(3, 596, 505, 64, 36, 220, 770);

    private static IReadOnlyList<Window> Row(int count, double frameWidth, double frameHeight, double plate, double gap, double areaTop, double areaHeight)
    {
        var left = (1920 - (count * frameWidth + (count - 1) * gap)) / 2;
        var top = areaTop + (areaHeight - frameHeight) / 2;
        return Enumerable.Range(0, count).Select(i => new Window(left + i * (frameWidth + gap), top, frameWidth, frameHeight - plate)).ToArray();
    }

    private static void ApplyCameraLayout(JsonArray sources, JsonArray groups)
    {
        JsonObject? Scene(string name) => sources.Select(s => s!.AsObject()).FirstOrDefault(s => s["id"]!.GetValue<string>() == "scene" && s["name"]!.GetValue<string>() == name);

        EnsureNdiSources(sources);
        var kinds = sources.ToDictionary(s => s!["name"]!.GetValue<string>(), s => s!["id"]!.GetValue<string>());

        // The camera scenes: the NDI source only (no frame group, name text or mask), covering the canvas.
        foreach (var (name, _) in FramingScenes)
        {
            if (Scene(name) is not { } scene) continue;
            var items = scene["settings"]!["items"]!.AsArray();
            for (var i = items.Count - 1; i >= 0; i--)
                if (!kinds.TryGetValue(items[i]!["name"]!.GetValue<string>(), out var kind) || kind != "ndi_source") items.RemoveAt(i);
            foreach (var item in items.Select(i => i!.AsObject()))
                Place(item, 0, 0, 1, 0, covering: true);
        }
        groups.Clear();

        // What only the removed frames used (name texts, the camera mask) goes too.
        var used = sources.Where(s => s!["id"]!.GetValue<string>() == "scene")
            .SelectMany(s => s!["settings"]!["items"]!.AsArray()).Select(i => i!["name"]!.GetValue<string>()).ToHashSet();
        for (var i = sources.Count - 1; i >= 0; i--)
            if (sources[i]!["id"]!.GetValue<string>() is "text_gdiplus" or "image_source" && !used.Contains(sources[i]!["name"]!.GetValue<string>())) sources.RemoveAt(i);

        // Single camera: the whole picture.
        PlaceCamera(Scene("Duo SingleCam"), "=> Caster 1 NDI Framing", new Window(0, 0, 1920, 1080));

        var duo = DuoWindows();
        PlaceCamera(Scene("DuoCam Row"), "=> Caster 1 NDI Framing", duo[0]);
        PlaceCamera(Scene("DuoCam Row"), "=> Caster 2 NDI Framing", duo[1]);

        var trio = TrioWindows();
        PlaceCamera(Scene("TrioCam Row"), "=> Caster 1 NDI Framing", trio[0]);
        PlaceCamera(Scene("TrioCam Row"), "=> Host NDI Framing", trio[1]);
        PlaceCamera(Scene("TrioCam Row"), "=> Caster 2 NDI Framing", trio[2]);
    }

    // A camera scene without an NDI source (caster 2's) gets one, copied from another camera scene: the source, and its item in the scene.
    private static void EnsureNdiSources(JsonArray sources)
    {
        JsonObject? Find(string name) => sources.Select(s => s!.AsObject()).FirstOrDefault(s => s["name"]!.GetValue<string>() == name);
        var template = FramingScenes.Select(f => Find(f.Source)).FirstOrDefault(s => s is not null && s["id"]!.GetValue<string>() == "ndi_source");
        if (template is null) return;

        foreach (var (sceneName, sourceName) in FramingScenes)
        {
            var scene = Find(sceneName);
            if (scene is null || scene["id"]!.GetValue<string>() != "scene") continue;
            var items = scene["settings"]!["items"]!.AsArray();
            if (items.Any(i => Find(i!["name"]!.GetValue<string>()) is { } s && s["id"]!.GetValue<string>() == "ndi_source")) continue;

            var source = template.DeepClone().AsObject();
            source["name"] = sourceName;
            source["uuid"] = NewNdiSourceId;
            sources.Add(source);

            var donor = FramingScenes.Select(f => Find(f.Scene)).Where(s => s is not null && s != scene)
                .SelectMany(s => s!["settings"]!["items"]!.AsArray()).Select(i => i!.AsObject())
                .First(i => i["name"]!.GetValue<string>() == template["name"]!.GetValue<string>());
            var item = donor.DeepClone().AsObject();
            item["name"] = sourceName;
            item["source_uuid"] = NewNdiSourceId;
            item["id"] = items.Select(i => i!["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty(0).Max() + 1;
            items.Add(item);
        }
    }

    // The camera scene's item in a parent scene: scaled so the 16:9 picture fills the window, and cropped left and right where the window is
    // narrower than 16:9 (the picture covers the window, it is never squeezed).
    private static void PlaceCamera(JsonObject? scene, string cameraScene, Window window)
    {
        if (scene is null) return;
        var item = scene["settings"]!["items"]!.AsArray().Select(i => i!.AsObject()).FirstOrDefault(i => i["name"]!.GetValue<string>() == cameraScene)
                   ?? throw new InvalidOperationException($"{scene["name"]} has no {cameraScene}.");
        var width = window.Width * PageScale;
        var height = window.Height * PageScale;
        var scale = Math.Max(width / CanvasWidth, height / CanvasHeight);
        var crop = (int)Math.Floor((CanvasWidth - width / scale) / 2);   // rounded down: the picture may overhang the window by a fraction of a pixel, never fall short
        Place(item, window.X * PageScale, window.Y * PageScale, scale, crop, covering: false);
    }

    private static void Place(JsonObject item, double x, double y, double scale, int cropSides, bool covering)
    {
        const double unit = CanvasHeight / 2;   // the *_rel values are in half canvas heights from the canvas centre
        item["scale_ref"] = Point(CanvasWidth, CanvasHeight);
        item["pos"] = Point(x, y);
        item["pos_rel"] = Point((x - CanvasWidth / 2) / unit, (y - CanvasHeight / 2) / unit);
        item["scale"] = Point(scale, scale);
        item["scale_rel"] = Point(scale, scale);
        item["rot"] = 0.0;
        item["crop_left"] = cropSides;
        item["crop_right"] = cropSides;
        item["crop_top"] = 0;
        item["crop_bottom"] = 0;
        item["bounds_type"] = covering ? 3 : 0;   // 3: fill the bounds, cropping what overflows
        item["bounds_align"] = 0;
        item["bounds"] = covering ? Point(CanvasWidth, CanvasHeight) : Point(0, 0);
        item["bounds_rel"] = covering ? Point(CanvasWidth / unit, CanvasHeight / unit) : Point(0, 0);
    }

    private static JsonObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };

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
        if (name.StartsWith("replay.html?mode=", StringComparison.OrdinalIgnoreCase)) return name;
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

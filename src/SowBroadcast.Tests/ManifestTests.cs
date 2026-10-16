using System.Diagnostics;
using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Link;
using SowBroadcast.Integrations.Assets;
using SowBroadcast.Integrations.Link;

namespace SowBroadcast.Tests;

public sealed class AssetManifestTests
{
    private const string Sample = """
        {
          "schema": 1, "revision": 4, "updated": "2026-10-16",
          "heroes": [
            { "name": "Lúcio", "slug": "lucio", "role": "Support", "image": "Heroes/lucio.png", "body_image": "HeroesBody\\Lucio.png", "aliases": ["Lucio", "Lúcio", " "] },
            { "name": "Mizuki", "role": "Support", "body_image_url": "https://example.com/mizuki.png", "image_url": "http://insecure.example/x.png" }
          ],
          "maps": [ { "name": "King's Row", "mode": "Hybrid", "aliases": ["Kings Row"] } ],
          "modes": [ { "name": "Control" } ]
        }
        """;

    [Fact]
    public void A_manifest_is_read_with_defaults_for_what_is_left_out()
    {
        var manifest = AssetManifest.Parse(Sample);

        Assert.Equal(4, manifest.Revision);
        var lucio = manifest.Of(AssetCategory.Heroes)[0].Asset;
        Assert.Equal("Support", lucio.Role);
        Assert.Equal("HeroesBody/Lucio.png", lucio.BodyImagePath);              // backslashes become slashes
        Assert.Equal(new[] { "Lucio" }, lucio.Aliases);                          // the name itself and blanks are not aliases
        var mizuki = manifest.Of(AssetCategory.Heroes)[1];
        Assert.Equal("Heroes/mizuki.png", mizuki.Asset.ImagePath);               // the slug of the name
        Assert.Equal("https://example.com/mizuki.png", mizuki.BodyImageUrl);
        Assert.Null(mizuki.ImageUrl);                                            // only https addresses count
        Assert.Equal("Hybrid", manifest.Of(AssetCategory.Maps)[0].Asset.Mode);
        Assert.Equal("Gametypes/control.png", manifest.Of(AssetCategory.Modes)[0].Asset.ImagePath);
    }

    [Fact]
    public void A_manifest_survives_writing_and_reading_again()
    {
        var first = AssetManifest.Parse(Sample);

        var again = AssetManifest.Parse(first.ToJson());

        Assert.Equal(first.ToJson(), again.ToJson());
        Assert.Equal(first.Revision, again.Revision);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{ \"schema\": 99 }")]
    [InlineData("{ \"heroes\": [ { \"name\": \"Ana\" }, { \"name\": \"ana\" } ] }")]
    public void A_manifest_that_cannot_be_used_is_refused_with_a_format_error(string json) =>
        Assert.Throws<FormatException>(() => AssetManifest.Parse(json));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Scoreboard", "manifest.json"))) dir = dir.Parent;
        return dir?.FullName ?? "";
    }

    [Fact]
    public void The_shipped_manifest_lists_what_the_shipped_catalogue_has_and_every_hero_has_a_role_and_body_picture()
    {
        var root = RepoRoot();
        if (root.Length == 0) return;
        var scoreboard = Path.Combine(root, "Scoreboard");
        var manifest = AssetManifest.Parse(File.ReadAllText(Path.Combine(scoreboard, "manifest.json")));

        Assert.Equal(54, manifest.Of(AssetCategory.Heroes).Count);
        Assert.Equal(31, manifest.Of(AssetCategory.Maps).Count);
        Assert.Equal(5, manifest.Of(AssetCategory.Modes).Count);
        foreach (var entry in manifest.Of(AssetCategory.Heroes))
        {
            Assert.Contains(entry.Asset.Role, AssetService.Roles);
            Assert.True(File.Exists(Path.Combine(scoreboard, entry.Asset.BodyImagePath!.Replace('/', Path.DirectorySeparatorChar))), $"{entry.Asset.Name}: body picture");
        }
        foreach (var entry in manifest.Of(AssetCategory.Maps))
            Assert.Contains(entry.Asset.Mode, new[] { "Control", "Escort", "Hybrid", "Push", "Flashpoint" });

        // The old hand-written tables of the code are now data: the special spellings resolve through aliases.
        var service = new AssetService(Path.Combine(Path.GetTempPath(), "sowb-shipped-" + Guid.NewGuid().ToString("N")), new[] { scoreboard });
        service.Load();
        Assert.Equal("King's Row", service.Resolve(AssetCategory.Maps, "Kings Row"));
        Assert.Equal("King's Row", service.Resolve(AssetCategory.Maps, "king’s row"));
        Assert.Equal("Soldier: 76", service.Resolve(AssetCategory.Heroes, "soldier 76"));
        Assert.Equal("D.Va", service.Resolve(AssetCategory.Heroes, "Dva"));
        Assert.Equal("Lúcio", service.Resolve(AssetCategory.Heroes, "Lucio"));
        Assert.Equal("Wrecking Ball", service.Resolve(AssetCategory.Heroes, "Hammond"));
        Assert.Equal("HeroesBody/Dva.png", service.Find(AssetCategory.Heroes, "D.Va")!.BodyImagePath);
        Assert.Equal("Tank", service.HeroRole("D.Va"));
    }
}

public sealed class AssetServiceManifestTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-manifest-" + Guid.NewGuid().ToString("N"));
    private string User => Path.Combine(_tmp, "user", "Scoreboard");
    private string Shipped => Path.Combine(_tmp, "shipped", "Scoreboard");

    public AssetServiceManifestTests() => Directory.CreateDirectory(Shipped);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private static string ManifestJson(int revision, params string[] heroes) =>
        "{ \"schema\": 1, \"revision\": " + revision + ", \"heroes\": [" + string.Join(",", heroes) + "], \"maps\": [], \"modes\": [] }";

    private static string Hero(string name, string role = "Damage", string aliases = "") =>
        $"{{ \"name\": \"{name}\", \"role\": \"{role}\", \"body_image\": \"HeroesBody/{name}.png\", \"aliases\": [{aliases}] }}";

    private AssetService Service(string? shippedManifest, string? userManifest = null)
    {
        if (shippedManifest is not null) File.WriteAllText(Path.Combine(Shipped, "manifest.json"), shippedManifest);
        if (userManifest is not null)
        {
            Directory.CreateDirectory(User);
            File.WriteAllText(Path.Combine(User, "manifest.json"), userManifest);
        }
        var service = new AssetService(User, new[] { Shipped });
        service.Load();
        return service;
    }

    [Fact]
    public void Heroes_in_the_manifest_appear_by_themselves_with_their_role_and_aliases()
    {
        var service = Service(ManifestJson(1, Hero("Ana", "Support", "\"Anna\""), Hero("Doctorine")));

        Assert.Equal(new[] { "Ana", "Doctorine" }, service.Names(AssetCategory.Heroes));
        Assert.Equal("Support", service.HeroRole("Ana"));
        Assert.Equal("HeroesBody/Doctorine.png", service.Find(AssetCategory.Heroes, "Doctorine")!.BodyImagePath);
        Assert.Equal("Ana", service.Resolve(AssetCategory.Heroes, "  anna "));
        Assert.Equal(1, service.ManifestRevision);
        Assert.True(File.Exists(Path.Combine(User, "Heroes", "index.json")));      // the overlays read the written list
        Assert.Contains("\"aliases\"", File.ReadAllText(Path.Combine(User, "Heroes", "index.json")));
    }

    [Fact]
    public void A_newer_manifest_wins_and_a_heros_own_changes_to_known_data_follow_the_manifest()
    {
        var service = Service(ManifestJson(1, Hero("Ana", "Support")), ManifestJson(2, Hero("Ana", "Damage"), Hero("Mizuki", "Support")));

        Assert.Equal(2, service.ManifestRevision);
        Assert.Equal("Damage", service.HeroRole("Ana"));
        Assert.Contains("Mizuki", service.Names(AssetCategory.Heroes));
    }

    [Fact]
    public void What_the_user_added_themselves_stays_and_what_they_deleted_does_not_come_back()
    {
        var service = Service(ManifestJson(1, Hero("Ana"), Hero("Doctorine")));
        service.AddOrUpdate(new AssetEdit { Category = AssetCategory.Heroes, Name = "Homebrew", Role = "Tank", ImageSourcePath = null, BodySourcePath = WritePng("body.png") });
        service.Delete(AssetCategory.Heroes, "Doctorine");

        var after = new AssetService(User, new[] { Shipped });
        after.Load();

        Assert.Equal(new[] { "Ana", "Homebrew" }, after.Names(AssetCategory.Heroes));
        // Adding the deleted one again undoes the delete.
        after.AddOrUpdate(new AssetEdit { Category = AssetCategory.Heroes, Name = "Doctorine", Role = "Damage", BodySourcePath = WritePng("doc.png") });
        var again = new AssetService(User, new[] { Shipped });
        again.Load();
        Assert.Contains("Doctorine", again.Names(AssetCategory.Heroes));
    }

    private string WritePng(string name)
    {
        var path = Path.Combine(_tmp, name);
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        return path;
    }

    [Fact]
    public void Resolve_matches_names_and_aliases_exactly_and_never_guesses()
    {
        var service = Service(ManifestJson(1, Hero("Soldier: 76", "Damage", "\"Soldier 76\"")));

        Assert.Equal("Soldier: 76", service.Resolve(AssetCategory.Heroes, "SOLDIER: 76"));
        Assert.Equal("Soldier: 76", service.Resolve(AssetCategory.Heroes, "soldier   76"));
        Assert.Null(service.Resolve(AssetCategory.Heroes, "Soldier76"));       // not an alias, so not found
        Assert.Null(service.Resolve(AssetCategory.Heroes, ""));
        Assert.Null(service.Resolve(AssetCategory.Maps, "Soldier 76"));
    }

    [Fact]
    public void Installing_a_manifest_keeps_it_only_when_it_is_newer_and_refuses_a_broken_one()
    {
        var service = Service(ManifestJson(3, Hero("Ana")));

        var old = service.InstallManifest(ManifestJson(3, Hero("Ana"), Hero("Old")));
        Assert.False(old.Installed);
        Assert.DoesNotContain("Old", service.Names(AssetCategory.Heroes));

        var newer = service.InstallManifest(ManifestJson(4, Hero("Ana"), Hero("Doctorine")));
        Assert.True(newer.Installed);
        Assert.Equal(new[] { "Doctorine" }, newer.Added);
        Assert.True(File.Exists(service.UserManifestPath));

        Assert.Throws<FormatException>(() => service.InstallManifest("{ nope"));
        var restarted = new AssetService(User, new[] { Shipped });
        restarted.Load();
        Assert.Equal(4, restarted.ManifestRevision);   // the fetched manifest is used after a restart
        Assert.Contains("Doctorine", restarted.Names(AssetCategory.Heroes));
    }

    [Fact]
    public void Without_any_manifest_the_catalogue_loads_as_before()
    {
        Directory.CreateDirectory(Path.Combine(Shipped, "Heroes"));
        File.WriteAllText(Path.Combine(Shipped, "Heroes", "index.json"), "{\"heroes\":[{\"name\":\"Ana\",\"slug\":\"ana\",\"image\":\"Heroes/ana.png\",\"role\":\"Support\"}]}");

        var service = Service(null);

        Assert.Equal(0, service.ManifestRevision);
        Assert.Equal("Support", service.HeroRole("Ana"));
    }
}

public sealed class ManifestUpdaterTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-updater-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, HttpResult> _responses = new();
    private readonly List<string> _requested = new();

    public ManifestUpdaterTests() => Directory.CreateDirectory(Path.Combine(_tmp, "shipped"));

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private sealed class FakeHttp : IHttpFetcher
    {
        private readonly ManifestUpdaterTests _owner;
        public FakeHttp(ManifestUpdaterTests owner) => _owner = owner;

        public Task<HttpResult> GetAsync(Uri url, int maxBytes, CancellationToken cancellation)
        {
            _owner._requested.Add(url.ToString());
            return Task.FromResult(_owner._responses.TryGetValue(url.ToString(), out var r) ? r : new HttpResult(404, Array.Empty<byte>()));
        }
    }

    private const string ReleaseUrl = "https://api.github.com/repos/owner/repo/releases/latest";
    private const string ManifestUrl = "https://github.com/owner/repo/releases/download/v1/manifest.json";

    private static HttpResult Json(string text) => new(200, System.Text.Encoding.UTF8.GetBytes(text));

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

    private (ManifestUpdater Updater, AssetService Assets) Create()
    {
        var assets = new AssetService(Path.Combine(_tmp, "user"), new[] { Path.Combine(_tmp, "shipped") });
        assets.Load();
        return (new ManifestUpdater(new FakeHttp(this), assets, "owner/repo"), assets);
    }

    private void Release(string manifestUrl = ManifestUrl) =>
        _responses[ReleaseUrl] = Json("{\"assets\":[{\"name\":\"other.zip\",\"browser_download_url\":\"https://github.com/x\"},{\"name\":\"manifest.json\",\"browser_download_url\":\"" + manifestUrl + "\"}]}");

    [Fact]
    public async Task A_newer_manifest_is_installed_and_missing_pictures_are_downloaded()
    {
        Release();
        _responses[ManifestUrl] = Json("""
            { "schema": 1, "revision": 2,
              "heroes": [ { "name": "Doctorine", "role": "Tank", "body_image": "HeroesBody/Doctorine.png",
                            "image_url": "https://github.com/owner/repo/releases/download/v1/doctorine.png",
                            "body_image_url": "https://github.com/owner/repo/releases/download/v1/doctorine_body.png" } ] }
            """);
        _responses["https://github.com/owner/repo/releases/download/v1/doctorine.png"] = new HttpResult(200, Png);
        _responses["https://github.com/owner/repo/releases/download/v1/doctorine_body.png"] = new HttpResult(200, Png);
        var (updater, assets) = Create();

        var result = await updater.UpdateAsync();

        Assert.True(result.Ok, result.Message);
        Assert.Equal(new[] { "Doctorine" }, result.Added);
        Assert.Contains("Doctorine", result.Message);
        Assert.True(File.Exists(Path.Combine(_tmp, "user", "Heroes", "doctorine.png")));
        Assert.True(File.Exists(Path.Combine(_tmp, "user", "HeroesBody", "Doctorine.png")));
        Assert.Equal("Tank", assets.HeroRole("Doctorine"));
        Assert.Contains("\"body_image\"", File.ReadAllText(Path.Combine(_tmp, "user", "Heroes", "index.json")));   // the picture was there when the list was written
    }

    [Fact]
    public async Task The_same_revision_again_changes_nothing_and_downloads_nothing()
    {
        Release();
        _responses[ManifestUrl] = Json("{ \"revision\": 2, \"heroes\": [ { \"name\": \"Ana\", \"role\": \"Support\" } ] }");
        var (updater, _) = Create();
        await updater.UpdateAsync();
        _requested.Clear();

        var result = await updater.UpdateAsync();

        Assert.True(result.Ok);
        Assert.Contains("up to date", result.Message);
        Assert.Empty(result.Added);
    }

    [Theory]
    [InlineData("no release")]
    [InlineData("no manifest asset")]
    [InlineData("not github")]
    [InlineData("broken manifest")]
    public async Task Problems_are_reported_and_nothing_is_changed(string problem)
    {
        switch (problem)
        {
            case "no release": break;                                                                      // 404
            case "no manifest asset": _responses[ReleaseUrl] = Json("{\"assets\":[]}"); break;
            case "not github": Release("https://evil.example/manifest.json"); break;
            default:
                Release();
                _responses[ManifestUrl] = Json("{ \"heroes\": [ { \"name\": \"A\" }, { \"name\": \"a\" } ] }");
                break;
        }
        var (updater, assets) = Create();

        var result = await updater.UpdateAsync();

        Assert.False(result.Ok);
        Assert.Empty(result.Added);
        Assert.Empty(assets.Names(AssetCategory.Heroes));
        Assert.DoesNotContain(_requested, u => u.Contains("evil.example"));
    }

    [Fact]
    public async Task A_picture_that_is_not_an_image_or_leads_out_of_the_folder_is_not_saved()
    {
        Release();
        _responses[ManifestUrl] = Json("""
            { "revision": 2, "heroes": [
              { "name": "Fake", "role": "Tank", "image": "Heroes/fake.png", "image_url": "https://github.com/x/fake.png", "body_image": "HeroesBody/fake.png" },
              { "name": "Escape", "role": "Tank", "image": "../outside.png", "image_url": "https://github.com/x/escape.png" } ] }
            """);
        _responses["https://github.com/x/fake.png"] = new HttpResult(200, System.Text.Encoding.UTF8.GetBytes("<html>not an image at all</html>"));
        _responses["https://github.com/x/escape.png"] = new HttpResult(200, Png);
        var (updater, _) = Create();

        var result = await updater.UpdateAsync();

        Assert.True(result.Ok);
        Assert.Contains("could not be downloaded", result.Message);
        Assert.False(File.Exists(Path.Combine(_tmp, "user", "Heroes", "fake.png")));
        Assert.False(File.Exists(Path.Combine(_tmp, "outside.png")));
        Assert.False(File.Exists(Path.Combine(_tmp, "user", "..", "outside.png")));
    }
}

public sealed class OverlayHeroLookupTests
{
    [Fact]
    public void The_overlay_finds_a_heros_body_picture_in_the_manifest_index_by_name_or_alias()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HTML", "sow-data.js"))) dir = dir.Parent;
        if (dir is null) return;

        var script = Path.Combine(Path.GetTempPath(), "sowb-hero-" + Guid.NewGuid().ToString("N") + ".mjs");
        File.WriteAllText(script, """
            import { readFileSync } from 'node:fs';
            import vm from 'node:vm';
            const window = {};
            const index = { heroes: [
              { name: 'D.Va', body_image: 'HeroesBody/Dva.png', aliases: ['Dva', 'D Va'] },
              { name: 'Ana', body_image: 'HeroesBody/Ana.png' },
              { name: 'NoBody' } ] };
            const ctx = vm.createContext({ window, Promise, JSON, Date, Math, Object, String, Array, console,
              fetch: async url => String(url).startsWith('/Scoreboard/Heroes/index.json')
                ? { ok: true, json: async () => index }
                : { ok: false, json: async () => ({}), text: async () => '' } });
            vm.runInContext(readFileSync(process.argv[2], 'utf8'), ctx);
            const out = [];
            for (const n of ['D.Va', 'dva', '  d   va ', 'ANA', 'NoBody', 'Unknown', '']) out.push(await window.SOW.heroBodyPath(n));
            console.log(JSON.stringify(out));
            """);
        try
        {
            var info = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            info.ArgumentList.Add(script);
            info.ArgumentList.Add(Path.Combine(dir.FullName, "HTML", "sow-data.js"));
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.Equal("[\"HeroesBody/Dva.png\",\"HeroesBody/Dva.png\",\"HeroesBody/Dva.png\",\"HeroesBody/Ana.png\",null,null,null]", output.Trim());
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // no Node here: skipped
        }
        finally { File.Delete(script); }
    }
}

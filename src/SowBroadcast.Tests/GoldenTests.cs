using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Tests;

/// <summary>
/// Compares the exporter with what the legacy Python app wrote for the same state
/// (regenerate with tools/golden/generate_legacy_golden.py).
/// </summary>
public sealed class GoldenTests : IDisposable
{
    private static readonly string GoldenRoot = Path.Combine(AppContext.BaseDirectory, "Golden");
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly string FixturesPosix = FixturesRoot.Replace('\\', '/');

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-golden-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    public static IEnumerable<object[]> Cases => new[] { new object[] { "default" }, new object[] { "full" } };

    private static string Detokenise(string text) => text.Replace("{FIXTURES}", FixturesPosix);

    private static AppState LoadState(string name) =>
        StateStore.Deserialize(Detokenise(File.ReadAllText(Path.Combine(GoldenRoot, name, "state.json"))));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Exporter_writes_the_same_files_as_the_legacy_app(string name)
    {
        var scoreboard = Path.Combine(_tmp, "Scoreboard");
        var exporter = new ScoreboardExporter(scoreboard, new SkiaLogoConverter());

        exporter.ExportAll(LoadState(name));

        var expectedRoot = Path.Combine(GoldenRoot, name, "Scoreboard");
        var expectedFiles = Directory.GetFiles(expectedRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(expectedRoot, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal).ToList();

        // Same set of text/json files (the legacy app's dead outputs and timestamps are not reproduced).
        var actualFiles = Directory.GetFiles(scoreboard, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(scoreboard, f).Replace('\\', '/'))
            .Where(f => (f.EndsWith(".txt") || f.EndsWith(".json")) && !f.EndsWith("updated_at.txt"))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.Equal(expectedFiles.Where(f => f != "Match/maps.txt" && f != "Match/map_pool.txt"), actualFiles);

        foreach (var relative in expectedFiles.Where(f => f != "Match/maps.txt" && f != "Match/map_pool.txt"))
        {
            var expected = Detokenise(File.ReadAllText(Path.Combine(expectedRoot, relative), System.Text.Encoding.UTF8));
            var actual = File.ReadAllText(Path.Combine(scoreboard, relative), System.Text.Encoding.UTF8);

            if (relative.EndsWith(".json"))
                Assert.True(Canonical(expected) == Canonical(actual), $"{relative}\nexpected: {Canonical(expected)}\nactual:   {Canonical(actual)}");
            else
                Assert.True(expected == actual, $"{relative}\nexpected: [{Escape(expected)}]\nactual:   [{Escape(actual)}]");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Exported_logos_are_png_with_the_legacy_dimensions(string name)
    {
        var scoreboard = Path.Combine(_tmp, "Scoreboard");
        new ScoreboardExporter(scoreboard, new SkiaLogoConverter()).ExportAll(LoadState(name));

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenRoot, name, "manifest.json")));
        var expected = manifest.RootElement.GetProperty("png").EnumerateObject()
            .ToDictionary(p => p.Name, p => (p.Value[0].GetInt32(), p.Value[1].GetInt32()));

        var actual = Directory.GetFiles(scoreboard, "*.png", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(scoreboard, f).Replace('\\', '/'), f =>
            {
                using var bitmap = SKBitmap.Decode(f);
                return (bitmap.Width, bitmap.Height);
            });

        Assert.Equal(expected.OrderBy(x => x.Key), actual.OrderBy(x => x.Key));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Match_json_has_the_legacy_shape(string name)
    {
        var scoreboard = Path.Combine(_tmp, "Scoreboard");
        new ScoreboardExporter(scoreboard, new SkiaLogoConverter()).ExportAll(LoadState(name));

        var expected = Detokenise(File.ReadAllText(Path.Combine(GoldenRoot, name, "match.json")));
        var actual = File.ReadAllText(Path.Combine(scoreboard, "Match", "match.json"));

        Assert.Equal(Canonical(expected), Canonical(actual));
    }

    [Fact]
    public void Second_export_of_the_same_state_writes_nothing()
    {
        var exporter = new ScoreboardExporter(Path.Combine(_tmp, "Scoreboard"), new SkiaLogoConverter());
        var state = LoadState("full");
        exporter.ExportAll(state);

        var again = exporter.ExportAll(state);

        Assert.Empty(again);
    }

    [Fact]
    public void Clearing_a_logo_removes_the_stale_png()
    {
        var scoreboard = Path.Combine(_tmp, "Scoreboard");
        var exporter = new ScoreboardExporter(scoreboard, new SkiaLogoConverter());
        var state = LoadState("full");
        exporter.ExportAll(state);
        Assert.True(File.Exists(Path.Combine(scoreboard, "Match", "T1Logo.png")));

        exporter.ExportAll(state with { Team1 = state.Team1 with { LogoPath = null } });

        Assert.False(File.Exists(Path.Combine(scoreboard, "Match", "T1Logo.png")));
    }

    // ---- diff keys ---------------------------------------------------------------------------------------

    private static readonly Dictionary<string, Func<AppState, AppState>> Mutations = new()
    {
        ["team1 score"] = s => s with { Team1 = s.Team1 with { Score = 2 } },
        ["team2 name"] = s => s with { Team2 = s.Team2 with { Name = "Midnight Sun 2" } },
        ["team1 color"] = s => s with { Team1 = s.Team1 with { ColorHex = "#112233" } },
        ["team1 abbr"] = s => s with { Team1 = s.Team1 with { Abbr = "NLX" } },
        ["team1 player name"] = s => s with { Team1 = s.Team1 with { Players = s.Team1.Players.Select((p, i) => i == 0 ? p with { Name = "Renamed" } : p).ToArray() } },
        ["map1 ban"] = s => s with { Maps = Map(s, 0, m => m with { T1Ban = "Echo" }) },
        ["map2 score"] = s => s with { Maps = Map(s, 1, m => m with { T1 = 1 }) },
        ["map2 pick"] = s => s with { Maps = Map(s, 1, m => m with { Pick = "T1" }) },
        ["map name"] = s => s with { Maps = Map(s, 2, m => m with { Map = "Havana" }) },
        ["map completed"] = s => s with { Maps = Map(s, 1, m => m with { Completed = true }) },
        ["general host"] = s => s with { General = s.General with { Host = "New Host" } },
        ["general caster1"] = s => s with { General = s.General with { Caster1 = "Other" } },
        ["general status"] = s => s with { General = s.General with { StatusText = "Another status" } },
        ["general maps_count"] = s => s with { General = s.General with { MapsCount = 5 } },
        ["general colors"] = s => s with { General = s.General with { Colors = new Dictionary<string, string>(s.General.Colors) { ["primary"] = "#000000" } } },
        ["waiting text"] = s => s with { Waiting = s.Waiting with { TextBrb = "BRB 2" } },
        ["waiting timer"] = s => s with { Waiting = s.Waiting with { TimerSeconds = 120 } },
        ["waiting timer running"] = s => s with { Waiting = s.Waiting with { TimerRunning = true } },
        ["waiting socials"] = s => s with { Waiting = s.Waiting with { Socials = new Dictionary<string, string>(s.Waiting.Socials) { ["twitch"] = "other" } } },
        ["waiting ticker"] = s => s with { Waiting = s.Waiting with { TickerOverride = "New ticker" } },
        ["standings title"] = s => s with { Standings = s.Standings with { Title = "New title" } },
        ["bracket title"] = s => s with { Bracket = s.Bracket with { Title = "New bracket" } },
        ["no change"] = s => s,
    };

    private static IReadOnlyList<MapEntry> Map(AppState s, int index, Func<MapEntry, MapEntry> change) =>
        s.Maps.Select((m, i) => i == index ? change(m) : m).ToArray();

    [Fact]
    public void First_update_reports_every_key_in_the_legacy_order()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenRoot, "diff_keys.json")));
        var expected = doc.RootElement.GetProperty("first_update").EnumerateArray().Select(e => e.GetString()!).ToList();

        expected.Add("schedule");   // new in SOW Broadcast 3 (the waiting-screen match schedule); the legacy app had no such key

        Assert.Equal(expected, StateDiff.ChangedKeys(null, LoadState("full")));
    }

    [Fact]
    public void Changed_keys_match_the_legacy_diff_for_every_recorded_step()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenRoot, "diff_keys.json")));
        var steps = doc.RootElement.GetProperty("steps").EnumerateArray().ToList();
        var baseline = LoadState("full");

        Assert.Equal(Mutations.Count, steps.Count);
        foreach (var step in steps)
        {
            var name = step.GetProperty("name").GetString()!;
            var expected = step.GetProperty("keys").EnumerateArray().Select(e => e.GetString()!).OrderBy(k => k, StringComparer.Ordinal).ToList();

            var actual = StateDiff.ChangedKeys(baseline, Mutations[name](baseline)).OrderBy(k => k, StringComparer.Ordinal).ToList();

            Assert.True(expected.SequenceEqual(actual), $"{name}: expected [{string.Join(", ", expected)}] got [{string.Join(", ", actual)}]");
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    /// <summary>JSON with sorted keys and no whitespace, so key order and indentation do not matter.</summary>
    private static string Canonical(string json) => Sort(JsonNode.Parse(json)).ToJsonString();

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
        JsonArray arr => new JsonArray(arr.Select(Sort).ToArray()),
        _ => node?.DeepClone(),
    };

    private static string Escape(string text) => text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
}

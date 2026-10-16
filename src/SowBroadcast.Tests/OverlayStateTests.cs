using System.Text.Json;
using System.Text.Json.Nodes;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;
using SowBroadcast.Server;

namespace SowBroadcast.Tests;

/// <summary>The overlay contract: <c>GET /api/state</c> JSON and the version that goes with each event.</summary>
public sealed class OverlayStateTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-state-" + Guid.NewGuid().ToString("N"));
    private readonly string _scoreboard;

    public OverlayStateTests()
    {
        _scoreboard = Path.Combine(_tmp, "Scoreboard");
        Directory.CreateDirectory(_scoreboard);
    }

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private ScoreboardExporter Exporter(bool legacy = false) => new(_scoreboard, new SkiaLogoConverter(), legacyFiles: legacy);

    private static AppState Sample() => AppState.CreateDefault() with
    {
        Team1 = new Team { Name = "Alpha", Abbr = "ALP", ColorHex = "#112233", Score = 2, LogoPath = "C:/secret/logo.png" },
        Team2 = new Team { Name = "Bravo", ColorHex = Defaults.Team2Color },
        Maps = AppState.DefaultMaps().Select(m => m.Index == 1 ? m with { Map = "Busan", T1 = 2, T2 = 1, Completed = true, Pick = Picks.Team1 } : m).ToArray(),
        General = new GeneralSettings { MapsCount = 3, StatusText = "Cup", OverlayLogoPath = "C:/secret/overlay.png" },
        Waiting = new WaitingSettings { TextBrb = "BRB", TimerSeconds = 90, TimerRunning = true, TimerEndsAtMs = 1_760_000_090_000 },
        Standings = new StandingsSettings { Title = "Table", Groups = new[] { new StandingsGroup { Key = "g", Name = "A", Rows = new[] { new StandingsRow { TeamName = "Alpha", Points = 3 } } } } },
        Bracket = new BracketSettings { Title = "Playoffs" },
    };

    private JsonObject Build(AppState state, long version = 5) =>
        JsonNode.Parse(OverlayStateJson.Build(state, Exporter(), version, 1_760_000_000_000))!.AsObject();

    [Fact]
    public void The_state_has_the_match_the_waiting_extras_and_the_ready_made_overlay_objects()
    {
        var state = Build(Sample());

        Assert.Equal(1, state["schema_version"]!.GetValue<int>());
        Assert.Equal((5L, 1_760_000_000_000L), (state["version"]!.GetValue<long>(), state["server_time"]!.GetValue<long>()));
        Assert.Equal("Alpha", state["match"]!["team1"]!["name"]!.GetValue<string>());
        Assert.Equal("#112233", state["match"]!["team1"]!["color_hex"]!.GetValue<string>());
        Assert.Equal("Busan", state["match"]!["maps"]![0]!["map"]!.GetValue<string>());
        Assert.Equal(2, state["match"]!["current_map"]!.GetValue<int>());
        Assert.StartsWith("Alpha (2 - 0) Bravo", state["matchtext"]!.GetValue<string>());
        Assert.Equal("Table", state["standings"]!["title"]!.GetValue<string>());
        Assert.Equal("Playoffs", state["bracket"]!["title"]!.GetValue<string>());
        Assert.Equal(1, state["standings"]!["groups"]![0]!["rows"]![0]!["rank"]!.GetValue<int>());   // ranks are filled in
    }

    [Fact]
    public void No_path_of_this_machine_is_part_of_the_state_and_the_tabs_models_are_not_duplicated()
    {
        var json = OverlayStateJson.Build(Sample(), Exporter(), 1, 0);

        Assert.DoesNotContain("secret", json);
        var match = JsonNode.Parse(json)!["match"]!.AsObject();
        Assert.False(match.ContainsKey("standings"));   // the overlay objects are top level
        Assert.False(match.ContainsKey("bracket"));
        Assert.Null(match["team1"]!["logo_path"]);
    }

    [Fact]
    public void The_timer_has_an_end_time_only_while_it_runs()
    {
        var running = Build(Sample())["timer"]!;
        var stopped = Build(Sample() with { Waiting = Sample().Waiting with { TimerRunning = false } })["timer"]!;

        Assert.Equal((90, true, 1_760_000_090_000L), (running["seconds"]!.GetValue<int>(), running["running"]!.GetValue<bool>(), running["ends_at"]!.GetValue<long>()));
        Assert.Equal((90, false, 0L), (stopped["seconds"]!.GetValue<int>(), stopped["running"]!.GetValue<bool>(), stopped["ends_at"]!.GetValue<long>()));
    }

    [Fact]
    public void The_waiting_playlist_is_part_of_the_state_after_the_exporter_synced_it()
    {
        var videos = Path.Combine(_tmp, "videos");
        Directory.CreateDirectory(videos);
        File.WriteAllText(Path.Combine(videos, "b.mp4"), "x");
        File.WriteAllText(Path.Combine(videos, "a.mp4"), "x");
        File.WriteAllText(Path.Combine(videos, "notes.txt"), "x");
        var exporter = Exporter();
        exporter.ExportAll(Sample() with { Waiting = Sample().Waiting with { VideosDir = videos } });

        var state = JsonNode.Parse(OverlayStateJson.Build(Sample(), exporter, 1, 0))!;

        Assert.Equal(new[] { "a.mp4", "b.mp4" }, state["videos"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.True(File.Exists(Path.Combine(_scoreboard, "Waiting", "Playlist", "a.mp4")));   // the videos themselves stay files
    }

    [Fact]
    public void Without_the_legacy_files_only_what_has_to_be_a_file_is_written()
    {
        var logo = Path.Combine(AppContext.BaseDirectory, "Fixtures", "logos", "team1.png");
        var state = Sample() with { Team1 = Sample().Team1 with { LogoPath = logo }, General = Sample().General with { Host = "Host", Caster1 = "C1", Caster2 = "C2", OverlayLogoPath = null } };

        Exporter(legacy: false).ExportAll(state);

        var files = Directory.GetFiles(_scoreboard, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_scoreboard, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "General/caster1.txt", "General/caster2.txt", "General/host.txt", "Match/T1Logo.png" }, files);
        Assert.Equal("Host", File.ReadAllText(Path.Combine(_scoreboard, "General", "host.txt")));
    }

    [Fact]
    public void The_standings_and_bracket_objects_are_what_the_legacy_files_held()
    {
        var goldenState = Path.Combine(AppContext.BaseDirectory, "Golden", "full", "state.json");
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures").Replace('\\', '/');
        var state = StateStore.Deserialize(File.ReadAllText(goldenState).Replace("{FIXTURES}", fixtures));
        var root = Path.Combine(AppContext.BaseDirectory, "Golden", "full", "Scoreboard");

        var api = JsonNode.Parse(OverlayStateJson.Build(state, new ScoreboardExporter(_scoreboard, new SkiaLogoConverter()), 1, 0))!;

        foreach (var (key, file) in new[] { ("standings", "Standings/standings.json"), ("bracket", "Bracket/bracket.json") })
        {
            var expected = File.ReadAllText(Path.Combine(root, file)).Replace("{FIXTURES}", fixtures);
            Assert.Equal(Canonical(JsonNode.Parse(expected)!), Canonical(api[key]!));
        }
    }

    private static string Canonical(JsonNode node) => JsonSerializer.Serialize(Sorted(node));

    private static object? Sorted(JsonNode? node) => node switch
    {
        JsonObject o => o.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => Sorted(p.Value)),
        JsonArray a => a.Select(Sorted).ToList(),
        JsonValue v => v.GetValue<JsonElement>().ValueKind == JsonValueKind.Null ? null : v.GetValue<JsonElement>().ToString(),
        _ => null,
    };

    // ---- the publisher keeps the state and the event version together --------------------------------------

    private (OverlayPublisher Publisher, OverlayStateHolder Holder, EventHub Hub) Publisher()
    {
        var hub = new EventHub(StateDiff.AllKeys);
        var holder = new OverlayStateHolder();
        return (new OverlayPublisher(Exporter(), hub, "http://127.0.0.1:1", holder, () => 1_760_000_000_000), holder, hub);
    }

    private static long VersionOf(OverlayStateHolder holder) => JsonNode.Parse(holder.Json)!["version"]!.GetValue<long>();

    [Fact]
    public void The_state_is_already_there_with_the_version_of_the_event_that_announces_it()
    {
        var (publisher, holder, hub) = Publisher();
        var reader = hub.Subscribe(null).Reader;

        publisher.Publish(Sample());

        Assert.True(reader.TryRead(out var announced));
        Assert.Equal(announced!.Id, VersionOf(holder));
        Assert.Contains("Alpha", holder.Json);
    }

    [Fact]
    public void Each_publish_with_changes_raises_the_version_and_one_without_leaves_it()
    {
        var (publisher, holder, hub) = Publisher();
        publisher.Publish(Sample());
        var first = VersionOf(holder);

        publisher.Publish(Sample());                                     // nothing changed
        Assert.Equal(first, VersionOf(holder));

        publisher.Publish(Sample() with { Team2 = Sample().Team2 with { Name = "Bravo 2" } });
        Assert.Equal(first + 1, VersionOf(holder));
        Assert.Equal(hub.LastId, VersionOf(holder));
    }

    [Fact]
    public void A_partial_publish_changes_only_its_part_of_the_state()
    {
        var (publisher, holder, _) = Publisher();
        publisher.Publish(Sample());
        var edited = Sample() with
        {
            Team1 = Sample().Team1 with { Name = "Unsent" },
            General = Sample().General with { StatusText = "New status" },
        };

        publisher.PublishPart(edited, PublishScope.General);

        var match = JsonNode.Parse(holder.Json)!["match"]!;
        Assert.Equal("New status", match["general"]!["status_text"]!.GetValue<string>());
        Assert.Equal("Alpha", match["team1"]!["name"]!.GetValue<string>());       // the Match tab was not updated
    }

    [Fact]
    public void A_timer_command_puts_the_new_end_time_into_the_state()
    {
        var (publisher, holder, _) = Publisher();
        publisher.Publish(Sample() with { Waiting = new WaitingSettings() });

        publisher.PublishPart(Sample(), PublishScope.Timer);

        var timer = JsonNode.Parse(holder.Json)!["timer"]!;
        Assert.Equal((true, 1_760_000_090_000L), (timer["running"]!.GetValue<bool>(), timer["ends_at"]!.GetValue<long>()));
    }

    [Fact]
    public void A_new_replay_raises_the_version_too()
    {
        var (publisher, holder, hub) = Publisher();
        publisher.Publish(Sample());
        var before = VersionOf(holder);

        publisher.AnnounceReplay();

        Assert.Equal(before + 1, VersionOf(holder));
        Assert.Equal(hub.LastId, VersionOf(holder));
    }
}

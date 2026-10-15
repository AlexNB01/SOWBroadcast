using System.Text.Json;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Tests;

public sealed class StateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-tests-" + Guid.NewGuid().ToString("N"));

    public StateStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    // Synthetic data in the shape of the legacy (Python) app's autosave.json, including leftovers
    // the new app ignores: "assets", "map_pool", "banned_hero", "maps_for"/"maps_against".
    private const string LegacyJson = """
    {
      "team1": { "name": "Alpha", "abbr": "ALP", "logo_path": "C:/x/alpha.png", "score": 1, "color_hex": "#00007f",
                 "players": [ { "name": "P1", "hero": "Ana", "role": "Support" }, { "name": "P2", "hero": "", "role": "" } ],
                 "banned_hero": "" },
      "team2": { "name": "Beta", "abbr": "BET", "logo_path": null, "score": 0, "color_hex": "#ffaa00", "players": [], "banned_hero": "" },
      "maps": [
        { "index": 1, "map": "Lijiang Tower", "t1": 2, "t2": 1, "completed": true, "pick": "T1", "t1_ban": "Ana", "t2_ban": "", "winner": "t1" },
        { "index": 2, "map": "King's Row", "t1": 0, "t2": 0, "completed": false, "pick": "—", "t1_ban": "", "t2_ban": "", "winner": "" }
      ],
      "current_map": null,
      "map_pool": [],
      "assets": { "heroes": { "Ana": { "name": "Ana", "image_path": "Heroes\\ana.png" } }, "maps": {}, "modes": {} },
      "general": { "first_to": 2, "maps_count": 3, "format_type": "First to", "num_players": 5, "division": "", "host": "H",
                   "caster1": "C1 @c1", "caster2": "", "status_text": "Cup #1", "overlay_logo_path": null, "transition_logo_path": null,
                   "colors": { "primary": "#ffffff", "secondary": "#000000", "tertiary": "#55aaff", "quaternary": "#006ea1",
                               "quinary": "#FFFFFF", "senary": "#FFFFFF", "septenary": "#FFFFFF", "octonary": "#006ea1" } },
      "waiting": { "videos_dir": "", "timer_seconds": 90, "text_starting": "STARTING SOON!", "text_brb": "BE RIGHT BACK!",
                   "text_end": "THANK YOU FOR WATCHING", "timer_running": false, "socials": { "twitch": "suomiow" },
                   "ticker_override": "", "ticker_override_enabled": false },
      "standings": { "title": "Group stage", "subtitle": "", "columns": { "mode": "map_diff" },
                     "rows": [], "groups": [ { "key": "group_1", "name": "Group 1", "rows": [
                        { "team_name": "Alpha", "abbr": "ALP", "logo_path": "", "wins": 2, "losses": 0, "map_diff": 3, "points": 6,
                          "status": "qualified", "rank": 1, "maps_for": 4, "maps_against": 1 } ] } ],
                     "display_group": "__all__" },
      "bracket": { "title": "", "stage": "", "rounds": [ { "name": "Final", "side": "upper", "matches": [
                      { "id": "m1-1", "bo_label": "BO5", "team1": { "name": "Alpha", "abbr": "ALP", "logo_path": null },
                        "team2": { "name": "", "abbr": "", "logo_path": null }, "score1": 3, "score2": 1, "status": "completed",
                        "scheduled_at": 1760000000000 } ] } ],
                   "double_elim_view": "", "teams": [] },
      "draft_link_url": "https://sowdraft.fi/stream?matchId=abc&role=stream",
      "tournament_link_url": ""
    }
    """;

    [Fact]
    public void Loads_legacy_autosave_and_ignores_unknown_keys()
    {
        var s = StateStore.Deserialize(LegacyJson);

        Assert.Equal(StateSchema.Current, s.SchemaVersion);
        Assert.Equal("Alpha", s.Team1.Name);
        Assert.Equal("#00007f", s.Team1.ColorHex);
        Assert.Equal("C:/x/alpha.png", s.Team1.LogoPath);
        Assert.Null(s.Team2.LogoPath);
        Assert.Equal("Ana", s.Team1.Players[0].Hero);
        Assert.Equal("Support", s.Team1.Players[0].Role);
        Assert.Equal("King's Row", s.Maps[1].Map);
        Assert.Equal("—", s.Maps[1].Pick);
        Assert.Equal("Ana", s.Maps[0].T1Ban);
        Assert.Null(s.CurrentMap);
        Assert.Equal(3, s.General.MapsCount);
        Assert.Equal("C1 @c1", s.General.Caster1);
        Assert.Equal(90, s.Waiting.TimerSeconds);
        Assert.Equal("suomiow", s.Waiting.Socials["twitch"]);
        Assert.Equal("qualified", s.Standings.Groups[0].Rows[0].Status);
        Assert.Equal(3, s.Standings.Groups[0].Rows[0].MapDiff);
        Assert.Equal(1760000000000L, s.Bracket.Rounds[0].Matches[0].ScheduledAt);
        Assert.Equal("BO5", s.Bracket.Rounds[0].Matches[0].BoLabel);
        Assert.Contains("matchId=abc", s.DraftLinkUrl);
    }

    [Fact]
    public void Loading_pads_players_and_maps_to_their_fixed_sizes()
    {
        var s = StateStore.Deserialize(LegacyJson);

        Assert.Equal(Limits.PlayersPerTeam, s.Team1.Players.Count);
        Assert.Equal(Limits.PlayersPerTeam, s.Team2.Players.Count);
        Assert.Equal(Limits.MapsPerSeries, s.Maps.Count);
        Assert.Equal(Enumerable.Range(1, Limits.MapsPerSeries), s.Maps.Select(m => m.Index));
    }

    [Fact]
    public void Winner_is_derived_from_scores_not_read_from_file()
    {
        var json = """{ "maps": [ { "index": 1, "t1": 1, "t2": 3, "winner": "t1" }, { "index": 2, "t1": 2, "t2": 2, "winner": "t2" } ] }""";

        var s = StateStore.Deserialize(json);

        Assert.Equal("t2", s.Maps[0].Winner);
        Assert.Equal("", s.Maps[1].Winner);
    }

    [Fact]
    public void Serialises_to_the_legacy_snake_case_keys()
    {
        var json = StateStore.Serialize(AppState.CreateDefault());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        foreach (var key in new[] { "team1", "team2", "maps", "current_map", "general", "waiting", "standings", "bracket",
                                    "draft_link_url", "tournament_link_url", "schema_version" })
            Assert.True(root.TryGetProperty(key, out _), $"missing top-level key {key}");

        var team = root.GetProperty("team1");
        foreach (var key in new[] { "name", "abbr", "logo_path", "score", "color_hex", "players", "banned_hero" })
            Assert.True(team.TryGetProperty(key, out _), $"missing team key {key}");

        var map = root.GetProperty("maps")[0];
        foreach (var key in new[] { "index", "map", "t1", "t2", "completed", "pick", "t1_ban", "t2_ban", "winner" })
            Assert.True(map.TryGetProperty(key, out _), $"missing map key {key}");

        var general = root.GetProperty("general");
        foreach (var key in new[] { "first_to", "maps_count", "num_players", "host", "caster1", "caster2", "status_text",
                                    "overlay_logo_path", "transition_logo_path", "colors" })
            Assert.True(general.TryGetProperty(key, out _), $"missing general key {key}");
        Assert.True(general.GetProperty("colors").TryGetProperty("octonary", out _));

        var waiting = root.GetProperty("waiting");
        foreach (var key in new[] { "videos_dir", "timer_seconds", "text_starting", "text_brb", "text_end", "timer_running",
                                    "socials", "ticker_override", "ticker_override_enabled" })
            Assert.True(waiting.TryGetProperty(key, out _), $"missing waiting key {key}");

        Assert.True(root.GetProperty("standings").TryGetProperty("display_group", out _));
        Assert.True(root.GetProperty("bracket").TryGetProperty("double_elim_view", out _));
    }

    [Fact]
    public void Round_trip_preserves_values_and_does_not_escape_the_em_dash()
    {
        var original = StateStore.Deserialize(LegacyJson);

        var json = StateStore.Serialize(original);
        var again = StateStore.Deserialize(json);

        Assert.Contains("\"pick\": \"—\"", json);
        Assert.Equal(original.Team1.Name, again.Team1.Name);
        Assert.Equal(original.Maps[0].T1Ban, again.Maps[0].T1Ban);
        Assert.Equal(original.Waiting.TimerSeconds, again.Waiting.TimerSeconds);
        Assert.Equal(original.Bracket.Rounds[0].Matches[0].Team1.Name, again.Bracket.Rounds[0].Matches[0].Team1.Name);
        Assert.Equal(StateStore.Serialize(again), json);
    }

    [Fact]
    public void Defaults_match_the_legacy_app()
    {
        var s = AppState.CreateDefault();

        Assert.Equal("#55aaff", s.Team1.ColorHex);
        Assert.Equal("#ff557f", s.Team2.ColorHex);
        Assert.Equal(Limits.MapsPerSeries, s.General.MapsCount);
        Assert.Equal(5, s.General.NumPlayers);
        Assert.Equal("STARTING SOON!", s.Waiting.TextStarting);
        Assert.Equal("#006ea1", s.General.Colors["octonary"]);
        Assert.Equal("—", s.Maps[0].Pick);
        Assert.Equal(Limits.BracketTeams, s.Bracket.Teams.Count);
    }

    [Fact]
    public void Save_writes_atomically_and_keeps_the_previous_version_as_backup()
    {
        var path = Path.Combine(_dir, "autosave.json");
        var first = AppState.CreateDefault() with { Team1 = new Team { Name = "One" } };
        var second = AppState.CreateDefault() with { Team1 = new Team { Name = "Two" } };

        StateStore.Save(path, first);
        StateStore.Save(path, second);

        Assert.Equal("Two", StateStore.Load(path).Team1.Name);
        Assert.Equal("One", StateStore.Load(path + ".bak").Team1.Name);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void LoadWithRecovery_falls_back_to_the_backup_when_the_file_is_corrupt()
    {
        var path = Path.Combine(_dir, "autosave.json");
        StateStore.Save(path, AppState.CreateDefault() with { Team1 = new Team { Name = "Good" } });
        StateStore.Save(path, AppState.CreateDefault() with { Team1 = new Team { Name = "Newer" } });
        File.WriteAllText(path, "{ this is not json");

        var recovered = StateStore.LoadWithRecovery(path);

        Assert.NotNull(recovered);
        Assert.Equal("Good", recovered!.Team1.Name);
    }

    [Fact]
    public void LoadWithRecovery_returns_null_when_nothing_exists()
    {
        Assert.Null(StateStore.LoadWithRecovery(Path.Combine(_dir, "missing.json")));
    }

    [Fact]
    public void Invalid_json_throws_a_state_load_exception()
    {
        Assert.Throws<StateLoadException>(() => StateStore.Deserialize("not json"));
        Assert.Throws<StateLoadException>(() => StateStore.Deserialize("null"));
    }

    [Fact]
    public void Save_creates_missing_directories()
    {
        var path = Path.Combine(_dir, "a", "b", "state.json");

        StateStore.Save(path, AppState.CreateDefault());

        Assert.True(File.Exists(path));
    }
}

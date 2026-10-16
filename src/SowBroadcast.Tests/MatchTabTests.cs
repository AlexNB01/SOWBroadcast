using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Match;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Tests;

public sealed class MatchTabTests : IDisposable
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly string FixturesAssets = Path.Combine(FixturesRoot, "assets");

    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-match-" + Guid.NewGuid().ToString("N"));
    private readonly FakePicker _picker = new();

    public MatchTabTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private static string Fx(string name) => Path.Combine(FixturesAssets, name);

    private AssetService Catalogue()
    {
        var assets = new AssetService(Path.Combine(_tmp, "Scoreboard"), Array.Empty<string>());
        assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Heroes, Name = "Ana", Role = "Support", ImageSourcePath = Fx("lucio.png"), BodySourcePath = Fx("lucio_body.png") });
        assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Heroes, Name = "Doomfist", Role = "Tank", ImageSourcePath = Fx("lucio.png"), BodySourcePath = Fx("lucio_body.png") });
        assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Heroes, Name = "Mystery", ImageSourcePath = Fx("lucio.png"), BodySourcePath = Fx("lucio_body.png") });
        assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = "Busan", Mode = "Control", ImageSourcePath = Fx("busan.png") });
        return assets;
    }

    private MatchViewModel Match(AssetService? assets = null) => new(assets ?? Catalogue(), _picker, Path.Combine(_tmp, "logos"));

    private static AppState Sample() => AppState.CreateDefault() with
    {
        Team1 = new Team
        {
            Name = "Northern Lights", Abbr = "NL", Score = 2, ColorHex = "#27aae1",
            Players = new[] { new Player { Name = "Aurora", Hero = "Ana", Role = "Support" } }.Concat(Team.EmptyPlayers().Skip(1)).ToArray(),
        },
        Team2 = new Team { Name = "Midnight Sun", Abbr = "MS", Score = 1, ColorHex = Defaults.Team2Color },
        Maps = new[]
        {
            new MapEntry { Index = 1, Map = "Busan", T1 = 2, T2 = 1, Completed = true, Pick = Picks.Team1, T1Ban = "Ana", T2Ban = "Doomfist" },
            new MapEntry { Index = 2, Map = "Busan", T1 = 0, T2 = 3, Completed = false, Pick = Picks.Team2 },
        }.Concat(Enumerable.Range(3, 5).Select(i => new MapEntry { Index = i })).ToArray(),
    };

    // ---- Swap / Reset ------------------------------------------------------------------------------------

    [Fact]
    public void Swap_trades_the_teams_with_their_scores_and_players()
    {
        var swapped = MatchOperations.Swap(Sample());

        Assert.Equal("Midnight Sun", swapped.Team1.Name);
        Assert.Equal(1, swapped.Team1.Score);
        Assert.Equal("Northern Lights", swapped.Team2.Name);
        Assert.Equal(2, swapped.Team2.Score);
        Assert.Equal("Aurora", swapped.Team2.Players[0].Name);
    }

    [Fact]
    public void A_custom_colour_travels_with_its_team_and_a_default_colour_stays_with_its_slot()
    {
        var swapped = MatchOperations.Swap(Sample());

        Assert.Equal(Defaults.Team1Color, swapped.Team1.ColorHex); // team 2 had the slot default
        Assert.Equal("#27aae1", swapped.Team2.ColorHex);           // team 1's own colour moved along
    }

    [Fact]
    public void Swap_exchanges_map_scores_picks_and_bans_but_not_the_completed_flag()
    {
        var swapped = MatchOperations.Swap(Sample());
        var first = swapped.Maps[0];
        var second = swapped.Maps[1];

        Assert.Equal((1, 2), (first.T1, first.T2));
        Assert.Equal(Picks.Team2, first.Pick);
        Assert.Equal(("Doomfist", "Ana"), (first.T1Ban, first.T2Ban));
        Assert.True(first.Completed);
        Assert.Equal("t2", first.Winner);
        Assert.Equal(Picks.Team1, second.Pick);
        Assert.False(second.Completed);
        Assert.Equal(2, swapped.CurrentMap);
    }

    [Fact]
    public void Swapping_twice_restores_the_match()
    {
        var original = Sample();

        var again = MatchOperations.Swap(MatchOperations.Swap(original));

        Assert.Equal(StateStore.Serialize(StateDerivations.Apply(original)), StateStore.Serialize(again));
    }

    [Fact]
    public void Swap_leaves_unset_picks_alone()
    {
        var swapped = MatchOperations.Swap(Sample());

        Assert.Equal(Picks.Tba, swapped.Maps[2].Pick);
    }

    [Fact]
    public void Reset_clears_the_teams_and_maps_but_keeps_everything_else()
    {
        var state = Sample() with
        {
            General = new GeneralSettings { MapsCount = 3, NumPlayers = 6, Host = "Host" },
            TournamentLinkUrl = "https://example.test/tournament/x",
        };

        var reset = MatchOperations.Reset(state);

        Assert.Equal("", reset.Team1.Name);
        Assert.Equal(Defaults.Team1Color, reset.Team1.ColorHex);
        Assert.Equal(Defaults.Team2Color, reset.Team2.ColorHex);
        Assert.All(reset.Maps, m => Assert.Equal("", m.Map));
        Assert.Equal(7, reset.Maps.Count);
        Assert.Equal(1, reset.CurrentMap);
        Assert.Equal(3, reset.General.MapsCount);
        Assert.Equal("Host", reset.General.Host);
        Assert.Equal("https://example.test/tournament/x", reset.TournamentLinkUrl);
    }

    // ---- team files --------------------------------------------------------------------------------------

    [Fact]
    public void A_team_file_round_trips_and_keeps_the_current_score()
    {
        var team = Sample().Team1 with { LogoPath = Fx("lucio.png"), BannedHero = "Ana" };
        var path = Path.Combine(_tmp, "teams", "northern-lights.sowteam.json");

        var warning = TeamFile.Export(team, path, new SkiaLogoConverter());
        var imported = TeamFile.Import(path, keepScore: 5, defaultColour: Defaults.Team1Color);

        Assert.Null(warning);
        Assert.True(File.Exists(Path.Combine(_tmp, "teams", "northern-lights.png")));
        Assert.Equal("Northern Lights", imported.Name);
        Assert.Equal("NL", imported.Abbr);
        Assert.Equal("#27aae1", imported.ColorHex);
        Assert.Equal(5, imported.Score);
        Assert.Equal("", imported.BannedHero);
        Assert.Equal("Aurora", imported.Players[0].Name);
        Assert.Equal("Ana", imported.Players[0].Hero);
        Assert.Equal(8, imported.Players.Count);
        Assert.Equal(Path.Combine(_tmp, "teams", "northern-lights.png"), imported.LogoPath);
    }

    [Fact]
    public void The_exported_file_has_the_legacy_keys()
    {
        var path = Path.Combine(_tmp, "t.sowteam.json");

        TeamFile.Export(Sample().Team1, path, new SkiaLogoConverter());

        var json = File.ReadAllText(path);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"color_hex\": \"#27aae1\"", json);
        Assert.Contains("\"logo_png\": null", json);
        Assert.DoesNotContain("score", json);
    }

    [Fact]
    public void Import_falls_back_to_the_default_colour_and_a_logo_named_like_the_file()
    {
        var dir = Path.Combine(_tmp, "imp");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "falcons.json"), """{ "name": "Falcons", "players": [ { "name": "One" } ] }""");
        File.Copy(Fx("lucio.png"), Path.Combine(dir, "falcons.png"));

        var team = TeamFile.Import(Path.Combine(dir, "falcons.json"), 0, Defaults.Team2Color);

        Assert.Equal(Defaults.Team2Color, team.ColorHex);
        Assert.Equal(Path.Combine(dir, "falcons.png"), team.LogoPath);
        Assert.Equal(8, team.Players.Count);
        Assert.Equal("One", team.Players[0].Name);
    }

    [Fact]
    public void Importing_something_that_is_not_a_team_file_is_an_error()
    {
        var path = Path.Combine(_tmp, "bad.json");
        File.WriteAllText(path, "not json");

        Assert.Throws<StateLoadException>(() => TeamFile.Import(path, 0, Defaults.Team1Color));
    }

    // ---- match view model --------------------------------------------------------------------------------

    [Fact]
    public void Showing_a_state_and_collecting_it_again_changes_nothing()
    {
        var original = StateDerivations.Apply(Sample() with { General = new GeneralSettings { MapsCount = 5, FirstTo = 5, NumPlayers = 7 } });
        var match = Match();

        match.Load(original);
        var collected = match.ApplyTo(original);

        Assert.Equal(StateStore.Serialize(original), StateStore.Serialize(collected));
    }

    [Fact]
    public void The_legacy_golden_match_exports_the_same_files_after_a_trip_through_the_tab()
    {
        var goldenState = Path.Combine(AppContext.BaseDirectory, "Golden", "full", "state.json");
        var text = File.ReadAllText(goldenState).Replace("{FIXTURES}", FixturesRoot.Replace('\\', '/'));
        var original = StateStore.Deserialize(text);
        var match = Match(new AssetService(Path.Combine(_tmp, "empty"), Array.Empty<string>()));

        match.Load(original);
        var viaTab = match.ApplyTo(original);

        var direct = Path.Combine(_tmp, "direct");
        var tab = Path.Combine(_tmp, "tab");
        new ScoreboardExporter(direct, new SkiaLogoConverter()).ExportAll(original);
        new ScoreboardExporter(tab, new SkiaLogoConverter()).ExportAll(viaTab);

        foreach (var file in Directory.GetFiles(direct, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith("updated_at.txt")))
        {
            var relative = Path.GetRelativePath(direct, file);
            Assert.True(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(tab, relative))), relative);
        }
    }

    [Fact]
    public void Picking_a_hero_sets_its_role_but_loading_a_state_does_not_override_a_saved_role()
    {
        var match = Match();
        var row = match.Team1.Players[0];

        row.Hero = "Doomfist";
        Assert.Equal("Tank", row.Role);

        match.Load(AppState.CreateDefault() with
        {
            Team1 = new Team { Players = new[] { new Player { Name = "P", Hero = "Ana", Role = "Flex" } }.Concat(Team.EmptyPlayers().Skip(1)).ToArray() },
        });
        Assert.Equal("Flex", row.Role);
        Assert.Equal("Ana", row.Hero);
    }

    [Fact]
    public void Clearing_the_hero_or_picking_one_without_a_role_keeps_the_role()
    {
        var match = Match();
        var row = match.Team1.Players[0];
        row.Hero = "Ana";

        row.Hero = "Mystery";
        Assert.Equal("Support", row.Role);

        row.Hero = null;
        Assert.Equal("Support", row.Role);
    }

    [Fact]
    public void Editing_raises_Edited_but_loading_does_not()
    {
        var match = Match();
        var edits = 0;
        match.Edited += () => edits++;

        match.Load(Sample());
        Assert.Equal(0, edits);

        match.Team1.Name = "Changed";
        match.Maps[0].T1 = 3;
        match.MapsCount = 4;
        match.Team2.Players[1].Name = "X";
        Assert.Equal(4, edits);
    }

    [Fact]
    public void Derived_properties_are_not_counted_as_edits()
    {
        var match = Match();
        var edits = 0;
        match.Edited += () => edits++;

        match.PlayersCount = 5;
        match.Maps[0].Map = null;
        Assert.Equal(0, edits); // unchanged values raise nothing

        match.Maps[0].Map = "Busan"; // one edit; the picture that follows is not another
        Assert.Equal(1, edits);
    }

    [Fact]
    public void The_pick_list_shows_the_team_names_while_the_stored_values_stay_put()
    {
        var match = Match();
        Assert.Equal(new[] { "TBA", "T1", "T2" }, match.PickOptions.Select(p => p.Label));

        match.Team1.Name = " Northern Lights ";
        match.Team2.Name = "Midnight Sun";

        Assert.Equal(new[] { "TBA", "Northern Lights", "Midnight Sun" }, match.PickOptions.Select(p => p.Label));
        Assert.Equal(new[] { Picks.Tba, Picks.Team1, Picks.Team2 }, match.PickOptions.Select(p => p.Value));

        match.Team1.Name = "";
        Assert.Equal("T1", match.PickOptions[1].Label);
    }

    [Fact]
    public void The_format_bar_decides_which_cards_and_rows_are_shown_and_what_is_stored()
    {
        var match = Match();

        match.MapsCount = 3;
        match.PlayersCount = 2;

        Assert.Equal(new[] { true, true, true, false, false, false, false }, match.Maps.Select(m => m.IsShown));
        Assert.Equal(new[] { true, true, false, false, false, false, false, false }, match.Team1.Players.Select(p => p.IsShown));
        var state = match.ApplyTo(AppState.CreateDefault());
        Assert.Equal(3, state.General.MapsCount);
        Assert.Equal(3, state.General.FirstTo);
        Assert.Equal(2, state.General.NumPlayers);
        Assert.Equal(7, state.Maps.Count); // hidden cards keep their data
    }

    [Fact]
    public void Collecting_derives_the_winner_and_the_current_map()
    {
        var match = Match();
        match.MapsCount = 2;
        match.Maps[0].T1 = 3;
        match.Maps[0].Completed = true;
        match.Maps[1].T2 = 1;

        var state = match.ApplyTo(AppState.CreateDefault());

        Assert.Equal("t1", state.Maps[0].Winner);
        Assert.Equal("t2", state.Maps[1].Winner);
        Assert.Equal(2, state.CurrentMap);
    }

    [Fact]
    public void Collecting_keeps_what_the_tab_does_not_edit()
    {
        var match = Match();
        var state = AppState.CreateDefault() with
        {
            Team1 = new Team { BannedHero = "Ana" },
            General = new GeneralSettings { Host = "Host" },
        };

        var collected = match.ApplyTo(state);

        Assert.Equal("Ana", collected.Team1.BannedHero);
        Assert.Equal("Host", collected.General.Host);
    }

    [Fact]
    public void The_lists_and_map_pictures_follow_the_asset_catalogue()
    {
        var assets = Catalogue();
        var match = Match(assets);
        match.Maps[0].Map = "Busan";
        Assert.NotNull(match.Maps[0].MapImagePath);
        Assert.Contains("Ana", match.Maps[0].HeroNames);

        assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Heroes, Name = "Zarya", Role = "Tank", ImageSourcePath = Fx("lucio.png"), BodySourcePath = Fx("lucio_body.png") });

        Assert.Contains("Zarya", match.Maps[1].HeroNames);
        Assert.Contains("Zarya", match.Team2.Players[7].HeroNames);
    }

    [Fact]
    public async Task Load_logo_takes_the_picked_file()
    {
        var match = Match();
        _picker.Next = Fx("lucio.png");

        await match.Team2.LoadLogoCommand.ExecuteAsync(null);

        Assert.Equal(Fx("lucio.png"), match.Team2.LogoPath);
        _picker.Next = null;
        await match.Team2.LoadLogoCommand.ExecuteAsync(null);
        Assert.Equal(Fx("lucio.png"), match.Team2.LogoPath);
    }

    private sealed class FakePicker : IImagePicker
    {
        public string? Next { get; set; }
        public Task<string?> PickImageAsync(string title, string? initialDirectory = null) => Task.FromResult(Next);
    }
}

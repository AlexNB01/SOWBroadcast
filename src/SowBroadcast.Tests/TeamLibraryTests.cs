using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Link;
using SowBroadcast.Core.Match;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Teams;

namespace SowBroadcast.Tests;

public sealed class TeamLibraryTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-library-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 16, 20, 0, 0);

    public TeamLibraryTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    // Logos are "converted" by copying the bytes, so the tests need no image decoding.
    private sealed class CopyLogos : ILogoConverter
    {
        public byte[]? ToPng(string? sourcePath) => sourcePath is not null && File.Exists(sourcePath) ? File.ReadAllBytes(sourcePath) : null;
    }

    private TeamLibrary Library() => new(Path.Combine(_tmp, "library"), new CopyLogos(), () => _now);

    private string Logo(string name, byte fill = 1)
    {
        var path = Path.Combine(_tmp, name);
        File.WriteAllBytes(path, Enumerable.Repeat(fill, 32).ToArray());
        return path;
    }

    private static Team Alpha(string? logo = null) => new()
    {
        Name = "Northern Lights", Abbr = "NL", ColorHex = "#27aae1", Score = 3, BannedHero = "Ana", LogoPath = logo,
        Players = new[] { new Player { Name = "Aurora", Hero = "Ana", Role = "Support" } },
    };

    [Fact]
    public void A_saved_team_is_kept_with_its_data_but_not_its_score_or_ban_and_survives_a_restart()
    {
        var library = Library();

        var saved = library.Save(Alpha(Logo("nl.png")));
        var again = Library();
        again.Load();

        Assert.True(saved.Added);
        var team = Assert.Single(again.All);
        Assert.Equal("Northern Lights", team.Name);
        Assert.Equal("NL", team.Abbr);
        Assert.Equal("#27aae1", team.ColorHex);
        Assert.Equal(8, team.Players.Count);                              // always the full roster
        Assert.Equal("Aurora", team.Players[0].Name);
        Assert.Equal("manual", team.Source);
        Assert.Equal(_now, team.Updated);
        Assert.True(File.Exists(again.LogoPathOf(team)));
    }

    [Fact]
    public void A_team_of_the_same_name_is_updated_not_duplicated_and_its_logo_is_replaced()
    {
        var library = Library();
        var first = library.Save(Alpha(Logo("one.png", 1)));
        _now = _now.AddHours(1);

        var second = library.Save(Alpha(Logo("two.png", 2)) with { Name = " northern lights ", Abbr = "NLG" });

        Assert.False(second.Added);
        Assert.Equal(first.Team.Id, second.Team.Id);
        var team = Assert.Single(library.All);
        Assert.Equal("NLG", team.Abbr);
        Assert.Equal(new byte[32].Select(_ => (byte)2).ToArray(), File.ReadAllBytes(library.LogoPathOf(team)!));
        Assert.Single(Directory.GetFiles(Path.Combine(library.Folder, "logos")));   // no second picture left behind
    }

    [Fact]
    public void A_team_without_a_logo_loses_the_one_the_library_had_and_deleting_removes_everything()
    {
        var library = Library();
        var saved = library.Save(Alpha(Logo("nl.png")));
        var logoFile = library.LogoPathOf(saved.Team)!;

        var withoutLogo = library.Save(Alpha(logo: null));
        Assert.Null(withoutLogo.Team.LogoFile);
        Assert.False(File.Exists(logoFile));

        library.Save(Alpha(Logo("nl.png")));
        var path = library.LogoPathOf(library.All[0])!;
        Assert.True(library.Delete(library.All[0].Id));
        Assert.Empty(library.All);
        Assert.False(File.Exists(path));
        Assert.False(library.Delete("nothing"));
    }

    [Fact]
    public void A_logo_that_cannot_be_read_keeps_the_old_one_and_warns()
    {
        var library = Library();
        library.Save(Alpha(Logo("nl.png")));

        var result = library.Save(Alpha(Path.Combine(_tmp, "missing.png")));

        Assert.NotNull(result.Warning);
        Assert.NotNull(library.LogoPathOf(result.Team));
    }

    [Fact]
    public void Saving_the_library_own_logo_again_copies_nothing()
    {
        var library = Library();
        var first = library.Save(Alpha(Logo("nl.png")));
        var ownPath = library.LogoPathOf(first.Team)!;
        var written = File.GetLastWriteTimeUtc(ownPath);

        var again = library.Save(Alpha(ownPath));

        Assert.Null(again.Warning);
        Assert.Equal(written, File.GetLastWriteTimeUtc(ownPath));
    }

    [Fact]
    public void A_team_needs_a_name()
    {
        Assert.Throws<ArgumentException>(() => Library().Save(new Team { Name = "   " }));
    }

    [Fact]
    public void Search_finds_teams_by_name_abbreviation_or_player_with_all_words_in_any_case()
    {
        var library = Library();
        library.Save(Alpha());
        library.Save(new Team { Name = "Midnight Sun", Abbr = "MS", Players = new[] { new Player { Name = "Kaamos" } } });
        library.Save(new Team { Name = "Aurora Borealis", Abbr = "AB" });

        Assert.Equal(3, library.Search("").Count);
        Assert.Equal(new[] { "Northern Lights" }, library.Search("NORTH").Select(t => t.Name));
        Assert.Equal(new[] { "Midnight Sun" }, library.Search("ms").Select(t => t.Name));
        Assert.Equal(new[] { "Midnight Sun" }, library.Search("kaamos").Select(t => t.Name));
        Assert.Equal(new[] { "Aurora Borealis", "Northern Lights" }, library.Search("aurora").Select(t => t.Name));   // the second one by its player
        Assert.Empty(library.Search("north sun"));
        Assert.Equal(new[] { "Aurora Borealis", "Midnight Sun", "Northern Lights" }, library.All.Select(t => t.Name));
    }

    [Fact]
    public void A_team_is_shown_with_the_panels_score_and_a_default_colour_when_it_has_none()
    {
        var library = Library();
        var saved = library.Save(new Team { Name = "Plain", ColorHex = "" }).Team;

        var team = library.ToTeam(saved, keepScore: 2, defaultColour: Defaults.Team2Color);

        Assert.Equal(2, team.Score);
        Assert.Equal(Defaults.Team2Color, team.ColorHex);
        Assert.Equal("", team.BannedHero);
        Assert.Equal(8, team.Players.Count);
    }

    [Fact]
    public void An_old_team_file_is_taken_over_with_its_logo()
    {
        var folder = Path.Combine(_tmp, "files");
        var json = Path.Combine(folder, "nl.sowteam.json");
        TeamFile.Export(Alpha(Logo("nl.png")), json, new CopyLogos());

        var result = Library().ImportFile(json);

        Assert.Equal("Northern Lights", result.Team.Name);
        Assert.Equal("file", result.Team.Source);
        Assert.NotNull(result.Team.LogoFile);
    }

    [Fact]
    public void A_damaged_library_file_starts_an_empty_library_and_is_kept_aside()
    {
        var library = Library();
        Directory.CreateDirectory(library.Folder);
        File.WriteAllText(Path.Combine(library.Folder, "library.json"), "{ broken");

        library.Load();

        Assert.Empty(library.All);
        Assert.True(File.Exists(Path.Combine(library.Folder, "library.json.bad")));
        library.Save(Alpha());   // and it can be used again
        Assert.Single(library.All);
    }

    // ---- SOWDraft ----------------------------------------------------------------------------------------

    private static LinkTeam Linked(string name, string? abbr = null, string? color = null, string? logo = null, params LinkPlayer[] players) =>
        new("id-" + name, name, abbr, color, logo, players);

    [Fact]
    public void SOWDraft_teams_are_imported_with_players_roles_logos_and_a_suggested_abbreviation()
    {
        var library = Library();
        var downloaded = Logo("downloaded.png");
        var teams = new[]
        {
            Linked("Kuusamo Wolves", abbr: null, color: "#112233", logo: "https://sowdraft.fi/logos/kw.png",
                new LinkPlayer("Susi", "Susi#1234", null, "Tank", Array.Empty<string>()),
                new LinkPlayer(null, null, "discord-name", "Main Support", new[] { "Hitscan", "Tank" })),
            Linked("Oulu", abbr: "OUL"),
            Linked("  ", abbr: "XX"),
        };

        var (added, updated, warnings) = library.ImportFromLink(teams, url => url.EndsWith("kw.png") ? downloaded : null);

        Assert.Equal((2, 0), (added, updated));
        Assert.Empty(warnings);
        var wolves = library.FindByName("kuusamo wolves")!;
        Assert.Equal("KUU", wolves.Abbr);                         // suggested from the name
        Assert.Equal("#112233", wolves.ColorHex);
        Assert.Equal("sowdraft", wolves.Source);
        Assert.Equal("Susi", wolves.Players[0].Name);             // the battletag before the #
        Assert.Equal("Tank", wolves.Players[0].Role);
        Assert.Equal("discord-name", wolves.Players[1].Name);
        Assert.Equal("Flex", wolves.Players[1].Role);             // main role + secondary roles cover all three
        Assert.NotNull(library.LogoPathOf(wolves));
        Assert.Equal("OUL", library.FindByName("Oulu")!.Abbr);
    }

    [Fact]
    public void Importing_again_updates_by_name_and_keeps_what_SOWDraft_does_not_have()
    {
        var library = Library();
        library.Save(new Team { Name = "Oulu", Abbr = "OUL", ColorHex = "#abcdef", LogoPath = Logo("oulu.png") });

        var (added, updated, _) = library.ImportFromLink(new[]
        {
            Linked("Oulu", abbr: null, color: null, logo: null,
                new LinkPlayer("Pohjola", "Pohjola#1", null, "Damage", Array.Empty<string>())),
        }, _ => null);

        Assert.Equal((0, 1), (added, updated));
        var oulu = library.FindByName("Oulu")!;
        Assert.Equal("OUL", oulu.Abbr);                           // SOWDraft has none: the library's stays
        Assert.Equal("#abcdef", oulu.ColorHex);
        Assert.Equal("Pohjola", oulu.Players[0].Name);            // SOWDraft's players replace the roster
    }
}

public sealed class TeamLibraryViewModelTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-libvm-" + Guid.NewGuid().ToString("N"));
    private readonly MainWindowViewModelTests.FakeDialogs _dialogs = new();
    private readonly MainWindowViewModelTests.FakePublisher _publisher = new();

    public TeamLibraryViewModelTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private MainWindowViewModel Create() => new(_dialogs, _dialogs, _publisher, Path.Combine(_tmp, "autosave.json"),
        library: new TeamLibrary(Path.Combine(_tmp, "library"), new SkiaLogoConverter()));

    [Fact]
    public void A_panel_is_kept_and_loaded_into_the_other_panel_keeping_that_panels_score()
    {
        var vm = Create();
        vm.Match.Team1.Name = "Northern Lights";
        vm.Match.Team1.Abbr = "NL";
        vm.Match.Team1.ColourHex = "#27aae1";
        vm.Match.Team2.Score = 4;

        Assert.Contains("added", vm.SaveTeamToLibrary(1));
        var id = vm.Library!.All.Single().Id;
        vm.LoadTeamFromLibrary(2, id);

        Assert.Equal("Northern Lights", vm.Match.Team2.Name);
        Assert.Equal("NL", vm.Match.Team2.Abbr);
        Assert.Equal(4, vm.Match.Team2.Score);                     // the score of the panel stays
        Assert.True(vm.HasUnsentChanges);
        Assert.Empty(_publisher.Published);                        // only Update sends
        Assert.Contains("updated", vm.SaveTeamToLibrary(1));
    }

    [Fact]
    public void Loading_a_team_is_an_undo_step()
    {
        var vm = Create();
        vm.Match.Team1.Name = "Alpha";
        vm.SaveTeamToLibrary(1);
        vm.Match.Team1.Name = "";
        vm.FlushHistory();

        vm.LoadTeamFromLibrary(1, vm.Library!.All.Single().Id);
        vm.UndoCommand.Execute(null);

        Assert.Equal("", vm.Match.Team1.Name);
    }

    [Fact]
    public void An_unnamed_panel_is_not_kept_and_a_vanished_team_is_reported()
    {
        var vm = Create();

        Assert.Contains("name", vm.SaveTeamToLibrary(2));
        Assert.Empty(vm.Library!.All);
        Assert.Contains("no longer", vm.LoadTeamFromLibrary(1, "gone"));
        Assert.Contains("no longer", vm.DeleteLibraryTeam("gone"));
    }

    [Fact]
    public async Task The_library_window_searches_loads_and_deletes()
    {
        var vm = Create();
        foreach (var name in new[] { "Alpha", "Beta", "Gamma" })
        {
            vm.Match.Team1.Name = name;
            vm.SaveTeamToLibrary(1);
        }
        var window = new TeamLibraryViewModel(vm);
        Assert.Equal(3, window.Items.Count);
        Assert.Equal("Alpha", window.Selected!.Name);

        window.Query = "gam";
        Assert.Equal("Gamma", Assert.Single(window.Items).Name);

        window.LoadAwayCommand.Execute(null);
        Assert.Equal("Gamma", vm.Match.Team2.Name);
        Assert.Contains("Gamma", window.Message);

        window.DeleteCommand.Execute(null);
        Assert.Empty(window.Items);
        window.Query = "";
        Assert.Equal(2, window.Items.Count);
        Assert.False(window.IsEmpty);
        Assert.False(window.CanImportFromLink);                    // no tournament linked
        await window.ImportFromLinkCommand.ExecuteAsync(null);
        Assert.Contains("Link a SOWDraft tournament", window.Message);
    }

    [Fact]
    public void Without_a_library_everything_says_so_instead_of_failing()
    {
        var vm = new MainWindowViewModel(_dialogs, _dialogs, _publisher, Path.Combine(_tmp, "autosave.json"));
        vm.Match.Team1.Name = "Alpha";

        Assert.Contains("not available", vm.SaveTeamToLibrary(1));
        Assert.Empty(new TeamLibraryViewModel(vm).Items);
    }
}

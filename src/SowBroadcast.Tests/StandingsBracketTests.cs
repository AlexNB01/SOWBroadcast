using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Bracket;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Standings;
using SowBroadcast.Core.Storage;
using SowBroadcast.Server;

namespace SowBroadcast.Tests;

public sealed class StandingsOperationsTests
{
    private static StandingsRow Row(string team, int points = 0, int diff = 0, int rank = 0, int wins = 0) =>
        new() { TeamName = team, Points = points, MapDiff = diff, Rank = rank, Wins = wins };

    [Fact]
    public void Empty_lines_are_blank_but_a_line_with_any_identity_or_figure_is_not()
    {
        Assert.True(StandingsOperations.IsBlank(new StandingsRow()));
        Assert.True(StandingsOperations.IsBlank(new StandingsRow { Rank = 3, Status = "qualified" })); // rank and status alone do not count
        Assert.False(StandingsOperations.IsBlank(new StandingsRow { Abbr = "AB" }));
        Assert.False(StandingsOperations.IsBlank(new StandingsRow { LogoPath = "x.png" }));
        Assert.False(StandingsOperations.IsBlank(new StandingsRow { MapDiff = -1 }));
    }

    [Fact]
    public void Update_and_sort_orders_by_points_then_map_difference_then_name_and_ranks_everyone()
    {
        var rows = new[]
        {
            Row("zulu", 3, 1, rank: 7),
            Row("Alpha", 6),
            Row("beta", 3, 1),
            Row("Charlie", 3, 4),
            new StandingsRow(),
        };

        var sorted = StandingsOperations.SortAndRank(rows);

        Assert.Equal(new[] { "Alpha", "Charlie", "beta", "zulu" }, sorted.Select(r => r.TeamName));
        Assert.Equal(new[] { 1, 2, 3, 4 }, sorted.Select(r => r.Rank)); // hand-typed ranks are replaced
    }

    [Fact]
    public void Teams_for_the_bracket_come_from_all_groups_best_rank_first_and_unranked_by_name()
    {
        var standings = new StandingsSettings
        {
            Groups = new[]
            {
                new StandingsGroup { Key = "a", Rows = new[] { Row("Delta", rank: 2), Row("echo"), Row("  ", rank: 1) } },
                new StandingsGroup { Key = "b", Rows = new[] { Row("Bravo", rank: 1), Row("Alpha") } },
            },
        };

        var teams = StandingsOperations.TeamsFromStandings(standings);

        Assert.Equal(new[] { "Bravo", "Delta", "Alpha", "echo" }, teams.Select(t => t.Name));
    }

    [Fact]
    public void Teams_fall_back_to_the_plain_rows_when_there_are_no_groups()
    {
        var standings = new StandingsSettings { Rows = new[] { new StandingsRow { TeamName = "Solo", Abbr = "SO", LogoPath = "l.png" } } };

        var team = Assert.Single(StandingsOperations.TeamsFromStandings(standings));

        Assert.Equal(("Solo", "SO", "l.png"), (team.Name, team.Abbr, team.LogoPath));
    }
}

public sealed class BracketTemplatesTests
{
    private static MatchIdSource Ids() => new(() => 1_760_000_000);

    private static int[] Sizes(IReadOnlyList<BracketRound> rounds, string side) =>
        rounds.Where(r => r.Side == side).Select(r => r.Matches.Count).ToArray();

    [Theory]
    [InlineData(BracketTemplate.Single4, new[] { 2, 1 }, new int[0], false)]
    [InlineData(BracketTemplate.Single6, new[] { 2, 2, 1 }, new int[0], false)]
    [InlineData(BracketTemplate.Single8, new[] { 4, 2, 1 }, new int[0], false)]
    [InlineData(BracketTemplate.Double4, new[] { 2, 1 }, new[] { 1, 1 }, true)]
    [InlineData(BracketTemplate.Double6, new[] { 2, 2, 1 }, new[] { 2, 1, 1 }, true)]
    [InlineData(BracketTemplate.Double8, new[] { 4, 2, 1 }, new[] { 2, 2, 1 }, true)]
    public void Templates_have_the_documented_rounds(BracketTemplate template, int[] upper, int[] lower, bool grandFinal)
    {
        var rounds = BracketTemplates.Build(template, Ids());

        Assert.Equal(upper, Sizes(rounds, "upper"));
        Assert.Equal(lower, Sizes(rounds, "lower"));
        Assert.Equal(grandFinal ? new[] { 1 } : new int[0], Sizes(rounds, "grand"));
    }

    [Fact]
    public void Round_names_follow_the_legacy_templates()
    {
        var double8 = BracketTemplates.Build(BracketTemplate.Double8, Ids());
        var single6 = BracketTemplates.Build(BracketTemplate.Single6, Ids());

        Assert.Equal(new[] { "Upper Round 1", "Upper Semifinals", "Upper Final", "Lower Round 1", "Lower Round 2", "Lower Final", "Grand Final" },
            double8.Select(r => r.Name));
        Assert.Equal(new[] { "Round 1", "Semifinals", "Final" }, single6.Select(r => r.Name));
    }

    [Fact]
    public void Every_match_gets_its_own_id_made_of_the_time_and_a_counter()
    {
        var ids = BracketTemplates.Build(BracketTemplate.Double8, Ids()).SelectMany(r => r.Matches).Select(m => m.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Matches(@"^m1760000000-\d+$", id));
    }

    [Theory]
    [InlineData(BracketTemplate.Double8, true)]
    [InlineData(BracketTemplate.Double6, true)]
    [InlineData(BracketTemplate.Double4, false)]
    [InlineData(BracketTemplate.Single8, false)]
    public void The_double_elimination_view_exists_for_six_and_eight_teams(BracketTemplate template, bool expected) =>
        Assert.Equal(expected, BracketTemplates.SupportsDoubleElimView(BracketTemplates.Build(template, Ids())));

    [Fact]
    public void Single_eliminations_may_carry_a_bronze_match_but_double_eliminations_are_not_single()
    {
        var single = BracketTemplates.Build(BracketTemplate.Single4, Ids());
        var withBronze = BracketTemplates.WithBronze(single, true, Ids());

        Assert.True(BracketTemplates.IsSingleElimination(single));
        Assert.True(BracketTemplates.IsSingleElimination(withBronze));
        Assert.True(BracketTemplates.HasBronze(withBronze));
        Assert.Equal("Bronze Match", withBronze[^1].Name);
        Assert.Equal("lower", withBronze[^1].Side);
        Assert.False(BracketTemplates.IsSingleElimination(BracketTemplates.Build(BracketTemplate.Double4, Ids())));
        Assert.False(BracketTemplates.IsSingleElimination(Array.Empty<BracketRound>()));
    }

    [Fact]
    public void Adding_the_bronze_match_twice_or_removing_a_missing_one_changes_nothing()
    {
        var single = BracketTemplates.Build(BracketTemplate.Single4, Ids());
        var once = BracketTemplates.WithBronze(single, true, Ids());

        Assert.Same(once, BracketTemplates.WithBronze(once, true, Ids()));
        Assert.Same(single, BracketTemplates.WithBronze(single, false, Ids()));
        Assert.Equal(single.Count, BracketTemplates.WithBronze(once, false, Ids()).Count);
    }
}

public sealed class StandingsBracketViewModelTests
{
    private readonly FakeMessages _messages = new();

    private static StandingsRow Row(string team, int points = 0, int diff = 0, int rank = 0) =>
        new() { TeamName = team, Points = points, MapDiff = diff, Rank = rank };

    private static StandingsViewModel NewStandings() => new(new NoPicker(), null);

    private BracketViewModel NewBracket(Func<IReadOnlyList<TeamRef>>? teams = null, ITeamPicker? picker = null) =>
        new(new NoPicker(), null, teams ?? (() => Array.Empty<TeamRef>()), picker, _messages, new MatchIdSource(() => 1_760_000_000));

    // ---- standings ---------------------------------------------------------------------------------------

    [Fact]
    public void A_fresh_standings_tab_has_one_empty_group_that_adds_nothing_to_the_state()
    {
        var vm = NewStandings();

        var state = vm.ApplyTo(AppState.CreateDefault());

        var group = Assert.Single(state.Standings.Groups);
        Assert.Equal(("group_1", "Group 1"), (group.Key, group.Name));
        Assert.Empty(group.Rows);
        Assert.Equal("__all__", state.Standings.DisplayGroup);
        Assert.False(vm.Groups[0].CanRemove);
    }

    [Fact]
    public void Standings_round_trip_and_keep_the_figures_that_only_a_tournament_link_fills_in()
    {
        var source = AppState.CreateDefault() with
        {
            Standings = new StandingsSettings
            {
                Title = "Cup", Subtitle = "Sub", DisplayGroup = "g2",
                Groups = new[]
                {
                    new StandingsGroup { Key = "g1", Name = "A", Rows = new[] { Row("One", 6, 2, 1) } },
                    new StandingsGroup
                    {
                        Key = "g2", Name = "B",
                        Rows = new[] { new StandingsRow { TeamName = "Two", Points = 3, Rank = 1, Played = 4, Draws = 1, Sf = 9, Sa = 7, Status = "qualified", LogoPath = "l.png" } },
                    },
                },
            },
        };
        var vm = NewStandings();

        vm.Load(source);
        var back = vm.ApplyTo(AppState.CreateDefault());

        Assert.Equal("g2", back.Standings.DisplayGroup);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(source.Standings.Groups, StateStore.Options),
            System.Text.Json.JsonSerializer.Serialize(back.Standings.Groups, StateStore.Options));
        var two = back.Standings.Groups[1].Rows.Single();
        Assert.Equal((4, 1, 9, 7), (two.Played, two.Draws, two.Sf, two.Sa));
        Assert.Equal("qualified", two.Status);
        Assert.Equal("Two", back.Standings.Rows.Single().TeamName); // the shown group
    }

    [Fact]
    public void Groups_are_added_with_the_next_key_and_name_and_the_last_one_cannot_be_removed()
    {
        var vm = NewStandings();

        vm.AddGroupCommand.Execute(null);

        Assert.Equal(new[] { "group_1", "group_2" }, vm.Groups.Select(g => g.Key));
        Assert.Equal("Group 2", vm.Groups[1].Name);
        Assert.All(vm.Groups, g => Assert.True(g.CanRemove));

        vm.Groups[0].RemoveGroupCommand.Execute(null);
        Assert.Single(vm.Groups);
        Assert.False(vm.Groups[0].CanRemove);

        vm.Groups[0].RemoveGroupCommand.Execute(null); // refused
        Assert.Single(vm.Groups);
    }

    [Fact]
    public void The_display_list_follows_group_names_and_falls_back_to_all_when_the_shown_group_goes()
    {
        var vm = NewStandings();
        vm.AddGroupCommand.Execute(null);
        vm.Groups[1].Name = "Playoff group";
        Assert.Equal(new[] { "All groups", "Group 1", "Playoff group" }, vm.DisplayOptions.Select(o => o.Label));

        vm.Groups[1].Name = "   ";
        Assert.Equal("group_2", vm.DisplayOptions[2].Label);

        vm.SelectedDisplay = vm.DisplayOptions[2];
        vm.Groups[1].RemoveGroupCommand.Execute(null);
        Assert.Same(vm.AllOption, vm.SelectedDisplay);
        Assert.Equal(2, vm.DisplayOptions.Count);
    }

    [Fact]
    public void Update_and_sort_orders_and_ranks_the_tables_on_screen()
    {
        var vm = NewStandings();
        vm.Load(AppState.CreateDefault() with
        {
            Standings = new StandingsSettings
            {
                Groups = new[] { new StandingsGroup { Key = "group_1", Name = "A", Rows = new[] { Row("low", 1), Row("high", 9), new StandingsRow() } } },
            },
        });

        vm.SortAll();

        var rows = vm.Groups[0].Rows;
        Assert.Equal(new[] { "high", "low" }, rows.Select(r => r.TeamName));
        Assert.Equal(new[] { 1, 2 }, rows.Select(r => r.Rank));
    }

    [Fact]
    public void Unranked_rows_get_their_places_in_the_state_without_changing_what_is_on_screen()
    {
        var vm = NewStandings();
        vm.Load(AppState.CreateDefault() with
        {
            Standings = new StandingsSettings
            {
                Groups = new[] { new StandingsGroup { Key = "group_1", Name = "A", Rows = new[] { Row("low", 1), Row("high", 9, rank: 0), Row("fixed", 0, rank: 1) } } },
            },
        });

        var rows = vm.ApplyTo(AppState.CreateDefault()).Standings.Groups[0].Rows;

        Assert.Equal(("fixed", 1), (rows[2].TeamName, rows[2].Rank));
        Assert.Equal(2, rows.Single(r => r.TeamName == "high").Rank);
        Assert.Equal(3, rows.Single(r => r.TeamName == "low").Rank);
        Assert.All(vm.Groups[0].Rows.Where(r => r.TeamName != "fixed"), r => Assert.Equal(0, r.Rank));
    }

    [Fact]
    public void Standings_edits_raise_Edited_but_loading_and_choosing_nothing_do_not()
    {
        var vm = NewStandings();
        var edits = 0;
        vm.Edited += () => edits++;

        vm.Load(AppState.CreateDefault());
        Assert.Equal(0, edits);

        vm.Groups[0].Rows[0].TeamName = "X";
        vm.Groups[0].AddRowCommand.Execute(null);
        vm.Title = "T";
        Assert.Equal(3, edits);

        vm.Groups[0].Rows[1].RemoveCommand.Execute(null);
        Assert.Equal(4, edits);
    }

    [Fact]
    public void Reset_leaves_one_empty_group_and_starts_the_keys_over()
    {
        var vm = NewStandings();
        vm.AddGroupCommand.Execute(null);
        vm.Title = "Cup";

        vm.ResetTab();

        Assert.Equal("", vm.Title);
        var group = Assert.Single(vm.Groups);
        Assert.Equal("group_1", group.Key);
        Assert.Single(group.Rows);
    }

    // ---- bracket -----------------------------------------------------------------------------------------

    [Fact]
    public void Loading_a_double_elimination_template_shows_three_sections_and_the_view_switch()
    {
        var vm = NewBracket();
        vm.SelectedTemplate = "8 team double elimination";

        vm.LoadTemplateCommand.Execute(null);

        Assert.Equal(new[] { "Upper Bracket", "Lower Bracket", "Grand Final" }, vm.Sections.Select(s => s.Title));
        Assert.True(vm.ShowDoubleView);
        Assert.False(vm.ShowBronze);
        Assert.True(vm.HasRounds);
        Assert.Equal("upper", vm.ApplyTo(AppState.CreateDefault()).Bracket.DoubleElimView);
    }

    [Fact]
    public void A_four_team_double_elimination_has_no_view_switch_and_stores_no_view()
    {
        var vm = NewBracket();
        vm.SelectedTemplate = "4 team double elimination";

        vm.LoadTemplateCommand.Execute(null);

        Assert.False(vm.ShowDoubleView);
        Assert.Equal("", vm.ApplyTo(AppState.CreateDefault()).Bracket.DoubleElimView);
    }

    [Fact]
    public void The_lower_view_is_stored_as_lower()
    {
        var vm = NewBracket();
        vm.SelectedTemplate = "6 team double elimination";
        vm.LoadTemplateCommand.Execute(null);

        vm.DoubleView = BracketViewModel.LowerView;

        Assert.Equal("lower", vm.ApplyTo(AppState.CreateDefault()).Bracket.DoubleElimView);
    }

    [Fact]
    public void The_bronze_match_is_added_and_removed_without_touching_the_other_matches()
    {
        var vm = NewBracket();
        vm.SelectedTemplate = "4 team single elimination";
        vm.LoadTemplateCommand.Execute(null);
        Assert.True(vm.ShowBronze);
        vm.Teams[0].Name = "Alpha";
        vm.Sections[0].Rounds[0].Matches[0].Team1 = "Alpha";
        vm.Sections[0].Rounds[0].Matches[0].Score1 = 3;

        vm.BronzeEnabled = true;
        var withBronze = vm.ApplyTo(AppState.CreateDefault()).Bracket.Rounds;
        Assert.Equal("Bronze Match", withBronze[^1].Name);
        Assert.Equal(("Alpha", 3), (withBronze[0].Matches[0].Team1.Name, withBronze[0].Matches[0].Score1));

        vm.BronzeEnabled = false;
        var without = vm.ApplyTo(AppState.CreateDefault()).Bracket.Rounds;
        Assert.DoesNotContain(without, r => r.Name == "Bronze Match");
        Assert.Equal(3, without[0].Matches[0].Score1);
    }

    [Fact]
    public void Picking_a_team_copies_its_name_and_logo_from_the_team_list_and_follows_later_changes()
    {
        var vm = NewBracket();
        vm.SelectedTemplate = "4 team single elimination";
        vm.LoadTemplateCommand.Execute(null);
        vm.Teams[0].Name = "Alpha";
        vm.Teams[0].LogoPath = "alpha.png";
        var match = vm.Sections[0].Rounds[0].Matches[0];
        Assert.Contains("Alpha", match.TeamNames);

        match.Team1 = "Alpha";
        var picked = vm.ApplyTo(AppState.CreateDefault()).Bracket.Rounds[0].Matches[0].Team1;
        Assert.Equal(("Alpha", "alpha.png", ""), (picked.Name, picked.LogoPath, picked.Abbr));

        vm.Teams[0].LogoPath = "new.png";
        Assert.Equal("new.png", vm.ApplyTo(AppState.CreateDefault()).Bracket.Rounds[0].Matches[0].Team1.LogoPath);
    }

    [Fact]
    public void A_team_name_that_is_not_in_the_list_is_kept_as_typed_by_a_tournament_link()
    {
        var vm = NewBracket();
        var state = AppState.CreateDefault() with
        {
            Bracket = new BracketSettings
            {
                Rounds = new[]
                {
                    new BracketRound
                    {
                        Name = "Final", Side = "upper",
                        Matches = new[] { new BracketMatch { Id = "m1", BoLabel = "BO5", Status = "live", ScheduledAt = 99, Team1 = new TeamRef { Name = "Linked", Abbr = "LK", LogoPath = "x.png" } } },
                    },
                },
            },
        };

        vm.Load(state);
        var match = vm.ApplyTo(AppState.CreateDefault()).Bracket.Rounds[0].Matches[0];

        Assert.Equal(("m1", "BO5", "live", 99L), (match.Id, match.BoLabel, match.Status, match.ScheduledAt));
        Assert.Equal(("Linked", "LK", "x.png"), (match.Team1.Name, match.Team1.Abbr, match.Team1.LogoPath));
    }

    [Fact]
    public void Clearing_a_match_team_empties_its_reference()
    {
        var vm = NewBracket();
        vm.SelectedTemplate = "4 team single elimination";
        vm.LoadTemplateCommand.Execute(null);
        vm.Teams[0].Name = "Alpha";
        var match = vm.Sections[0].Rounds[0].Matches[0];
        match.Team1 = "Alpha";

        match.Team1 = null;

        Assert.Equal(new TeamRef(), vm.ApplyTo(AppState.CreateDefault()).Bracket.Rounds[0].Matches[0].Team1);
    }

    [Fact]
    public async Task Import_from_standings_fills_the_chosen_teams_in_order_and_clears_the_rest()
    {
        var teams = new[] { new TeamRef { Name = "A" }, new TeamRef { Name = "B" }, new TeamRef { Name = "C" } };
        var picker = new FakeTeamPicker { Result = new[] { teams[2], teams[0] } };
        var vm = NewBracket(() => teams, picker);
        vm.Teams[5].Name = "Old";

        await vm.ImportFromStandingsCommand.ExecuteAsync(null);

        Assert.Equal(teams, picker.Offered);
        Assert.Equal(new[] { "C", "A" }, vm.Teams.Take(2).Select(t => t.Name));
        Assert.All(vm.Teams.Skip(2), t => Assert.Equal("", t.Name));
    }

    [Fact]
    public async Task Import_with_no_standings_teams_says_so_and_cancelling_changes_nothing()
    {
        var empty = NewBracket();
        await empty.ImportFromStandingsCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "No teams found in standings." }, _messages.Infos);

        var picker = new FakeTeamPicker { Result = null };
        var vm = NewBracket(() => new[] { new TeamRef { Name = "A" } }, picker);
        vm.Teams[0].Name = "Keep";
        await vm.ImportFromStandingsCommand.ExecuteAsync(null);
        Assert.Equal("Keep", vm.Teams[0].Name);
    }

    [Fact]
    public void The_bracket_round_trips_through_the_tab()
    {
        var vm = NewBracket();
        vm.SelectedTemplate = "8 team double elimination";
        vm.LoadTemplateCommand.Execute(null);
        vm.Title = " Playoffs ";
        vm.Stage = "Finals";
        vm.Teams[0].Name = "Alpha";
        vm.Sections[0].Rounds[0].Matches[1].Score2 = 2;
        var first = vm.ApplyTo(AppState.CreateDefault());

        var again = NewBracket();
        again.Load(first);
        var second = again.ApplyTo(AppState.CreateDefault());

        Assert.Equal(StateStore.Serialize(first), StateStore.Serialize(second));
        Assert.Equal("Playoffs", second.Bracket.Title);
        Assert.True(again.ShowDoubleView);
        Assert.Equal(16, second.Bracket.Teams.Count);
    }

    [Fact]
    public void Edits_raise_Edited_but_choosing_a_template_in_the_list_does_not()
    {
        var vm = NewBracket();
        var edits = 0;
        vm.Edited += () => edits++;

        vm.SelectedTemplate = "8 team single elimination";
        Assert.Equal(0, edits);

        vm.LoadTemplateCommand.Execute(null);
        var afterLoad = edits;
        Assert.True(afterLoad > 0);

        vm.Sections[0].Rounds[0].Matches[0].Score1 = 1;
        vm.Title = "x";
        Assert.Equal(afterLoad + 2, edits);

        vm.Load(AppState.CreateDefault());
        Assert.Equal(afterLoad + 2, edits);
    }

    [Fact]
    public void Reset_empties_the_bracket_but_a_reset_bracket_can_load_a_template_again()
    {
        var vm = NewBracket();
        vm.SelectedTemplate = "8 team double elimination";
        vm.LoadTemplateCommand.Execute(null);
        vm.Title = "T";
        vm.Teams[0].Name = "Alpha";

        vm.ResetTab();

        Assert.Equal("", vm.Title);
        Assert.False(vm.HasRounds);
        Assert.Empty(vm.Sections);
        Assert.False(vm.ShowDoubleView);
        Assert.All(vm.Teams, t => Assert.Equal("", t.Name));
        Assert.Empty(vm.ApplyTo(AppState.CreateDefault()).Bracket.Rounds);

        vm.LoadTemplateCommand.Execute(null);
        Assert.True(vm.HasRounds);
    }

    private sealed class NoPicker : IImagePicker
    {
        public Task<string?> PickImageAsync(string title, string? initialDirectory = null) => Task.FromResult<string?>(null);
    }

    private sealed class FakeTeamPicker : ITeamPicker
    {
        public IReadOnlyList<TeamRef>? Result { get; set; }
        public IReadOnlyList<TeamRef>? Offered { get; private set; }

        public Task<IReadOnlyList<TeamRef>?> PickTeamsAsync(IReadOnlyList<TeamRef> teams)
        {
            Offered = teams;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeMessages : IMessages
    {
        public List<string> Infos { get; } = new();
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
        public Task ShowInfoAsync(string title, string message)
        {
            Infos.Add(message);
            return Task.CompletedTask;
        }
    }
}

public sealed class StandingsBracketExportTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-m5-" + Guid.NewGuid().ToString("N"));

    public StandingsBracketExportTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    [Fact]
    public void The_legacy_golden_standings_and_bracket_export_the_same_files_after_a_trip_through_the_tabs()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures").Replace('\\', '/');
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "full", "state.json")).Replace("{FIXTURES}", fixtures);
        var original = StateStore.Deserialize(text);
        var standings = new StandingsViewModel(new NoPicker(), null);
        var bracket = new BracketViewModel(new NoPicker(), null, () => Array.Empty<TeamRef>(), null, new NoMessages());

        standings.Load(original);
        bracket.Load(original);
        var viaTabs = bracket.ApplyTo(standings.ApplyTo(original));

        var direct = Path.Combine(_tmp, "direct");
        var tabs = Path.Combine(_tmp, "tabs");
        new ScoreboardExporter(direct, new SkiaLogoConverter()).ExportAll(original);
        new ScoreboardExporter(tabs, new SkiaLogoConverter()).ExportAll(viaTabs);

        foreach (var file in new[] { "Standings/standings.json", "Bracket/bracket.json" })
            Assert.Equal(File.ReadAllText(Path.Combine(direct, file)), File.ReadAllText(Path.Combine(tabs, file)));
    }

    [Fact]
    public void Standings_and_bracket_updates_announce_only_their_own_key()
    {
        var exporter = new ScoreboardExporter(Path.Combine(_tmp, "Scoreboard"), new SkiaLogoConverter());
        var publisher = new OverlayPublisher(exporter, new EventHub(StateDiff.AllKeys), "http://127.0.0.1:1");
        publisher.Publish(AppState.CreateDefault());
        var edited = AppState.CreateDefault() with
        {
            Team1 = new Team { Name = "Home", ColorHex = Defaults.Team1Color },
            Standings = new StandingsSettings { Title = "Cup" },
            Bracket = new BracketSettings { Title = "Playoffs" },
        };

        var standings = publisher.PublishPart(edited, PublishScope.Standings);
        var bracket = publisher.PublishPart(edited, PublishScope.Bracket);
        var full = publisher.Publish(edited);

        Assert.Equal(new[] { "standings" }, standings.ChangedKeys);
        Assert.Equal(new[] { "bracket" }, bracket.ChangedKeys);
        Assert.Contains("t1.name", full.ChangedKeys);
        Assert.DoesNotContain("standings", full.ChangedKeys);
        Assert.Contains("Cup", File.ReadAllText(Path.Combine(_tmp, "Scoreboard", "Standings", "standings.json")));
    }

    private sealed class NoPicker : IImagePicker
    {
        public Task<string?> PickImageAsync(string title, string? initialDirectory = null) => Task.FromResult<string?>(null);
    }

    private sealed class NoMessages : IMessages
    {
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }
}

public sealed class StandingsLayoutTests
{
    private sealed class NoPicker : IImagePicker
    {
        public Task<string?> PickImageAsync(string title, string? initialDirectory = null) => Task.FromResult<string?>(null);
    }

    [Fact]
    public void Groups_sit_side_by_side_only_with_two_groups_and_a_wide_window_and_the_choice_is_not_an_edit()
    {
        var standings = new StandingsViewModel(new NoPicker(), null);
        var edits = 0;
        standings.Edited += () => edits++;

        standings.WideLayout = true;
        Assert.Equal(1, standings.GroupColumns);          // one group: the whole width

        standings.AddGroupCommand.Execute(null);
        var afterAdd = edits;
        Assert.Equal(2, standings.GroupColumns);

        standings.WideLayout = false;
        Assert.Equal(1, standings.GroupColumns);          // narrow window: one column

        Assert.Equal(afterAdd, edits);                    // changing the layout does not mark the tab as edited
    }
}

using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Automation;
using SowBroadcast.Core.Bracket;
using SowBroadcast.Core.Control;
using SowBroadcast.Core.Model;

namespace SowBroadcast.Tests;

public sealed class MapAutomationTests
{
    [Theory]
    [InlineData("Control", 2, 0, true)]
    [InlineData("Control", 1, 0, false)]
    [InlineData("Control", 2, 2, false)]      // a tie is not a win
    [InlineData("Control", 1, 2, true)]
    [InlineData("Flashpoint", 2, 1, false)]
    [InlineData("Flashpoint", 3, 2, true)]
    [InlineData("Push", 1, 0, true)]
    [InlineData("Escort", 3, 0, false)]       // not decided by points alone
    [InlineData("Hybrid", 3, 0, false)]
    [InlineData(null, 5, 0, false)]           // no map chosen
    [InlineData("Clash", 5, 0, false)]        // unknown mode
    public void A_map_is_decided_when_one_team_has_the_winning_points_of_its_mode(string? mode, int t1, int t2, bool decided) =>
        Assert.Equal(decided, MapAutomation.ShouldComplete(new AutomationSettings(), mode, t1, t2));

    [Fact]
    public void The_points_that_win_a_map_can_be_changed_per_mode()
    {
        var settings = new AutomationSettings();
        settings.MapWinPoints["Escort"] = 3;
        settings.MapWinPoints["Control"] = 0;

        Assert.True(MapAutomation.ShouldComplete(settings, "Escort", 3, 1));
        Assert.False(MapAutomation.ShouldComplete(settings, "Control", 2, 0));
        Assert.Equal(3, settings.PointsToWin("flashpoint"));   // not listed in the user's table: the default
    }

    [Fact]
    public void Maps_won_counts_finished_maps_only_and_ignores_draws()
    {
        var wins = MapAutomation.Wins(new[] { (true, 2, 0), (true, 0, 2), (true, 1, 1), (false, 2, 0), (true, 3, 2) });

        Assert.Equal((2, 1), wins);
    }
}

public sealed class BracketAdvanceTests
{
    private static readonly MatchIdSource Ids = new(() => 1_760_000_000);

    private static TeamRef T(string name) => new() { Name = name };

    private static BracketRound[] Rounds(BracketTemplate template) => BracketTemplates.Build(template, Ids).ToArray();

    private static BracketRound WithMatch(BracketRound round, int index, Func<BracketMatch, BracketMatch> change) =>
        round with { Matches = round.Matches.Select((m, i) => i == index ? change(m) : m).ToArray() };

    private static BracketMatch Played(string a, string b, int s1, int s2) => new() { Team1 = T(a), Team2 = T(b), Score1 = s1, Score2 = s2 };

    [Theory]
    [InlineData("BO5", 3)]
    [InlineData("bo3", 2)]
    [InlineData("BO1", 1)]
    [InlineData("", 2)]            // falls back to the default best-of (3)
    [InlineData("final", 2)]
    public void Wins_needed_come_from_the_BO_label_or_the_default(string label, int wins) =>
        Assert.Equal(wins, BracketAdvance.WinsNeeded(label, 3));

    [Fact]
    public void In_single_elimination_the_winners_meet_in_the_final_and_the_losers_in_the_bronze_match()
    {
        var rounds = BracketTemplates.WithBronze(Rounds(BracketTemplate.Single4), true, Ids).ToArray();
        rounds[0] = WithMatch(rounds[0], 0, _ => Played("Alpha", "Beta", 2, 0));
        rounds[0] = WithMatch(rounds[0], 1, _ => Played("Gamma", "Delta", 1, 2));

        var plan = BracketAdvance.Plan(rounds, 3);

        Assert.Contains(plan, a => a.Round == 1 && a.Match == 0 && a.Slot == 1 && a.Team.Name == "Alpha");
        Assert.Contains(plan, a => a.Round == 1 && a.Match == 0 && a.Slot == 2 && a.Team.Name == "Delta");
        Assert.Contains(plan, a => a.Round == 2 && a.Match == 0 && a.Slot == 1 && a.Team.Name == "Beta");
        Assert.Contains(plan, a => a.Round == 2 && a.Match == 0 && a.Slot == 2 && a.Team.Name == "Gamma");
        Assert.Equal(4, plan.Count);
    }

    [Fact]
    public void An_unfinished_match_moves_nobody()
    {
        var rounds = Rounds(BracketTemplate.Single4);
        rounds[0] = WithMatch(rounds[0], 0, _ => Played("Alpha", "Beta", 1, 0));   // best of 3: needs 2

        Assert.Empty(BracketAdvance.Plan(rounds, 3));
    }

    [Fact]
    public void A_corrected_result_replaces_the_team_that_was_moved_up_but_not_a_hand_placed_one()
    {
        var rounds = Rounds(BracketTemplate.Single4);
        rounds[0] = WithMatch(rounds[0], 0, _ => Played("Alpha", "Beta", 0, 2));    // Beta really won
        rounds[1] = WithMatch(rounds[1], 0, m => m with { Team1 = T("Alpha") });    // Alpha was moved up before

        var corrected = BracketAdvance.Plan(rounds, 3);
        Assert.Equal("Beta", Assert.Single(corrected).Team.Name);

        rounds[1] = WithMatch(rounds[1], 0, m => m with { Team1 = T("Seed") });     // put there by hand
        Assert.Empty(BracketAdvance.Plan(rounds, 3));
    }

    [Fact]
    public void Nothing_changes_when_the_winner_is_already_in_place()
    {
        var rounds = Rounds(BracketTemplate.Single4);
        rounds[0] = WithMatch(rounds[0], 1, _ => Played("Gamma", "Delta", 2, 1));
        rounds[1] = WithMatch(rounds[1], 0, m => m with { Team2 = T("Gamma") });

        Assert.Empty(BracketAdvance.Plan(rounds, 3));
    }

    [Fact]
    public void A_six_team_bracket_moves_round_one_winners_into_the_free_semifinal_places()
    {
        var rounds = Rounds(BracketTemplate.Single6);
        rounds[1] = WithMatch(rounds[1], 0, m => m with { Team1 = T("Seed 1") });
        rounds[1] = WithMatch(rounds[1], 1, m => m with { Team1 = T("Seed 2") });
        rounds[0] = WithMatch(rounds[0], 0, _ => Played("Third", "Sixth", 2, 0));
        rounds[0] = WithMatch(rounds[0], 1, _ => Played("Fourth", "Fifth", 0, 2));

        var plan = BracketAdvance.Plan(rounds, 3);

        Assert.Contains(plan, a => a.Round == 1 && a.Match == 0 && a.Slot == 2 && a.Team.Name == "Third");
        Assert.Contains(plan, a => a.Round == 1 && a.Match == 1 && a.Slot == 2 && a.Team.Name == "Fifth");
    }

    [Fact]
    public void In_double_elimination_winners_follow_their_side_into_the_grand_final_and_losers_are_left_alone()
    {
        var rounds = Rounds(BracketTemplate.Double8);
        var upperFinal = rounds.ToList().FindIndex(r => r.Name == "Upper Final");
        var lowerFinal = rounds.ToList().FindIndex(r => r.Name == "Lower Final");
        var grand = rounds.ToList().FindIndex(r => r.Name == "Grand Final");
        rounds[0] = WithMatch(rounds[0], 2, _ => Played("A", "B", 2, 0));
        rounds[upperFinal] = WithMatch(rounds[upperFinal], 0, _ => Played("A", "C", 2, 1));
        rounds[lowerFinal] = WithMatch(rounds[lowerFinal], 0, _ => Played("D", "E", 0, 2));

        var plan = BracketAdvance.Plan(rounds, 3);

        Assert.Contains(plan, a => a.Round == 1 && a.Match == 1 && a.Slot == 1 && a.Team.Name == "A");   // upper round 1 match 3 -> semifinal 2
        Assert.Contains(plan, a => a.Round == grand && a.Slot == 1 && a.Team.Name == "A");
        Assert.Contains(plan, a => a.Round == grand && a.Slot == 2 && a.Team.Name == "E");
        Assert.DoesNotContain(plan, a => a.Team.Name is "B" or "C" or "D");
    }

    [Fact]
    public void A_match_whose_winner_has_no_name_moves_nobody()
    {
        var rounds = Rounds(BracketTemplate.Single4);
        rounds[0] = WithMatch(rounds[0], 0, _ => Played("", "Beta", 2, 0));

        Assert.Empty(BracketAdvance.Plan(rounds, 3));
    }
}

public sealed class AutomationViewModelTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-auto-" + Guid.NewGuid().ToString("N"));

    public AutomationViewModelTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private static string Fx(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "assets", name);

    private MatchViewModel Match(AutomationSettings? automation)
    {
        var assets = new AssetService(Path.Combine(_tmp, "Scoreboard"), Array.Empty<string>());
        assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = "Busan", Mode = "Control", ImageSourcePath = Fx("busan.png") });
        assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = "Circuit Royal", Mode = "Escort", ImageSourcePath = Fx("busan.png") });
        assets.AddOrUpdate(new AssetEdit { Category = AssetCategory.Maps, Name = "Suravasa", Mode = "Flashpoint", ImageSourcePath = Fx("busan.png") });
        return new MatchViewModel(assets, new NoPicker(), null) { Automation = automation };
    }

    private sealed class NoPicker : IImagePicker
    {
        public Task<string?> PickImageAsync(string title, string? initialDirectory = null) => Task.FromResult<string?>(null);
    }

    private static AutomationSettings All() => new() { TeamScoreFromMaps = true, CompleteMapsAutomatically = true, AdvanceBracketWinners = true };

    [Fact]
    public void Without_automations_nothing_follows_from_the_scores()
    {
        var match = Match(null);

        match.Maps[0].Map = "Busan";
        match.Maps[0].T1 = 2;

        Assert.False(match.Maps[0].Completed);
        Assert.Equal(0, match.Team1.Score);
    }

    [Fact]
    public void A_control_map_is_finished_at_two_points_and_the_team_score_follows()
    {
        var match = Match(All());
        var edits = 0;
        match.Edited += () => edits++;

        match.Maps[0].Map = "Busan";
        match.Maps[0].T1 = 1;
        Assert.False(match.Maps[0].Completed);
        match.Maps[0].T1 = 2;

        Assert.True(match.Maps[0].Completed);
        Assert.Equal(1, match.Team1.Score);
        Assert.Equal(0, match.Team2.Score);
        Assert.True(edits > 0);
    }

    [Fact]
    public void The_next_map_becomes_current_and_a_later_win_for_the_other_team_adds_to_its_score()
    {
        var match = Match(All());
        match.Maps[0].Map = "Busan";
        match.Maps[0].T1 = 2;
        match.Maps[1].Map = "Busan";
        match.Maps[1].T2 = 2;
        match.Maps[1].T1 = 1;

        Assert.True(match.Maps[1].Completed);
        Assert.Equal((1, 1), (match.Team1.Score, match.Team2.Score));
        Assert.Equal(3, match.Maps.First(c => c.IsShown && !c.Completed).Index);
    }

    [Fact]
    public void An_escort_map_is_not_decided_by_points_alone_but_a_finished_one_still_counts()
    {
        var match = Match(All());
        match.Maps[0].Map = "Circuit Royal";
        match.Maps[0].T1 = 3;

        Assert.False(match.Maps[0].Completed);
        Assert.Equal(0, match.Team1.Score);

        match.Maps[0].Completed = true;   // the producer finishes it

        Assert.Equal(1, match.Team1.Score);
    }

    [Fact]
    public void Unticking_Completed_takes_the_win_back_off_the_score_and_the_score_switch_alone_does_not_finish_maps()
    {
        var match = Match(new AutomationSettings { TeamScoreFromMaps = true });
        match.Maps[0].Map = "Busan";
        match.Maps[0].T1 = 2;
        Assert.False(match.Maps[0].Completed);   // finishing maps is a separate switch

        match.Maps[0].Completed = true;
        Assert.Equal(1, match.Team1.Score);
        match.Maps[0].Completed = false;

        Assert.Equal(0, match.Team1.Score);
    }

    [Fact]
    public void Automations_stand_aside_while_they_are_not_allowed_and_when_a_state_is_loaded()
    {
        var match = Match(All());
        match.AutomationAllowed = () => false;
        match.Maps[0].Map = "Busan";
        match.Maps[0].T1 = 2;
        Assert.False(match.Maps[0].Completed);

        match.AutomationAllowed = () => true;
        var state = AppState.CreateDefault() with
        {
            Maps = new[] { new MapEntry { Index = 1, Map = "Busan", T1 = 2, T2 = 0 } }
                .Concat(Enumerable.Range(2, 6).Select(i => new MapEntry { Index = i })).ToArray(),
        };
        match.Load(state);

        Assert.False(match.Maps[0].Completed);   // loading is not an edit
        Assert.Equal(0, match.Team1.Score);
    }

    [Fact]
    public void Reducing_the_series_length_recounts_the_score()
    {
        var match = Match(new AutomationSettings { TeamScoreFromMaps = true });
        match.Maps[0].T1 = 2;
        match.Maps[0].Completed = true;
        match.Maps[4].T1 = 2;
        match.Maps[4].Completed = true;
        Assert.Equal(2, match.Team1.Score);

        match.MapsCount = 3;   // map 5 is hidden

        Assert.Equal(1, match.Team1.Score);
    }

    [Fact]
    public void The_bracket_moves_a_winner_up_when_the_score_is_typed()
    {
        var bracket = NewBracket(All());
        bracket.SelectedTemplate = BracketTemplates.All[0].Label;
        bracket.LoadTemplateCommand.Execute(null);
        var semis = bracket.Sections[0].Rounds[0];
        var final = bracket.Sections[0].Rounds[1].Matches[0];
        var edits = 0;
        bracket.Edited += () => edits++;

        semis.Matches[0].Team1 = "Alpha";
        semis.Matches[0].Team2 = "Beta";
        semis.Matches[0].Score1 = 2;

        Assert.Equal("Alpha", final.Team1);
        Assert.Null(final.Team2);
        Assert.True(edits > 0);
        Assert.Equal("Alpha", bracket.ApplyTo(AppState.CreateDefault()).Bracket.Rounds[1].Matches[0].Team1.Name);
    }

    [Fact]
    public void The_bracket_keeps_the_logo_of_the_team_that_moves_up()
    {
        var bracket = NewBracket(All());
        bracket.Teams[0].Set(new TeamRef { Name = "Alpha", LogoPath = "Teams/alpha.png" });
        bracket.Teams[1].Set(new TeamRef { Name = "Beta" });
        bracket.SelectedTemplate = BracketTemplates.All[0].Label;
        bracket.LoadTemplateCommand.Execute(null);
        var semis = bracket.Sections[0].Rounds[0];

        semis.Matches[0].Team1 = "Alpha";
        semis.Matches[0].Team2 = "Beta";
        semis.Matches[0].Score1 = 2;

        Assert.Equal("Teams/alpha.png", bracket.ApplyTo(AppState.CreateDefault()).Bracket.Rounds[1].Matches[0].Team1.LogoPath);
    }

    [Fact]
    public void The_bracket_does_nothing_when_the_switch_is_off_or_the_tournament_fills_it()
    {
        var off = NewBracket(new AutomationSettings { TeamScoreFromMaps = true });
        off.SelectedTemplate = BracketTemplates.All[0].Label;
        off.LoadTemplateCommand.Execute(null);
        off.Sections[0].Rounds[0].Matches[0].Team1 = "Alpha";
        off.Sections[0].Rounds[0].Matches[0].Score1 = 2;
        Assert.Null(off.Sections[0].Rounds[1].Matches[0].Team1);

        var linked = NewBracket(All());
        linked.AutomationAllowed = () => false;
        linked.SelectedTemplate = BracketTemplates.All[0].Label;
        linked.LoadTemplateCommand.Execute(null);
        linked.Sections[0].Rounds[0].Matches[0].Team1 = "Alpha";
        linked.Sections[0].Rounds[0].Matches[0].Score1 = 2;
        Assert.Null(linked.Sections[0].Rounds[1].Matches[0].Team1);
    }

    private static BracketViewModel NewBracket(AutomationSettings automation) =>
        new(new NoPicker(), null, () => Array.Empty<TeamRef>(), null, new MainWindowViewModelTests.FakeDialogs(), new MatchIdSource(() => 1_760_000_000))
        {
            Automation = automation,
        };
}

public sealed class AutomationSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-autoset-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    [Fact]
    public void Everything_is_off_by_default()
    {
        var settings = new ControlSettings();

        Assert.False(settings.Automation.AnyEnabled);
        Assert.Equal(3, settings.Automation.DefaultBracketBestOf);
    }

    [Fact]
    public void The_switches_and_points_survive_saving_and_copying_keeps_the_same_object()
    {
        var path = Path.Combine(_dir, "control.json");
        var saved = new ControlSettings();
        saved.Automation.CompleteMapsAutomatically = true;
        saved.Automation.DefaultBracketBestOf = 5;
        saved.Automation.MapWinPoints["Escort"] = 3;
        saved.Save(path);

        var loaded = ControlSettings.Load(path);
        Assert.True(loaded.Automation.CompleteMapsAutomatically);
        Assert.Equal(5, loaded.Automation.DefaultBracketBestOf);
        Assert.Equal(3, loaded.Automation.PointsToWin("escort"));

        var shared = new ControlSettings();
        var held = shared.Automation;
        shared.CopyFrom(loaded);
        Assert.Same(held, shared.Automation);
        Assert.True(held.CompleteMapsAutomatically);
    }

    [Fact]
    public void The_window_turns_its_fields_into_automation_settings()
    {
        var start = new ControlSettings();
        start.Automation.AdvanceBracketWinners = true;
        var window = new ControlSettingsViewModel(start, null)
        {
            TeamScoreFromMaps = true,
            BestOf = 5,
            EscortPoints = 3,
        };

        var result = window.ToSettings().Automation;

        Assert.True(result.TeamScoreFromMaps);
        Assert.True(result.AdvanceBracketWinners);
        Assert.False(result.CompleteMapsAutomatically);
        Assert.Equal(5, result.DefaultBracketBestOf);
        Assert.Equal(3, result.PointsToWin("Escort"));
        Assert.Equal(2, result.PointsToWin("Control"));
    }
}

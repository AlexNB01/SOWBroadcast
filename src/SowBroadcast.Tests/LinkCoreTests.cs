using System.Text.Json;
using SowBroadcast.Core.Link;
using SowBroadcast.Core.Model;

namespace SowBroadcast.Tests;

public sealed class LinkNamingTests
{
    [Theory]
    [InlineData("Tank", null, "Tank")]
    [InlineData("Hitscan", null, "Damage")]
    [InlineData("Flex DPS", null, "Damage")]
    [InlineData("DPS", null, "Damage")]
    [InlineData("Main Support", null, "Support")]
    [InlineData("Flex Support", null, "Support")]
    [InlineData("Flex", null, "Flex")]
    [InlineData("Tank", "Hitscan,Main Support", "Flex")]   // main + secondary cover all three
    [InlineData("Tank", "Hitscan", "Tank")]
    [InlineData("Unknown", null, "")]
    [InlineData(null, null, "")]
    public void Roles_follow_the_documented_mapping(string? role, string? secondary, string expected) =>
        Assert.Equal(expected, LinkNaming.RoleOf(role, secondary?.Split(',')));

    [Theory]
    [InlineData("Pelaaja#1234", "Some Name", "d", "Pelaaja")]
    [InlineData("AVeryLongBattletagName#1", null, null, "AVeryLongBat")]
    [InlineData(null, "  Plain Name ", "d", "Plain Name")]
    [InlineData("#9999", "Fallback", null, "Fallback")]
    [InlineData(null, null, "discord_user", "discord_user")]
    [InlineData(null, null, null, "")]
    public void Player_names_come_from_the_battletag_then_name_then_discord(string? tag, string? name, string? discord, string expected) =>
        Assert.Equal(expected, LinkNaming.PlayerName(new LinkPlayer(name, tag, discord, null, Array.Empty<string>())));

    [Theory]
    [InlineData("Northern Lights", "NOR")]
    [InlineData("  a-b c", "ABC")]
    [InlineData("42 Squad", "42S")]
    [InlineData("--", "")]
    public void Abbreviations_are_the_first_three_letters_or_digits(string name, string expected) =>
        Assert.Equal(expected, LinkNaming.SuggestAbbreviation(name));

    [Fact]
    public void Names_match_exactly_then_ignoring_only_case_and_extra_spaces()
    {
        var catalogue = new[] { "King S Row", "Busan", "Lijiang Tower" };

        Assert.Equal("Busan", LinkNaming.ResolveName("Busan", catalogue));
        Assert.Equal("Lijiang Tower", LinkNaming.ResolveName("lijiang  tower", catalogue));
        Assert.Null(LinkNaming.ResolveName("King's Row", catalogue));   // punctuation is not guessed at: that is what aliases are for
        Assert.Null(LinkNaming.ResolveName("Nowhere", catalogue));
        Assert.Null(LinkNaming.ResolveName("'''", catalogue));
    }
}

/// <summary>Synthetic SOWDraft payloads (no real tournament data).</summary>
internal static class LinkFixtures
{
    public const string Origin = "https://sowdraft.test";

    public static string LegacyTournament(bool staged = true) => $$"""
    {
      "tournament": {
        "id": "t-uuid", "name": "Synthetic Cup", "slug": "synth", "format": "{{(staged ? "league" : "double_elimination")}}", "best_of": 3,
        "stages": {{(staged ? "\"[{\\\"id\\\":\\\"s1\\\",\\\"name\\\":\\\"League\\\",\\\"format\\\":\\\"league\\\",\\\"best_of\\\":2,\\\"pts_win\\\":3,\\\"pts_draw\\\":1,\\\"pts_loss\\\":0},{\\\"id\\\":\\\"s2\\\",\\\"name\\\":\\\"Playoffs\\\",\\\"format\\\":\\\"double_elimination\\\",\\\"best_of\\\":5}]\"" : "null")}}
      },
      "teams": [
        { "id": "a", "name": "Alpha", "logo_url": "/uploads/logos/a.png", "abbr": "ALP", "color": "#112233",
          "players": [ { "name": "One", "role": "Tank", "battletag": "OnePlayer#1234", "discord": "one" }, { "name": "Two", "role": "DPS" } ] },
        { "id": "b", "name": "Bravo", "logo_url": null, "players": [] },
        { "id": "c", "name": "Charlie", "logo_url": "https://cdn.test/c.png", "players": [] },
        { "id": "d", "name": "Delta", "players": [] }
      ],
      "matches": [
        { "id": "m1", "stage_id": "s1", "bracket": "main", "round": 1, "match_number": 1, "team1_id": "a", "team2_id": "b", "score1": 2, "score2": 0, "winner_team_id": "a", "status": "completed", "draft_match_id": "room-1", "scheduled_at": 1760000000000 },
        { "id": "m2", "stage_id": "s1", "bracket": "main", "round": 1, "match_number": 2, "team1_id": "c", "team2_id": "d", "score1": 1, "score2": 1, "winner_team_id": null, "status": "completed" },
        { "id": "m3", "stage_id": "s1", "bracket": "main", "round": 2, "match_number": 1, "team1_id": "a", "team2_id": "c", "score1": 2, "score2": 1, "winner_team_id": "a", "status": "completed" },
        { "id": "m4", "stage_id": "s1", "bracket": "main", "round": 2, "match_number": 2, "team1_id": "b", "team2_id": "d", "score1": 0, "score2": 2, "winner_team_id": "d", "status": "pending" },
        { "id": "p1", "stage_id": "s2", "bracket": "winners", "round": 1, "match_number": 1, "team1_id": "a", "team2_id": "d", "score1": 0, "score2": 0, "status": "pending" },
        { "id": "p2", "stage_id": "s2", "bracket": "winners", "round": 1, "match_number": 2, "team1_id": "b", "team2_id": "c", "status": "bye" },
        { "id": "p3", "stage_id": "s2", "bracket": "winners", "round": 2, "match_number": 1, "status": "pending" },
        { "id": "p4", "stage_id": "s2", "bracket": "losers", "round": 1, "match_number": 1, "status": "pending" },
        { "id": "p5", "stage_id": "s2", "bracket": "grand_final", "round": 1, "match_number": 1, "status": "pending" },
        { "id": "x1", "stage_id": "s2", "bracket": "group", "round": 1, "match_number": 1, "status": "pending" }
      ],
      "standings": null
    }
    """;

    /// <summary>A draft room's <c>update-state</c> payload: three maps (one decided, one drawn, one in progress).</summary>
    public const string DraftRoom = """
    {
      "matchId": "room-1", "tournamentMatchId": "m1", "team1Name": "Alpha", "team2Name": "Bravo", "phase": "hero-ban", "mapCount": 2,
      "score": { "team1": 1, "team2": 0 },
      "heroBans": [ { "team": "Alpha", "hero": "Genji" } ],
      "summary": [
        { "type": "map", "value": "Busan", "gamemode": "control", "pickedBy": "Alpha", "entries": [
            { "type": "ban", "value": "Ana", "team": "Alpha" }, { "type": "ban", "value": "Ashe", "team": "Bravo" },
            { "type": "result", "winner": "Alpha", "winnerSlot": "team1", "score": "2-1", "team1Score": 2, "team2Score": 1 } ] },
        { "type": "map", "value": "Midtown", "gamemode": "hybrid", "pickedBy": "Bravo", "entries": [
            { "type": "result", "winner": "draw", "winnerSlot": null, "team1Score": null, "team2Score": null } ] },
        { "type": "map", "value": "Busan", "gamemode": "control", "auto": true, "entries": [] }
      ]
    }
    """;

    public static TournamentSnapshot Snapshot(bool withLive = true, bool staged = true)
    {
        var snap = SnapshotParser.FromLegacy(LegacyTournament(staged), Origin);
        if (!withLive) return snap;
        using var room = JsonDocument.Parse(DraftRoom);
        return snap with { Live = LiveBuilder.Build(room.RootElement, "a", "b") };
    }
}

public sealed class SnapshotParserTests
{
    [Fact]
    public void The_legacy_response_is_read_with_stages_logos_and_best_of()
    {
        var snap = SnapshotParser.FromLegacy(LinkFixtures.LegacyTournament(), LinkFixtures.Origin);

        Assert.Equal(("t-uuid", "synth", "Synthetic Cup", 3), (snap.Id, snap.Slug, snap.Name, snap.BestOf));
        Assert.Equal(new[] { "s1", "s2" }, snap.Stages.Select(s => s.Id));
        Assert.Equal((3, 1, 0), (snap.Stages[0].PtsWin, snap.Stages[0].PtsDraw, snap.Stages[0].PtsLoss));
        Assert.Equal("https://sowdraft.test/uploads/logos/a.png", snap.TeamById("a")!.LogoUrl);
        Assert.Equal("https://cdn.test/c.png", snap.TeamById("c")!.LogoUrl);
        Assert.Null(snap.TeamById("b")!.LogoUrl);
        Assert.Equal(("ALP", "#112233"), (snap.TeamById("a")!.Abbr, snap.TeamById("a")!.Color));
        Assert.Equal(2, snap.MatchById("m1")!.BestOf);   // the stage's best of
        Assert.Equal(5, snap.MatchById("p1")!.BestOf);
        Assert.Equal("room-1", snap.MatchById("m1")!.DraftMatchId);
        Assert.Null(snap.Standings);
    }

    [Fact]
    public void An_unstaged_tournament_has_no_stages_and_keeps_the_tournament_best_of()
    {
        var snap = SnapshotParser.FromLegacy(LinkFixtures.LegacyTournament(staged: false), LinkFixtures.Origin);

        Assert.Empty(snap.Stages);
        Assert.Equal(3, snap.MatchById("m1")!.BestOf);
    }

    [Fact]
    public void The_servers_standings_are_read_as_groups_or_as_a_plain_list()
    {
        const string grouped = """{ "tournament": { "id": "t" }, "standings": { "A": [ { "id": "a", "name": "Alpha", "wins": 2, "losses": 1, "mapDiff": 3, "points": 6 } ], "B": [] } }""";
        const string plain = """{ "tournament": { "id": "t" }, "standings": [ { "id": "a", "name": "Alpha", "wins": 1, "losses": 0, "mapDiff": 2, "points": 3 } ] }""";

        var a = SnapshotParser.FromLegacy(grouped, "https://x.test");
        var b = SnapshotParser.FromLegacy(plain, "https://x.test");

        Assert.Equal(new[] { "A", "B" }, a.Standings!.Select(g => g.Key));
        Assert.Equal(("Alpha", 6, 3), (a.Standings![0].Entries[0].Name, a.Standings[0].Entries[0].Points, a.Standings[0].Entries[0].MapDiff));
        Assert.Equal("group_1", Assert.Single(b.Standings!).Key);
    }

    [Fact]
    public void A_response_without_a_tournament_is_an_error()
    {
        Assert.Throws<FormatException>(() => SnapshotParser.FromLegacy("""{ "teams": [] }""", "https://x.test"));
        Assert.Throws<FormatException>(() => SnapshotParser.FromBroadcast("""{ "teams": [] }""", "https://x.test"));
    }

    [Fact]
    public void The_broadcast_response_is_read_including_the_ready_made_live_state()
    {
        const string json = """
        {
          "schemaVersion": 1, "version": 42, "updatedAt": 1,
          "tournament": { "id": "t", "slug": "s", "name": "Cup", "format": "single_elimination", "status": "live", "bestOf": 3, "logoUrl": null },
          "stages": [ { "id": "s1", "name": "Main", "format": "single_elimination", "bestOf": 3, "ptsWin": 3, "ptsDraw": 1, "ptsLoss": 0, "tiebreakers": [] } ],
          "teams": [ { "id": "a", "name": "Alpha", "abbr": null, "color": "#ff0000", "logoUrl": "https://sowdraft.test/l.png",
                       "players": [ { "name": "P", "battletag": "P#1", "role": "Tank", "secondaryRoles": ["Hitscan"], "country": "FI", "hero": "Ana" }, { "name": "Q", "role": "Support", "hero": null } ] } ],
          "matches": [ { "id": "m", "stageId": "s1", "bracket": "main", "groupName": null, "round": 1, "matchNumber": 1, "team1Id": "a", "team2Id": null,
                         "score1": null, "score2": null, "winnerTeamId": null, "status": "in_progress", "scheduledAt": 5, "draftMatchId": "r", "bestOf": 3,
                         "startedAt": 7, "completedAt": null, "roundName": "Quarterfinals", "statusText": "Cup – Quarterfinals – BO3" } ],
          "standings": null,
          "advancement": [ { "stageId": "s1", "group": "A", "teamId": "a", "status": "qualified" }, { "stageId": null, "group": null, "teamId": "", "status": "" } ],
          "live": { "tournamentMatchId": "m", "draftMatchId": "r", "team1Id": "a", "team2Id": null, "phase": "map", "mapCount": 2,
                    "score": { "team1": 1, "team2": 0 }, "currentTurn": "team1", "countdownEnd": null,
                    "maps": [ { "order": 1, "map": "Busan", "gamemode": "control", "pickedBy": null, "auto": true, "side": null,
                                "bans": [ { "team": "team1", "hero": "Ana" } ], "status": "decided",
                                "result": { "winner": "team1", "team1Score": 2, "team2Score": 1 } } ] }
        }
        """;

        var snap = SnapshotParser.FromBroadcast(json, "https://sowdraft.test");

        Assert.Equal(42, snap.Version);
        Assert.Equal(("#ff0000", "https://sowdraft.test/l.png"), (snap.Teams[0].Color, snap.Teams[0].LogoUrl));
        Assert.Equal(new[] { "Hitscan" }, snap.Teams[0].Players[0].SecondaryRoles);
        Assert.Equal(("Ana", null), (snap.Teams[0].Players[0].Hero, snap.Teams[0].Players[1].Hero));   // the hero an organiser set in SOWDraft
        Assert.Equal(5, snap.Matches[0].ScheduledAt);
        Assert.Equal((7L, (long?)null, "Quarterfinals", "Cup – Quarterfinals – BO3"),
            (snap.Matches[0].StartedAt, snap.Matches[0].CompletedAt, snap.Matches[0].RoundName, snap.Matches[0].StatusText));
        Assert.Equal(new AdvancementEntry("s1", "A", "a", "qualified"), Assert.Single(snap.Advancement));   // an empty entry is dropped
        var live = snap.Live!;
        Assert.Equal((1, 0, "map", 2), (live.Score1, live.Score2, live.Phase, live.MapCount));
        var map = Assert.Single(live.Maps);
        Assert.True(map.Auto);
        Assert.Equal(("team1", "Ana"), (map.Bans[0].Team, map.Bans[0].Hero));
        Assert.Equal(("team1", 2, 1), (map.Result!.Winner, map.Result.Team1Score, map.Result.Team2Score));
    }
}

public sealed class LiveBuilderTests
{
    private static LiveState Build(string room, string? a = "a", string? b = "b")
    {
        using var doc = JsonDocument.Parse(room);
        return LiveBuilder.Build(doc.RootElement, a, b);
    }

    [Fact]
    public void The_series_is_built_in_order_with_slots_instead_of_names()
    {
        var live = Build(LinkFixtures.DraftRoom);

        Assert.Equal(("room-1", "m1", "hero-ban", 2, 1, 0), (live.DraftMatchId, live.TournamentMatchId, live.Phase, live.MapCount, live.Score1, live.Score2));
        Assert.Equal(("a", "b"), (live.Team1Id, live.Team2Id));
        Assert.Equal(new[] { "Busan", "Midtown", "Busan" }, live.Maps.Select(m => m.Map));
        Assert.Equal(new[] { 1, 2, 3 }, live.Maps.Select(m => m.Order));
        Assert.Equal(new[] { "team1", "team2", null }, live.Maps.Select(m => m.PickedBy));
        Assert.Equal(new[] { "decided", "decided", "current" }, live.Maps.Select(m => m.Status));
    }

    [Fact]
    public void Bans_are_resolved_to_slots_and_the_running_rounds_bans_go_to_the_last_map()
    {
        var live = Build(LinkFixtures.DraftRoom);

        Assert.Equal(new[] { ("team1", "Ana"), ("team2", "Ashe") }, live.Maps[0].Bans.Select(b => (b.Team!, b.Hero)));
        Assert.Empty(live.Maps[1].Bans);
        Assert.Equal(("team1", "Genji"), (live.Maps[2].Bans.Single().Team!, live.Maps[2].Bans.Single().Hero));
    }

    [Fact]
    public void Results_carry_the_scores_a_draw_and_a_pick_that_was_automatic_has_no_picker()
    {
        var live = Build(LinkFixtures.DraftRoom);

        Assert.Equal(("team1", 2, 1), (live.Maps[0].Result!.Winner, live.Maps[0].Result!.Team1Score, live.Maps[0].Result!.Team2Score));
        Assert.Equal("draw", live.Maps[1].Result!.Winner);
        Assert.Null(live.Maps[1].Result!.Team1Score);
        Assert.True(live.Maps[2].Auto);
        Assert.Null(live.Maps[2].PickedBy);
        Assert.Null(live.Maps[2].Result);
    }

    [Fact]
    public void A_score_given_only_as_text_and_a_winner_given_only_by_name_are_understood()
    {
        const string room = """
        { "team1Name": "Alpha", "team2Name": "Bravo", "phase": "complete", "mapCount": 1, "summary": [
          { "type": "map", "value": "Busan", "pickedBy": "Bravo", "entries": [ { "type": "result", "winner": "Bravo", "score": "1 - 3" } ] } ] }
        """;

        var result = Build(room).Maps[0].Result!;

        Assert.Equal(("team2", 1, 3), (result.Winner, result.Team1Score, result.Team2Score));
    }

    [Fact]
    public void Teams_with_the_same_name_cannot_be_told_apart_so_nothing_is_attributed()
    {
        const string room = """
        { "team1Name": "Same", "team2Name": "Same", "phase": "map", "mapCount": 1, "summary": [
          { "type": "map", "value": "Busan", "pickedBy": "Same", "entries": [ { "type": "ban", "value": "Ana", "team": "Same" } ] } ] }
        """;

        var map = Build(room).Maps[0];

        Assert.Null(map.PickedBy);
        Assert.Null(map.Bans.Single().Team);
    }

    [Fact]
    public void A_room_without_maps_has_an_empty_series()
    {
        var live = Build("""{ "team1Name": "A", "team2Name": "B", "phase": "map", "mapCount": 2 }""");

        Assert.Empty(live.Maps);
    }
}

public sealed class MatchLinkMapperTests
{
    private static readonly string[] MapNames = { "Busan", "Midtown", "King S Row" };

    private static LinkApplyOptions Options(bool swapped = false, Func<string, string?>? logos = null) =>
        new(swapped, MapNames, logos ?? (_ => null));

    // The catalogue's aliases (from the asset manifest) are what finds "King's Row" in the list.
    private static LinkApplyOptions OptionsWithAliases() => Options() with
    {
        ResolveMap = name => name == "King's Row" ? "King S Row" : null,
        ResolveHero = name => name == "Soldier 76" ? "Soldier: 76" : null,
    };

    private static AppState Blank() => AppState.CreateDefault();

    private static readonly IReadOnlyDictionary<string, string> NoHistory = new Dictionary<string, string>();

    [Fact]
    public void The_teams_are_filled_in_with_their_data_and_the_players_in_order()
    {
        var result = MatchLinkMapper.Apply(Blank(), LinkFixtures.Snapshot(), "m1", Options(logos: _ => "C:/logos/a.png"), NoHistory);
        var t1 = result.State.Team1;
        var t2 = result.State.Team2;

        Assert.Equal(("Alpha", "ALP", "#112233", "C:/logos/a.png"), (t1.Name, t1.Abbr, t1.ColorHex, t1.LogoPath));
        Assert.Equal(("OnePlayer", "Tank"), (t1.Players[0].Name, t1.Players[0].Role));
        Assert.Equal(("Two", "Damage"), (t1.Players[1].Name, t1.Players[1].Role));
        Assert.Equal(("Bravo", "BRA"), (t2.Name, t2.Abbr));                    // abbreviation suggested
        Assert.Equal(Defaults.Team2Color, t2.ColorHex);                          // no colour from SOWDraft: panel keeps its own
        Assert.Equal(8, t1.Players.Count);
    }

    [Fact]
    public void Sides_swapped_puts_SOWDrafts_team_one_into_the_second_panel_with_everything_that_belongs_to_it()
    {
        var result = MatchLinkMapper.Apply(Blank(), LinkFixtures.Snapshot(), "m1", Options(swapped: true), NoHistory);

        Assert.Equal(("Bravo", 0), (result.State.Team1.Name, result.State.Team1.Score));
        Assert.Equal(("Alpha", 1), (result.State.Team2.Name, result.State.Team2.Score));
        var first = result.State.Maps[0];
        Assert.Equal((1, 2), (first.T1, first.T2));            // Alpha won 2-1; Alpha is the T2 panel now
        Assert.Equal(Picks.Team2, first.Pick);                  // Alpha picked
        Assert.Equal(("Ashe", "Ana"), (first.T1Ban, first.T2Ban));
    }

    [Fact]
    public void The_match_score_and_the_map_cards_come_from_the_live_draft()
    {
        var state = MatchLinkMapper.Apply(Blank(), LinkFixtures.Snapshot(), "m1", Options(), NoHistory).State;

        Assert.Equal((1, 0), (state.Team1.Score, state.Team2.Score));
        var (m1, m2, m3) = (state.Maps[0], state.Maps[1], state.Maps[2]);
        Assert.Equal(("Busan", 2, 1, true, Picks.Team1, "Ana", "Ashe"), (m1.Map, m1.T1, m1.T2, m1.Completed, m1.Pick, m1.T1Ban, m1.T2Ban));
        Assert.Equal(("Midtown", 0, 0, true, Picks.Team2), (m2.Map, m2.T1, m2.T2, m2.Completed, m2.Pick)); // a drawn map is a card of its own
        Assert.Equal(("Busan", false, Picks.Tba, "Genji"), (m3.Map, m3.Completed, m3.Pick, m3.T1Ban)); // the same map twice; auto pick is TBA
        Assert.Equal(3, state.CurrentMap);
        Assert.Equal("t1", m1.Winner);
    }

    [Fact]
    public void A_push_map_without_detailed_scores_counts_one_to_nothing()
    {
        var snap = LinkFixtures.Snapshot();
        var live = snap.Live! with
        {
            Maps = new[] { new LiveMap(1, "Busan", "push", "team2", false, Array.Empty<LiveBan>(), "decided", new LiveResult("team2", null, null)) },
        };

        var state = MatchLinkMapper.Apply(Blank(), snap with { Live = live }, "m1", Options(), NoHistory).State;

        Assert.Equal((0, 1, true), (state.Maps[0].T1, state.Maps[0].T2, state.Maps[0].Completed));
    }

    [Fact]
    public void A_manual_correction_stays_until_SOWDraft_changes_that_value()
    {
        var snap = LinkFixtures.Snapshot();
        var first = MatchLinkMapper.Apply(Blank(), snap, "m1", Options(), NoHistory);

        var corrected = first.State with { Team1 = first.State.Team1 with { Name = "Alpha (typed)" }, Maps = first.State.Maps.Select((m, i) => i == 0 ? m with { T1 = 9 } : m).ToArray() };
        var again = MatchLinkMapper.Apply(corrected, snap, "m1", Options(), first.Received);
        Assert.Equal("Alpha (typed)", again.State.Team1.Name);
        Assert.Equal(9, again.State.Maps[0].T1);

        var renamed = snap with { Teams = snap.Teams.Select(t => t.Id == "a" ? t with { Name = "Alpha 2" } : t).ToArray() };
        var changed = MatchLinkMapper.Apply(corrected, renamed, "m1", Options(), again.Received);
        Assert.Equal("Alpha 2", changed.State.Team1.Name);
        Assert.Equal(9, changed.State.Maps[0].T1);   // that value did not change in SOWDraft
    }

    private static TournamentSnapshot WithHeroes(TournamentSnapshot snap, params string?[] heroes) =>
        snap with
        {
            Teams = snap.Teams.Select(t => t.Id != "a" ? t : t with
            {
                Players = t.Players.Select((p, i) => i < heroes.Length ? p with { Hero = heroes[i] } : p).ToArray(),
            }).ToArray(),
        };

    [Fact]
    public void The_hero_set_for_a_player_in_SOWDraft_is_filled_in_as_the_catalogue_spells_it()
    {
        var snap = WithHeroes(LinkFixtures.Snapshot(), "Soldier 76", "Ana");

        var result = MatchLinkMapper.Apply(Blank(), snap, "m1", OptionsWithAliases() with { ResolveHero = name => name == "Soldier 76" ? "Soldier: 76" : name, }, NoHistory);

        var players = result.State.Team1.Players;
        Assert.Equal("Soldier: 76", players[0].Hero);
        Assert.Equal("Ana", players[1].Hero);
        Assert.Equal("", players[2].Hero);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_hero_the_catalogue_does_not_know_is_left_out_with_a_warning()
    {
        var snap = WithHeroes(LinkFixtures.Snapshot(), "Nobody");

        var result = MatchLinkMapper.Apply(Blank(), snap, "m1", OptionsWithAliases(), NoHistory);

        Assert.Equal("", result.State.Team1.Players[0].Hero);
        Assert.Contains(result.Warnings, w => w.Contains("Hero \"Nobody\" is not in the hero list."));
    }

    [Fact]
    public void A_hero_chosen_by_hand_stays_until_the_hero_in_SOWDraft_changes()
    {
        var opts = Options() with { ResolveHero = name => name };
        var first = MatchLinkMapper.Apply(Blank(), WithHeroes(LinkFixtures.Snapshot(), "Ana"), "m1", opts, NoHistory);
        var corrected = first.State with { Team1 = first.State.Team1 with { Players = first.State.Team1.Players.Select((p, i) => i == 0 ? p with { Hero = "Ashe" } : p).ToArray() } };

        var same = MatchLinkMapper.Apply(corrected, WithHeroes(LinkFixtures.Snapshot(), "Ana"), "m1", opts, first.Received);
        var changed = MatchLinkMapper.Apply(corrected, WithHeroes(LinkFixtures.Snapshot(), "Genji"), "m1", opts, first.Received);

        Assert.Equal("Ashe", same.State.Team1.Players[0].Hero);      // SOWDraft's value did not change: the correction stays
        Assert.Equal("Genji", changed.State.Team1.Players[0].Hero);  // it did: SOWDraft wins
    }

    private static TournamentSnapshot WithStatusText(TournamentSnapshot snap, string? text) =>
        snap with { Matches = snap.Matches.Select(m => m.Id == "m1" ? m with { StatusText = text } : m).ToArray() };

    [Fact]
    public void The_status_line_of_the_match_is_filled_into_the_general_settings()
    {
        var result = MatchLinkMapper.Apply(Blank(), WithStatusText(LinkFixtures.Snapshot(), "Cup – Final – BO5"), "m1", Options(), NoHistory);

        Assert.Equal("Cup – Final – BO5", result.State.General.StatusText);
    }

    [Fact]
    public void A_status_line_typed_by_hand_stays_until_SOWDraft_changes_it_and_a_missing_one_changes_nothing()
    {
        var snap = WithStatusText(LinkFixtures.Snapshot(), "Cup – Final – BO5");
        var first = MatchLinkMapper.Apply(Blank(), snap, "m1", Options(), NoHistory);
        var typed = first.State with { General = first.State.General with { StatusText = "My own line" } };

        var same = MatchLinkMapper.Apply(typed, snap, "m1", Options(), first.Received);
        var changed = MatchLinkMapper.Apply(typed, WithStatusText(snap, "Cup – Semifinals – BO5"), "m1", Options(), first.Received);
        var none = MatchLinkMapper.Apply(Blank() with { General = Blank().General with { StatusText = "Kept" } }, WithStatusText(LinkFixtures.Snapshot(), null), "m1", Options(), NoHistory);

        Assert.Equal("My own line", same.State.General.StatusText);
        Assert.Equal("Cup – Semifinals – BO5", changed.State.General.StatusText);
        Assert.Equal("Kept", none.State.General.StatusText);
    }

    [Fact]
    public void Switching_to_a_team_with_fewer_players_clears_the_leftover_rows_and_their_heroes()
    {
        var snap = LinkFixtures.Snapshot();
        var first = MatchLinkMapper.Apply(Blank(), snap, "m1", Options(), NoHistory);
        var withHero = first.State with
        {
            Team1 = first.State.Team1 with { Players = first.State.Team1.Players.Select(p => p with { Hero = "Ana" }).ToArray() },
        };

        var other = snap with { Teams = snap.Teams.Select(t => t.Id == "a" ? t with { Players = new[] { new LinkPlayer("Solo", null, null, "Support", Array.Empty<string>()) } } : t).ToArray() };
        var next = MatchLinkMapper.Apply(withHero, other, "m1", Options(), first.Received).State.Team1.Players;

        Assert.Equal(("Solo", "Support", ""), (next[0].Name, next[0].Role, next[0].Hero));   // another player: hero cleared
        Assert.Equal(("", "", ""), (next[1].Name, next[1].Role, next[1].Hero));               // row emptied
    }

    [Fact]
    public void Empty_rows_do_not_wipe_what_the_operator_typed_on_the_first_apply()
    {
        var typed = Blank() with { Team1 = Blank().Team1 with { Players = Blank().Team1.Players.Select((p, i) => i == 5 ? p with { Name = "Manual" } : p).ToArray() } };

        var result = MatchLinkMapper.Apply(typed, LinkFixtures.Snapshot(), "m1", Options(), NoHistory);

        Assert.Equal("Manual", result.State.Team1.Players[5].Name);
    }

    [Fact]
    public void Unknown_maps_are_kept_by_name_with_a_warning_and_extra_maps_are_reported()
    {
        var snap = LinkFixtures.Snapshot();
        var live = snap.Live! with
        {
            Maps = new[]
            {
                new LiveMap(1, "King's Row", null, "team1", false, Array.Empty<LiveBan>(), "decided", new LiveResult("team1", 1, 0)),
                new LiveMap(2, "Atlantis", null, null, false, Array.Empty<LiveBan>(), "current", null),
            },
        };
        var state = Blank() with { General = Blank().General with { MapsCount = 1 } };

        var result = MatchLinkMapper.Apply(state, snap with { Live = live }, "m1", OptionsWithAliases(), NoHistory);

        Assert.Equal("King S Row", result.State.Maps[0].Map);        // found by an alias of the manifest
        Assert.Equal("Atlantis", result.State.Maps[1].Map);          // not silently dropped
        Assert.Contains(result.Warnings, w => w.Contains("Atlantis"));
        Assert.Contains(result.Warnings, w => w.Contains("2 maps") && w.Contains("shows 1"));
    }

    [Fact]
    public void Logos_that_are_not_on_disk_yet_are_reported_and_retried()
    {
        var snap = LinkFixtures.Snapshot();

        var first = MatchLinkMapper.Apply(Blank(), snap, "m1", Options(), NoHistory);
        Assert.Equal(new[] { "https://sowdraft.test/uploads/logos/a.png" }, first.LogosToFetch);
        Assert.Null(first.State.Team1.LogoPath);

        var second = MatchLinkMapper.Apply(first.State, snap, "m1", Options(logos: _ => "C:/cache/a.png"), first.Received);
        Assert.Equal("C:/cache/a.png", second.State.Team1.LogoPath);
        Assert.Empty(second.LogosToFetch);
    }

    [Fact]
    public void A_selected_match_that_vanished_changes_nothing_and_says_so()
    {
        var result = MatchLinkMapper.Apply(Blank(), LinkFixtures.Snapshot(), "gone", Options(), NoHistory);

        Assert.Equal(Blank().Team1.Name, result.State.Team1.Name);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void A_match_without_a_draft_fills_the_teams_but_not_the_maps()
    {
        var state = MatchLinkMapper.Apply(Blank(), LinkFixtures.Snapshot(withLive: false), "m1", Options(), NoHistory).State;

        Assert.Equal("Alpha", state.Team1.Name);
        Assert.All(state.Maps, m => Assert.Equal("", m.Map));
        Assert.Equal((0, 0), (state.Team1.Score, state.Team2.Score));
    }
}

public sealed class TournamentMapperTests
{
    private static string? NoLogo(string? url) => url is null ? null : "L:" + Path.GetFileName(url);

    [Fact]
    public void A_league_stage_becomes_a_table_with_the_stages_points_and_extended_figures()
    {
        var standings = TournamentMapper.Standings(LinkFixtures.Snapshot(withLive: false), NoLogo);

        var group = Assert.Single(standings.Groups);
        Assert.Equal(("s1", "League"), (group.Key, group.Name));
        var rows = group.Rows;
        // Alpha 2 wins (6), Delta 1 draw (1), Charlie 1 draw (1), Bravo 0. Charlie vs Delta tie on points and difference: stable order.
        Assert.Equal(new[] { "Alpha", "Charlie", "Delta", "Bravo" }.Length, rows.Count);
        Assert.Equal("Alpha", rows[0].TeamName);
        Assert.Equal((6, 2, 0, 3, 4, 1), (rows[0].Points, rows[0].Wins, rows[0].Draws + rows[0].Losses, rows[0].MapDiff + 0, rows[0].Sf, rows[0].Sa));
        Assert.Equal(new[] { 1, 2, 3, 4 }, rows.Select(r => r.Rank));
        Assert.Equal("Bravo", rows[^1].TeamName);
        Assert.Equal(("s1", "Synthetic Cup"), (standings.DisplayGroup, standings.Title));
        Assert.Same(group.Rows, standings.Rows);
    }

    [Fact]
    public void A_drawn_finished_match_gives_both_teams_the_draw_points_and_a_pending_match_none()
    {
        var rows = TournamentMapper.Standings(LinkFixtures.Snapshot(withLive: false), NoLogo).Groups[0].Rows;

        var charlie = rows.Single(r => r.TeamName == "Charlie");
        var delta = rows.Single(r => r.TeamName == "Delta");
        Assert.Equal((1, 1, 1), (charlie.Draws, delta.Draws, delta.Played));     // m2 draw only; m4 is pending
        Assert.Equal(2, charlie.Played);                                          // m2 and m3
        Assert.Equal(1, delta.Points);
    }

    [Fact]
    public void Logos_are_resolved_through_the_cache_and_a_missing_one_stays_empty()
    {
        var rows = TournamentMapper.Standings(LinkFixtures.Snapshot(withLive: false), NoLogo).Groups[0].Rows;

        Assert.Equal("L:a.png", rows.Single(r => r.TeamName == "Alpha").LogoPath);
        Assert.Null(rows.Single(r => r.TeamName == "Bravo").LogoPath);
        Assert.Equal("ALP", rows.Single(r => r.TeamName == "Alpha").Abbr);
    }

    [Fact]
    public void A_group_stage_gets_one_group_per_group_name()
    {
        var snap = new TournamentSnapshot
        {
            Name = "Groups",
            Stages = new[] { new LinkStage("g", "Groups", "group_stage", 1) },
            Teams = new[] { new LinkTeam("a", "A", null, null, null, Array.Empty<LinkPlayer>()), new LinkTeam("b", "B", null, null, null, Array.Empty<LinkPlayer>()) },
            Matches = new[]
            {
                new LinkMatch("1", "g", "group", "B", 1, 1, "a", "b", 2, 0, "a", "completed", null, null, 3),
                new LinkMatch("2", "g", "group", "A", 1, 1, "a", "b", 0, 2, "b", "completed", null, null, 3),
                new LinkMatch("3", "g", "playoff", null, 1, 1, "a", "b", null, null, null, "pending", null, null, 3),
            },
        };

        var groups = TournamentMapper.Standings(snap, NoLogo).Groups;

        Assert.Equal(new[] { "g_A", "g_B" }, groups.Select(g => g.Key));
        Assert.Equal("Groups - Group A", groups[0].Name);
        Assert.Equal("B", groups[0].Rows[0].TeamName);
    }

    [Fact]
    public void An_unstaged_tournament_uses_the_servers_table()
    {
        var snap = new TournamentSnapshot
        {
            Name = "Old style",
            Teams = new[] { new LinkTeam("a", "Alpha", null, null, "https://x.test/a.png", Array.Empty<LinkPlayer>()) },
            Standings = new[]
            {
                new ServerStandingsGroup("A", new[] { new ServerStandingsEntry("a", "Alpha", 3, 1, 4, 9), new ServerStandingsEntry("z", "Zed", 0, 4, -4, 0) }),
                new ServerStandingsGroup("B", Array.Empty<ServerStandingsEntry>()),
            },
        };

        var standings = TournamentMapper.Standings(snap, NoLogo);

        Assert.Equal(new[] { "Group A", "Group B" }, standings.Groups.Select(g => g.Name));
        Assert.Equal(("Alpha", 9, 1, "L:a.png"), (standings.Rows[0].TeamName, standings.Rows[0].Points, standings.Rows[0].Rank, standings.Rows[0].LogoPath));
        Assert.Equal(2, standings.Rows[1].Rank);
        Assert.Equal("A", standings.DisplayGroup);
    }

    [Fact]
    public void The_bracket_drops_byes_and_group_matches_and_names_rounds_by_their_distance_from_the_final()
    {
        var bracket = TournamentMapper.Bracket(LinkFixtures.Snapshot(withLive: false), NoLogo);

        // Only the double elimination stage is a bracket stage here (the league is not); "x1" is a group match and p2 a bye.
        Assert.Equal(new[] { "Upper Semifinals", "Upper Final", "Lower Final", "Grand Final" }, bracket.Rounds.Select(r => r.Name));
        Assert.Equal(new[] { "upper", "upper", "lower", "grand" }, bracket.Rounds.Select(r => r.Side));
        Assert.Equal(new[] { "p1" }, bracket.Rounds[0].Matches.Select(m => m.Id));
        Assert.Equal("BO5", bracket.Rounds[0].Matches[0].BoLabel);
        Assert.Equal(("Alpha", "Delta"), (bracket.Rounds[0].Matches[0].Team1.Name, bracket.Rounds[0].Matches[0].Team2.Name));
        Assert.Equal("upper", bracket.DoubleElimView);
        Assert.Equal(16, bracket.Teams.Count);
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie", "Delta", "" }, bracket.Teams.Take(5).Select(t => t.Name));
        Assert.Equal(("Synthetic Cup", ""), (bracket.Title, bracket.Stage));
    }

    [Fact]
    public void A_round_whose_byes_are_left_out_keeps_its_matches_in_their_places()
    {
        // Five teams: of the four first-round places only the second is played, the others are byes.
        var snap = new TournamentSnapshot
        {
            Stages = new[] { new LinkStage("a", "Cup", "single_elimination", 3) },
            Matches = new[]
            {
                new LinkMatch("b1", "a", "main", null, 1, 1, "t1", null, null, null, "t1", "bye", null, null, 3),
                new LinkMatch("p2", "a", "main", null, 1, 2, "t4", "t5", null, null, null, "pending", null, null, 3),
                new LinkMatch("b3", "a", "main", null, 1, 3, "t2", null, null, null, "t2", "bye", null, null, 3),
                new LinkMatch("b4", "a", "main", null, 1, 4, "t3", null, null, null, "t3", "bye", null, null, 3),
                new LinkMatch("s1", "a", "main", null, 2, 1, "t1", null, null, null, null, "pending", null, null, 3),
                new LinkMatch("s2", "a", "main", null, 2, 2, "t2", "t3", null, null, null, "pending", null, null, 3),
                new LinkMatch("f", "a", "main", null, 3, 1, null, null, null, null, null, "pending", null, null, 3),
            },
        };

        var rounds = TournamentMapper.Bracket(snap, NoLogo).Rounds;

        Assert.Equal(new[] { "Upper Quarterfinals", "Upper Semifinals", "Upper Final" }, rounds.Select(r => r.Name));
        Assert.Equal(4, rounds[0].Slots);
        Assert.Equal(new[] { "p2" }, rounds[0].Matches.Select(m => m.Id));
        Assert.Equal(2, rounds[0].Matches[0].Slot);
        // Rounds with all their matches need no places.
        Assert.Equal(new[] { 0, 0 }, new[] { rounds[1].Slots, rounds[2].Slots });
        Assert.All(rounds[1].Matches.Concat(rounds[2].Matches), m => Assert.Equal(0, m.Slot));
    }

    [Fact]
    public void The_places_reach_the_overlays_bracket_json_only_when_a_round_has_them()
    {
        var withPlaces = new BracketSettings
        {
            Rounds = new[]
            {
                new BracketRound { Name = "R1", Slots = 4, Matches = new[] { new BracketMatch { Id = "x", Slot = 2 } } },
                new BracketRound { Name = "R2", Matches = new[] { new BracketMatch { Id = "y" } } },
            },
        };

        var json = new SowBroadcast.Core.Export.ScoreboardExporter(Path.GetTempPath(), new SowBroadcast.Core.Export.SkiaLogoConverter())
            .BuildBracketJson(new AppState { Bracket = withPlaces });

        var rounds = json["rounds"]!.AsArray();
        Assert.Equal(4, (int)rounds[0]!["slots"]!);
        Assert.Equal(2, (int)rounds[0]!["matches"]![0]!["slot"]!);
        Assert.Null(rounds[1]!["slots"]);
        Assert.Null(rounds[1]!["matches"]![0]!["slot"]);
    }

    [Fact]
    public void With_several_bracket_stages_the_round_names_do_not_carry_the_stage_name()
    {
        var snap = new TournamentSnapshot
        {
            Stages = new[] { new LinkStage("a", "Qualifier", "single_elimination", 3), new LinkStage("b", "Finals", "single_elimination", 5) },
            Matches = new[]
            {
                new LinkMatch("1", "a", "main", null, 1, 1, null, null, null, null, null, "pending", null, null, 3),
                new LinkMatch("2", "b", "main", null, 1, 1, null, null, null, null, null, "pending", null, null, 5),
            },
        };

        var rounds = TournamentMapper.Bracket(snap, NoLogo).Rounds;

        Assert.Equal(new[] { "Upper Final", "Upper Final" }, rounds.Select(r => r.Name));
    }
}

public sealed class StandingsStatusTests
{
    private static string? NoLogo(string? _) => null;

    private static TournamentSnapshot GroupSnapshot(params AdvancementEntry[] advancement)
    {
        LinkMatch Group(string id, string group, string t1, string t2, string? winner) =>
            new(id, "g1", "group", group, 1, 1, t1, t2, winner is null ? null : 2, winner is null ? null : 0, winner, winner is null ? "pending" : "completed", null, null, 3);
        var team = (string id, string name) => new LinkTeam(id, name, null, null, null, Array.Empty<LinkPlayer>());
        return new TournamentSnapshot
        {
            Id = "t", Name = "Cup", Format = "group_stage",
            Stages = new[] { new LinkStage("g1", "Groups", "group_stage", 3, 3, 1, 0) },
            Teams = new[] { team("a", "Alpha"), team("b", "Bravo"), team("c", "Charlie"), team("d", "Delta") },
            Matches = new[] { Group("m1", "A", "a", "b", "a"), Group("m2", "B", "c", "d", null) },
            Advancement = advancement,
        };
    }

    [Fact]
    public void A_group_table_row_gets_the_status_SOWDraft_decided_for_its_team()
    {
        var standings = TournamentMapper.Standings(GroupSnapshot(
            new AdvancementEntry("g1", "A", "a", "qualified"), new AdvancementEntry("g1", "A", "b", "eliminated")), NoLogo);

        var groupA = standings.Groups.Single(g => g.Name.EndsWith("Group A"));
        var groupB = standings.Groups.Single(g => g.Name.EndsWith("Group B"));
        Assert.Equal(new[] { ("Alpha", "qualified"), ("Bravo", "eliminated") }, groupA.Rows.Select(r => (r.TeamName, r.Status)));
        Assert.All(groupB.Rows, r => Assert.Equal("", r.Status));      // nothing decided there
    }

    [Fact]
    public void An_entry_for_another_group_or_stage_does_not_leak_into_a_table()
    {
        var standings = TournamentMapper.Standings(GroupSnapshot(
            new AdvancementEntry("g1", "B", "a", "qualified"), new AdvancementEntry("other", "A", "a", "qualified")), NoLogo);

        Assert.All(standings.Groups.SelectMany(g => g.Rows), r => Assert.Equal("", r.Status));
    }

    [Fact]
    public void Without_a_stage_the_servers_own_table_gets_the_status_too()
    {
        var snap = GroupSnapshot(new AdvancementEntry(null, "A", "a", "qualified")) with
        {
            Stages = Array.Empty<LinkStage>(),
            Standings = new[] { new ServerStandingsGroup("A", new[] { new ServerStandingsEntry("a", "Alpha", 2, 0, 3, 6), new ServerStandingsEntry("b", "Bravo", 0, 2, -3, 0) }) },
        };

        var rows = TournamentMapper.Standings(snap, NoLogo).Groups[0].Rows;

        Assert.Equal(new[] { "qualified", "" }, rows.Select(r => r.Status));
    }
}

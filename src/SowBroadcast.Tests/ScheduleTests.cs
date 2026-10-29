using System.Text.Json.Nodes;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Link;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;
using SowBroadcast.Core.Waiting;
using Xunit;

namespace SowBroadcast.Tests;

/// <summary>The match schedule of the waiting screens: SOWDraft matches -> entries -> what the overlay lists.</summary>
public sealed class ScheduleTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-schedule-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private const string Twitch = "https://twitch.tv/suomiow";

    private static ScheduleEntry Upcoming(string id, string t1, string t2, long at, string stream = Twitch, int round = 1, int number = 1) =>
        new() { Id = id, Team1 = t1, Team2 = t2, Status = ScheduleStatus.Upcoming, ScheduledAt = at, StreamUrl = stream, Round = round, MatchNumber = number, RoundName = $"Round {round}" };

    private static ScheduleEntry Done(string id, string t1, string t2, int s1, int s2, long completedAt, int round = 1, int number = 1) =>
        new() { Id = id, Team1 = t1, Team2 = t2, Score1 = s1, Score2 = s2, Status = ScheduleStatus.Completed, CompletedAt = completedAt, Round = round, MatchNumber = number, RoundName = $"Round {round}" };

    private static AppState With(IEnumerable<ScheduleEntry> entries, WaitingSettings? waiting = null) =>
        new() { Schedule = entries.ToArray(), Waiting = waiting ?? new WaitingSettings() };

    private static string[] Ids(IEnumerable<ScheduleEntry> list) => list.Select(e => e.Id).ToArray();

    // ---- from the tournament ---------------------------------------------------------------------------

    [Fact]
    public void Mapper_turns_matches_into_entries_and_skips_byes()
    {
        var snapshot = new TournamentSnapshot
        {
            Teams = new[]
            {
                new LinkTeam("a", "Alpha", null, null, null, Array.Empty<LinkPlayer>()),
                new LinkTeam("b", "Bravo", null, null, null, Array.Empty<LinkPlayer>()),
            },
            Matches = new[]
            {
                new LinkMatch("m1", null, "main", null, 2, 1, "a", "b", 2, 1, "a", "completed", 1000, null, 3, 500, 900, "Semifinals", null, "https://twitch.tv/x"),
                new LinkMatch("m2", null, "main", null, 3, 1, "a", null, null, null, null, "in_progress", 2000, "d1", 3),
                new LinkMatch("m3", null, "group", "A", 1, 2, "b", "a", null, null, null, "pending", null, null, 3),
                new LinkMatch("m4", null, "main", null, 1, 1, "a", null, null, null, "a", "bye", null, null, 3),
            },
        };

        var entries = ScheduleMapper.FromSnapshot(snapshot);

        Assert.Equal(new[] { "m1", "m2", "m3" }, Ids(entries));
        var done = entries[0];
        Assert.Equal(("Alpha", "Bravo", 2, 1, ScheduleStatus.Completed), (done.Team1, done.Team2, done.Score1, done.Score2, done.Status));
        Assert.Equal((1000L, 500L, 900L, "Semifinals", "https://twitch.tv/x"), (done.ScheduledAt, done.StartedAt, done.CompletedAt, done.RoundName, done.StreamUrl));
        Assert.Equal((ScheduleStatus.Live, "Alpha", ""), (entries[1].Status, entries[1].Team1, entries[1].Team2));   // a team that is not known yet has no name
        Assert.Equal((ScheduleStatus.Upcoming, "A", "Round 1", 0L), (entries[2].Status, entries[2].Group, entries[2].RoundName, entries[2].ScheduledAt));
    }

    [Fact]
    public void The_broadcast_api_carries_the_stream_link()
    {
        var json = """
        { "tournament": { "id": "t" }, "teams": [], "stages": [],
          "matches": [ { "id": "m1", "status": "pending", "streamUrl": "https://twitch.tv/suomiow" }, { "id": "m2", "status": "pending", "streamUrl": "  " } ] }
        """;

        var matches = SnapshotParser.FromBroadcast(json, "https://x.test").Matches;

        Assert.Equal("https://twitch.tv/suomiow", matches[0].StreamUrl);
        Assert.Null(matches[1].StreamUrl);
    }

    [Fact]
    public void The_legacy_response_carries_the_stream_link_and_the_times_it_has()
    {
        var json = """
        { "tournament": { "id": "t" }, "teams": [],
          "matches": [ { "id": "m1", "status": "completed", "stream_url": "https://twitch.tv/suomiow", "started_at": 100, "completed_at": 200 } ] }
        """;

        var m = SnapshotParser.FromLegacy(json, "https://x.test").Matches[0];

        Assert.Equal(("https://twitch.tv/suomiow", 100L, 200L), (m.StreamUrl, m.StartedAt, m.CompletedAt));
    }

    // ---- upcoming --------------------------------------------------------------------------------------

    [Fact]
    public void Upcoming_lists_only_matches_with_a_stream_link()
    {
        var state = With(new[]
        {
            Upcoming("streamed", "A", "B", 1000),
            Upcoming("no-link", "C", "D", 500, stream: ""),
        });

        Assert.Equal(new[] { "streamed" }, Ids(ScheduleOverlay.Upcoming(state)));
    }

    [Fact]
    public void The_filter_text_must_be_in_the_stream_link_whatever_its_case()
    {
        var entries = new[]
        {
            Upcoming("ours", "A", "B", 1000, "https://www.twitch.tv/SuomiOW"),
            Upcoming("elsewhere", "C", "D", 2000, "https://youtube.com/watch?v=1"),
        };

        var filtered = With(entries, new WaitingSettings { ScheduleStreamFilter = " twitch.tv/suomiow " });
        var any = With(entries);

        Assert.Equal(new[] { "ours" }, Ids(ScheduleOverlay.Upcoming(filtered)));
        Assert.Equal(new[] { "ours", "elsewhere" }, Ids(ScheduleOverlay.Upcoming(any)));
    }

    [Fact]
    public void Matches_in_play_come_first_then_the_soonest_and_the_unscheduled_last()
    {
        var live = Upcoming("live", "A", "B", 5000) with { Status = ScheduleStatus.Live, StartedAt = 4000 };
        var state = With(new[]
        {
            Upcoming("unscheduled", "A", "B", 0),
            Upcoming("later", "A", "B", 9000),
            live,
            Upcoming("soon", "A", "B", 6000),
            Done("finished", "A", "B", 1, 0, 100) with { StreamUrl = Twitch },
        });

        Assert.Equal(new[] { "live", "soon", "later", "unscheduled" }, Ids(ScheduleOverlay.Upcoming(state)));
    }

    [Fact]
    public void An_asap_match_is_placed_at_its_target_time_among_the_scheduled_ones()
    {
        var state = With(new[]
        {
            Upcoming("no-time", "A", "B", 0),
            Upcoming("asap-late", "A", "B", 0) with { Asap = true, TargetAt = 8000 },
            Upcoming("scheduled-9", "A", "B", 9000),
            Upcoming("asap-early", "A", "B", 0) with { Asap = true, TargetAt = 3000 },
            Upcoming("scheduled-5", "A", "B", 5000),
            Upcoming("asap-no-target", "A", "B", 0) with { Asap = true },
        }, new WaitingSettings { ScheduleRows = 8 });

        Assert.Equal(new[] { "asap-early", "scheduled-5", "asap-late", "scheduled-9", "asap-no-target", "no-time" }, Ids(ScheduleOverlay.Upcoming(state)));
    }

    [Fact]
    public void Without_a_time_the_stage_comes_before_the_round_so_group_matches_list_before_the_final()
    {
        var state = With(new[]
        {
            Upcoming("final", "A", "B", 0, round: 1) with { StageOrder = 3 },
            Upcoming("group-r3", "A", "B", 0, round: 3) with { StageOrder = 0 },
            Upcoming("group-r2", "A", "B", 0, round: 2) with { StageOrder = 0 },
            Upcoming("scheduled-playoff", "A", "B", 5000, round: 2) with { StageOrder = 3 },
        });

        Assert.Equal(new[] { "scheduled-playoff", "group-r2", "group-r3", "final" }, Ids(ScheduleOverlay.Upcoming(state)));
    }

    [Fact]
    public void A_match_in_which_no_team_is_known_is_left_out_unless_the_tab_asks_for_it()
    {
        var entries = new[]
        {
            Upcoming("final", "", "", 0),
            Upcoming("half-known", "Alpha", "", 0, round: 2),
        };

        Assert.Equal(new[] { "half-known" }, Ids(ScheduleOverlay.Upcoming(With(entries))));
        Assert.Equal(new[] { "half-known", "final" }.OrderBy(x => x), Ids(ScheduleOverlay.Upcoming(With(entries, new WaitingSettings { ScheduleShowUnknown = true }))).OrderBy(x => x));
    }

    [Fact]
    public void The_number_of_rows_limits_a_page_and_is_kept_in_range()
    {
        var entries = Enumerable.Range(1, 12).Select(i => Upcoming($"m{i}", "A", "B", i * 1000)).ToArray();

        Assert.Equal(2, ScheduleOverlay.Upcoming(With(entries, new WaitingSettings { ScheduleRows = 2 })).Count);
        Assert.Equal(8, ScheduleOverlay.Upcoming(With(entries, new WaitingSettings { ScheduleRows = 50 })).Count);
        Assert.Single(ScheduleOverlay.Upcoming(With(entries, new WaitingSettings { ScheduleRows = 0 })));
    }

    // ---- results ---------------------------------------------------------------------------------------

    [Fact]
    public void Results_are_the_newest_finished_matches_streamed_or_not()
    {
        var state = With(new[]
        {
            Done("old", "A", "B", 2, 0, 1000),
            Done("new", "C", "D", 1, 2, 3000),
            Done("mid", "E", "F", 2, 2, 2000),
            Upcoming("open", "G", "H", 4000),
        }, new WaitingSettings { ScheduleRows = 2 });

        Assert.Equal(new[] { "new", "mid" }, Ids(ScheduleOverlay.Results(state)));
    }

    // ---- what the overlay gets -------------------------------------------------------------------------

    [Fact]
    public void The_overlay_json_names_unknown_teams_tbd_and_labels_the_round()
    {
        var state = With(new[]
        {
            Upcoming("u", "Alpha", "", 1_760_000_000_000) with { Group = "A", RoundName = "Round 2" },
            Done("d", "Alpha", "Bravo", 2, 1, 10) with { RoundName = "Final" },
        }, new WaitingSettings { ScheduleRows = 3, ScheduleSeconds = 1 });

        var json = ScheduleOverlay.BuildJson(state);

        Assert.True(json["enabled"]!.GetValue<bool>());
        Assert.Equal((3, 5), (json["rows"]!.GetValue<int>(), json["seconds"]!.GetValue<int>()));   // seconds are kept at 5 or more
        Assert.Equal(Defaults.ScheduleVideoSeconds, json["video_seconds"]!.GetValue<int>());      // the video plays alone for a minute by default
        Assert.Equal(10, ScheduleOverlay.VideoSecondsOf(new WaitingSettings { ScheduleVideoSeconds = 1 }));
        Assert.Equal(600, ScheduleOverlay.VideoSecondsOf(new WaitingSettings { ScheduleVideoSeconds = 5000 }));
        var next = json["upcoming"]!.AsArray()[0]!.AsObject();
        Assert.Equal(("Alpha", "TBD", false, 1_760_000_000_000L, "Group A · Round 2"),
            (next["team1"]!.GetValue<string>(), next["team2"]!.GetValue<string>(), next["live"]!.GetValue<bool>(), next["scheduled_at"]!.GetValue<long>(), next["label"]!.GetValue<string>()));
        var result = json["results"]!.AsArray()[0]!.AsObject();
        Assert.Equal((2, 1, "Final"), (result["score1"]!.GetValue<int>(), result["score2"]!.GetValue<int>(), result["label"]!.GetValue<string>()));
    }

    [Fact]
    public void The_overlay_json_says_asap_only_for_a_match_without_a_time_and_gives_the_logos_the_overlay_can_load()
    {
        var state = With(new[]
        {
            Upcoming("asap", "Alpha", "Bravo", 0) with { Asap = true, TargetAt = 7000, Team1Logo = "C:/data/Scoreboard/TeamLogos/a.png", Team2Logo = null },
            Upcoming("timed", "Alpha", "Bravo", 9000) with { Asap = true, TargetAt = 7000 },
        });

        var json = ScheduleOverlay.BuildJson(state, p => p is null ? null : p.Replace("C:/data/Scoreboard/", ""));

        var asap = json["upcoming"]![0]!.AsObject();
        var timed = json["upcoming"]![1]!.AsObject();
        Assert.Equal((true, 7000L, "TeamLogos/a.png"), (asap["asap"]!.GetValue<bool>(), asap["target_at"]!.GetValue<long>(), asap["team1_logo"]!.GetValue<string>()));
        Assert.Null(asap["team2_logo"]);
        Assert.Equal((false, 0L), (timed["asap"]!.GetValue<bool>(), timed["target_at"]!.GetValue<long>()));   // a set time wins over ASAP
    }

    [Fact]
    public void Mapper_knows_asap_the_stage_order_and_the_downloaded_logos()
    {
        var snapshot = new TournamentSnapshot
        {
            Stages = new[] { new LinkStage("s1", "Groups", "group_stage", 3), new LinkStage("s2", "Playoffs", "single_elimination", 5) },
            Teams = new[]
            {
                new LinkTeam("a", "Alpha", null, null, "https://x.test/a.png", Array.Empty<LinkPlayer>()),
                new LinkTeam("b", "Bravo", null, null, null, Array.Empty<LinkPlayer>()),
            },
            Matches = new[]
            {
                new LinkMatch("g", "s1", "group", "A", 2, 1, "a", "b", null, null, null, "pending", null, null, 3, ScheduleAsap: true, TargetAt: 123),
                new LinkMatch("f", "s2", "main", null, 1, 1, null, null, null, null, null, "pending", null, null, 5),
            },
        };

        var entries = ScheduleMapper.FromSnapshot(snapshot, url => url == "https://x.test/a.png" ? "C:/logos/a.png" : null);

        Assert.Equal((true, 123L, "C:/logos/a.png", (string?)null), (entries[0].Asap, entries[0].TargetAt, entries[0].Team1Logo, entries[0].Team2Logo));
        Assert.True(entries[0].StageOrder < entries[1].StageOrder);   // groups before the playoffs
    }

    [Fact]
    public void Both_responses_carry_asap_and_the_target_time()
    {
        var broadcast = SnapshotParser.FromBroadcast("""{ "tournament": { "id": "t" }, "matches": [ { "id": "m", "scheduleAsap": true, "targetAt": 555 }, { "id": "n" } ] }""", "https://x.test").Matches;
        var legacy = SnapshotParser.FromLegacy("""{ "tournament": { "id": "t" }, "matches": [ { "id": "m", "schedule_asap": 1, "target_at": 555 }, { "id": "n", "schedule_asap": 0, "target_at": null } ] }""", "https://x.test").Matches;

        foreach (var matches in new[] { broadcast, legacy })
        {
            Assert.Equal((true, 555L), (matches[0].ScheduleAsap, matches[0].TargetAt));
            Assert.Equal((false, (long?)null), (matches[1].ScheduleAsap, matches[1].TargetAt));
        }
    }

    [Fact]
    public void The_overlay_state_carries_the_schedule_but_the_stream_links_stay_here()
    {
        var state = With(new[] { Upcoming("u", "Alpha", "Bravo", 1000) });
        Directory.CreateDirectory(_tmp);
        var exporter = new ScoreboardExporter(Path.Combine(_tmp, "Scoreboard"), new SkiaLogoConverter(), legacyFiles: false);

        var root = JsonNode.Parse(OverlayStateJson.Build(state, exporter, 1, 0))!.AsObject();

        Assert.Single(root["schedule"]!["upcoming"]!.AsArray());
        Assert.DoesNotContain("twitch", root.ToJsonString());
        Assert.False(root["match"]!.AsObject().ContainsKey("schedule"));   // not part of the legacy match shape
        Assert.False(root["match"]!["waiting"]!.AsObject().ContainsKey("schedule_rows"));
    }

    // ---- which parts changed ---------------------------------------------------------------------------

    [Fact]
    public void A_change_of_what_is_listed_reports_the_schedule_key()
    {
        var before = With(new[] { Upcoming("u", "Alpha", "Bravo", 1000) });

        var moved = before with { Schedule = new[] { Upcoming("u", "Alpha", "Bravo", 2000) } };
        var farOff = before with { Schedule = before.Schedule.Append(Upcoming("far", "C", "D", 9000, "")).ToArray() };   // no stream link: not listed
        var filtered = before with { Waiting = before.Waiting with { ScheduleStreamFilter = "youtube" } };
        var off = before with { Waiting = before.Waiting with { ScheduleEnabled = false } };
        var unknownTeams = With(new[] { Upcoming("tbd", "", "", 0) });
        var withUnknown = unknownTeams with { Waiting = unknownTeams.Waiting with { ScheduleShowUnknown = true } };
        var logo = before with { Schedule = new[] { Upcoming("u", "Alpha", "Bravo", 1000) with { Team1Logo = "TeamLogos/a.png" } } };

        Assert.Contains("schedule", StateDiff.ChangedKeys(unknownTeams, withUnknown));
        Assert.Contains("schedule", StateDiff.ChangedKeys(before, logo));
        Assert.Contains("schedule", StateDiff.ChangedKeys(before, moved));
        Assert.DoesNotContain("schedule", StateDiff.ChangedKeys(before, farOff));
        Assert.Contains("schedule", StateDiff.ChangedKeys(before, filtered));
        Assert.Contains("schedule", StateDiff.ChangedKeys(before, off));
        Assert.DoesNotContain("schedule", StateDiff.ChangedKeys(before, before with { }));
    }

    [Fact]
    public void An_old_save_without_the_schedule_loads_with_the_defaults()
    {
        var state = StateStore.Deserialize("""{ "waiting": { "text_brb": "BRB" } }""");

        Assert.Empty(state.Schedule);
        Assert.Equal((true, Defaults.ScheduleRows, Defaults.ScheduleSeconds, ""),
            (state.Waiting.ScheduleEnabled, state.Waiting.ScheduleRows, state.Waiting.ScheduleSeconds, state.Waiting.ScheduleStreamFilter));
    }

    [Fact]
    public void The_schedule_survives_a_save()
    {
        var state = With(new[] { Upcoming("u", "Alpha", "Bravo", 1000) }, new WaitingSettings { ScheduleStreamFilter = "twitch.tv/x" });

        var back = StateStore.Deserialize(StateStore.Serialize(state));

        Assert.Equal(("u", "Alpha", Twitch, "twitch.tv/x"), (back.Schedule[0].Id, back.Schedule[0].Team1, back.Schedule[0].StreamUrl, back.Waiting.ScheduleStreamFilter));
    }
}

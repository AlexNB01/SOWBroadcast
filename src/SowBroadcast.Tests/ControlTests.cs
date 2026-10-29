using System.Net;
using System.Text.Json;
using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Control;
using SowBroadcast.Core.Model;
using SowBroadcast.Server;

namespace SowBroadcast.Tests;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+F1", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "F1")]
    [InlineData("shift + ctrl + a", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, "A")]
    [InlineData("Win+1", HotkeyModifiers.Win, "D1")]
    [InlineData("Ctrl+PgUp", HotkeyModifiers.Ctrl, "PageUp")]
    [InlineData("Alt+numpad7", HotkeyModifiers.Alt, "NumPad7")]
    public void Gestures_are_read_leniently(string text, HotkeyModifiers modifiers, string key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+Banana")]
    public void Nonsense_is_not_a_gesture(string text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Fact]
    public void A_gesture_is_written_in_one_canonical_form_and_maps_to_the_windows_codes()
    {
        Assert.True(HotkeyGesture.TryParse("alt+ctrl+f12", out var gesture));

        Assert.Equal("Ctrl+Alt+F12", gesture.ToString());
        Assert.Equal(0x7B, gesture.VirtualKey);
        Assert.Equal(3, (int)gesture.Modifiers);   // MOD_ALT | MOD_CONTROL, as RegisterHotKey wants it
        Assert.Equal(0x41, HotkeyGesture.TryParse("Ctrl+A", out var a) ? a.VirtualKey : -1);
    }

    [Theory]
    [InlineData("Ctrl+Alt+F1", true)]
    [InlineData("F5", true)]
    [InlineData("NumPad1", true)]
    [InlineData("A", false)]
    [InlineData("Shift+A", false)]
    [InlineData("Win+A", true)]
    public void A_bare_letter_is_not_safe_as_a_global_hotkey(string text, bool safe)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(safe, gesture.IsSafeAsGlobal);
    }
}

public sealed class ControlSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-control-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    [Fact]
    public void The_defaults_bind_the_main_commands_to_distinct_safe_keys()
    {
        var settings = new ControlSettings();

        var active = settings.ActiveBindings();

        Assert.Equal(13, active.Count);
        Assert.Equal(active.Count, active.Select(a => a.Gesture).Distinct().Count());
        Assert.Empty(settings.Problems());
        Assert.Equal("Ctrl+Alt+F1", settings.GestureFor(ControlCatalog.Team1ScoreUp));
        Assert.Null(settings.GestureFor(ControlCatalog.TimerStart));
    }

    [Fact]
    public void A_users_gesture_replaces_the_default_and_an_empty_one_removes_it()
    {
        var settings = new ControlSettings();
        settings.Bindings[ControlCatalog.Swap] = "Ctrl+Shift+S";
        settings.Bindings[ControlCatalog.Update] = "";

        Assert.Equal("Ctrl+Shift+S", settings.GestureFor(ControlCatalog.Swap));
        Assert.Null(settings.GestureFor(ControlCatalog.Update));
        Assert.DoesNotContain(settings.ActiveBindings(), b => b.Id == ControlCatalog.Update);
    }

    [Fact]
    public void Problems_name_shared_gestures_unknown_keys_and_unsafe_global_keys()
    {
        var settings = new ControlSettings();
        settings.Bindings[ControlCatalog.Swap] = "Ctrl+Alt+F1";            // shared with team 1 +1
        settings.Bindings[ControlCatalog.Update] = "Ctrl+Banana";          // not a key
        settings.Bindings[ControlCatalog.MapNext] = "G";                   // bare letter, global

        var problems = settings.Problems();

        Assert.Contains(problems, p => p.Contains("both use Ctrl+Alt+F1"));
        Assert.Contains(problems, p => p.Contains("not a key combination"));
        Assert.Contains(problems, p => p.Contains("needs Ctrl, Alt or Win"));
        settings.GlobalHotkeys = false;
        Assert.DoesNotContain(settings.Problems(), p => p.Contains("needs Ctrl, Alt or Win"));
    }

    [Fact]
    public void The_token_and_bindings_survive_saving_and_a_damaged_file_starts_over()
    {
        var path = Path.Combine(_dir, "control.json");
        var first = new ControlSettings { GlobalHotkeys = false };
        first.Bindings[ControlCatalog.Swap] = "Ctrl+Shift+S";
        first.Save(path);

        var again = ControlSettings.Load(path);

        Assert.Equal(first.Token, again.Token);
        Assert.False(again.GlobalHotkeys);
        Assert.Equal("Ctrl+Shift+S", again.GestureFor(ControlCatalog.Swap));
        Assert.Equal(32, first.Token.Length);

        File.WriteAllText(path, "{ not json");
        Assert.Equal(32, ControlSettings.Load(path).Token.Length);
        Assert.NotEqual(first.Token, ControlSettings.Load(path).Token);
    }
}

public sealed class ControlCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-ctl-" + Guid.NewGuid().ToString("N"));
    private readonly MainWindowViewModelTests.FakeDialogs _dialogs = new();
    private readonly MainWindowViewModelTests.FakePublisher _publisher = new();
    private long _now;

    public ControlCommandTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private MainWindowViewModel Create() => new(_dialogs, _dialogs, _publisher, Path.Combine(_dir, "autosave.json"),
        defaultVideosDir: Path.Combine(_dir, "Highlights"), clock: () => _now, epochMs: () => 1_760_000_000_000 + _now);

    [Fact]
    public void Scores_go_up_and_down_within_their_limits_and_the_overlays_wait_for_Update()
    {
        var vm = Create();

        var up = vm.RunControl(ControlCatalog.Team1ScoreUp);
        vm.RunControl(ControlCatalog.Team1ScoreUp);
        var other = vm.RunControl(ControlCatalog.Team2ScoreUp);
        var below = vm.RunControl(ControlCatalog.Team2ScoreDown);
        var again = vm.RunControl(ControlCatalog.Team2ScoreDown);

        Assert.True(up.Ok);
        Assert.Equal(1, up.Team1Score);
        Assert.Equal(1, other.Team2Score);
        Assert.True(below.Ok);
        Assert.False(again.Ok);                       // already 0
        Assert.Equal(2, again.Team1Score);
        Assert.Equal(0, again.Team2Score);
        Assert.True(again.Unsent);
        Assert.Empty(_publisher.Published);           // KYS-9: only Update sends
        Assert.Equal(2, vm.State.Team1.Score);        // the autosave path collected the tabs
    }

    [Fact]
    public void Show_schedule_tells_the_waiting_screens_when_there_are_matches_to_show_and_says_so_when_there_are_none()
    {
        var vm = Create();

        var none = vm.RunControl(ControlCatalog.ScheduleShow);
        vm.State = vm.State with { Schedule = new[] { new ScheduleEntry { Id = "a", Team1 = "Alpha", Team2 = "Bravo", StreamUrl = "https://twitch.tv/x", Status = ScheduleStatus.Upcoming } } };
        var shown = vm.RunControl(ControlCatalog.ScheduleShow);
        vm.State = vm.State with { Waiting = vm.State.Waiting with { ScheduleEnabled = false } };
        var off = vm.RunControl(ControlCatalog.ScheduleShow);

        Assert.False(none.Ok);
        Assert.True(shown.Ok);
        Assert.False(off.Ok);
        Assert.Equal(1, _publisher.ScheduleShows);    // only the one that had something to show
        Assert.Empty(_publisher.Published);           // it is not an Update: nothing else is sent
    }

    [Fact]
    public void Update_sends_what_the_commands_changed()
    {
        var vm = Create();
        vm.RunControl(ControlCatalog.Team1ScoreUp);

        var outcome = vm.RunControl(ControlCatalog.Update);

        Assert.True(outcome.Ok);
        Assert.False(outcome.Unsent);
        Assert.Equal(1, Assert.Single(_publisher.Published).Team1.Score);
    }

    [Fact]
    public void The_current_map_is_the_first_open_one_and_next_map_finishes_it()
    {
        var vm = Create();
        vm.Match.MapsCount = 3;

        var first = vm.RunControl(ControlCatalog.MapTeam1Up);
        Assert.Equal(1, first.CurrentMap);
        vm.RunControl(ControlCatalog.MapTeam1Up);
        var finished = vm.RunControl(ControlCatalog.MapNext);
        var second = vm.RunControl(ControlCatalog.MapTeam2Up);

        Assert.Equal(2, vm.Match.Maps[0].T1);
        Assert.True(vm.Match.Maps[0].Completed);
        Assert.Contains("map 2 is next", finished.Message);
        Assert.Equal(2, second.CurrentMap);
        Assert.Equal(1, vm.Match.Maps[1].T2);
    }

    [Fact]
    public void Next_map_after_the_last_map_says_there_is_nothing_open()
    {
        var vm = Create();
        vm.Match.MapsCount = 1;
        vm.RunControl(ControlCatalog.MapNext);

        var outcome = vm.RunControl(ControlCatalog.MapNext);

        Assert.False(outcome.Ok);
        Assert.Null(outcome.CurrentMap);
        Assert.Equal("No open map", outcome.Message);
    }

    [Fact]
    public void Swap_exchanges_the_teams_and_marks_the_match_unsent()
    {
        var vm = Create();
        vm.Match.Team1.Name = "Alpha";
        vm.Match.Team2.Name = "Beta";

        var outcome = vm.RunControl(ControlCatalog.Swap);

        Assert.True(outcome.Ok);
        Assert.Equal("Beta", vm.Match.Team1.Name);
        Assert.True(vm.HasUnsentChanges);
    }

    [Fact]
    public void The_countdown_toggles_and_is_published_at_once()
    {
        var vm = Create();
        vm.Waiting.Timer.Set(300);

        var started = vm.RunControl(ControlCatalog.TimerToggle);
        _now += 10_000;
        var paused = vm.RunControl(ControlCatalog.TimerToggle);
        var plus = vm.RunControl(ControlCatalog.TimerPlusMinute);

        Assert.True(started.TimerRunning);
        Assert.False(paused.TimerRunning);
        Assert.Equal("04:50", paused.Timer);
        Assert.Equal("05:50", plus.Timer);
        Assert.Contains(_publisher.Parts, p => p.Scope == PublishScope.Timer);
    }

    [Fact]
    public void An_unknown_command_changes_nothing()
    {
        var vm = Create();

        var outcome = vm.RunControl("explode");

        Assert.False(outcome.Ok);
        Assert.Contains("Unknown command", outcome.Message);
        Assert.False(vm.HasUnsentChanges);
    }

    [Fact]
    public async Task The_http_host_runs_commands_reports_the_match_and_maps_failures_to_status_codes()
    {
        var vm = Create();
        var host = new ControlHost(vm, new ControlSettings());

        var done = await host.HandleAsync(ControlCatalog.Team1ScoreUp, new Dictionary<string, string>());
        var refused = await host.HandleAsync(ControlCatalog.Team2ScoreDown, new Dictionary<string, string>());
        var unknown = await host.HandleAsync("nope", new Dictionary<string, string>());
        var sent = await host.HandleAsync(ControlCatalog.Team1ScoreUp, new Dictionary<string, string> { ["publish"] = "1" });
        var list = await host.HandleAsync("", new Dictionary<string, string>());

        Assert.Equal(200, done.Status);
        using (var doc = JsonDocument.Parse(done.Json))
        {
            Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(1, doc.RootElement.GetProperty("team1").GetProperty("score").GetInt32());
            Assert.Equal(ControlCatalog.Team1ScoreUp, doc.RootElement.GetProperty("command").GetString());
        }
        Assert.Equal(409, refused.Status);
        Assert.Equal(404, unknown.Status);
        Assert.Equal(200, sent.Status);
        Assert.Equal(2, Assert.Single(_publisher.Published).Team1.Score);
        using var listing = JsonDocument.Parse(list.Json);
        Assert.Equal(ControlCatalog.All.Count, listing.RootElement.GetProperty("commands").GetArrayLength());
        Assert.Equal("Ctrl+Alt+F1", listing.RootElement.GetProperty("commands")[0].GetProperty("hotkey").GetString());
    }
}

public sealed class ControlEndpointTests : IAsyncLifetime
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ControlGateway _gateway = new() { Token = "tok" };
    private OverlayServer _server = null!;

    public async Task InitializeAsync() =>
        _server = await OverlayServer.StartAsync(new OverlayServerOptions { Port = 0, Control = _gateway });

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        _http.Dispose();
    }

    [Fact]
    public async Task Calls_need_the_token_as_a_header_or_a_query_value()
    {
        _gateway.Handler = (command, _) => Task.FromResult(new ControlReply(200, "{\"command\":\"" + command + "\"}"));

        var none = await _http.GetAsync(_server.BaseUrl + "/api/control/swap");
        var wrong = await _http.GetAsync(_server.BaseUrl + "/api/control/swap?token=nope");
        var query = await _http.GetAsync(_server.BaseUrl + "/api/control/swap?token=tok");
        var header = new HttpRequestMessage(HttpMethod.Post, _server.BaseUrl + "/api/control/timer.toggle");
        header.Headers.Add("X-SOWB-Token", "tok");
        var viaHeader = await _http.SendAsync(header);

        Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, query.StatusCode);
        Assert.Equal("{\"command\":\"swap\"}", await query.Content.ReadAsStringAsync());
        Assert.Equal("{\"command\":\"timer.toggle\"}", await viaHeader.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Query_values_reach_the_handler_without_the_token_and_the_listing_has_an_empty_command()
    {
        IReadOnlyDictionary<string, string>? seen = null;
        string? command = null;
        _gateway.Handler = (c, args) =>
        {
            command = c;
            seen = args;
            return Task.FromResult(new ControlReply(409, "{}"));
        };

        var response = await _http.GetAsync(_server.BaseUrl + "/api/control?token=tok&publish=1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);   // the handler decides the status
        Assert.Equal("", command);
        Assert.Equal("1", seen!["publish"]);
        Assert.DoesNotContain("token", seen.Keys);
    }

    [Fact]
    public async Task Without_a_handler_the_interface_is_not_ready_yet()
    {
        _gateway.Handler = null;

        var response = await _http.GetAsync(_server.BaseUrl + "/api/control/swap?token=tok");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task A_new_token_takes_effect_at_once()
    {
        _gateway.Handler = (_, _) => Task.FromResult(new ControlReply(200, "{}"));
        _gateway.Token = "fresh";

        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync(_server.BaseUrl + "/api/control/swap?token=tok")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync(_server.BaseUrl + "/api/control/swap?token=fresh")).StatusCode);
    }
}

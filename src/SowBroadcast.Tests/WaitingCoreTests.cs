using SowBroadcast.App.Services;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Waiting;
using SowBroadcast.Server;

namespace SowBroadcast.Tests;

public sealed class CountdownTimerTests
{
    private long _now;

    private CountdownTimer Timer() => new(() => _now);

    [Fact]
    public void Adjusting_a_stopped_timer_moves_the_preset_too_and_never_goes_below_zero()
    {
        var timer = Timer();

        timer.Adjust(60);
        Assert.Equal((60, 60), (timer.Remaining, timer.Preset));

        timer.Adjust(-120);
        Assert.Equal((0, 0), (timer.Remaining, timer.Preset));
    }

    [Fact]
    public void A_running_timer_counts_down_whole_seconds_and_stops_at_zero()
    {
        var timer = Timer();
        timer.Set(10);

        Assert.True(timer.Start());
        Assert.Equal(10, timer.Remaining);
        _now = 500;
        Assert.Equal(10, timer.Remaining); // a second has not fully passed
        _now = 1000;
        Assert.Equal(9, timer.Remaining);
        _now = 9_500;
        Assert.Equal(1, timer.Remaining);
        _now = 10_000;
        Assert.Equal(0, timer.Remaining);
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public void Pause_keeps_what_is_left_and_Start_goes_on_from_there()
    {
        var timer = Timer();
        timer.Set(60);
        timer.Start();
        _now = 20_000;

        Assert.True(timer.Pause());
        _now = 100_000;
        Assert.Equal(40, timer.Remaining);
        Assert.False(timer.Pause()); // nothing to pause

        timer.Start();
        _now = 110_000;
        Assert.Equal(30, timer.Remaining);
    }

    [Fact]
    public void Reset_returns_to_the_preset_and_adjusting_a_running_timer_leaves_the_preset_alone()
    {
        var timer = Timer();
        timer.Set(300);
        timer.Start();
        _now = 60_000;

        timer.Adjust(30);
        Assert.Equal(270, timer.Remaining);
        Assert.Equal(300, timer.Preset);

        timer.Reset();
        Assert.False(timer.IsRunning);
        Assert.Equal(300, timer.Remaining);
    }

    [Fact]
    public void Starting_from_zero_restarts_from_the_preset()
    {
        var timer = Timer();
        timer.Set(5);
        timer.Start();
        _now = 5_000;
        Assert.Equal(0, timer.Remaining);

        Assert.True(timer.Start());
        Assert.Equal(5, timer.Remaining);
    }

    [Fact]
    public void A_timer_with_nothing_to_count_does_not_start()
    {
        var timer = Timer();

        Assert.False(timer.Start());
        Assert.False(timer.IsRunning);
    }

    [Fact]
    public void Setting_a_value_stops_the_timer()
    {
        var timer = Timer();
        timer.Set(30);
        timer.Start();

        timer.Set(45);

        Assert.False(timer.IsRunning);
        Assert.Equal((45, 45), (timer.Remaining, timer.Preset));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(59, "00:59")]
    [InlineData(600, "10:00")]
    [InlineData(6000, "100:00")]
    [InlineData(-5, "00:00")]
    public void Minutes_are_not_capped(int seconds, string expected) =>
        Assert.Equal(expected, CountdownTimer.Format(seconds));
}

public sealed class SocialHandlesTests
{
    [Theory]
    [InlineData("twitch", "https://www.twitch.tv/SuomiOW?x=1", "SuomiOW")]
    [InlineData("twitch", "twitch.tv/SuomiOW", "SuomiOW")]
    [InlineData("twitter", "@handle", "handle")]
    [InlineData("twitter", "https://x.com/SuomiOW/status/1", "SuomiOW")]
    [InlineData("twitter", "http://twitter.com/SuomiOW", "SuomiOW")]
    [InlineData("youtube", "https://youtu.be/abc#t=1", "abc")]
    [InlineData("youtube", "https://www.youtube.com/@SuomiOW", "SuomiOW")]
    [InlineData("instagram", "https://www.instagram.com/suomiow/", "suomiow")]
    [InlineData("discord", "discord.gg/abc", "abc")]
    [InlineData("discord", "https://discord.com/invite/xyz", "xyz")]
    [InlineData("web", "https://suomiow.fi/path?q=1", "suomiow.fi")]
    [InlineData("web", "//a/b", "a")]
    [InlineData("web", "   ", "")]
    public void Handles_are_reduced_to_the_bare_name(string kind, string typed, string expected) =>
        Assert.Equal(expected, SocialHandles.Normalize(kind, typed));

    [Fact]
    public void Null_is_an_empty_handle() => Assert.Equal("", SocialHandles.Normalize("twitch", null));
}

public sealed class PartialPublishTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-part-" + Guid.NewGuid().ToString("N"));
    private readonly EventHub _hub = new(StateDiff.AllKeys);
    private readonly OverlayPublisher _publisher;
    private readonly ScoreboardExporter _exporter;

    public PartialPublishTests()
    {
        _exporter = new ScoreboardExporter(Path.Combine(_tmp, "Scoreboard"), new SkiaLogoConverter());
        _publisher = new OverlayPublisher(_exporter, _hub, "http://127.0.0.1:1");
    }

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private string Read(string relative) => File.ReadAllText(Path.Combine(_tmp, "Scoreboard", relative));

    [Fact]
    public void A_general_update_writes_and_announces_only_the_general_part()
    {
        var baseline = AppState.CreateDefault();
        _publisher.Publish(baseline);
        var edited = baseline with
        {
            Team1 = new Team { Name = "Not yet", ColorHex = Defaults.Team1Color },
            General = new GeneralSettings { Host = "New host", StatusText = "Cup" },
        };

        var result = _publisher.PublishPart(edited, PublishScope.General);

        Assert.Equal(new[] { "general.host", "general.status" }, result.ChangedKeys.OrderBy(k => k));
        Assert.Equal("New host", Read("General/host.txt"));
        Assert.Equal("Cup", Read("Match/status.txt"));
        Assert.Equal("", Read("Match/T1Name.txt")); // the Match tab was not updated
    }

    [Fact]
    public void A_partial_update_does_not_swallow_the_changes_of_the_other_parts()
    {
        _publisher.Publish(AppState.CreateDefault());
        var edited = AppState.CreateDefault() with
        {
            Team1 = new Team { Name = "Home", ColorHex = Defaults.Team1Color },
            General = new GeneralSettings { Host = "Host" },
        };

        _publisher.PublishPart(edited, PublishScope.General);
        var full = _publisher.Publish(edited);

        Assert.Contains("t1.name", full.ChangedKeys);
        Assert.DoesNotContain("general.host", full.ChangedKeys); // already announced
    }

    [Fact]
    public void A_waiting_update_announces_texts_socials_and_videos_keys()
    {
        _publisher.Publish(AppState.CreateDefault());
        var edited = AppState.CreateDefault() with
        {
            Waiting = new WaitingSettings
            {
                TextBrb = "BRB",
                Socials = new Dictionary<string, string> { ["twitch"] = "SuomiOW" },
            },
        };

        var result = _publisher.PublishPart(edited, PublishScope.Waiting);

        Assert.Equal(new[] { "waiting.socials", "waiting.texts" }, result.ChangedKeys.OrderBy(k => k));
        Assert.Equal("BRB", Read("Waiting/text_brb.txt"));
    }

    [Fact]
    public void A_timer_command_always_announces_the_timer_and_writes_the_files()
    {
        _publisher.Publish(AppState.CreateDefault());
        var reader = _hub.Subscribe(null).Reader;
        var state = AppState.CreateDefault() with { Waiting = new WaitingSettings { TimerSeconds = 90, TimerRunning = true } };

        var first = _publisher.PublishPart(state, PublishScope.Timer);
        var again = _publisher.PublishPart(state, PublishScope.Timer);

        Assert.Equal(new[] { "waiting.timer" }, first.ChangedKeys);
        Assert.Equal(new[] { "waiting.timer" }, again.ChangedKeys); // sent even when nothing differs
        Assert.Equal("90", Read("Waiting/timer_seconds.txt"));
        Assert.Equal("1", Read("Waiting/timer_running.txt"));
        Assert.True(reader.TryRead(out var ev));
        Assert.Equal(new[] { "waiting.timer" }, ev!.Changed);
    }

    [Fact]
    public void Partial_publishes_before_the_first_full_one_report_only_their_own_keys()
    {
        var result = _publisher.PublishPart(AppState.CreateDefault(), PublishScope.General);

        Assert.NotEmpty(result.ChangedKeys);
        Assert.All(result.ChangedKeys, k => Assert.StartsWith("general.", k));
    }
}

using SowBroadcast.App.Services;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Model;
using SowBroadcast.Server;

namespace SowBroadcast.Tests;

public sealed class OverlayPublisherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-pub-" + Guid.NewGuid().ToString("N"));
    private readonly EventHub _hub = new(StateDiff.AllKeys);
    private readonly OverlayPublisher _publisher;

    public OverlayPublisherTests()
    {
        _publisher = new OverlayPublisher(new ScoreboardExporter(Path.Combine(_dir, "Scoreboard"), new SkiaLogoConverter()), _hub, "http://127.0.0.1:1");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private static AppState State(string team1 = "Alpha", int score = 0) =>
        AppState.CreateDefault() with { Team1 = new Team { Name = team1, Score = score, ColorHex = "#55aaff" } };

    [Fact]
    public void First_publish_writes_files_and_reports_every_key()
    {
        var result = _publisher.Publish(State());

        Assert.Equal(StateDiff.AllKeys, result.ChangedKeys);
        Assert.NotEmpty(result.WrittenFiles);
        Assert.Equal("Alpha", File.ReadAllText(Path.Combine(_dir, "Scoreboard", "Match", "T1Name.txt")));
        Assert.Equal(1, _hub.LastId);
    }

    [Fact]
    public void Later_publishes_report_only_what_changed_and_stay_silent_when_nothing_did()
    {
        _publisher.Publish(State());

        var scored = _publisher.Publish(State(score: 1));
        var same = _publisher.Publish(State(score: 1));

        Assert.Equal(new[] { "matchtext", "t1.score" }, scored.ChangedKeys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Empty(same.ChangedKeys);
        Assert.Equal(2, _hub.LastId);
    }

    [Fact]
    public void Derived_fields_are_recomputed_before_publishing()
    {
        var state = State() with
        {
            General = new GeneralSettings { MapsCount = 3 },
            Maps = AppState.DefaultMaps().Select(m => m.Index == 1 ? m with { Completed = true } : m).ToArray(),
        };

        _publisher.Publish(state);

        Assert.Equal("2", File.ReadAllText(Path.Combine(_dir, "Scoreboard", "Match", "CurrentMap.txt")));
    }

    [Fact]
    public void File_only_publisher_writes_files_without_an_event_stream()
    {
        var publisher = new FileOnlyPublisher(new ScoreboardExporter(Path.Combine(_dir, "Scoreboard2"), new SkiaLogoConverter()));

        var result = publisher.Publish(State());

        Assert.Null(publisher.BaseUrl);
        Assert.Empty(result.ChangedKeys);
        Assert.True(File.Exists(Path.Combine(_dir, "Scoreboard2", "Match", "T1Name.txt")));
    }
}

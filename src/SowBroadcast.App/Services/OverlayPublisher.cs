using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Model;
using SowBroadcast.Server;

namespace SowBroadcast.App.Services;

public sealed record PublishResult(IReadOnlyList<string> ChangedKeys, IReadOnlyList<string> WrittenFiles);

/// <summary>What a partial publish writes and announces.</summary>
public enum PublishScope
{
    /// <summary>Update (General): the general files and the status text.</summary>
    General,

    /// <summary>Update (Waiting): the waiting-screen files and the status text.</summary>
    Waiting,

    /// <summary>Update + sort: the standings file.</summary>
    Standings,

    /// <summary>Update (Bracket): the bracket file.</summary>
    Bracket,

    /// <summary>A timer command (start, pause, reset, adjust): the countdown files, announced as <c>waiting.timer</c> at once.</summary>
    Timer,

    /// <summary>The running clock: the countdown files only, nothing is announced (the overlays read them on their own).</summary>
    TimerTick,
}

/// <summary>Sends state to the overlays: writes the files, works out what changed and tells connected overlays.</summary>
public interface IOverlayPublisher
{
    string? BaseUrl { get; }
    PublishResult Publish(AppState state);

    /// <summary>Publishes one part of the state. The rest of what was last published stays as it was, so later diffs stay right.</summary>
    PublishResult PublishPart(AppState state, PublishScope scope);

    /// <summary>Asset edits are written to disk at once but only announced to overlays with the next Update.</summary>
    void MarkAssetsChanged(AssetCategory category);
}

public sealed class OverlayPublisher : IOverlayPublisher
{
    private readonly ScoreboardExporter _exporter;
    private readonly EventHub _hub;
    private readonly HashSet<string> _pendingAssetKeys = new();
    private AppState? _lastPublished;

    public OverlayPublisher(ScoreboardExporter exporter, EventHub hub, string? baseUrl)
    {
        _exporter = exporter;
        _hub = hub;
        BaseUrl = baseUrl;
    }

    public string? BaseUrl { get; }

    /// <summary>The first call after startup reports every key, so overlays that were already open refresh.</summary>
    public PublishResult Publish(AppState state)
    {
        var derived = StateDerivations.Apply(state);
        var written = _exporter.ExportAll(derived);
        var keys = StateDiff.ChangedKeys(_lastPublished, derived).Concat(_pendingAssetKeys).Distinct().ToArray();
        _pendingAssetKeys.Clear();
        _lastPublished = derived;
        _hub.Publish(keys);
        return new PublishResult(keys, written);
    }

    public PublishResult PublishPart(AppState state, PublishScope scope)
    {
        var derived = StateDerivations.Apply(state);
        var written = WritePart(_exporter, derived, scope);

        List<string> keys;
        if (_lastPublished is null)
        {
            keys = StateDiff.AllKeys.Where(k => InScope(scope, k)).ToList();
        }
        else
        {
            var candidate = Merge(_lastPublished, derived, scope);
            keys = StateDiff.ChangedKeys(_lastPublished, candidate).Where(k => InScope(scope, k)).ToList();
            _lastPublished = candidate;
        }

        if (scope == PublishScope.Timer && !keys.Contains("waiting.timer")) keys.Add("waiting.timer");
        if (scope != PublishScope.TimerTick) _hub.Publish(keys);
        return new PublishResult(keys, written);
    }

    internal static List<string> WritePart(ScoreboardExporter exporter, AppState derived, PublishScope scope)
    {
        var written = new List<string>();
        switch (scope)
        {
            case PublishScope.General:
                exporter.ExportGeneral(derived, written);
                exporter.ExportStatus(derived, written);
                break;
            case PublishScope.Waiting:
                exporter.ExportWaiting(derived, written);
                exporter.ExportStatus(derived, written);
                break;
            case PublishScope.Standings:
                exporter.ExportStandings(derived, written);
                break;
            case PublishScope.Bracket:
                exporter.ExportBracket(derived, written);
                break;
            default:
                exporter.ExportTimer(derived, written);
                break;
        }
        return written;
    }

    private static bool InScope(PublishScope scope, string key) => scope switch
    {
        PublishScope.General => key.StartsWith("general.", StringComparison.Ordinal),
        PublishScope.Waiting => key.StartsWith("waiting.", StringComparison.Ordinal) || key == "matchtext",
        PublishScope.Standings => key == "standings",
        PublishScope.Bracket => key == "bracket",
        _ => key == "waiting.timer",
    };

    /// <summary>The last published state with only the published part replaced.</summary>
    private static AppState Merge(AppState last, AppState now, PublishScope scope) => scope switch
    {
        PublishScope.General => last with { General = now.General },
        PublishScope.Waiting => last with { Waiting = now.Waiting },
        PublishScope.Standings => last with { Standings = now.Standings },
        PublishScope.Bracket => last with { Bracket = now.Bracket },
        _ => last with { Waiting = last.Waiting with { TimerSeconds = now.Waiting.TimerSeconds, TimerRunning = now.Waiting.TimerRunning } },
    };

    public void MarkAssetsChanged(AssetCategory category) => _pendingAssetKeys.Add("assets." + category.Key());
}

/// <summary>Used when the local server could not start: files are still written so OBS text sources keep working.</summary>
public sealed class FileOnlyPublisher : IOverlayPublisher
{
    private readonly ScoreboardExporter _exporter;
    public FileOnlyPublisher(ScoreboardExporter exporter) => _exporter = exporter;
    public string? BaseUrl => null;

    public PublishResult Publish(AppState state) =>
        new(Array.Empty<string>(), _exporter.ExportAll(StateDerivations.Apply(state)));

    public PublishResult PublishPart(AppState state, PublishScope scope) =>
        new(Array.Empty<string>(), OverlayPublisher.WritePart(_exporter, StateDerivations.Apply(state), scope));

    public void MarkAssetsChanged(AssetCategory category) { }
}

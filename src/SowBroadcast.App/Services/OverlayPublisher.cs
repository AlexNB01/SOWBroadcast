using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Model;
using SowBroadcast.Server;

namespace SowBroadcast.App.Services;

public sealed record PublishResult(IReadOnlyList<string> ChangedKeys, IReadOnlyList<string> WrittenFiles);

/// <summary>Sends state to the overlays: writes the files, works out what changed and tells connected overlays.</summary>
public interface IOverlayPublisher
{
    string? BaseUrl { get; }
    PublishResult Publish(AppState state);

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

    public void MarkAssetsChanged(AssetCategory category) { }
}

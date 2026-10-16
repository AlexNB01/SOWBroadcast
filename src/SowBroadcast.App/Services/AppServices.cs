using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Storage;
using SowBroadcast.Server;

namespace SowBroadcast.App.Services;

/// <summary>Starts the local overlay server and the exporter; one place that knows the folder layout.</summary>
public sealed class AppServices : IAsyncDisposable
{
    private readonly OverlayServer? _server;

    private AppServices(IOverlayPublisher publisher, AssetService assets, OverlayServer? server, string? startupError)
    {
        Publisher = publisher;
        Assets = assets;
        _server = server;
        StartupError = startupError;
        assets.Changed += publisher.MarkAssetsChanged;
    }

    public IOverlayPublisher Publisher { get; }

    /// <summary>Heroes, maps and game modes.</summary>
    public AssetService Assets { get; }

    /// <summary>Set when the server could not start (the app still works and writes files).</summary>
    public string? StartupError { get; }

    public static AppServices Start()
    {
        var exporter = new ScoreboardExporter(AppPaths.ScoreboardDir, new SkiaLogoConverter(), AppPaths.DefaultHighlightsDir);
        var content = AppPaths.ContentRoot;
        var data = AppPaths.DataRoot;

        // The server only serves folders that exist when it starts, and generated files must win over shipped ones.
        foreach (var folder in new[] { AppPaths.ScoreboardDir, Path.Combine(data, "Music"), Path.Combine(data, "Videos"), Path.Combine(data, "Highlights") })
            Directory.CreateDirectory(folder);

        var folders = new Dictionary<string, IReadOnlyList<string>>
        {
            ["/HTML"] = new[] { Path.Combine(content, "HTML") },
            // Generated overlay data comes only from the per-user folder, so stale files next to the program never leak in.
            ["/Scoreboard"] = new[] { AppPaths.ScoreboardDir },
            ["/Music"] = new[] { Path.Combine(data, "Music"), Path.Combine(content, "Music") },
            ["/Videos"] = new[] { Path.Combine(data, "Videos"), Path.Combine(content, "Videos") },
            ["/Highlights"] = new[] { Path.Combine(data, "Highlights"), Path.Combine(content, "Highlights") },
        };

        // Shipped, read-only asset folders: user additions (data) win over the bundled files.
        foreach (var asset in new[] { "Heroes", "HeroesBody", "Maps", "Gametypes", "Roles", "General/Icons" })
        {
            var userFolder = Path.Combine(AppPaths.ScoreboardDir, asset);
            Directory.CreateDirectory(userFolder);
            folders["/Scoreboard/" + asset] = new[] { userFolder, Path.Combine(content, "Scoreboard", asset) };
        }

        var assets = new AssetService(AppPaths.ScoreboardDir, new[] { Path.Combine(content, "Scoreboard") });
        assets.Load();

        var options = new OverlayServerOptions
        {
            FullRefreshKeys = StateDiff.AllKeys,
            StaticFolders = folders,
        };

        try
        {
            var server = Task.Run(() => OverlayServer.StartAsync(options)).GetAwaiter().GetResult();
            return new AppServices(new OverlayPublisher(exporter, server.Hub, server.BaseUrl), assets, server, null);
        }
        catch (IOException ex)
        {
            return new AppServices(new FileOnlyPublisher(exporter), assets, null, ex.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null) await _server.DisposeAsync();
    }
}

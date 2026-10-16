using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Replay;
using SowBroadcast.Core.Storage;
using SowBroadcast.Integrations.Link;
using SowBroadcast.Server;

namespace SowBroadcast.App.Services;

/// <summary>Starts the local overlay server and the exporter; one place that knows the folder layout.</summary>
public sealed class AppServices : IAsyncDisposable
{
    private readonly OverlayServer? _server;

    private readonly HttpFetcher _http;

    private AppServices(IOverlayPublisher publisher, AssetService assets, OverlayServer? server, string? startupError, HttpFetcher http)
    {
        Publisher = publisher;
        Assets = assets;
        _http = http;
        Link = new LinkService(http, () => new SocketIoConnection());
        Replay = new ReplayWatcher(Path.Combine(AppPaths.ScoreboardDir, "Replay"));
        Replay.ReplayReady += _ => Publisher.AnnounceReplay();
        Logos = new LogoCache(Path.Combine(AppPaths.ScoreboardDir, "TeamLogos"), http);
        _server = server;
        StartupError = startupError;
        assets.Changed += publisher.MarkAssetsChanged;
    }

    public IOverlayPublisher Publisher { get; }

    /// <summary>Takes the clips OBS' replay buffer saves into the playlist the replay overlay plays.</summary>
    public ReplayWatcher Replay { get; }

    /// <summary>The SOWDraft connection.</summary>
    public LinkService Link { get; }

    /// <summary>Team logos downloaded from SOWDraft (served to the overlays with the rest of the Scoreboard folder).</summary>
    public LogoCache Logos { get; }

    /// <summary>Heroes, maps and game modes.</summary>
    public AssetService Assets { get; }

    /// <summary>Set when the server could not start (the app still works and writes files).</summary>
    public string? StartupError { get; }

    public static AppServices Start()
    {
        // The old flat text/JSON files are not written any more: the overlays read /api/state.
        var exporter = new ScoreboardExporter(AppPaths.ScoreboardDir, new SkiaLogoConverter(), AppPaths.DefaultHighlightsDir, legacyFiles: false);
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

        var http = new HttpFetcher(AppInfo.Version);
        var assets = new AssetService(AppPaths.ScoreboardDir, new[] { Path.Combine(content, "Scoreboard") });
        assets.Load();

        // What /api/state serves; starts as an empty match and is rebuilt after every publish.
        var stateHolder = new OverlayStateHolder
        {
            Json = OverlayStateJson.Build(AppState.CreateDefault(), exporter, 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
        };
        var options = new OverlayServerOptions
        {
            FullRefreshKeys = StateDiff.AllKeys,
            StaticFolders = folders,
            StateProvider = () => stateHolder.Json,
        };

        try
        {
            var server = Task.Run(() => OverlayServer.StartAsync(options)).GetAwaiter().GetResult();
            Serilog.Log.Information("Overlay server listening on {Url}", server.BaseUrl);
            return new AppServices(new OverlayPublisher(exporter, server.Hub, server.BaseUrl, stateHolder), assets, server, null, http);
        }
        catch (IOException ex)
        {
            Serilog.Log.Error(ex, "The overlay server could not start");
            return new AppServices(new FileOnlyPublisher(exporter), assets, null, ex.Message, http);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Replay.Dispose();
        await Link.DisposeAsync();
        _http.Dispose();
        if (_server is not null) await _server.DisposeAsync();
    }
}

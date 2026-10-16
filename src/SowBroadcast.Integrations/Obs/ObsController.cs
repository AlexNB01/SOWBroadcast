using System.Text.Json;

namespace SowBroadcast.Integrations.Obs;

public enum CheckLevel { Ok, Warning, Problem }

/// <summary>One line of the "go live" checklist.</summary>
public sealed record CheckResult(CheckLevel Level, string Title, string Detail);

/// <summary>What the checklist compares OBS against.</summary>
/// <param name="BaseUrl">The overlay server's address (null when it could not start).</param>
/// <param name="ExpectedScenes">The scene names the program's scene collection has.</param>
/// <param name="OverlayPages">The overlay pages (<c>scoreboard.html</c>...) a browser source may point at.</param>
/// <param name="ReplayFolder">The folder the replay watcher looks in.</param>
public sealed record ChecklistInput(string? BaseUrl, IReadOnlyList<string> ExpectedScenes, IReadOnlyCollection<string> OverlayPages, string ReplayFolder);

/// <summary>
/// What the program does with OBS (EHD-6): follows the scenes, switches scene, starts/stops the replay buffer and saves a replay,
/// and checks that OBS is set up for the program before going live.
/// </summary>
public sealed class ObsController : IAsyncDisposable
{
    private readonly ObsClient _client;
    private List<string> _scenes = new();

    public ObsController(ObsClient? client = null)
    {
        _client = client ?? new ObsClient();
        _client.EventReceived += OnEvent;
        _client.Disconnected += reason =>
        {
            LastProblem = reason;
            Changed?.Invoke();
        };
    }

    public bool IsConnected => _client.IsConnected;
    public string ObsVersion => _client.ObsVersion;
    public IReadOnlyList<string> Scenes => _scenes;
    public string CurrentScene { get; private set; } = "";
    public bool ReplayBufferActive { get; private set; }
    public bool IsStreaming { get; private set; }
    public bool IsRecording { get; private set; }

    /// <summary>Why the connection ended, when OBS or the network ended it (cleared by the next connect).</summary>
    public string? LastProblem { get; private set; }

    /// <summary>Anything that is shown changed (scenes, current scene, outputs, connection). Raised on a background thread.</summary>
    public event Action? Changed;

    /// <summary>OBS saved a replay; the argument is the file it wrote.</summary>
    public event Action<string>? ReplaySaved;

    public async Task ConnectAsync(string host, int port, string? password, CancellationToken cancellation = default)
    {
        LastProblem = null;
        await _client.ConnectAsync(host, port, password, cancellation).ConfigureAwait(false);
        await RefreshAsync(cancellation).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        await _client.DisconnectAsync().ConfigureAwait(false);
        _scenes = new List<string>();
        CurrentScene = "";
        ReplayBufferActive = IsStreaming = IsRecording = false;
        Changed?.Invoke();
    }

    /// <summary>Reads the scenes and the state of the outputs.</summary>
    public async Task RefreshAsync(CancellationToken cancellation = default)
    {
        var list = await _client.RequestAsync("GetSceneList", null, cancellation).ConfigureAwait(false);
        // OBS lists scenes bottom-up; the program shows them in the order of OBS's own scene list.
        _scenes = list.GetProperty("scenes").EnumerateArray()
            .OrderBy(s => s.TryGetProperty("sceneIndex", out var i) ? i.GetInt32() : 0)
            .Select(s => s.GetProperty("sceneName").GetString() ?? "").Where(n => n.Length > 0).Reverse().ToList();
        CurrentScene = list.TryGetProperty("currentProgramSceneName", out var current) ? current.GetString() ?? "" : "";

        ReplayBufferActive = await OutputActiveAsync("GetReplayBufferStatus", cancellation).ConfigureAwait(false);
        IsStreaming = await OutputActiveAsync("GetStreamStatus", cancellation).ConfigureAwait(false);
        IsRecording = await OutputActiveAsync("GetRecordStatus", cancellation).ConfigureAwait(false);
        Changed?.Invoke();
    }

    private async Task<bool> OutputActiveAsync(string request, CancellationToken cancellation)
    {
        try
        {
            var status = await _client.RequestAsync(request, null, cancellation).ConfigureAwait(false);
            return status.TryGetProperty("outputActive", out var active) && active.GetBoolean();
        }
        catch (ObsException) { return false; }   // for example a replay buffer that is not switched on in OBS
    }

    private void OnEvent(string type, JsonElement data)
    {
        switch (type)
        {
            case "CurrentProgramSceneChanged":
                CurrentScene = data.TryGetProperty("sceneName", out var scene) ? scene.GetString() ?? "" : CurrentScene;
                break;
            case "SceneListChanged":
                _ = Task.Run(async () => { try { await RefreshAsync().ConfigureAwait(false); } catch (ObsException) { /* gone again */ } });
                return;
            case "ReplayBufferStateChanged":
                ReplayBufferActive = data.TryGetProperty("outputActive", out var replay) && replay.GetBoolean();
                break;
            case "StreamStateChanged":
                IsStreaming = data.TryGetProperty("outputActive", out var stream) && stream.GetBoolean();
                break;
            case "RecordStateChanged":
                IsRecording = data.TryGetProperty("outputActive", out var record) && record.GetBoolean();
                break;
            case "ReplayBufferSaved":
                if (data.TryGetProperty("savedReplayPath", out var path) && path.GetString() is { Length: > 0 } saved) ReplaySaved?.Invoke(saved);
                return;
            default:
                return;
        }
        Changed?.Invoke();
    }

    // ---- actions -----------------------------------------------------------------------------------------

    public async Task SwitchSceneAsync(string sceneName, CancellationToken cancellation = default)
    {
        await _client.RequestAsync("SetCurrentProgramScene", new { sceneName }, cancellation).ConfigureAwait(false);
        CurrentScene = sceneName;
        Changed?.Invoke();
    }

    public async Task StartReplayBufferAsync(CancellationToken cancellation = default)
    {
        await _client.RequestAsync("StartReplayBuffer", null, cancellation).ConfigureAwait(false);
        ReplayBufferActive = true;
        Changed?.Invoke();
    }

    public async Task StopReplayBufferAsync(CancellationToken cancellation = default)
    {
        await _client.RequestAsync("StopReplayBuffer", null, cancellation).ConfigureAwait(false);
        ReplayBufferActive = false;
        Changed?.Invoke();
    }

    /// <summary>Saves the replay buffer (OBS writes the clip; the replay watcher then picks it up). Starts the buffer first when it is not running.</summary>
    public async Task SaveReplayAsync(CancellationToken cancellation = default)
    {
        if (!ReplayBufferActive) throw new ObsException("The replay buffer is not running. Start it first.");
        await _client.RequestAsync("SaveReplayBuffer", null, cancellation).ConfigureAwait(false);
    }

    // ---- go-live checklist -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<CheckResult>> RunChecklistAsync(ChecklistInput input, CancellationToken cancellation = default)
    {
        var results = new List<CheckResult>();
        if (!IsConnected)
        {
            results.Add(new CheckResult(CheckLevel.Problem, "OBS", "Not connected to OBS."));
            return results;
        }
        results.Add(new CheckResult(CheckLevel.Ok, "OBS", $"Connected to OBS Studio {ObsVersion}."));

        results.Add(input.BaseUrl is null
            ? new CheckResult(CheckLevel.Problem, "Overlay server", "The overlay server did not start, so browser sources have nothing to show.")
            : new CheckResult(CheckLevel.Ok, "Overlay server", $"Running at {input.BaseUrl}."));

        await RefreshAsync(cancellation).ConfigureAwait(false);
        var missing = input.ExpectedScenes.Where(s => !_scenes.Contains(s, StringComparer.Ordinal)).ToArray();
        results.Add(missing.Length == 0
            ? new CheckResult(CheckLevel.Ok, "Scenes", $"All {input.ExpectedScenes.Count} scenes of the program's collection are in OBS.")
            : new CheckResult(CheckLevel.Problem, "Scenes", $"Missing in OBS: {string.Join(", ", missing)}. Import the program's scene collection (Obs menu > Export scene collection)."));

        results.Add(await CheckBrowserSourcesAsync(input, cancellation).ConfigureAwait(false));
        results.AddRange(await CheckReplayAsync(input, cancellation).ConfigureAwait(false));

        if (IsStreaming) results.Add(new CheckResult(CheckLevel.Ok, "Stream", "OBS is streaming."));
        else results.Add(new CheckResult(CheckLevel.Warning, "Stream", "OBS is not streaming yet."));
        return results;
    }

    private async Task<CheckResult> CheckBrowserSourcesAsync(ChecklistInput input, CancellationToken cancellation)
    {
        try
        {
            var list = await _client.RequestAsync("GetInputList", new { inputKind = "browser_source" }, cancellation).ConfigureAwait(false);
            var wrongServer = new List<string>();
            var oldPages = new List<string>();
            var count = 0;
            foreach (var input1 in list.GetProperty("inputs").EnumerateArray())
            {
                var name = input1.GetProperty("inputName").GetString() ?? "";
                var settings = await _client.RequestAsync("GetInputSettings", new { inputName = name }, cancellation).ConfigureAwait(false);
                var url = settings.GetProperty("inputSettings").TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                if (!url.Contains("/HTML/", StringComparison.OrdinalIgnoreCase)) continue;   // not one of the overlays
                count++;
                if (input.BaseUrl is not null && !url.StartsWith(input.BaseUrl + "/", StringComparison.OrdinalIgnoreCase)) wrongServer.Add(name);
                else if (PageOf(url) is { } page && !input.OverlayPages.Contains(page, StringComparer.Ordinal)) oldPages.Add($"{name} ({page})");
            }

            if (wrongServer.Count > 0)
                return new CheckResult(CheckLevel.Problem, "Browser sources", $"These point at another address than {input.BaseUrl}: {string.Join(", ", wrongServer)}.");
            if (oldPages.Count > 0)
                return new CheckResult(CheckLevel.Problem, "Browser sources", $"These use overlay pages that no longer exist: {string.Join(", ", oldPages)}.");
            return count == 0
                ? new CheckResult(CheckLevel.Warning, "Browser sources", "OBS has no browser source for the program's overlays.")
                : new CheckResult(CheckLevel.Ok, "Browser sources", $"{count} overlay browser sources point at the program.");
        }
        catch (Exception ex) when (ex is ObsException or KeyNotFoundException or InvalidOperationException)
        {
            return new CheckResult(CheckLevel.Warning, "Browser sources", "Could not read OBS's browser sources: " + ex.Message);
        }
    }

    // The page of an overlay address: "http://127.0.0.1:8324/HTML/waiting.html?mode=brb" -> "waiting.html".
    private static string? PageOf(string url)
    {
        var at = url.IndexOf("/HTML/", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var page = url[(at + 6)..];
        var end = page.IndexOfAny(new[] { '?', '#' });
        return end >= 0 ? page[..end] : page;
    }

    private async Task<IEnumerable<CheckResult>> CheckReplayAsync(ChecklistInput input, CancellationToken cancellation)
    {
        var results = new List<CheckResult>();
        try
        {
            var status = await _client.RequestAsync("GetReplayBufferStatus", null, cancellation).ConfigureAwait(false);
            var active = status.TryGetProperty("outputActive", out var a) && a.GetBoolean();
            results.Add(active
                ? new CheckResult(CheckLevel.Ok, "Replay buffer", "The replay buffer is running.")
                : new CheckResult(CheckLevel.Warning, "Replay buffer", "The replay buffer is switched on but not running. Start it from the OBS window of this program."));
        }
        catch (ObsException)
        {
            results.Add(new CheckResult(CheckLevel.Problem, "Replay buffer", "The replay buffer is not enabled. Turn it on in OBS: Settings > Output > Replay Buffer."));
            return results;
        }

        try
        {
            var folder = await _client.RequestAsync("GetRecordDirectory", null, cancellation).ConfigureAwait(false);
            var recordDirectory = folder.TryGetProperty("recordDirectory", out var d) ? d.GetString() ?? "" : "";
            results.Add(SameFolder(recordDirectory, input.ReplayFolder)
                ? new CheckResult(CheckLevel.Ok, "Replay folder", "OBS saves clips where the program looks for them.")
                : new CheckResult(CheckLevel.Warning, "Replay folder", $"OBS saves clips to {recordDirectory}, the program looks in {input.ReplayFolder} (Settings > Output > Recording Path)."));
        }
        catch (ObsException)
        {
            results.Add(new CheckResult(CheckLevel.Warning, "Replay folder", "Could not read OBS's recording folder."));
        }
        return results;
    }

    private static bool SameFolder(string a, string b)
    {
        static string Norm(string path) => path.Trim().Replace('\\', '/').TrimEnd('/');
        return string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync().ConfigureAwait(false);
}

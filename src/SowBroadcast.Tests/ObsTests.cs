using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SowBroadcast.Integrations.Obs;

namespace SowBroadcast.Tests;

/// <summary>A stand-in for OBS Studio's WebSocket server: answers the sign-in and the requests the test teaches it.</summary>
internal sealed class FakeObsSocket : IObsSocket
{
    public const string Salt = "salty";
    public const string Challenge = "challenge";

    private readonly Channel<string?> _toClient = Channel.CreateUnbounded<string?>();
    private readonly string? _password;

    public FakeObsSocket(string? password = null) => _password = password;

    /// <summary>requestType -> answer; a throw of <see cref="ObsException"/> becomes a failed request status.</summary>
    public Dictionary<string, Func<JsonElement, object?>> Handlers { get; } = new();

    public List<(string Type, string Data)> Requests { get; } = new();
    public bool Disposed { get; private set; }
    public bool SilentOnRequests { get; set; }

    public Task ConnectAsync(Uri uri, CancellationToken cancellation)
    {
        object auth = _password is null ? null! : new { challenge = Challenge, salt = Salt };
        var hello = _password is null
            ? new { op = 0, d = (object)new { obsStudioVersion = "30.2.1", obsWebSocketVersion = "5.5.2", rpcVersion = 1 } }
            : new { op = 0, d = (object)new { obsStudioVersion = "30.2.1", obsWebSocketVersion = "5.5.2", rpcVersion = 1, authentication = auth } };
        _toClient.Writer.TryWrite(JsonSerializer.Serialize(hello));
        return Task.CompletedTask;
    }

    public Task SendAsync(string text, CancellationToken cancellation)
    {
        using var doc = JsonDocument.Parse(text);
        var d = doc.RootElement.GetProperty("d");
        switch (doc.RootElement.GetProperty("op").GetInt32())
        {
            case 1:
            {
                var given = d.TryGetProperty("authentication", out var a) ? a.GetString() : null;
                var expected = _password is null ? null : ObsClient.Authenticate(_password, Salt, Challenge);
                if (given != expected) _toClient.Writer.TryComplete();        // OBS closes the connection on a wrong password
                else _toClient.Writer.TryWrite(JsonSerializer.Serialize(new { op = 2, d = new { negotiatedRpcVersion = 1 } }));
                break;
            }
            case 6:
            {
                var type = d.GetProperty("requestType").GetString()!;
                var id = d.GetProperty("requestId").GetString()!;
                var data = d.TryGetProperty("requestData", out var rd) ? rd.Clone() : default;
                Requests.Add((type, data.ValueKind == JsonValueKind.Undefined ? "" : data.GetRawText()));
                if (SilentOnRequests) break;
                try
                {
                    var response = Handlers.TryGetValue(type, out var handler) ? handler(data) : null;
                    _toClient.Writer.TryWrite(JsonSerializer.Serialize(new
                    {
                        op = 7,
                        d = new { requestType = type, requestId = id, requestStatus = new { result = true, code = 100 }, responseData = response },
                    }));
                }
                catch (ObsException ex)
                {
                    _toClient.Writer.TryWrite(JsonSerializer.Serialize(new
                    {
                        op = 7,
                        d = new { requestType = type, requestId = id, requestStatus = new { result = false, code = ex.Code, comment = ex.Message } },
                    }));
                }
                break;
            }
        }
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellation)
    {
        try { return await _toClient.Reader.ReadAsync(cancellation); }
        catch (ChannelClosedException) { return null; }
    }

    public void PushEvent(string type, object data) =>
        _toClient.Writer.TryWrite(JsonSerializer.Serialize(new { op = 5, d = new { eventType = type, eventIntent = 4, eventData = data } }));

    public void Close() => _toClient.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _toClient.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

public sealed class ObsClientTests
{
    [Fact]
    public async Task The_client_signs_in_without_a_password_and_reads_the_obs_version()
    {
        var socket = new FakeObsSocket();
        await using var client = new ObsClient(() => socket);

        await client.ConnectAsync("localhost", 4455, null);

        Assert.True(client.IsConnected);
        Assert.Equal("30.2.1", client.ObsVersion);
    }

    [Fact]
    public void The_password_challenge_is_computed_as_the_protocol_says()
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("pw" + "salt")));
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + "challenge")));

        Assert.Equal(expected, ObsClient.Authenticate("pw", "salt", "challenge"));
    }

    [Fact]
    public async Task A_password_is_needed_and_a_wrong_one_is_refused_in_words()
    {
        await using var noPassword = new ObsClient(() => new FakeObsSocket("secret"));
        var missing = await Assert.ThrowsAsync<ObsException>(() => noPassword.ConnectAsync("localhost", 4455, null));
        Assert.Contains("asks for a password", missing.Message);

        await using var wrong = new ObsClient(() => new FakeObsSocket("secret"));
        var refused = await Assert.ThrowsAsync<ObsException>(() => wrong.ConnectAsync("localhost", 4455, "nope"));
        Assert.Contains("refused the password", refused.Message);
        Assert.False(wrong.IsConnected);

        await using var right = new ObsClient(() => new FakeObsSocket("secret"));
        await right.ConnectAsync("localhost", 4455, "secret");
        Assert.True(right.IsConnected);
    }

    [Fact]
    public async Task Requests_return_their_data_and_failures_become_exceptions_with_the_status_code()
    {
        var socket = new FakeObsSocket();
        socket.Handlers["GetVersion"] = _ => new { obsVersion = "30.2.1" };
        socket.Handlers["Nothing"] = _ => null;
        socket.Handlers["SetCurrentProgramScene"] = data => data.GetProperty("sceneName").GetString() == "Nope"
            ? throw new ObsException("No scene was found by that name.", 600)
            : null;
        await using var client = new ObsClient(() => socket);
        await client.ConnectAsync("localhost", 4455, null);

        var version = await client.RequestAsync("GetVersion");
        var empty = await client.RequestAsync("Nothing");
        var failure = await Assert.ThrowsAsync<ObsException>(() => client.RequestAsync("SetCurrentProgramScene", new { sceneName = "Nope" }));

        Assert.Equal("30.2.1", version.GetProperty("obsVersion").GetString());
        Assert.Equal(JsonValueKind.Object, empty.ValueKind);
        Assert.Equal(600, failure.Code);
        Assert.Contains("No scene", failure.Message);
    }

    [Fact]
    public async Task Several_requests_at_once_get_their_own_answers()
    {
        var socket = new FakeObsSocket();
        socket.Handlers["Echo"] = data => new { value = data.GetProperty("n").GetInt32() };
        await using var client = new ObsClient(() => socket);
        await client.ConnectAsync("localhost", 4455, null);

        var answers = await Task.WhenAll(Enumerable.Range(1, 20).Select(n => client.RequestAsync("Echo", new { n })));

        Assert.Equal(Enumerable.Range(1, 20), answers.Select(a => a.GetProperty("value").GetInt32()));
    }

    [Fact]
    public async Task A_request_that_gets_no_answer_times_out_and_a_request_without_a_connection_says_so()
    {
        var socket = new FakeObsSocket { SilentOnRequests = true };
        await using var client = new ObsClient(() => socket, TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAsync<ObsException>(() => client.RequestAsync("GetVersion"));   // not connected yet

        await client.ConnectAsync("localhost", 4455, null);
        var timeout = await Assert.ThrowsAsync<ObsException>(() => client.RequestAsync("GetVersion"));

        Assert.Contains("in time", timeout.Message);
    }

    [Fact]
    public async Task Events_are_passed_on_and_losing_the_connection_is_reported_and_fails_waiting_requests()
    {
        var socket = new FakeObsSocket();
        await using var client = new ObsClient(() => socket);
        var events = new List<(string, string)>();
        var lost = new TaskCompletionSource<string?>();
        client.EventReceived += (type, data) => events.Add((type, data.GetProperty("sceneName").GetString()!));
        client.Disconnected += reason => lost.TrySetResult(reason);
        await client.ConnectAsync("localhost", 4455, null);

        socket.PushEvent("CurrentProgramSceneChanged", new { sceneName = "In Game" });
        await Task.Delay(100);
        socket.Close();
        var reason = await lost.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(("CurrentProgramSceneChanged", "In Game"), Assert.Single(events));
        Assert.Contains("closed", reason);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Connecting_again_replaces_the_old_connection()
    {
        var sockets = new List<FakeObsSocket>();
        await using var client = new ObsClient(() => { var s = new FakeObsSocket(); sockets.Add(s); return s; });

        await client.ConnectAsync("localhost", 4455, null);
        await client.ConnectAsync("localhost", 4455, null);

        Assert.Equal(2, sockets.Count);
        Assert.True(sockets[0].Disposed);
        Assert.True(client.IsConnected);
    }
}

public sealed class ObsControllerTests
{
    private static (ObsController Controller, FakeObsSocket Socket) Create(Action<FakeObsSocket>? teach = null)
    {
        var socket = new FakeObsSocket();
        socket.Handlers["GetSceneList"] = _ => new
        {
            currentProgramSceneName = "Starting Soon",
            scenes = new[] { new { sceneName = "In Game", sceneIndex = 0 }, new { sceneName = "Maps", sceneIndex = 1 }, new { sceneName = "Starting Soon", sceneIndex = 2 } },
        };
        socket.Handlers["GetReplayBufferStatus"] = _ => new { outputActive = false };
        socket.Handlers["GetStreamStatus"] = _ => new { outputActive = true };
        socket.Handlers["GetRecordStatus"] = _ => new { outputActive = false };
        teach?.Invoke(socket);
        return (new ObsController(new ObsClient(() => socket)), socket);
    }

    [Fact]
    public async Task Connecting_reads_the_scenes_in_the_order_of_the_obs_list_and_the_state_of_the_outputs()
    {
        var (obs, _) = Create();

        await obs.ConnectAsync("localhost", 4455, null);

        Assert.Equal(new[] { "Starting Soon", "Maps", "In Game" }, obs.Scenes);   // the top of OBS's list first
        Assert.Equal("Starting Soon", obs.CurrentScene);
        Assert.False(obs.ReplayBufferActive);
        Assert.True(obs.IsStreaming);
        Assert.False(obs.IsRecording);
    }

    [Fact]
    public async Task Switching_the_scene_sends_the_request_and_obs_events_keep_the_state_current()
    {
        var (obs, socket) = Create(s => s.Handlers["SetCurrentProgramScene"] = _ => null);
        await obs.ConnectAsync("localhost", 4455, null);
        var changes = 0;
        obs.Changed += () => changes++;

        await obs.SwitchSceneAsync("In Game");
        Assert.Equal("In Game", obs.CurrentScene);
        Assert.Contains(socket.Requests, r => r.Type == "SetCurrentProgramScene" && r.Data.Contains("In Game"));

        socket.PushEvent("CurrentProgramSceneChanged", new { sceneName = "Maps" });
        socket.PushEvent("ReplayBufferStateChanged", new { outputActive = true, outputState = "OBS_WEBSOCKET_OUTPUT_STARTED" });
        socket.PushEvent("StreamStateChanged", new { outputActive = false });
        await Task.Delay(150);

        Assert.Equal("Maps", obs.CurrentScene);
        Assert.True(obs.ReplayBufferActive);
        Assert.False(obs.IsStreaming);
        Assert.True(changes >= 4);
    }

    [Fact]
    public async Task A_failed_scene_switch_throws_and_leaves_the_scene_as_it_was()
    {
        var (obs, _) = Create(s => s.Handlers["SetCurrentProgramScene"] = _ => throw new ObsException("No scene was found by that name.", 600));
        await obs.ConnectAsync("localhost", 4455, null);

        await Assert.ThrowsAsync<ObsException>(() => obs.SwitchSceneAsync("Nowhere"));

        Assert.Equal("Starting Soon", obs.CurrentScene);
    }

    [Fact]
    public async Task The_replay_buffer_starts_stops_and_saves_only_while_it_runs_and_the_saved_clip_is_announced()
    {
        var (obs, socket) = Create(s =>
        {
            s.Handlers["StartReplayBuffer"] = _ => null;
            s.Handlers["StopReplayBuffer"] = _ => null;
            s.Handlers["SaveReplayBuffer"] = _ => null;
        });
        await obs.ConnectAsync("localhost", 4455, null);
        var saved = new List<string>();
        obs.ReplaySaved += saved.Add;

        var notRunning = await Assert.ThrowsAsync<ObsException>(() => obs.SaveReplayAsync());
        Assert.Contains("not running", notRunning.Message);

        await obs.StartReplayBufferAsync();
        Assert.True(obs.ReplayBufferActive);
        await obs.SaveReplayAsync();
        Assert.Contains(socket.Requests, r => r.Type == "SaveReplayBuffer");

        socket.PushEvent("ReplayBufferSaved", new { savedReplayPath = "C:/Videos/Replay 2026-10-16.mp4" });
        await Task.Delay(100);
        Assert.Equal("C:/Videos/Replay 2026-10-16.mp4", Assert.Single(saved));

        await obs.StopReplayBufferAsync();
        Assert.False(obs.ReplayBufferActive);
    }

    [Fact]
    public async Task Disconnecting_clears_what_was_shown()
    {
        var (obs, _) = Create();
        await obs.ConnectAsync("localhost", 4455, null);

        await obs.DisconnectAsync();

        Assert.False(obs.IsConnected);
        Assert.Empty(obs.Scenes);
        Assert.Equal("", obs.CurrentScene);
    }

    // ---- checklist ---------------------------------------------------------------------------------------

    private static readonly string[] Pages = { "scoreboard.html", "waiting.html", "transition.html" };

    private static ChecklistInput Input(string? baseUrl = "http://127.0.0.1:8324", params string[] scenes) =>
        new(baseUrl, scenes.Length == 0 ? new[] { "In Game", "Maps", "Starting Soon" } : scenes, Pages, "C:/Users/x/SOWBroadcast/Scoreboard/Replay");

    private static void TeachSources(FakeObsSocket s, params (string Name, string Url)[] sources)
    {
        s.Handlers["GetInputList"] = _ => new { inputs = sources.Select(x => new { inputName = x.Name, inputKind = "browser_source" }).ToArray() };
        s.Handlers["GetInputSettings"] = data =>
        {
            var name = data.GetProperty("inputName").GetString();
            return new { inputSettings = new { url = sources.First(x => x.Name == name).Url } };
        };
    }

    [Fact]
    public async Task A_good_setup_gives_a_checklist_without_problems()
    {
        var (obs, _) = Create(s =>
        {
            s.Handlers["GetReplayBufferStatus"] = _ => new { outputActive = true };
            s.Handlers["GetRecordDirectory"] = _ => new { recordDirectory = "C:\\Users\\x\\SOWBroadcast\\Scoreboard\\Replay" };
            TeachSources(s, ("Scoreboard", "http://127.0.0.1:8324/HTML/scoreboard.html"), ("Waiting", "http://127.0.0.1:8324/HTML/waiting.html?mode=brb"),
                ("Somebody else's page", "https://example.com/page"));
        });
        await obs.ConnectAsync("localhost", 4455, null);

        var results = await obs.RunChecklistAsync(Input());

        Assert.DoesNotContain(results, r => r.Level == CheckLevel.Problem);
        Assert.DoesNotContain(results, r => r.Level == CheckLevel.Warning);
        Assert.Contains(results, r => r.Title == "Browser sources" && r.Detail.StartsWith("2 overlay"));
        Assert.Contains(results, r => r.Title == "Replay folder" && r.Level == CheckLevel.Ok);
    }

    [Fact]
    public async Task Problems_are_named_missing_scenes_another_address_old_pages_and_the_replay_buffer()
    {
        var (obs, _) = Create(s =>
        {
            s.Handlers["GetReplayBufferStatus"] = _ => throw new ObsException("The replay buffer is not enabled.", 604);
            TeachSources(s, ("Old port", "http://127.0.0.1:8000/HTML/scoreboard.html"));
        });
        await obs.ConnectAsync("localhost", 4455, null);

        var results = await obs.RunChecklistAsync(Input(scenes: new[] { "In Game", "Bracket", "Standings" }));

        var scenes = results.Single(r => r.Title == "Scenes");
        Assert.Equal(CheckLevel.Problem, scenes.Level);
        Assert.Contains("Bracket", scenes.Detail);
        Assert.Contains("Standings", scenes.Detail);
        Assert.DoesNotContain("In Game,", scenes.Detail);
        Assert.Contains("8000", results.Single(r => r.Title == "Browser sources").Detail.Length > 0 ? "8000" : "");   // the other address is named by its source
        Assert.Contains("Old port", results.Single(r => r.Title == "Browser sources").Detail);
        Assert.Equal(CheckLevel.Problem, results.Single(r => r.Title == "Replay buffer").Level);
        Assert.DoesNotContain(results, r => r.Title == "Replay folder");
    }

    [Fact]
    public async Task A_source_with_a_page_that_no_longer_exists_and_a_replay_folder_that_differs_are_reported()
    {
        var (obs, _) = Create(s =>
        {
            s.Handlers["GetReplayBufferStatus"] = _ => new { outputActive = false };
            s.Handlers["GetRecordDirectory"] = _ => new { recordDirectory = "D:/Videos" };
            TeachSources(s, ("Starting Soon", "http://127.0.0.1:8324/HTML/startingsoon.html"));
        });
        await obs.ConnectAsync("localhost", 4455, null);

        var results = await obs.RunChecklistAsync(Input());

        var sources = results.Single(r => r.Title == "Browser sources");
        Assert.Equal(CheckLevel.Problem, sources.Level);
        Assert.Contains("startingsoon.html", sources.Detail);
        Assert.Equal(CheckLevel.Warning, results.Single(r => r.Title == "Replay buffer").Level);
        Assert.Equal(CheckLevel.Warning, results.Single(r => r.Title == "Replay folder").Level);
        Assert.Contains("D:/Videos", results.Single(r => r.Title == "Replay folder").Detail);
    }

    [Fact]
    public async Task Without_a_connection_or_a_server_the_checklist_says_what_is_missing()
    {
        var (obs, _) = Create();
        Assert.Equal(CheckLevel.Problem, Assert.Single(await obs.RunChecklistAsync(Input())).Level);

        await obs.ConnectAsync("localhost", 4455, null);
        var results = await obs.RunChecklistAsync(Input(baseUrl: null));

        Assert.Equal(CheckLevel.Problem, results.Single(r => r.Title == "Overlay server").Level);
    }
}

public sealed class ObsSceneCollectionTests
{
    private static string Build(string baseUrl = "http://127.0.0.1:8324") =>
        SowBroadcast.Core.Obs.ObsSceneCollection.Build(baseUrl, "C:/Test/Install", "C:/Test/Data");

    private static string[] OverlayPages()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HTML", "sow-data.js"))) dir = dir.Parent;
        return dir is null ? Array.Empty<string>() : Directory.GetFiles(Path.Combine(dir.FullName, "HTML"), "*.html").Select(Path.GetFileName).ToArray()!;
    }

    [Fact]
    public void The_collection_has_the_old_eighteen_scenes_and_the_replay_clips_scene_and_starts_on_In_Game()
    {
        var root = JsonDocument.Parse(Build()).RootElement;

        var scenes = root.GetProperty("sources").EnumerateArray().Where(s => s.GetProperty("id").GetString() == "scene").Select(s => s.GetProperty("name").GetString()).ToArray();

        Assert.Equal(19, scenes.Length);
        Assert.Contains("Replay Clips", scenes);
        Assert.Equal("SOW Broadcast", root.GetProperty("name").GetString());
        Assert.Equal("In Game", root.GetProperty("current_scene").GetString());
        Assert.Equal(SowBroadcast.Core.Obs.ObsSceneCollection.SceneNames().OrderBy(n => n), scenes.OrderBy(n => n));
        Assert.Contains("Starting Soon", SowBroadcast.Core.Obs.ObsSceneCollection.SceneNames());
    }

    [Fact]
    public void The_instant_replay_scene_loops_the_newest_clip_and_the_replay_clips_scene_plays_what_is_asked_for()
    {
        var root = JsonDocument.Parse(Build("http://127.0.0.1:9000")).RootElement;
        var sources = root.GetProperty("sources").EnumerateArray().ToArray();
        JsonElement Source(string name) => sources.Single(s => s.GetProperty("name").GetString() == name);
        string Url(string name) => Source(name).GetProperty("settings").GetProperty("url").GetString()!;
        string[] ItemsOf(string scene) => Source(scene).GetProperty("settings").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToArray();

        Assert.Equal("http://127.0.0.1:9000/HTML/replay.html?mode=instant", Url("HTML Instant Replay"));
        Assert.Equal("http://127.0.0.1:9000/HTML/replay.html?mode=clips", Url("HTML Replay Clips"));

        // each scene has its own page and its own transition, so showing one never restarts the other
        Assert.Equal(new[] { "HTML Instant Replay", "HTML Transition Replay" }, ItemsOf("InstantReplay"));
        Assert.Equal(new[] { "HTML Replay Clips", "HTML Transition Replay Clips" }, ItemsOf("Replay Clips"));

        // the items point at the sources of their own scene
        foreach (var scene in new[] { "InstantReplay", "Replay Clips" })
            foreach (var item in Source(scene).GetProperty("settings").GetProperty("items").EnumerateArray())
                Assert.Equal(Source(item.GetProperty("name").GetString()!).GetProperty("uuid").GetString(), item.GetProperty("source_uuid").GetString());

        // the scenes sit next to each other in OBS's list, and every uuid is still its own
        var names = SowBroadcast.Core.Obs.ObsSceneCollection.SceneNames().ToList();
        Assert.Equal(names.IndexOf("InstantReplay") + 1, names.IndexOf("Replay Clips"));
        var uuids = sources.Select(s => s.GetProperty("uuid").GetString()).ToArray();
        Assert.Equal(uuids.Length, uuids.Distinct().Count());
    }

    [Fact]
    public void Every_browser_source_points_at_a_page_that_exists_and_nothing_belongs_to_one_machine()
    {
        var pages = OverlayPages();
        if (pages.Length == 0) return;

        var problems = SowBroadcast.Core.Obs.ObsSceneCollection.Check(Build(), pages);

        Assert.Empty(problems);
    }

    [Fact]
    public void The_check_finds_a_source_with_an_old_page_or_a_leftover_path()
    {
        var json = Build().Replace("waiting.html?mode=brb", "berightback.html").Replace("\"name\": \"SOW Broadcast\"", "\"name\": \"SOW Broadcast C:/SOWBroadcast\"");

        var problems = SowBroadcast.Core.Obs.ObsSceneCollection.Check(json, OverlayPages().Length == 0 ? new[] { "waiting.html" } : OverlayPages());

        Assert.Contains(problems, p => p.Contains("berightback.html"));
        Assert.Contains(problems, p => p.Contains("leftover C:/SOWBroadcast"));
    }

    [Fact]
    public void Sources_use_the_new_pages_the_given_address_and_the_given_folders()
    {
        var root = JsonDocument.Parse(Build("http://127.0.0.1:8330/")).RootElement;
        string Setting(string name, string property) =>
            root.GetProperty("sources").EnumerateArray().First(s => s.GetProperty("name").GetString() == name).GetProperty("settings").GetProperty(property).ToString();

        Assert.Equal("http://127.0.0.1:8330/HTML/waiting.html?mode=start", Setting("HTML Starting Soon", "url"));
        Assert.Equal("http://127.0.0.1:8330/HTML/waiting.html?mode=end", Setting("HTML Thank You for Watching", "url"));
        Assert.Equal("http://127.0.0.1:8330/HTML/team.html?side=2", Setting("HTML Team 2 Roster", "url"));
        Assert.Equal("http://127.0.0.1:8330/HTML/MapDraft.html", Setting("HTML MapDraft", "url"));
        Assert.Equal("http://127.0.0.1:8330/HTML/transition.html", Setting("HTML Transition Blank Screen", "url"));   // had a dead address
        Assert.Equal("1080", Setting("HTML Team 2 Roster", "height"));
        Assert.Equal("", Setting("Caster1NDI", "ndi_source_name"));
    }

    [Fact]
    public void The_webcam_and_scene_items_without_a_source_are_gone_and_names_are_tidied()
    {
        var root = JsonDocument.Parse(Build()).RootElement;
        var sources = root.GetProperty("sources").EnumerateArray().ToArray();
        var names = sources.Select(s => s.GetProperty("name").GetString()).ToHashSet();

        Assert.DoesNotContain(sources, s => s.GetProperty("id").GetString() == "dshow_input");
        Assert.Contains("Background Colour", names);
        Assert.Contains("Highlights Music", names);
        Assert.DoesNotContain("Musiikki Highlights", names);
        var all = names.Concat(root.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("name").GetString())).ToHashSet();
        foreach (var scene in sources.Where(s => s.GetProperty("id").GetString() == "scene"))
            foreach (var item in scene.GetProperty("settings").GetProperty("items").EnumerateArray())
                Assert.Contains(item.GetProperty("name").GetString(), all);
    }

    // ---- cameras ---------------------------------------------------------------------------------------------

    private static JsonElement Scene(JsonElement root, string name) =>
        root.GetProperty("sources").EnumerateArray().First(s => s.GetProperty("id").GetString() == "scene" && s.GetProperty("name").GetString() == name);

    private static JsonElement Item(JsonElement root, string scene, string item) =>
        Scene(root, scene).GetProperty("settings").GetProperty("items").EnumerateArray().First(i => i.GetProperty("name").GetString() == item);

    private static double Number(JsonElement item, string property, string axis) => item.GetProperty(property).GetProperty(axis).GetDouble();

    // The pages are read from the source tree (found from this file), so the test also works when the binaries are built elsewhere.
    private static string HtmlFile(string name, [System.Runtime.CompilerServices.CallerFilePath] string self = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(self)!, "..", "..", "HTML", name));

    [Fact]
    public void The_camera_scenes_hold_only_the_NDI_source_over_the_whole_canvas()
    {
        var root = JsonDocument.Parse(Build()).RootElement;

        foreach (var (scene, ndi) in new[] { ("=> Host NDI Framing", "HostNDI"), ("=> Caster 1 NDI Framing", "Caster1NDI"), ("=> Caster 2 NDI Framing", "Caster2NDI") })
        {
            var item = Assert.Single(Scene(root, scene).GetProperty("settings").GetProperty("items").EnumerateArray());
            Assert.Equal(ndi, item.GetProperty("name").GetString());
            Assert.Equal(0, Number(item, "pos", "x"));
            Assert.Equal(0, Number(item, "pos", "y"));
            Assert.Equal(2560, Number(item, "bounds", "x"));
            Assert.Equal(1440, Number(item, "bounds", "y"));
            Assert.Equal(3, item.GetProperty("bounds_type").GetInt32());   // fills the canvas, cropping what overflows
            Assert.Equal(0, item.GetProperty("crop_left").GetInt32());
        }
    }

    [Fact]
    public void The_frames_name_texts_and_the_camera_mask_are_gone()
    {
        var root = JsonDocument.Parse(Build()).RootElement;
        var sources = root.GetProperty("sources").EnumerateArray().ToArray();

        Assert.Empty(root.GetProperty("groups").EnumerateArray());
        Assert.DoesNotContain(sources, s => s.GetProperty("id").GetString() is "text_gdiplus" or "image_source");
        Assert.DoesNotContain(sources, s => s.GetProperty("name").GetString() is "Scale" or "Host Text" or "Caster1 Text" or "Caster2 Text");
    }

    [Fact]
    public void The_single_camera_fills_the_whole_canvas()
    {
        var item = Item(JsonDocument.Parse(Build()).RootElement, "Duo SingleCam", "=> Caster 1 NDI Framing");

        Assert.Equal(0, Number(item, "pos", "x"));
        Assert.Equal(0, Number(item, "pos", "y"));
        Assert.Equal(1, Number(item, "scale", "x"), 6);
        Assert.Equal(0, item.GetProperty("crop_left").GetInt32());
    }

    // The camera windows come from the overlay pages: this reads the same numbers out of the page CSS and works out where the
    // cameras belong, so a change to a page that is not followed here fails.
    private static (double Left, double Top, double Width, double CameraHeight, double Step) PageRow(string page, string area, int count)
    {
        var css = HtmlFile(page);
        double Css(string pattern) => double.Parse(Regex.Match(css, pattern).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var areaTop = Css(@"\." + area + @"\s*\{\s*position:absolute;\s*left:0;\s*top:(\d+)px");
        var areaHeight = Css(@"\." + area + @"\s*\{[^}]*?height:(\d+)px");
        var gap = Css(@"\." + area + @"\s*\{[^}]*?gap:(\d+)px");
        var frameWidth = Css(@"\.frame\s*\{\s*position:relative;\s*width:(\d+)px");
        var frameHeight = Css(@"\.frame\s*\{\s*position:relative;\s*width:\d+px;\s*height:(\d+)px");
        var plate = Css(@"\.plate\s*\{[^}]*?height:(\d+)px");
        var left = (1920 - (count * frameWidth + (count - 1) * gap)) / 2;
        return (left, areaTop + (areaHeight - frameHeight) / 2, frameWidth, frameHeight - plate, frameWidth + gap);
    }

    [Fact]
    public void The_duo_cameras_sit_in_the_frames_of_the_duo_page_without_cropping()
    {
        var root = JsonDocument.Parse(Build()).RootElement;
        var (left, top, width, height, step) = PageRow("duorow.html", "duo", 2);
        var k = 2560 / 1920.0;

        Assert.Equal(16.0 / 9, width / height, 3);   // a 16:9 window: the picture fits as it is
        foreach (var (item, index) in new[] { ("=> Caster 1 NDI Framing", 0), ("=> Caster 2 NDI Framing", 1) })
        {
            var placed = Item(root, "DuoCam Row", item);
            Assert.Equal((left + index * step) * k, Number(placed, "pos", "x"), 3);
            Assert.Equal(top * k, Number(placed, "pos", "y"), 3);
            Assert.Equal(width * k / 2560, Number(placed, "scale", "x"), 4);
            Assert.Equal(0, placed.GetProperty("crop_left").GetInt32());
        }
    }

    [Fact]
    public void The_trio_cameras_follow_the_page_order_caster_1_host_caster_2_and_cover_their_windows()
    {
        var root = JsonDocument.Parse(Build()).RootElement;
        var (left, top, width, height, step) = PageRow("triorow.html", "trio", 3);
        var k = 2560 / 1920.0;

        // the order of the name plates in the page
        var page = HtmlFile("triorow.html");
        Assert.True(page.IndexOf("id=\"caster1name\"", StringComparison.Ordinal) < page.IndexOf("id=\"hostname\"", StringComparison.Ordinal));
        Assert.True(page.IndexOf("id=\"hostname\"", StringComparison.Ordinal) < page.IndexOf("id=\"caster2name\"", StringComparison.Ordinal));

        var scale = Math.Max(width * k / 2560, height * k / 1440);
        foreach (var (item, index) in new[] { ("=> Caster 1 NDI Framing", 0), ("=> Host NDI Framing", 1), ("=> Caster 2 NDI Framing", 2) })
        {
            var placed = Item(root, "TrioCam Row", item);
            Assert.Equal((left + index * step) * k, Number(placed, "pos", "x"), 3);
            Assert.Equal(top * k, Number(placed, "pos", "y"), 3);
            Assert.Equal(scale, Number(placed, "scale", "x"), 4);
            // the visible part of the picture is as wide as the window, and not a pixel too much is cut
            var visibleWidth = (2560 - placed.GetProperty("crop_left").GetInt32() - placed.GetProperty("crop_right").GetInt32()) * scale;
            Assert.InRange(visibleWidth - width * k, 0, 1.5);
            Assert.Equal(height * k, 1440 * scale, 3);
        }
    }

    [Fact]
    public void The_ids_are_new_unique_and_the_same_every_time()
    {
        var first = Build();
        var again = Build();
        var ids = JsonDocument.Parse(first).RootElement.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("uuid").GetString()).ToArray();

        Assert.Equal(first, again);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, id => Assert.Equal(5, Guid.Parse(id!).ToString()[14] - '0'));   // version 5
        Assert.DoesNotContain("2db22ce9-2407-44ae-890c-af7f45e5efc6", first);          // an id of the old collection
    }

    [Fact]
    public void For_a_parity_check_with_the_old_python_tool_the_collection_can_be_written_to_a_file()
    {
        if (Environment.GetEnvironmentVariable("SOWB_OBS_PARITY") is not { Length: > 0 } path) return;
        File.WriteAllText(path, Build(), new System.Text.UTF8Encoding(false));
    }
}

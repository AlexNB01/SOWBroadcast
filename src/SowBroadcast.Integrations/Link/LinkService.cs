using System.Text.Json;
using SowBroadcast.Core.Link;

namespace SowBroadcast.Integrations.Link;

public enum LinkState
{
    NotLinked,
    Connecting,
    Linked,
    NoMatchSelected,
    WaitingForDraft,
    MatchComplete,
    Error,
}

/// <summary>The line the UI shows next to the link: <c>Text</c> is final English text (the UI adds "last update hh:mm:ss").</summary>
public sealed record LinkStatus(LinkState State, string Text);

/// <param name="SelectedMatchId">The tournament match the user chose ("" when none; "manual" for a draft room linked by hand).</param>
public sealed record LinkUpdate(TournamentSnapshot Snapshot, string SelectedMatchId, bool IsManualDraftRoom);

public sealed class LinkServiceOptions
{
    /// <summary>Silent refetch of everything, in case an event was missed (SOWDraft's events carry no sequence number on the legacy route).</summary>
    public TimeSpan ResyncInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>One more attempt after a failed fetch.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Events that arrive in bursts cause one refetch.</summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(250);

    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = (span, token) => Task.Delay(span, token);
}

/// <summary>
/// The live connection to SOWDraft (CLAUDE.md 8.4). Linked to a tournament it follows the match the user chose: through the new
/// <c>/broadcast</c> API and its <c>tournament-event</c> push when the server has it, otherwise through the legacy public REST
/// response plus the draft room's <c>update-state</c>. Either way it hands out one <see cref="TournamentSnapshot"/> (with the live draft
/// of the selected match) whenever something changed, refetches after every reconnect and every 30 seconds, and never picks a match by itself.
/// Events are raised on background threads.
/// </summary>
public sealed class LinkService : IAsyncDisposable
{
    private const int MaxJsonBytes = 8 * 1024 * 1024;
    public const string ManualMatchId = "manual";

    private readonly IHttpFetcher _http;
    private readonly Func<ISocketConnection> _sockets;
    private readonly LinkServiceOptions _options;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private ISocketConnection? _socket;
    private bool _socketConnected;
    private int _generation;

    private string _origin = "";
    private string _key = "";
    private string _tournamentId = "";
    private string _selectedMatchId = "";
    private string _manualRoomId = "";
    private bool _manual;
    private bool? _broadcast;          // null = not known yet, false = the server has no /broadcast
    private bool _joinedTournament;
    private int _connectCount;
    private Task _connectWork = Task.CompletedTask;
    private string _joinedDraft = "";
    private JsonElement? _room;        // the selected match's draft room (legacy route / manual link)
    private TournamentSnapshot? _snapshot;
    private long _version;
    private bool _refreshRunning, _refreshPending, _debouncing, _retryScheduled;

    public LinkService(IHttpFetcher http, Func<ISocketConnection> sockets, LinkServiceOptions? options = null)
    {
        _http = http;
        _sockets = sockets;
        _options = options ?? new LinkServiceOptions();
    }

    /// <summary>A new snapshot is available (also after a plain refetch with nothing new).</summary>
    public event Action<LinkUpdate>? Updated;

    public event Action<LinkStatus>? StatusChanged;

    public LinkStatus Status { get; private set; } = new(LinkState.NotLinked, "Not linked");

    public TournamentSnapshot? Snapshot => _snapshot;

    public bool IsLinked => _cts is not null;

    // ---- linking -----------------------------------------------------------------------------------------

    /// <summary>Links a tournament. <paramref name="selectedMatchId"/> restores the choice of an earlier session ("" = none yet).</summary>
    /// <exception cref="LinkException">The address cannot be used.</exception>
    public async Task LinkTournamentAsync(string url, string selectedMatchId = "")
    {
        var (origin, key) = LinkUrl.ParseTournament(url);
        await UnlinkCoreAsync(raiseStatus: false).ConfigureAwait(false);
        Begin(origin, key, selectedMatchId, manual: false, manualRoomId: "");
        await StartAsync().ConfigureAwait(false);
    }

    /// <summary>The advanced fallback: links one draft room directly (no tournament, no match list).</summary>
    /// <exception cref="LinkException">The address cannot be used.</exception>
    public async Task LinkDraftRoomAsync(string url)
    {
        var (origin, matchId) = LinkUrl.ParseDraftRoom(url);
        await UnlinkCoreAsync(raiseStatus: false).ConfigureAwait(false);
        Begin(origin, "", ManualMatchId, manual: true, manualRoomId: matchId);
        await StartAsync().ConfigureAwait(false);
    }

    private void Begin(string origin, string key, string selectedMatchId, bool manual, string manualRoomId)
    {
        lock (_lock)
        {
            _generation++;
            _cts = new CancellationTokenSource();
            _origin = origin;
            _key = key;
            _tournamentId = "";
            _selectedMatchId = selectedMatchId;
            _manual = manual;
            _manualRoomId = manualRoomId;
            _broadcast = null;
            _joinedTournament = false;
            _connectCount = 0;
            _joinedDraft = "";
            _room = null;
            _snapshot = null;
            _version = 0;
            _retryScheduled = false;
        }
    }

    private async Task StartAsync()
    {
        SetStatus(new LinkStatus(LinkState.Connecting, "Connecting…"));
        var generation = _generation;
        var token = _cts!.Token;
        await RefreshAsync().ConfigureAwait(false);   // the data does not wait for the socket
        await ConnectSocketAsync().ConfigureAwait(false);
        _ = Task.Run(() => LoopAsync(generation, token));
    }

    public Task UnlinkAsync() => UnlinkCoreAsync(raiseStatus: true);

    private async Task UnlinkCoreAsync(bool raiseStatus)
    {
        ISocketConnection? socket;
        CancellationTokenSource? cts;
        lock (_lock)
        {
            cts = _cts;
            socket = _socket;
            _cts = null;
            _socket = null;
            _socketConnected = false;
            _generation++;
            _snapshot = null;
            _room = null;
            _selectedMatchId = "";
            _manual = false;
        }
        cts?.Cancel();
        if (socket is not null) await SafeDisposeAsync(socket).ConfigureAwait(false);
        cts?.Dispose();
        if (raiseStatus) SetStatus(new LinkStatus(LinkState.NotLinked, "Not linked"));
    }

    public ValueTask DisposeAsync() => new(UnlinkCoreAsync(raiseStatus: false));

    /// <summary>Chooses the match to follow ("" = none). Nothing is ever chosen automatically.</summary>
    public async Task SelectMatchAsync(string? matchId)
    {
        if (_cts is null || _manual) return;
        lock (_lock)
        {
            _selectedMatchId = matchId ?? "";
            _room = null;
            _joinedDraft = "";
        }
        await RefreshAsync().ConfigureAwait(false);
    }

    // ---- socket ------------------------------------------------------------------------------------------

    private async Task ConnectSocketAsync()
    {
        var generation = _generation;
        var socket = _sockets();
        socket.Connected += () => OnConnected(socket, generation);
        socket.Disconnected += () => OnDisconnected(socket, generation);
        socket.Error += message => OnSocketError(generation, message);
        socket.On("update-state", room => OnDraftState(room, generation));
        socket.On("tournament-update", id => OnTournamentUpdate(id, generation));
        socket.On("tournament-event", payload => OnTournamentEvent(payload, generation));

        lock (_lock)
        {
            if (generation != _generation) return;
            _socket = socket;
        }
        try
        {
            await socket.ConnectAsync(_origin).ConfigureAwait(false);
            await _connectWork.ConfigureAwait(false);   // the rooms are joined when linking returns
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OnSocketError(generation, ex.Message);
        }
    }

    private void OnConnected(ISocketConnection socket, int generation)
    {
        bool first;
        lock (_lock)
        {
            if (generation != _generation || !ReferenceEquals(socket, _socket)) return;
            _socketConnected = true;
            _joinedTournament = false;
            _joinedDraft = "";
            first = ++_connectCount == 1;
        }
        // Join the rooms again after every (re)connect. After a reconnect everything is also fetched again, so nothing missed while
        // away stays missing; the first connection only needs a fetch when the initial one has not delivered anything yet.
        _connectWork = Task.Run(async () =>
        {
            if (_manual) { await JoinDraftAsync(_manualRoomId).ConfigureAwait(false); return; }
            if (!first || _snapshot is null) { await RefreshAsync().ConfigureAwait(false); return; }

            if (_broadcast == true) await JoinTournamentAsync().ConfigureAwait(false);
            if (_broadcast == false && _snapshot.MatchById(_selectedMatchId)?.DraftMatchId is { Length: > 0 } draftId)
                await JoinDraftAsync(draftId).ConfigureAwait(false);
        });
    }

    private void OnDisconnected(ISocketConnection socket, int generation)
    {
        lock (_lock)
        {
            if (generation != _generation || !ReferenceEquals(socket, _socket)) return;
            _socketConnected = false;
        }
        if (Status.State is LinkState.Linked or LinkState.WaitingForDraft or LinkState.NoMatchSelected or LinkState.MatchComplete)
            SetStatus(new LinkStatus(LinkState.Connecting, "Connecting…"));
    }

    private void OnSocketError(int generation, string message)
    {
        if (generation != _generation) return;
        SetStatus(new LinkStatus(LinkState.Error, $"Error: {message}"));
    }

    private async Task JoinDraftAsync(string draftId)
    {
        ISocketConnection? socket;
        lock (_lock)
        {
            if (!_socketConnected || _joinedDraft == draftId || draftId.Length == 0) return;
            _joinedDraft = draftId;
            socket = _socket;
        }
        if (socket is null) return;
        try { await socket.EmitAsync("join-match", new { matchId = draftId, role = "stream" }).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { OnSocketError(_generation, ex.Message); }
    }

    private async Task JoinTournamentAsync()
    {
        ISocketConnection? socket;
        lock (_lock)
        {
            if (!_socketConnected || _joinedTournament) return;
            _joinedTournament = true;
            socket = _socket;
        }
        if (socket is null) return;
        try { await socket.EmitAsync("join-tournament", new { tournamentId = _key }).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { OnSocketError(_generation, ex.Message); }
    }

    // ---- events from the server --------------------------------------------------------------------------

    private void OnDraftState(JsonElement room, int generation)
    {
        if (generation != _generation) return;
        var roomId = SnapshotParser.Str(room, "matchId");
        if (roomId is null) return;
        var clone = room.Clone();

        if (_manual)
        {
            if (roomId != _manualRoomId) return;
            lock (_lock) _room = clone;
            Publish(ManualSnapshot(clone), generation);
            return;
        }

        var snapshot = _snapshot;
        var match = snapshot?.MatchById(_selectedMatchId);
        if (snapshot is null || match?.DraftMatchId != roomId) return;
        lock (_lock) _room = clone;
        Publish(snapshot with { Live = LiveBuilder.Build(clone, match.Team1Id, match.Team2Id) }, generation);
    }

    private void OnTournamentUpdate(JsonElement id, int generation)
    {
        if (generation != _generation || _manual) return;
        var text = id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        // The server sends the UUID (for every tournament it runs); the link may have been made with the slug.
        if (text is null || (text != _tournamentId && text != _key)) return;
        _ = DebouncedRefreshAsync();
    }

    private void OnTournamentEvent(JsonElement payload, int generation)
    {
        if (generation != _generation || _manual) return;
        var id = SnapshotParser.Str(payload, "tournamentId");
        if (id is null || (id != _tournamentId && id != _key)) return;

        var version = SnapshotParser.Long(payload, "version") ?? 0;
        var changed = SnapshotParser.Array(payload, "changed").Select(c => c.GetString()).ToArray();
        var snapshot = _snapshot;

        // A live-only change that carries the live state and follows on from what we have needs no refetch.
        if (snapshot is not null && changed.Length > 0 && changed.All(c => c == "live") && version == _version + 1
            && SnapshotParser.Prop(payload, "live") is { ValueKind: JsonValueKind.Object } liveElement)
        {
            var live = SnapshotParser.ParseLive(liveElement);
            if (live.TournamentMatchId != _selectedMatchId) { _version = version; return; }
            _version = version;
            Publish(snapshot with { Live = live, Version = version }, generation);
            return;
        }
        _ = DebouncedRefreshAsync();
    }

    private async Task DebouncedRefreshAsync()
    {
        lock (_lock)
        {
            if (_debouncing) return;
            _debouncing = true;
        }
        try
        {
            await _options.Delay(_options.Debounce, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        finally { lock (_lock) _debouncing = false; }
        await RefreshAsync().ConfigureAwait(false);
    }

    // ---- fetching ----------------------------------------------------------------------------------------

    /// <summary>Fetches everything again. Calls that arrive while a fetch is running cause exactly one more fetch afterwards.</summary>
    public async Task RefreshAsync()
    {
        lock (_lock)
        {
            if (_cts is null) return;
            if (_refreshRunning) { _refreshPending = true; return; }
            _refreshRunning = true;
        }
        while (true)
        {
            try { await RefreshOnceAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { Fail(ex.Message); }
            lock (_lock)
            {
                if (!_refreshPending) { _refreshRunning = false; return; }
                _refreshPending = false;
            }
        }
    }

    private async Task RefreshOnceAsync()
    {
        var generation = _generation;
        var token = _cts?.Token ?? CancellationToken.None;

        if (_manual)
        {
            await JoinDraftAsync(_manualRoomId).ConfigureAwait(false);
            if (_room is { } room) Publish(ManualSnapshot(room), generation);
            else SetStatus(new LinkStatus(LinkState.WaitingForDraft, "Waiting for draft to start…"));
            return;
        }

        TournamentSnapshot snapshot;
        try
        {
            snapshot = await FetchAsync(token).ConfigureAwait(false);
        }
        catch (LinkException ex)
        {
            if (generation == _generation) Fail(ex.Message);
            return;
        }
        if (generation != _generation) return;

        _tournamentId = snapshot.Id;
        _version = snapshot.Version;
        if (_broadcast == true) await JoinTournamentAsync().ConfigureAwait(false);

        // Legacy route: the live draft comes from the draft room's own events.
        if (_broadcast == false && snapshot.MatchById(_selectedMatchId) is { DraftMatchId: { Length: > 0 } draftId } match)
        {
            await JoinDraftAsync(draftId).ConfigureAwait(false);
            if (_room is { } room && SnapshotParser.Str(room, "matchId") == draftId)
                snapshot = snapshot with { Live = LiveBuilder.Build(room, match.Team1Id, match.Team2Id) };
        }

        lock (_lock) _retryScheduled = false;
        Publish(snapshot, generation);
    }

    private async Task<TournamentSnapshot> FetchAsync(CancellationToken token)
    {
        var root = $"{_origin}/api/tournament-system/{Uri.EscapeDataString(_key)}";

        if (_broadcast != false)
        {
            var url = root + "/broadcast" + (_selectedMatchId.Length > 0 ? $"?matchId={Uri.EscapeDataString(_selectedMatchId)}" : "");
            var response = await _http.GetAsync(new Uri(url), MaxJsonBytes, token).ConfigureAwait(false);
            if (response.Status == 200)
            {
                _broadcast = true;
                return Parse(() => SnapshotParser.FromBroadcast(response.Text, _origin));
            }
            if (response.Status != 404) throw HttpError(response);
            _broadcast = false; // the server does not have the new API (yet): use the public REST response
        }

        var legacy = await _http.GetAsync(new Uri(root), MaxJsonBytes, token).ConfigureAwait(false);
        if (legacy.Status == 200) return Parse(() => SnapshotParser.FromLegacy(legacy.Text, _origin));
        throw HttpError(legacy);
    }

    private static TournamentSnapshot Parse(Func<TournamentSnapshot> parse)
    {
        try { return parse(); }
        catch (FormatException ex) { throw new LinkException(ex.Message); }
        catch (JsonException) { throw new LinkException("The server's answer could not be read."); }
    }

    private static LinkException HttpError(HttpResult response) => response.Status switch
    {
        0 => new LinkException(response.Error ?? "The server could not be reached."),
        404 => new LinkException("Tournament not found."),
        403 => new LinkException("The server refused the request (403)."),
        _ => new LinkException($"The server answered {response.Status}."),
    };

    private void Fail(string message)
    {
        SetStatus(new LinkStatus(LinkState.Error, $"Error: {message}"));
        bool retry;
        var generation = _generation;
        var token = _cts?.Token ?? CancellationToken.None;
        lock (_lock)
        {
            retry = !_retryScheduled && _cts is not null;
            if (retry) _retryScheduled = true;
        }
        if (!retry) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await _options.Delay(_options.RetryDelay, token).ConfigureAwait(false);
                if (generation == _generation) await RefreshAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        });
    }

    private async Task LoopAsync(int generation, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && generation == _generation)
            {
                await _options.Delay(_options.ResyncInterval, token).ConfigureAwait(false);
                if (generation != _generation) return;

                // A connection that could not be made at the start (server down) is tried again here.
                if (!_socketConnected && _socket is null) await ConnectSocketAsync().ConfigureAwait(false);
                await RefreshAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    // ---- result ------------------------------------------------------------------------------------------

    private TournamentSnapshot ManualSnapshot(JsonElement room)
    {
        var name1 = SnapshotParser.Str(room, "team1Name") ?? "";
        var name2 = SnapshotParser.Str(room, "team2Name") ?? "";
        return new TournamentSnapshot
        {
            Id = ManualMatchId,
            Name = "",
            Origin = _origin,
            Teams = new[]
            {
                new LinkTeam("team1", name1, null, null, null, Array.Empty<LinkPlayer>()),
                new LinkTeam("team2", name2, null, null, null, Array.Empty<LinkPlayer>()),
            },
            Matches = new[] { new LinkMatch(ManualMatchId, null, null, null, 1, 1, "team1", "team2", null, null, null, "in_progress", null, _manualRoomId, null) },
            Live = LiveBuilder.Build(room, "team1", "team2"),
        };
    }

    private void Publish(TournamentSnapshot snapshot, int generation)
    {
        if (generation != _generation) return;
        _snapshot = snapshot;
        var selected = _manual ? ManualMatchId : _selectedMatchId;
        SetStatus(StatusOf(snapshot, selected));
        Updated?.Invoke(new LinkUpdate(snapshot, selected, _manual));
    }

    private LinkStatus StatusOf(TournamentSnapshot snapshot, string selected)
    {
        if (!_manual && selected.Length == 0) return new LinkStatus(LinkState.NoMatchSelected, "No match selected");
        var match = snapshot.MatchById(selected);
        if (match is null) return new LinkStatus(LinkState.Error, "Error: the selected match is no longer in the tournament.");

        if (snapshot.Live is null || string.IsNullOrEmpty(match.DraftMatchId))
            return new LinkStatus(LinkState.WaitingForDraft, "Waiting for draft to start…");
        if (snapshot.Live.Phase == "complete") return new LinkStatus(LinkState.MatchComplete, "Match complete");

        var team1 = snapshot.TeamById(match.Team1Id)?.Name ?? "?";
        var team2 = snapshot.TeamById(match.Team2Id)?.Name ?? "?";
        var prefix = _manual || snapshot.Name.Length == 0 ? "" : $" – {snapshot.Name}";
        return new LinkStatus(LinkState.Linked, $"Linked{prefix} – {team1} vs {team2}");
    }

    private void SetStatus(LinkStatus status)
    {
        if (status == Status) return;
        Status = status;
        StatusChanged?.Invoke(status);
    }

    private static async Task SafeDisposeAsync(ISocketConnection socket)
    {
        try { await socket.DisposeAsync().ConfigureAwait(false); }
        catch (Exception) { /* closing anyway */ }
    }
}

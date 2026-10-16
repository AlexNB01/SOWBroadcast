using System.Text.Json;
using System.Threading.Channels;

namespace SowBroadcast.Server;

/// <summary>
/// One "these overlay data keys changed" message: <c>{"changed":[...],"version":n}</c>. The version is the event id, which is also the
/// <c>version</c> of the <c>/api/state</c> the overlay should fetch next (CLAUDE.md EHD-2).
/// </summary>
public sealed record OverlayEvent(long Id, IReadOnlyList<string> Changed)
{
    public string Payload => JsonSerializer.Serialize(new { changed = Changed, version = Id });
}

/// <summary>
/// Fan-out of change events to every connected overlay. Unlike the legacy server (one global "last payload"),
/// every client gets its own queue, so rapid updates never drop keys; a reconnecting client replays what it
/// missed from a short history using <c>Last-Event-ID</c>.
/// </summary>
public sealed class EventHub
{
    private readonly object _gate = new();
    private readonly List<Channel<OverlayEvent>> _clients = new();
    private readonly LinkedList<OverlayEvent> _history = new();
    private readonly int _historySize;
    private readonly IReadOnlyList<string> _fullRefreshKeys;
    private long _lastId;

    public EventHub(IReadOnlyList<string> fullRefreshKeys, int historySize = 256)
    {
        _fullRefreshKeys = fullRefreshKeys;
        _historySize = historySize;
    }

    public long LastId { get { lock (_gate) return _lastId; } }

    public int ClientCount { get { lock (_gate) return _clients.Count; } }

    /// <summary>Publishes a change; returns its id (0 when there was nothing to publish).</summary>
    public long Publish(IEnumerable<string> changedKeys)
    {
        var keys = changedKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToArray();
        if (keys.Length == 0) return 0;

        lock (_gate)
        {
            var ev = new OverlayEvent(++_lastId, keys);
            _history.AddLast(ev);
            while (_history.Count > _historySize) _history.RemoveFirst();
            foreach (var client in _clients) client.Writer.TryWrite(ev);
            return ev.Id;
        }
    }

    /// <summary>
    /// Registers a client. Events after <paramref name="lastEventId"/> that are still in the history are queued first;
    /// if the id is from a previous server run (newer than anything we have) the client gets one full refresh.
    /// </summary>
    public (ChannelReader<OverlayEvent> Reader, IDisposable Subscription) Subscribe(long? lastEventId)
    {
        var channel = Channel.CreateUnbounded<OverlayEvent>(new UnboundedChannelOptions { SingleReader = true });
        lock (_gate)
        {
            if (lastEventId is { } last)
            {
                if (last > _lastId)
                    channel.Writer.TryWrite(new OverlayEvent(_lastId, _fullRefreshKeys));
                else
                    foreach (var ev in _history.Where(e => e.Id > last)) channel.Writer.TryWrite(ev);
            }
            _clients.Add(channel);
        }
        return (channel.Reader, new Subscription(this, channel));
    }

    private void Remove(Channel<OverlayEvent> channel)
    {
        lock (_gate) _clients.Remove(channel);
        channel.Writer.TryComplete();
    }

    private sealed class Subscription : IDisposable
    {
        private readonly EventHub _hub;
        private readonly Channel<OverlayEvent> _channel;

        public Subscription(EventHub hub, Channel<OverlayEvent> channel)
        {
            _hub = hub;
            _channel = channel;
        }

        public void Dispose() => _hub.Remove(_channel);
    }
}

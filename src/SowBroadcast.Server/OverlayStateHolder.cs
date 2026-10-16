namespace SowBroadcast.Server;

/// <summary>The latest overlay state JSON (see <c>GET /api/state</c>): set by the publisher, read by the server for every request.</summary>
public sealed class OverlayStateHolder
{
    private volatile string _json = "";

    public string Json
    {
        get => _json;
        set => _json = value;
    }
}

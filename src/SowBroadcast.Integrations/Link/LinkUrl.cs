using System.Web;

namespace SowBroadcast.Integrations.Link;

/// <summary>The user gave something that cannot be linked (the message is shown as it is).</summary>
public sealed class LinkException : Exception
{
    public LinkException(string message) : base(message) { }
}

/// <summary>Reads the addresses the user pastes.</summary>
public static class LinkUrl
{
    private static Uri Absolute(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https") || string.IsNullOrEmpty(uri.Host))
            throw new LinkException("The link is not a valid URL.");
        return uri;
    }

    private static string OriginOf(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    /// <summary><c>https://host/tournament/&lt;id-or-slug&gt;</c> or <c>?id=</c>.</summary>
    public static (string Origin, string Key) ParseTournament(string url)
    {
        var uri = Absolute(url);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var key = "";
        for (var i = 0; i < segments.Length - 1; i++)
            if (segments[i] == "tournament") { key = Uri.UnescapeDataString(segments[i + 1]); break; }
        if (key.Length == 0) key = HttpUtility.ParseQueryString(uri.Query)["id"] ?? "";
        if (key.Length == 0) throw new LinkException("The link has no tournament identifier.");
        return (OriginOf(uri), key);
    }

    /// <summary>A draft room address such as <c>https://host/stream?matchId=&lt;id&gt;</c>.</summary>
    public static (string Origin, string MatchId) ParseDraftRoom(string url)
    {
        var uri = Absolute(url);
        var id = HttpUtility.ParseQueryString(uri.Query)["matchId"];
        if (string.IsNullOrWhiteSpace(id)) throw new LinkException("The link has no matchId parameter.");
        return (OriginOf(uri), id.Trim());
    }
}

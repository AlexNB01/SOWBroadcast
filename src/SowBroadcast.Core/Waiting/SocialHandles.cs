using System.Text.RegularExpressions;

namespace SowBroadcast.Core.Waiting;

/// <summary>Turns whatever was pasted into a social field (a URL, "@name", "site/name") into the bare handle the overlays show.</summary>
public static class SocialHandles
{
    public static readonly IReadOnlyList<string> Kinds = new[] { "twitch", "twitter", "youtube", "instagram", "discord", "web" };

    private static readonly Dictionary<string, Regex> Prefixes = new()
    {
        ["twitch"] = Build(@"^[^/]*twitch\.tv/"),
        ["twitter"] = Build(@"^[^/]*(twitter\.com|x\.com)/"),
        ["youtube"] = Build(@"^[^/]*(youtube\.com|youtu\.be)/"),
        ["instagram"] = Build(@"^[^/]*instagram\.com/"),
        ["discord"] = Build(@"^[^/]*(discord\.gg/|discord\.com/invite/)"),
    };

    private static readonly Regex Scheme = Build(@"^https?://");
    private static readonly Regex LeadingAt = new("^@+", RegexOptions.Compiled);
    private static readonly Regex LeadingSlash = new("^/+", RegexOptions.Compiled);

    private static Regex Build(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Normalize(string kind, string? text)
    {
        var s = Scheme.Replace((text ?? "").Trim(), "");
        if (Prefixes.TryGetValue(kind, out var prefix)) s = prefix.Replace(s, "");
        s = LeadingAt.Replace(s, "");
        s = LeadingSlash.Replace(s, "");
        var end = s.IndexOfAny(new[] { '/', '?', '#' });
        return end >= 0 ? s[..end] : s;
    }
}

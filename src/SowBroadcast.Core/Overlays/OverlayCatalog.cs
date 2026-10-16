namespace SowBroadcast.Core.Overlays;

/// <summary>An overlay page: what the producer calls it and where it is under <c>/HTML/</c> (with its query, e.g. <c>waiting.html?mode=brb</c>).</summary>
public sealed record OverlayPage(string Label, string Path);

/// <summary>
/// The overlays the program serves (EHD-13). The preview menu lists them, and <c>HTML/preview.html</c> shows all of them at once;
/// a test keeps this list and that page in step, and checks that every page exists.
/// </summary>
public static class OverlayCatalog
{
    public static IReadOnlyList<OverlayPage> All { get; } = new OverlayPage[]
    {
        new("In-game scoreboard", "scoreboard.html"),
        new("Map draft", "MapDraft.html"),
        new("Maps", "maps.html"),
        new("Team 1 roster", "team.html?side=1"),
        new("Team 2 roster", "team.html?side=2"),
        new("Starting soon", "waiting.html?mode=start"),
        new("Be right back", "waiting.html?mode=brb"),
        new("Thank you for watching", "waiting.html?mode=end"),
        new("Standings", "standings.html"),
        new("Bracket", "bracket.html"),
        new("Duo camera row", "duorow.html"),
        new("Trio camera row", "triorow.html"),
        new("Single camera", "singlecam.html"),
        new("Instant replay", "replay.html"),
        new("Transition", "transition.html"),
    };

    /// <summary>The page file of an overlay without its query (<c>waiting.html</c>).</summary>
    public static string FileOf(OverlayPage page) => page.Path.Split('?')[0];

    /// <summary>One overlay on a checkerboard, scaled to the browser window (the page's <c>?preview</c> mode).</summary>
    public static string PreviewUrl(string baseUrl, OverlayPage page) =>
        $"{baseUrl.TrimEnd('/')}/HTML/{page.Path}{(page.Path.Contains('?') ? '&' : '?')}preview";

    /// <summary>All overlays side by side, live.</summary>
    public static string OverviewUrl(string baseUrl) => $"{baseUrl.TrimEnd('/')}/HTML/preview.html";
}

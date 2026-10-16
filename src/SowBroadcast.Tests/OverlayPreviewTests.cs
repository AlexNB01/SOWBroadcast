using SowBroadcast.Core.Overlays;

namespace SowBroadcast.Tests;

public sealed class OverlayPreviewTests
{
    private static string? HtmlFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HTML", "sow-data.js"))) dir = dir.Parent;
        return dir is null ? null : Path.Combine(dir.FullName, "HTML");
    }

    [Fact]
    public void Every_overlay_of_the_catalogue_is_a_page_that_exists()
    {
        if (HtmlFolder() is not { } html) return;

        foreach (var page in OverlayCatalog.All)
            Assert.True(File.Exists(Path.Combine(html, OverlayCatalog.FileOf(page))), $"{page.Label}: {page.Path}");
    }

    [Fact]
    public void The_overview_page_lists_exactly_the_overlays_of_the_catalogue()
    {
        if (HtmlFolder() is not { } html) return;
        var text = File.ReadAllText(Path.Combine(html, "preview.html"));

        foreach (var page in OverlayCatalog.All)
            Assert.Contains($"[\"{page.Label}\", \"{page.Path}\"]", text);
        Assert.Equal(OverlayCatalog.All.Count, text.Split("[\"").Length - 1);   // and nothing more
    }

    [Fact]
    public void The_pages_of_the_catalogue_are_unique_and_a_second_query_value_is_joined_with_an_ampersand()
    {
        Assert.Equal(OverlayCatalog.All.Count, OverlayCatalog.All.Select(p => p.Path).Distinct().Count());

        var plain = OverlayCatalog.PreviewUrl("http://127.0.0.1:8324/", new OverlayPage("Scoreboard", "scoreboard.html"));
        var withQuery = OverlayCatalog.PreviewUrl("http://127.0.0.1:8324", new OverlayPage("Brb", "waiting.html?mode=brb"));

        Assert.Equal("http://127.0.0.1:8324/HTML/scoreboard.html?preview", plain);
        Assert.Equal("http://127.0.0.1:8324/HTML/waiting.html?mode=brb&preview", withQuery);
        Assert.Equal("http://127.0.0.1:8324/HTML/preview.html", OverlayCatalog.OverviewUrl("http://127.0.0.1:8324/"));
        Assert.Equal("waiting.html", OverlayCatalog.FileOf(OverlayCatalog.All.First(p => p.Path.StartsWith("waiting"))));
    }

    [Fact]
    public void The_scene_collection_and_the_catalogue_use_the_same_overlay_pages()
    {
        var pages = OverlayCatalog.All.Select(OverlayCatalog.FileOf).ToHashSet();
        var json = System.Text.Json.JsonDocument.Parse(SowBroadcast.Core.Obs.ObsSceneCollection.Build("http://127.0.0.1:8324", "C:/I", "C:/D")).RootElement;

        foreach (var source in json.GetProperty("sources").EnumerateArray().Where(s => s.GetProperty("id").GetString() == "browser_source"))
        {
            var url = source.GetProperty("settings").GetProperty("url").GetString()!;
            var file = url[(url.IndexOf("/HTML/", StringComparison.Ordinal) + 6)..].Split('?')[0];
            Assert.Contains(file, pages);
        }
    }
}

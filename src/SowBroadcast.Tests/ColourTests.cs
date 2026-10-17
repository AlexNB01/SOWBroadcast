using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Colour;
using SowBroadcast.Core.Model;

namespace SowBroadcast.Tests;

public sealed class RecentColoursTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-recent-" + Guid.NewGuid().ToString("N"));

    public RecentColoursTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    [Fact]
    public void The_newest_colour_comes_first_a_repeat_moves_up_and_only_hex_colours_count()
    {
        var recent = new RecentColours();
        var changes = 0;
        recent.Changed += () => changes++;

        recent.Add("#111111");
        recent.Add("#222222");
        recent.Add("#111111");
        recent.Add("222222");
        recent.Add("not a colour");
        recent.Add(null);

        Assert.Equal(new[] { "#222222", "#111111" }, recent.Items);
        Assert.Equal(4, changes);                              // the same colour again at the front is no change
        recent.Add("#222222");
        Assert.Equal(4, changes);
    }

    [Fact]
    public void Only_the_eight_newest_are_kept()
    {
        var recent = new RecentColours();
        for (var i = 1; i <= 12; i++) recent.Add($"#0000{i:x2}");

        Assert.Equal(RecentColours.Capacity, recent.Items.Count);
        Assert.Equal("#00000c", recent.Items[0]);
        Assert.DoesNotContain("#000001", recent.Items);
    }

    [Fact]
    public void The_list_is_kept_between_runs_and_a_damaged_file_starts_empty()
    {
        var path = Path.Combine(_dir, "recent-colours.json");
        var first = new RecentColours();
        first.UseFile(path);
        first.Add("#abcdef");
        first.Add("#123456");

        var second = new RecentColours();
        second.UseFile(path);
        Assert.Equal(new[] { "#123456", "#abcdef" }, second.Items);

        File.WriteAllText(path, "[ nope");
        var third = new RecentColours();
        third.UseFile(path);
        Assert.Empty(third.Items);
        third.Add("#ffffff");                                  // and it keeps working
        Assert.Single(third.Items);
    }

    [Fact]
    public void A_file_with_junk_entries_and_too_many_colours_is_cleaned_when_read()
    {
        var path = Path.Combine(_dir, "recent-colours.json");
        var entries = Enumerable.Range(1, 12).Select(i => $"\"#00000{i % 10}\"").Concat(new[] { "\"junk\"", "\"#00000A\"" });
        File.WriteAllText(path, "[" + string.Join(",", entries) + "]");

        var recent = new RecentColours();
        recent.UseFile(path);

        Assert.True(recent.Items.Count <= RecentColours.Capacity);
        Assert.DoesNotContain("junk", recent.Items);
        Assert.Equal(recent.Items.Count, recent.Items.Distinct().Count());
    }
}

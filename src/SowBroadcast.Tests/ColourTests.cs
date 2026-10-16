using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Colour;
using SowBroadcast.Core.Model;

namespace SowBroadcast.Tests;

public sealed class ColourContrastTests
{
    [Fact]
    public void Black_on_white_is_the_maximum_and_a_colour_against_itself_the_minimum()
    {
        Assert.Equal(21.0, ColourContrast.Ratio("#000000", "#ffffff")!.Value, 1);
        Assert.Equal(1.0, ColourContrast.Ratio("#27aae1", "#27AAE1")!.Value, 3);
        Assert.Equal(ColourContrast.Ratio("#001445", "#ffffff"), ColourContrast.Ratio("#ffffff", "#001445"));   // order does not matter
    }

    [Fact]
    public void Ratios_match_the_values_of_the_brand_guideline_table()
    {
        // CLAUDE.md 11.1: white on night blue 17.7:1, white on shadow blue 12.1:1, blade on night 8.5:1.
        Assert.Equal(17.7, ColourContrast.Ratio("#ffffff", "#001445")!.Value, 1);
        Assert.Equal(12.1, ColourContrast.Ratio("#ffffff", "#0f356b")!.Value, 1);
        Assert.Equal(8.5, ColourContrast.Ratio("#23c5d7", "#001445")!.Value, 1);
    }

    [Theory]
    [InlineData("#123456", true)]
    [InlineData("123456", true)]
    [InlineData(" #ABCDEF ", true)]
    [InlineData("#12345", false)]
    [InlineData("#gggggg", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_six_digit_hex_colours_are_read(string? text, bool ok) =>
        Assert.Equal(ok, ColourContrast.TryParse(text, out _));

    [Fact]
    public void A_colour_that_is_not_a_colour_gives_no_ratio_and_no_advice()
    {
        Assert.Null(ColourContrast.Ratio("red", "#ffffff"));
        Assert.Equal("", ColourContrast.TeamColourAdvice("not a colour", "#000000", "#ffffff"));
    }

    [Fact]
    public void The_default_team_colours_are_fine_on_the_default_overlay_colours()
    {
        Assert.Equal("", ColourContrast.TeamColourAdvice(Defaults.Team1Color, "#000000", "#ffffff"));
        Assert.Equal("", ColourContrast.TeamColourAdvice(Defaults.Team2Color, "#000000", "#ffffff"));
    }

    [Fact]
    public void Advice_says_when_text_is_hard_to_read_on_the_colour_and_when_it_vanishes_on_the_background()
    {
        var hardToRead = ColourContrast.TeamColourAdvice("#1a1a40", "#000000", "#ffffff");
        Assert.Contains("hard to read", hardToRead);
        Assert.Contains("#000000", hardToRead);
        Assert.DoesNotContain("disappears", hardToRead);

        var vanishes = ColourContrast.TeamColourAdvice("#fafafa", "#000000", "#ffffff");
        Assert.Contains("almost disappears", vanishes);
        Assert.DoesNotContain("hard to read", vanishes);

        var both = ColourContrast.TeamColourAdvice("#808080", "#808080", "#808080");
        Assert.Contains("hard to read", both);
        Assert.Contains("almost disappears", both);
    }
}

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

public sealed class TeamColourAdviceViewModelTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-advice-" + Guid.NewGuid().ToString("N"));
    private readonly MainWindowViewModelTests.FakeDialogs _dialogs = new();
    private readonly MainWindowViewModelTests.FakePublisher _publisher = new();

    public TeamColourAdviceViewModelTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private MainWindowViewModel Create() => new(_dialogs, _dialogs, _publisher, Path.Combine(_tmp, "autosave.json"));

    [Fact]
    public void The_default_colours_give_no_warning()
    {
        var vm = Create();

        Assert.False(vm.Match.Team1.HasColourWarning);
        Assert.False(vm.Match.Team2.HasColourWarning);
    }

    [Fact]
    public void Choosing_a_dark_team_colour_warns_about_the_overlay_text_and_choosing_another_clears_it()
    {
        var vm = Create();

        vm.Match.Team1.ColourHex = "#1a1a40";
        Assert.True(vm.Match.Team1.HasColourWarning);
        Assert.Contains("hard to read", vm.Match.Team1.ColourWarning);
        Assert.False(vm.Match.Team2.HasColourWarning);

        vm.Match.Team1.ColourHex = "#55aaff";
        Assert.False(vm.Match.Team1.HasColourWarning);
    }

    [Fact]
    public void The_advice_follows_the_overlay_colours_of_the_general_tab()
    {
        var vm = Create();
        vm.Match.Team1.ColourHex = "#ffcc00";          // yellow: fine against black text
        Assert.False(vm.Match.Team1.HasColourWarning);

        vm.General.Colours.Single(c => c.Key == "secondary").Hex = "#ffffff";   // white text on yellow

        Assert.True(vm.Match.Team1.HasColourWarning);
        Assert.Contains("#ffffff", vm.Match.Team1.ColourWarning);

        vm.General.Colours.Single(c => c.Key == "secondary").Hex = "#000000";
        Assert.False(vm.Match.Team1.HasColourWarning);
    }

    [Fact]
    public void A_loaded_state_and_an_undo_bring_the_advice_along()
    {
        var vm = Create();
        vm.General.Colours.Single(c => c.Key == "secondary").Hex = "#222222";
        vm.Match.Team2.ColourHex = "#1a1a40";
        vm.FlushHistory();
        vm.Match.Team2.ColourHex = "#55aaff";
        vm.FlushHistory();
        Assert.False(vm.Match.Team2.HasColourWarning);

        vm.UndoCommand.Execute(null);

        Assert.True(vm.Match.Team2.HasColourWarning);
    }

    [Fact]
    public void A_change_of_the_advice_is_not_an_edit_of_the_match()
    {
        var vm = Create();
        var edits = 0;
        vm.Match.Edited += () => edits++;

        vm.Match.SetOverlayColours("#111111", "#ffffff");          // the colour 'ColourHex' did not change: only the advice may
        vm.Match.Team1.ColourHex = Defaults.Team1Color;

        Assert.Equal(0, edits);
    }
}

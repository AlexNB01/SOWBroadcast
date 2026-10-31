using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;

namespace SowBroadcast.Tests;

public sealed class UpdatePromptTests
{
    private const string Assets = "\"assets\": [{ \"name\": \"SOWBroadcast-Setup-3.4.0.exe\", \"browser_download_url\": \"https://github.com/o/r/releases/download/v3.4.0/SOWBroadcast-Setup-3.4.0.exe\", \"size\": 1000 }]";

    [Fact]
    public void The_release_description_becomes_the_notes()
    {
        var release = ReleaseInfo.Parse("{ \"tag_name\": \"v3.4.0\", \"body\": \"\\r\\n- Faster replay\\r\\n- Fixes\\r\\n\", " + Assets + " }");

        Assert.Equal("- Faster replay\n- Fixes", release.Notes);
    }

    [Fact]
    public void A_release_without_a_description_has_empty_notes()
    {
        Assert.Equal("", ReleaseInfo.Parse("{ \"tag_name\": \"v3.4.0\", \"body\": null, " + Assets + " }").Notes);
        Assert.Equal("", ReleaseInfo.Parse("{ \"tag_name\": \"v3.4.0\", " + Assets + " }").Notes);
    }

    [Fact]
    public void Very_long_notes_are_cut()
    {
        var release = ReleaseInfo.Parse("{ \"tag_name\": \"v3.4.0\", \"body\": \"" + new string('x', 9000) + "\", " + Assets + " }");

        Assert.True(release.Notes.Length <= 4001);
        Assert.EndsWith("…", release.Notes);
    }

    [Fact]
    public void The_window_texts_name_both_versions_the_size_and_the_notes()
    {
        var model = new UpdatePromptViewModel(new UpdateOffer("3.4.0", 186L * 1024 * 1024, new object(), "- Faster replay"), "3.3.1");

        Assert.Equal("Version 3.4.0 is available (you have 3.3.1).", model.VersionLine);
        Assert.Equal("Download: 186 MB", model.SizeLine);
        Assert.True(model.HasSize);
        Assert.True(model.HasNotes);
        Assert.Contains("Do not update during a broadcast", model.Warning);
    }

    [Fact]
    public void An_unknown_size_and_empty_notes_are_left_out()
    {
        var model = new UpdatePromptViewModel(new UpdateOffer("3.4.0", null, new object()), "3.3.1");

        Assert.False(model.HasSize);
        Assert.False(model.HasNotes);
    }

    [Theory]
    [InlineData(false, false, true)]    // first time, nobody on air: open
    [InlineData(true, false, false)]    // already shown in this run
    [InlineData(false, true, false)]    // streaming or recording: never interrupt
    [InlineData(true, true, false)]
    public void The_window_opens_once_and_never_while_on_air(bool alreadyShown, bool onAir, bool expected) =>
        Assert.Equal(expected, UpdatePromptViewModel.ShouldOpenAtStartup(alreadyShown, onAir));
}

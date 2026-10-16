using SowBroadcast.Core.Replay;

namespace SowBroadcast.Tests;

public sealed class ReplayWatcherTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "sowb-replay-" + Guid.NewGuid().ToString("N"));
    private readonly ReplayWatcher _watcher;

    public ReplayWatcherTests()
    {
        Directory.CreateDirectory(_folder);
        _watcher = new ReplayWatcher(_folder, keep: 3);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        try { Directory.Delete(_folder, true); } catch { /* best effort */ }
    }

    private string Source => Path.Combine(_folder, "Replay Replay.mp4");

    private void SaveClip(string content, int secondsAgo = 0)
    {
        File.WriteAllText(Source, content);
        File.SetLastWriteTimeUtc(Source, DateTime.UtcNow.AddSeconds(-secondsAgo));
    }

    /// <summary>Looks until the watcher takes the clip (it needs a few identical looks in a row).</summary>
    private string? TakeClip()
    {
        string? name = null;
        for (var i = 0; i < ReplayWatcher.StableChecks + 2 && name is null; i++) name = _watcher.Check();
        return name;
    }

    private string[] Playlist() =>
        Directory.Exists(_watcher.PlaylistFolder)
            ? Directory.GetFiles(_watcher.PlaylistFolder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!
            : Array.Empty<string>();

    [Fact]
    public void A_clip_is_copied_once_it_has_stopped_changing_and_the_pointer_names_it()
    {
        SaveClip("clip one");

        Assert.Null(_watcher.Check());
        Assert.Null(_watcher.Check());
        var name = _watcher.Check();

        Assert.Equal("1-Replay-Replay.mp4", name);
        Assert.Equal("clip one", File.ReadAllText(Path.Combine(_watcher.PlaylistFolder, name!)));
        Assert.Equal("1-Replay-Replay.mp4", File.ReadAllText(_watcher.PointerPath));   // just the file name, no path
    }

    [Fact]
    public void A_clip_that_is_still_growing_is_not_taken()
    {
        SaveClip("a");
        _watcher.Check();
        _watcher.Check();
        SaveClip("a bit more"); // OBS is still writing

        Assert.Null(_watcher.Check());
        Assert.Null(_watcher.Check());
        Assert.Empty(Playlist());
        Assert.NotNull(_watcher.Check());               // now it has been the same three looks in a row
    }

    [Fact]
    public void The_same_clip_is_never_copied_twice_but_a_new_one_gets_the_next_number()
    {
        SaveClip("first", secondsAgo: 30);
        Assert.Equal("1-Replay-Replay.mp4", TakeClip());
        for (var i = 0; i < 6; i++) Assert.Null(_watcher.Check());

        SaveClip("second clip is longer");

        Assert.Equal("2-Replay-Replay.mp4", TakeClip());
        Assert.Equal(new[] { "1-Replay-Replay.mp4", "2-Replay-Replay.mp4" }, Playlist());
        Assert.Equal("2-Replay-Replay.mp4", File.ReadAllText(_watcher.PointerPath));
    }

    [Fact]
    public void A_clip_that_was_there_before_the_program_started_is_not_copied()
    {
        SaveClip("old clip");

        _watcher.Baseline();

        for (var i = 0; i < 6; i++) Assert.Null(_watcher.Check());
        Assert.Empty(Playlist());
        Assert.False(_watcher.IsPending);
    }

    [Fact]
    public void Numbering_goes_on_after_the_highest_clip_already_in_the_playlist()
    {
        Directory.CreateDirectory(_watcher.PlaylistFolder);
        File.WriteAllText(Path.Combine(_watcher.PlaylistFolder, "7-Replay-Replay.mp4"), "x");
        File.WriteAllText(Path.Combine(_watcher.PlaylistFolder, "notes.txt"), "not a clip");
        SaveClip("clip");

        Assert.Equal("8-Replay-Replay.mp4", TakeClip());
        Assert.Contains("notes.txt", Playlist());
    }

    [Fact]
    public void Only_the_newest_clips_stay_and_the_one_in_use_is_never_removed()
    {
        for (var i = 1; i <= 5; i++)
        {
            SaveClip($"clip {i} " + new string('x', i));
            Assert.NotNull(TakeClip());
        }

        Assert.Equal(new[] { "3-Replay-Replay.mp4", "4-Replay-Replay.mp4", "5-Replay-Replay.mp4" }, Playlist());
        Assert.Equal("5-Replay-Replay.mp4", File.ReadAllText(_watcher.PointerPath));
    }

    [Fact]
    public void A_clip_that_is_still_open_in_OBS_is_tried_again_later()
    {
        SaveClip("being written");
        string? name;
        using (new FileStream(Source, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            name = TakeClip();                         // cannot be copied while it is locked
            Assert.Null(name);
            Assert.Empty(Playlist());
        }

        Assert.Equal("1-Replay-Replay.mp4", TakeClip());
    }

    [Fact]
    public void A_missing_clip_is_simply_nothing_to_do()
    {
        Assert.Null(_watcher.Check());
        Assert.False(_watcher.IsPending);
    }

    [Fact]
    public async Task The_running_watcher_announces_a_clip_that_appears_and_ignores_the_one_already_there()
    {
        SaveClip("already here", secondsAgo: 60);
        var seen = new List<string>();
        _watcher.ReplayReady += seen.Add;
        _watcher.Start();
        await Task.Delay(2200);
        Assert.Empty(seen);

        SaveClip("a fresh replay from OBS");
        for (var i = 0; i < 100 && seen.Count == 0; i++) await Task.Delay(100);

        Assert.Equal(new[] { "1-Replay-Replay.mp4" }, seen);
    }
}

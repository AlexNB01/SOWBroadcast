using System.Text.Json.Nodes;
using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Control;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Replay;

namespace SowBroadcast.Tests;

public sealed class ReplayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sowb-replay-test-" + Guid.NewGuid().ToString("N"));
    private long _now = 1_760_000_000_000;

    public ReplayTests() => Directory.CreateDirectory(Path.Combine(_root, "Playlist"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private ReplayLibrary Library() => new(_root, () => _now += 1000);

    private string Clip(int number, byte[]? content = null)
    {
        var name = $"{number}-Replay-Replay.mp4";
        File.WriteAllBytes(Path.Combine(_root, "Playlist", name), content ?? new byte[] { 1, 2, 3 });
        return name;
    }

    // ---- the length of an MP4 ----------------------------------------------------------------------------

    private static byte[] Box(string type, byte[] payload)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(payload.Length + 8).Reverse());
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(type));
        bytes.AddRange(payload);
        return bytes.ToArray();
    }

    private static byte[] Mvhd(uint timescale, uint duration)
    {
        var p = new byte[100];
        void Put(int at, uint v) { p[at] = (byte)(v >> 24); p[at + 1] = (byte)(v >> 16); p[at + 2] = (byte)(v >> 8); p[at + 3] = (byte)v; }
        Put(12, timescale);
        Put(16, duration);
        return Box("mvhd", p);
    }

    [Fact]
    public void Mp4_length_comes_from_the_movie_header_wherever_the_moov_box_is()
    {
        var path = Path.Combine(_root, "Playlist", "7-Replay-Replay.mp4");
        // ftyp, a big mdat, then moov at the end (how OBS writes a finished file)
        File.WriteAllBytes(path, Box("ftyp", new byte[8]).Concat(Box("mdat", new byte[5000])).Concat(Box("moov", Mvhd(1000, 12_340))).ToArray());
        Assert.Equal(12_340, Mp4Info.ReadDurationMs(path));

        File.WriteAllBytes(path, Box("ftyp", new byte[8]).Concat(Box("moov", Mvhd(90_000, 450_000))).Concat(Box("mdat", new byte[100])).ToArray());
        Assert.Equal(5_000, Mp4Info.ReadDurationMs(path));
    }

    [Fact]
    public void Mp4_length_is_zero_when_it_cannot_be_read()
    {
        var path = Path.Combine(_root, "Playlist", "8-Replay-Replay.mp4");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
        Assert.Equal(0, Mp4Info.ReadDurationMs(path));
        Assert.Equal(0, Mp4Info.ReadDurationMs(Path.Combine(_root, "missing.mp4")));
        Assert.Equal(0, Mp4Info.ParseMvhd(new byte[5]));
    }

    // ---- the library -------------------------------------------------------------------------------------

    [Fact]
    public void A_new_clip_is_filed_under_the_match_and_the_tournament()
    {
        var library = Library();
        var clip = library.Add(Clip(1), "Alpha vs Bravo", "ESM 2026");

        Assert.Equal("Clip 1", clip.Title);
        Assert.Equal("Alpha vs Bravo", clip.Match);
        Assert.Equal("ESM 2026", clip.Tournament);
        var categories = library.Categories();
        Assert.Contains(categories, c => c.Kind == ReplayCategoryKind.Match && c.Name == "Alpha vs Bravo" && c.Count == 1);
        Assert.Contains(categories, c => c.Kind == ReplayCategoryKind.Tournament && c.Name == "ESM 2026");
    }

    [Fact]
    public void The_same_file_is_never_added_twice()
    {
        var library = Library();
        var first = library.Add(Clip(1), "A vs B", "");
        var second = library.Add("1-Replay-Replay.mp4", "C vs D", "");
        Assert.Equal(first.Id, second.Id);
        Assert.Single(library.Clips());
    }

    [Fact]
    public void Clips_are_listed_newest_first_and_a_category_oldest_first()
    {
        var library = Library();
        library.Add(Clip(1), "A vs B", "");
        library.Add(Clip(2), "A vs B", "");
        library.Add(Clip(3), "C vs D", "");

        Assert.Equal(new[] { "3-Replay-Replay.mp4", "2-Replay-Replay.mp4", "1-Replay-Replay.mp4" }, library.Clips().Select(c => c.File));
        Assert.Equal(new[] { "1-Replay-Replay.mp4", "2-Replay-Replay.mp4" }, library.ClipsIn("a vs b").Select(c => c.File));
        Assert.Empty(library.ClipsIn("nothing"));
        Assert.Equal("3-Replay-Replay.mp4", library.Latest()!.File);
    }

    [Fact]
    public void The_library_survives_a_restart_with_edits_and_custom_categories()
    {
        var library = Library();
        var clip = library.Add(Clip(1), "A vs B", "Cup");
        library.AddCategory("Best plays");
        library.Update(clip with { Title = "Triple kill", StartMs = 2000, EndMs = 6000, Speed = 0.5, Tags = new[] { "Best plays" } });

        var again = new ReplayLibrary(_root);
        var loaded = Assert.Single(again.Clips());
        Assert.Equal("Triple kill", loaded.Title);
        Assert.Equal(2000, loaded.StartMs);
        Assert.Equal(6000, loaded.EndMs);
        Assert.Equal(0.5, loaded.Speed);
        Assert.Equal(new[] { "Best plays" }, loaded.Tags);
        Assert.Contains(again.Categories(), c => c.Kind == ReplayCategoryKind.Custom && c.Name == "Best plays" && c.Count == 1);
    }

    [Fact]
    public void A_trim_that_cannot_be_played_is_put_right()
    {
        var library = Library();
        var clip = library.Add(Clip(1), "", "");
        library.Update(clip with { StartMs = 5000, EndMs = 3000, Speed = 99 });
        var fixedClip = library.Find(clip.Id)!;
        Assert.Equal(5000, fixedClip.StartMs);
        Assert.Equal(0, fixedClip.EndMs);   // end before start = to the end
        Assert.Equal(4, fixedClip.Speed);

        library.Update(clip with { StartMs = -10, Speed = 0 });
        Assert.Equal(0, library.Find(clip.Id)!.StartMs);
        Assert.Equal(0.1, library.Find(clip.Id)!.Speed);
    }

    [Fact]
    public void Sync_finds_clips_that_were_copied_in_and_forgets_the_ones_that_are_gone()
    {
        var library = Library();
        var gone = library.Add(Clip(1), "A vs B", "");
        Clip(2);
        File.WriteAllText(Path.Combine(_root, "Playlist", "notes.txt"), "not a clip");
        File.Delete(Path.Combine(_root, "Playlist", gone.File));

        Assert.True(library.Sync());
        var only = Assert.Single(library.Clips());
        Assert.Equal("2-Replay-Replay.mp4", only.File);
        Assert.Equal("", only.Match);
        Assert.False(library.Sync());   // nothing more to do
    }

    [Fact]
    public void Removing_a_clip_deletes_its_file_and_takes_it_out_of_a_pending_request()
    {
        var library = Library();
        var a = library.Add(Clip(1), "", "");
        var b = library.Add(Clip(2), "", "");
        library.Play(new[] { a, b });

        Assert.True(library.Remove(a.Id));
        Assert.False(File.Exists(Path.Combine(_root, "Playlist", a.File)));
        Assert.Equal(new[] { b.Id }, library.Request!.Clips.Select(c => c.Id));
        Assert.False(library.Remove("nope"));
    }

    [Fact]
    public void Removing_a_category_takes_the_tag_off_the_clips_but_keeps_the_clips()
    {
        var library = Library();
        var clip = library.Add(Clip(1), "", "");
        library.Update(clip with { Tags = new[] { "Funny" } });
        Assert.True(library.RemoveCategory("funny"));
        Assert.Empty(library.Find(clip.Id)!.Tags);
        Assert.DoesNotContain(library.Categories(), c => c.Kind == ReplayCategoryKind.Custom);
        Assert.Single(library.Clips());
    }

    [Fact]
    public void A_damaged_clip_list_is_set_aside_and_the_clips_are_found_again()
    {
        Clip(1);
        File.WriteAllText(Path.Combine(_root, ReplayLibrary.FileName), "{ not json");
        var library = new ReplayLibrary(_root);
        Assert.Empty(library.Clips());
        Assert.True(File.Exists(Path.Combine(_root, ReplayLibrary.FileName + ".bad")));
        library.Sync();
        Assert.Single(library.Clips());
    }

    [Fact]
    public void Every_play_request_gets_a_new_number()
    {
        var library = Library();
        var clip = library.Add(Clip(1), "", "");
        var first = library.Play(new[] { clip });
        var second = library.Play(new[] { clip });
        Assert.True(second.Nonce > first.Nonce);
        Assert.Empty(library.Stop().Clips);
    }

    [Fact]
    public void The_overlay_gets_the_newest_clip_and_the_request_with_trim_and_speed()
    {
        var library = Library();
        var clip = library.Add(Clip(1), "", "");
        library.Update(clip with { StartMs = 1500, EndMs = 4000, Speed = 0.5 });
        library.Play(new[] { library.Find(clip.Id)! });

        var json = library.BuildOverlayJson();
        Assert.Equal("1-Replay-Replay.mp4", (string?)json["latest"]!["file"]);
        Assert.Equal(1500, (long)json["latest"]!["start_ms"]!);
        var play = json["play"]!;
        Assert.Equal(0.5, (double)play["clips"]![0]!["speed"]!);
        Assert.Equal(4000, (long)play["clips"]![0]!["end_ms"]!);
        Assert.NotNull(play["nonce"]);
    }

    [Fact]
    public void An_empty_library_gives_the_overlay_nothing_to_play()
    {
        var json = Library().BuildOverlayJson();
        Assert.Null(json["latest"]);
        Assert.Null(json["play"]);
    }

    [Fact]
    public void The_state_the_overlays_fetch_carries_the_replay_part()
    {
        var exporter = new ScoreboardExporter(Path.Combine(_root, "sb"), new FakeLogos());
        var library = Library();
        library.Add(Clip(1), "A vs B", "");
        var withClips = JsonNode.Parse(OverlayStateJson.Build(AppState.CreateDefault(), exporter, 1, 0, library.BuildOverlayJson()))!;
        Assert.Equal("1-Replay-Replay.mp4", (string?)withClips["replay"]!["latest"]!["file"]);

        var without = JsonNode.Parse(OverlayStateJson.Build(AppState.CreateDefault(), exporter, 1, 0))!;
        Assert.NotNull(without["replay"]);
        Assert.Null(without["replay"]!["latest"]);
    }

    private sealed class FakeLogos : ILogoConverter
    {
        public byte[]? ToPng(string? sourcePath) => null;
    }

    // ---- the tab -----------------------------------------------------------------------------------------

    private sealed class Recorder : IOverlayPublisher
    {
        public int ReplayAnnouncements;
        public string? BaseUrl => null;
        public PublishResult Publish(AppState state) => new(Array.Empty<string>(), Array.Empty<string>());
        public PublishResult PublishPart(AppState state, PublishScope scope) => new(Array.Empty<string>(), Array.Empty<string>());
        public void AnnounceReplay() => ReplayAnnouncements++;
        public void AnnounceScheduleShow() { }
        public void MarkAssetsChanged(AssetCategory category) { }
    }

    private sealed class Asks(bool answer) : IMessages
    {
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(answer);
    }

    private (ReplayViewModel Tab, ReplayLibrary Library, Recorder Publisher, List<string> Said) Tab(bool confirm = true)
    {
        var library = Library();
        var publisher = new Recorder();
        var said = new List<string>();
        var tab = new ReplayViewModel(library, publisher, null, () => ("Alpha vs Bravo", "ESM 2026"), new Asks(confirm), null, said.Add);
        return (tab, library, publisher, said);
    }

    [Fact]
    public void A_clip_that_arrives_is_filed_with_the_match_on_the_screen_and_announced()
    {
        var (tab, library, publisher, _) = Tab();
        tab.OnClipArrived(Clip(1));

        var clip = Assert.Single(library.Clips());
        Assert.Equal("Alpha vs Bravo", clip.Match);
        Assert.Equal("ESM 2026", clip.Tournament);
        Assert.Equal(1, publisher.ReplayAnnouncements);
        Assert.Single(tab.Clips);
        Assert.Contains(tab.Categories, c => c.Name == "Alpha vs Bravo");
    }

    [Fact]
    public void Editing_the_selected_clip_saves_title_trim_and_speed()
    {
        var (tab, library, _, _) = Tab();
        tab.OnClipArrived(Clip(1));
        tab.Selected = tab.Clips[0];

        tab.EditTitle = "Nice save";
        tab.EditStart = 1.5m;
        tab.EditEnd = 4m;
        tab.EditSpeed = 0.5m;

        var saved = library.Clips()[0];
        Assert.Equal("Nice save", saved.Title);
        Assert.Equal(1500, saved.StartMs);
        Assert.Equal(4000, saved.EndMs);
        Assert.Equal(0.5, saved.Speed);
        Assert.Equal("Nice save", tab.Selected!.Title);   // the row follows
    }

    [Fact]
    public void A_custom_category_can_be_made_and_a_clip_put_in_it()
    {
        var (tab, library, _, _) = Tab();
        tab.OnClipArrived(Clip(1));
        tab.OnClipArrived(Clip(2));
        tab.Selected = tab.Clips[0];

        tab.NewCategoryName = "Best plays";
        tab.AddCategoryCommand.Execute(null);

        Assert.Equal("", tab.NewCategoryName);
        Assert.Single(library.ClipsIn("Best plays"));   // the selected clip went in
        var tag = Assert.Single(tab.Tags);
        Assert.True(tag.IsOn);

        tab.Selected = tab.Clips[1];
        Assert.False(Assert.Single(tab.Tags).IsOn);
        tab.Tags[0].IsOn = true;
        Assert.Equal(2, library.ClipsIn("Best plays").Count);
    }

    [Fact]
    public void Showing_a_clip_asks_the_overlay_to_play_it_once_with_its_trim()
    {
        var (tab, library, publisher, said) = Tab();
        tab.OnClipArrived(Clip(1));
        tab.Selected = tab.Clips[0];
        tab.EditStart = 2m;
        var before = publisher.ReplayAnnouncements;

        tab.ShowSelectedCommand.Execute(null);

        Assert.Equal(before + 1, publisher.ReplayAnnouncements);
        var request = library.Request!;
        Assert.Single(request.Clips);
        Assert.Equal(2000, request.Clips[0].StartMs);
        Assert.Contains("showing", said.Last());
    }

    [Fact]
    public void A_category_plays_its_clips_in_the_order_they_were_saved()
    {
        var (tab, library, _, _) = Tab();
        tab.OnClipArrived(Clip(1));
        tab.OnClipArrived(Clip(2));
        tab.OnClipArrived(Clip(3));

        var result = tab.RunCommand(ControlCatalog.ReplayCategory, new Dictionary<string, string> { ["name"] = "alpha vs bravo" });

        Assert.True(result.Ok);
        Assert.Equal(new[] { "1-Replay-Replay.mp4", "2-Replay-Replay.mp4", "3-Replay-Replay.mp4" }, library.Request!.Clips.Select(c => c.File));
    }

    [Fact]
    public void The_control_commands_say_what_is_missing()
    {
        var (tab, library, publisher, _) = Tab();
        Assert.False(tab.RunCommand(ControlCatalog.ReplayLatest, null).Ok);   // no clips yet
        Assert.False(tab.RunCommand(ControlCatalog.ReplayClip, null).Ok);
        Assert.False(tab.RunCommand(ControlCatalog.ReplayClip, new Dictionary<string, string> { ["id"] = "zzz" }).Ok);
        Assert.False(tab.RunCommand(ControlCatalog.ReplayCategory, new Dictionary<string, string> { ["name"] = "nothing" }).Ok);
        Assert.False(tab.RunCommand(ControlCatalog.ReplayCategory, null).Ok);
        Assert.Equal(0, publisher.ReplayAnnouncements);

        tab.OnClipArrived(Clip(1));
        var id = library.Clips()[0].Id;
        Assert.True(tab.RunCommand(ControlCatalog.ReplayClip, new Dictionary<string, string> { ["id"] = id }).Ok);
        Assert.True(tab.RunCommand(ControlCatalog.ReplayLatest, null).Ok);
        Assert.True(tab.RunCommand(ControlCatalog.ReplayStop, null).Ok);
        Assert.Empty(library.Request!.Clips);
    }

    [Fact]
    public async Task Deleting_a_clip_needs_a_yes_and_removes_the_file()
    {
        var (no, noLibrary, _, _) = Tab(confirm: false);
        no.OnClipArrived(Clip(1));
        no.Selected = no.Clips[0];
        await no.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.Single(noLibrary.Clips());

        var (yes, yesLibrary, _, _) = Tab(confirm: true);
        yes.Selected = yes.Clips[0];
        await yes.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.Empty(yesLibrary.Clips());
        Assert.False(File.Exists(Path.Combine(_root, "Playlist", "1-Replay-Replay.mp4")));
    }

    [Fact]
    public void The_replay_commands_are_in_the_catalog_without_default_keys()
    {
        foreach (var id in new[] { ControlCatalog.ReplayLatest, ControlCatalog.ReplayClip, ControlCatalog.ReplayCategory, ControlCatalog.ReplayStop })
        {
            var info = ControlCatalog.Find(id);
            Assert.NotNull(info);
            Assert.Null(info!.DefaultGesture);
            Assert.True(ControlCatalog.IsReplay(id));
        }
        Assert.False(ControlCatalog.IsReplay(ControlCatalog.ObsReplaySave));
    }
}

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

    // ---- saving the shown part as a new clip -------------------------------------------------------------

    [Fact]
    public void The_ffmpeg_arguments_cut_exactly_and_change_the_speed()
    {
        var args = FfmpegClipExporter.BuildArguments("in.mp4", "out.tmp", 2000, 5000, 0.5);
        Assert.Equal(new[] { "-ss", "2", "-t", "3", "-i", "in.mp4" }, args.Skip(5).Take(6));
        Assert.Contains("setpts=PTS/0.5", args);
        Assert.Contains("atempo=0.5", args);
        Assert.Equal("out.tmp", args[^1]);
        Assert.Contains("libx264", args);

        var whole = FfmpegClipExporter.BuildArguments("in.mp4", "out.tmp", 0, 0, 1);
        Assert.DoesNotContain("-ss", whole);
        Assert.DoesNotContain("-t", whole);
        Assert.DoesNotContain("-vf", whole);

        var fromStart = FfmpegClipExporter.BuildArguments("in.mp4", "out.tmp", 1500, 0, 1);
        Assert.Contains("-ss", fromStart);
        Assert.DoesNotContain("-t", fromStart);   // to the end of the file
    }

    [Theory]
    [InlineData(0.1, "atempo=0.5,atempo=0.5,atempo=0.5,atempo=0.8")]
    [InlineData(4, "atempo=2,atempo=2")]
    [InlineData(1.5, "atempo=1.5")]
    public void Audio_speed_is_split_into_stages_ffmpeg_accepts(double speed, string expected)
    {
        var args = FfmpegClipExporter.BuildArguments("in.mp4", "out.tmp", 0, 0, speed);
        Assert.Equal(expected, args[args.ToList().IndexOf("-af") + 1]);
    }

    [Fact]
    public void ffmpeg_is_found_in_the_given_folders_and_reported_missing_otherwise()
    {
        var folder = Path.Combine(_root, "tools");
        Directory.CreateDirectory(folder);
        Assert.Null(new FfmpegClipExporter(searchFolders: new[] { folder }).FindTool());

        var exe = Path.Combine(folder, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        File.WriteAllBytes(exe, new byte[] { 1 });
        Assert.Equal(exe, new FfmpegClipExporter(searchFolders: new[] { folder }).FindTool());
        Assert.Null(new FfmpegClipExporter(tool: Path.Combine(folder, "nothing.exe")).FindTool());
    }

    [Fact]
    public void A_cut_copy_is_filed_just_below_its_source_and_never_becomes_the_newest_clip()
    {
        var library = Library();
        library.Add(Clip(1), "Alpha vs Bravo", "ESM 2026");
        var source = library.Add(Clip(2), "Alpha vs Bravo", "ESM 2026");
        source = source with { Title = "Nice save", StartMs = 1000, EndMs = 3000, Speed = 0.5, Tags = new[] { "Best plays" } };
        library.Update(source);

        var temp = library.TempExportPath();
        File.WriteAllBytes(temp, new byte[] { 9, 9, 9 });
        var copy = library.AdoptCopy(library.Find(source.Id)!, temp, "Nice save (cut)");

        Assert.NotNull(copy);
        Assert.Equal("3-Replay-Replay.mp4", copy!.File);
        Assert.True(File.Exists(library.PathOf(copy)));
        Assert.False(File.Exists(temp));
        Assert.Equal(0, copy.StartMs);
        Assert.Equal(0, copy.EndMs);
        Assert.Equal(1, copy.Speed);
        Assert.Equal("Alpha vs Bravo", copy.Match);
        Assert.Equal(new[] { "Best plays" }, copy.Tags);
        Assert.Equal(source.Id, library.Latest()!.Id);   // the stream keeps showing what it showed
        Assert.Equal(new[] { "2-Replay-Replay.mp4", "3-Replay-Replay.mp4", "1-Replay-Replay.mp4" }, library.Clips().Select(c => c.File));
        Assert.Equal(1000, library.Find(source.Id)!.StartMs);   // the original keeps its own trim
    }

    // ---- how long the clips on the stream still last ------------------------------------------------------

    private static ReplayClip Timed(string title, long durationMs, long startMs = 0, long endMs = 0, double speed = 1)
        => new() { Id = title, File = title + ".mp4", Title = title, DurationMs = durationMs, StartMs = startMs, EndMs = endMs, Speed = speed };

    private static readonly long Lead = ReplayRun.StartLagMs;
    private static readonly long Gap = ReplayRun.ClipLagMs;

    [Fact]
    public void Nothing_is_playing_without_a_request_or_after_a_stop()
    {
        Assert.False(ReplayRun.At(null, 1000).IsPlaying);
        Assert.False(ReplayRun.At(new ReplayRequest(1, 0, Array.Empty<ReplayClip>()), 1000).IsPlaying);
    }

    [Fact]
    public void The_run_adds_up_the_shown_lengths_with_trim_and_speed_and_the_lags()
    {
        var request = new ReplayRequest(1, 10_000, new[]
        {
            Timed("A", 8000),                                             // 8 s
            Timed("B", 20_000, startMs: 1000, endMs: 5000, speed: 0.5),   // 4 s of file at half speed = 8 s
        });

        var start = ReplayRun.At(request, 10_000);
        Assert.True(start.IsPlaying);
        Assert.Equal(0, start.Index);
        Assert.Equal(8000 + Gap + Lead + 8000 + Gap, start.TotalMs);
        Assert.Equal(start.TotalMs, start.RemainingMs);
        Assert.False(start.LengthUnknown);
        Assert.Equal(new long[] { 8000 + Gap + Lead, 8000 + Gap }, start.Segments.Select(s => s.LengthMs));

        // still on the first clip one millisecond before it ends, on the second right after
        var firstEnds = 10_000 + 8000 + Gap + Lead;
        Assert.Equal(0, ReplayRun.At(request, firstEnds - 1).Index);
        var second = ReplayRun.At(request, firstEnds);
        Assert.Equal(1, second.Index);
        Assert.Equal("B", second.Current!.Title);
        Assert.Equal(0, second.ClipElapsedMs);
        Assert.Equal(1000, ReplayRun.At(request, firstEnds + 1000).ClipElapsedMs);

        // gone when the last clip has ended
        Assert.True(ReplayRun.At(request, 10_000 + start.TotalMs - 1).IsPlaying);
        Assert.False(ReplayRun.At(request, 10_000 + start.TotalMs).IsPlaying);
    }

    [Fact]
    public void A_clip_of_unknown_length_is_flagged_until_the_library_has_learned_it()
    {
        var request = new ReplayRequest(1, 0, new[] { Timed("A", 0), Timed("B", 4000) });

        var unknown = ReplayRun.At(request, 0);
        Assert.True(unknown.LengthUnknown);
        Assert.Equal(Lead + Gap + 4000 + Gap, unknown.TotalMs);   // at least this much

        var learned = ReplayRun.At(request, 0, clip => clip.Id == "A" ? clip with { DurationMs = 6000 } : clip);
        Assert.False(learned.LengthUnknown);
        Assert.Equal(Lead + 6000 + Gap + 4000 + Gap, learned.TotalMs);
    }

    [Fact]
    public void The_view_model_counts_the_time_down_and_turns_urgent_for_the_last_seconds()
    {
        var now = 100_000L;
        var library = new ReplayLibrary(_root, () => 100_000);
        var clip = library.Add(Clip(1), "", "");
        library.Update(clip with { DurationMs = 20_000 });
        var tab = new ReplayViewModel(library, new Recorder(), null, () => ("", ""), new Asks(true), null, null, null, () => now);

        tab.UpdateRun();
        Assert.False(tab.RunActive);
        Assert.True(tab.RunIdle);
        Assert.Equal("", tab.RunPillText);

        library.Play(new[] { library.Find(clip.Id)! });
        tab.UpdateRun();
        Assert.True(tab.RunActive);
        Assert.False(tab.RunIdle);
        Assert.False(tab.RunUrgent);
        Assert.Equal("0:21", tab.RunRemainingText);   // 20.75 s, rounded up
        Assert.Equal("of 0:21", tab.RunTotalText);
        Assert.Equal("Replay clips · 0:21", tab.RunPillText);
        Assert.Equal(new long[] { 20_000 + Gap + Lead }, tab.RunSegments);
        Assert.False(tab.HasRunNote);

        now += 10_000;   // 10.75 s left
        tab.UpdateRun();
        Assert.Equal("0:11", tab.RunRemainingText);
        Assert.False(tab.RunUrgent);

        now += 1000;   // 9.75 s left
        tab.UpdateRun();
        Assert.Equal("0:10", tab.RunRemainingText);
        Assert.True(tab.RunUrgent);

        now += 10_000;   // over
        tab.UpdateRun();
        Assert.False(tab.RunActive);
        Assert.Equal("", tab.RunPillText);
    }

    [Fact]
    public void Stopping_clears_the_run_and_a_clip_of_unknown_length_says_so()
    {
        var now = 100_000L;
        var library = new ReplayLibrary(_root, () => 100_000);
        var clip = library.Add(Clip(1), "", "");   // the file is not a real video: its length is unknown
        var tab = new ReplayViewModel(library, new Recorder(), null, () => ("", ""), new Asks(true), null, null, null, () => now);

        library.Play(new[] { library.Find(clip.Id)! });
        tab.UpdateRun();
        Assert.True(tab.RunActive);
        Assert.True(tab.HasRunNote);

        Assert.True(tab.RunCommand(ControlCatalog.ReplayStop, null).Ok);
        Assert.False(tab.RunActive);
        Assert.Equal("", tab.RunRemainingText);
    }

    private sealed class FakeExporter : IClipExporter
    {
        public string? Tool = "ffmpeg.exe";
        public ClipExportResult Result = new(true);
        public (long Start, long End, double Speed)? Called;

        public string? FindTool() => Tool;

        public Task<ClipExportResult> ExportAsync(string input, string output, long startMs, long endMs, double speed, CancellationToken cancel = default)
        {
            Called = (startMs, endMs, speed);
            if (Result.Ok) File.WriteAllBytes(output, new byte[] { 1, 2, 3, 4 });
            return Task.FromResult(Result);
        }
    }

    private sealed class Speaker : IMessages
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Infos = new();
        public Task ShowErrorAsync(string title, string message) { Errors.Add(message); return Task.CompletedTask; }
        public Task ShowInfoAsync(string title, string message) { Infos.Add(message); return Task.CompletedTask; }
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
    }

    private (ReplayViewModel Tab, ReplayLibrary Library, FakeExporter Exporter, Speaker Messages) CuttingTab()
    {
        var library = Library();
        var exporter = new FakeExporter();
        var messages = new Speaker();
        var tab = new ReplayViewModel(library, new Recorder(), null, () => ("Alpha vs Bravo", "ESM 2026"), messages, null, null, exporter);
        tab.OnClipArrived(Clip(1));
        tab.Selected = tab.Clips[0];
        return (tab, library, exporter, messages);
    }

    [Fact]
    public async Task Saving_the_shown_part_adds_a_new_clip_and_leaves_the_original_alone()
    {
        var (tab, library, exporter, messages) = CuttingTab();
        var originalId = tab.Selected!.Id;
        tab.EditStart = 1m;
        tab.EditEnd = 3m;
        tab.EditSpeed = 0.5m;

        await tab.SaveCopyCommand.ExecuteAsync(null);

        Assert.Equal((1000L, 3000L, 0.5), exporter.Called);
        Assert.Empty(messages.Errors);
        Assert.Equal(2, library.Clips().Count);
        var copy = library.Clips().Single(c => c.Id != originalId);
        Assert.Equal("Clip 1 (cut)", copy.Title);
        Assert.Equal(copy.Id, tab.Selected!.Id);   // the new clip is the one shown
        var original = library.Find(originalId)!;
        Assert.Equal(1000, original.StartMs);
        Assert.Equal(0.5, original.Speed);
        Assert.False(tab.IsExporting);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Playlist"), "*.tmp"));
    }

    [Fact]
    public async Task Saving_with_nothing_to_cut_or_without_ffmpeg_says_so_and_does_nothing()
    {
        var (tab, library, exporter, messages) = CuttingTab();
        await tab.SaveCopyCommand.ExecuteAsync(null);   // no trim, normal speed
        Assert.Single(messages.Infos);
        Assert.Null(exporter.Called);

        tab.EditStart = 1m;
        exporter.Tool = null;
        await tab.SaveCopyCommand.ExecuteAsync(null);
        Assert.Contains(messages.Errors, m => m.Contains("ffmpeg"));
        Assert.Null(exporter.Called);
        Assert.Single(library.Clips());
    }

    [Fact]
    public async Task A_failed_cut_is_reported_and_leaves_no_half_written_file()
    {
        var (tab, library, exporter, messages) = CuttingTab();
        tab.EditEnd = 2m;
        exporter.Result = new ClipExportResult(false, "Unknown encoder 'libx264'");

        await tab.SaveCopyCommand.ExecuteAsync(null);

        Assert.Contains(messages.Errors, m => m.Contains("libx264"));
        Assert.Single(library.Clips());
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Playlist"), "*.tmp"));
        Assert.True(tab.CanSaveCopy);
    }

    [Fact]
    public async Task A_real_ffmpeg_cuts_the_part_and_changes_the_speed()
    {
        var exporter = new FfmpegClipExporter();
        if (exporter.FindTool() is not { } tool) return;   // ffmpeg is not installed on this machine: nothing to try

        var source = Path.Combine(_root, "source.mp4");
        var made = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(tool,
            $"-hide_banner -loglevel error -y -f lavfi -i testsrc=duration=4:size=320x180:rate=25 -f lavfi -i sine=duration=4 -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest \"{source}\"")
        { UseShellExecute = false, CreateNoWindow = true })!;
        made.WaitForExit();
        Assert.Equal(0, made.ExitCode);

        var output = Path.Combine(_root, "cut.mp4");
        var result = await exporter.ExportAsync(source, output, 1000, 3000, 2);   // two seconds at double speed = one second
        Assert.True(result.Ok, result.Error);
        var length = Mp4Info.ReadDurationMs(output);
        Assert.InRange(length, 800, 1300);

        var missing = await exporter.ExportAsync(Path.Combine(_root, "nothing.mp4"), Path.Combine(_root, "none.mp4"), 0, 0, 1);
        Assert.False(missing.Ok);
        Assert.False(File.Exists(Path.Combine(_root, "none.mp4")));
    }
}

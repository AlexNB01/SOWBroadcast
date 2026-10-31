using SowBroadcast.App.Services;
using SowBroadcast.Core.Diagnostics;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Tests;

public sealed class UpdateServiceTests
{
    private sealed class FakeUpdater : IAppUpdater
    {
        public bool IsInstalled { get; set; } = true;
        public UpdateOffer? Offer { get; set; }
        public Exception? CheckFails { get; set; }
        public Exception? DownloadFails { get; set; }
        public int Checks { get; private set; }
        public List<int> Progress { get; } = new();
        public bool Applied { get; private set; }

        public Task<UpdateOffer?> CheckAsync(CancellationToken cancellation)
        {
            Checks++;
            if (CheckFails is not null) throw CheckFails;
            return Task.FromResult(Offer);
        }

        public Task DownloadAsync(UpdateOffer offer, Action<int> progress, CancellationToken cancellation)
        {
            if (DownloadFails is not null) throw DownloadFails;
            foreach (var step in new[] { 0, 50, 100 }) { Progress.Add(step); progress(step); }
            return Task.CompletedTask;
        }

        public void ApplyAndRestart(UpdateOffer offer) => Applied = true;
    }

    [Fact]
    public async Task A_copy_that_was_not_installed_cannot_update_itself_and_does_not_ask_github()
    {
        var updater = new FakeUpdater { IsInstalled = false };

        var result = await new UpdateService(updater, "3.0.0").CheckAsync();

        Assert.Equal(UpdateCheckStatus.NotInstalled, result.Status);
        Assert.Contains("installed program", result.Message);
        Assert.Equal(0, updater.Checks);
    }

    [Fact]
    public async Task The_latest_version_says_so_and_a_newer_one_is_offered_with_its_size()
    {
        var updater = new FakeUpdater();
        var service = new UpdateService(updater, "3.0.0");

        var same = await service.CheckAsync();
        Assert.Equal(UpdateCheckStatus.UpToDate, same.Status);
        Assert.Contains("3.0.0 is the latest", same.Message);
        Assert.Null(same.Offer);

        updater.Offer = new UpdateOffer("3.1.0", 150L * 1024 * 1024, new object());
        var newer = await service.CheckAsync();
        Assert.Equal(UpdateCheckStatus.Available, newer.Status);
        Assert.Contains("3.1.0 is available", newer.Message);
        Assert.Contains("you have 3.0.0", newer.Message);
        Assert.Contains("150 MB", newer.Message);
        Assert.Same(updater.Offer, newer.Offer);
    }

    [Fact]
    public async Task A_failed_check_gives_the_first_line_of_the_reason_not_a_stack_trace()
    {
        var updater = new FakeUpdater { CheckFails = new HttpRequestException("No such host is known.\nat System.Net...") };

        var result = await new UpdateService(updater, "3.0.0").CheckAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal("Could not check for updates: No such host is known.", result.Message);
    }

    [Fact]
    public async Task Installing_downloads_reports_progress_and_restarts_and_a_failure_is_reported_without_restarting()
    {
        var updater = new FakeUpdater();
        var service = new UpdateService(updater, "3.0.0");
        var offer = new UpdateOffer("3.1.0", null, new object());
        var seen = new List<int>();

        var ok = await service.InstallAsync(offer, seen.Add);

        Assert.Null(ok);
        Assert.Equal(new[] { 0, 50, 100 }, seen);
        Assert.True(updater.Applied);

        var failing = new FakeUpdater { DownloadFails = new IOException("The disk is full.") };
        var problem = await new UpdateService(failing, "3.0.0").InstallAsync(offer, _ => { });
        Assert.Equal("The update could not be installed: The disk is full.", problem);
        Assert.False(failing.Applied);
    }

    [Fact]
    public async Task A_cancelled_check_is_not_reported_as_a_failure()
    {
        var updater = new FakeUpdater { CheckFails = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() => new UpdateService(updater, "3.0.0").CheckAsync());
    }

    [Fact]
    public async Task A_surface_that_cannot_ask_never_says_yes()
    {
        IMessages messages = new SilentMessages();

        Assert.False(await messages.ConfirmAsync("Update", "Install now?"));
    }

    private sealed class SilentMessages : IMessages
    {
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
        public Task ShowInfoAsync(string title, string message) => Task.CompletedTask;
    }
}

public sealed class CrashReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-crash-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private static Exception Thrown()
    {
        try { throw new InvalidOperationException("Something broke"); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void A_terminating_error_writes_a_report_and_a_note_that_the_next_start_takes_once()
    {
        var path = CrashReports.Write(_dir, Thrown(), "3.0.0", "Windows 11", new DateTime(2026, 10, 16, 21, 0, 0), terminating: true);

        Assert.NotNull(path);
        var text = File.ReadAllText(path!);
        Assert.Contains("Version: 3.0.0", text);
        Assert.Contains("Windows 11", text);
        Assert.Contains("InvalidOperationException: Something broke", text);

        Assert.Equal(path, CrashReports.TakePending(_dir));
        Assert.Null(CrashReports.TakePending(_dir));                 // shown once
        Assert.True(File.Exists(path));                              // the report itself stays
    }

    [Fact]
    public void An_error_that_does_not_end_the_program_leaves_a_report_but_no_note()
    {
        CrashReports.Write(_dir, Thrown(), "3.0.0", "Windows 11", DateTime.Now, terminating: false);

        Assert.Null(CrashReports.TakePending(_dir));
        Assert.Single(Directory.GetFiles(_dir, "crash-*.txt"));
    }

    [Fact]
    public void Without_a_note_or_with_a_note_for_a_deleted_report_there_is_nothing_to_show()
    {
        Assert.Null(CrashReports.TakePending(_dir));

        var path = CrashReports.Write(_dir, null, "3.0.0", "Windows", DateTime.Now, terminating: true)!;
        File.Delete(path);
        Assert.Null(CrashReports.TakePending(_dir));
    }

    [Fact]
    public void Only_the_newest_ten_reports_are_kept()
    {
        for (var i = 0; i < 14; i++)
            CrashReports.Write(_dir, Thrown(), "3.0.0", "Windows", new DateTime(2026, 10, 16, 20, 0, i), terminating: false);

        var kept = Directory.GetFiles(_dir, "crash-*.txt").Select(Path.GetFileName).OrderBy(n => n).ToArray();

        Assert.Equal(10, kept.Length);
        Assert.Equal("crash-20261016-200013.txt", kept[^1]);
    }

    [Fact]
    public void A_folder_that_cannot_be_written_gives_no_report_instead_of_another_crash()
    {
        var blocked = Path.Combine(_dir, "file");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocked, "not a folder");

        Assert.Null(CrashReports.Write(Path.Combine(blocked, "Logs"), Thrown(), "3.0.0", "Windows", DateTime.Now, terminating: true));
    }
}

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-appset-" + Guid.NewGuid().ToString("N"));

    public AppSettingsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    [Fact]
    public void The_startup_check_is_on_by_default_and_switching_it_off_is_kept()
    {
        var path = Path.Combine(_dir, "app-settings.json");
        Assert.True(AppSettings.Load(path).CheckForUpdatesAtStartup);

        new AppSettings { CheckForUpdatesAtStartup = false }.Save(path);

        Assert.False(AppSettings.Load(path).CheckForUpdatesAtStartup);
    }

    [Fact]
    public void A_settings_file_without_the_choice_keeps_the_startup_check_on()
    {
        var path = Path.Combine(_dir, "app-settings.json");
        File.WriteAllText(path, "{ }");

        Assert.True(AppSettings.Load(path).CheckForUpdatesAtStartup);
    }

    [Fact]
    public void A_damaged_file_gives_the_defaults()
    {
        var path = Path.Combine(_dir, "app-settings.json");
        File.WriteAllText(path, "{ nope");

        Assert.True(AppSettings.Load(path).CheckForUpdatesAtStartup);
    }
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Tests;

public sealed class DataFolderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-data-" + Guid.NewGuid().ToString("N"));

    public DataFolderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    [Fact]
    public void An_override_always_wins()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Data"));

        Assert.Equal("D:/elsewhere", AppPaths.ResolveDataRoot("D:/elsewhere", _dir, "C:/Users/x/AppData/Local"));
    }

    [Fact]
    public void An_installed_program_keeps_its_data_in_the_folder_next_to_it()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Data"));

        Assert.Equal(Path.Combine(_dir, "Data"), AppPaths.ResolveDataRoot(null, _dir, "C:/Users/x/AppData/Local"));
    }

    [Fact]
    public void A_copy_without_a_data_folder_keeps_the_per_user_location_so_existing_data_is_not_lost()
    {
        Assert.Equal(Path.Combine("C:/Users/x/AppData/Local", "SOWBroadcast"), AppPaths.ResolveDataRoot(null, _dir, "C:/Users/x/AppData/Local"));
    }
}

public sealed class InstallerUpdaterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sowb-updater-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, (HttpStatusCode Status, byte[] Body)> _routes = new();
    private readonly List<string> _requests = new();
    private readonly List<(string File, string Arguments)> _started = new();

    private const string ReleaseApi = "https://api.github.com/repos/owner/repo/releases/latest";
    private const string InstallerUrl = "https://github.com/owner/repo/releases/download/v3.1.0/SOWBroadcast-Setup-3.1.0.exe";
    private const string ChecksumUrl = InstallerUrl + ".sha256";

    public InstallerUpdaterTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "program"));
        Directory.CreateDirectory(Path.Combine(_dir, "download"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly InstallerUpdaterTests _owner;
        public FakeHandler(InstallerUpdaterTests owner) => _owner = owner;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            _owner._requests.Add(url);
            if (!_owner._routes.TryGetValue(url, out var route)) route = (HttpStatusCode.NotFound, Array.Empty<byte>());
            return Task.FromResult(new HttpResponseMessage(route.Status) { Content = new ByteArrayContent(route.Body) });
        }
    }

    private static readonly byte[] Installer = Enumerable.Range(0, 4000).Select(i => (byte)(i % 251)).ToArray();

    private static string ReleaseJson(string tag = "v3.1.0", bool withInstaller = true, bool withChecksum = true, long? size = null, string? installerUrl = null) =>
        "{ \"tag_name\": \"" + tag + "\", \"assets\": [" +
        "{ \"name\": \"manifest.json\", \"browser_download_url\": \"https://github.com/owner/repo/releases/download/" + tag + "/manifest.json\", \"size\": 10 }" +
        (withInstaller ? ",{ \"name\": \"SOWBroadcast-Setup-3.1.0.exe\", \"browser_download_url\": \"" + (installerUrl ?? InstallerUrl) + "\", \"size\": " + (size ?? Installer.Length) + " }" : "") +
        (withChecksum ? ",{ \"name\": \"SOWBroadcast-Setup-3.1.0.exe.sha256\", \"browser_download_url\": \"" + ChecksumUrl + "\", \"size\": 70 }" : "") +
        "] }";

    private InstallerUpdater Create(string current = "3.0.0") =>
        new("owner/repo", current, Path.Combine(_dir, "program"), new HttpClient(new FakeHandler(this)),
            (file, arguments) => _started.Add((file, arguments)), Path.Combine(_dir, "download"));

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    [Theory]
    [InlineData("v3.1.0", "3.1.0")]
    [InlineData("3.1.0", "3.1.0")]
    [InlineData("v3.2.0-beta.1", "3.2.0")]
    [InlineData("V10.0.1+build5", "10.0.1")]
    public void Release_tags_are_read_as_versions(string tag, string version)
    {
        Assert.True(ReleaseInfo.TryParseVersion(tag, out var parsed));
        Assert.Equal(Version.Parse(version), parsed);
    }

    [Fact]
    public void A_release_answer_is_read_with_its_installer_and_checksum_and_a_broken_one_is_refused()
    {
        var release = ReleaseInfo.Parse(ReleaseJson());

        Assert.Equal(new Version(3, 1, 0), release.Version);
        Assert.Equal("SOWBroadcast-Setup-3.1.0.exe", release.Installer!.Name);
        Assert.Equal(Installer.Length, release.Installer.Size);
        Assert.EndsWith(".exe.sha256", release.Checksum!.Name);
        Assert.Null(ReleaseInfo.Parse(ReleaseJson(withInstaller: false, withChecksum: false)).Installer);
        Assert.Throws<FormatException>(() => ReleaseInfo.Parse("not json"));
        Assert.Throws<FormatException>(() => ReleaseInfo.Parse("{ \"tag_name\": \"nightly\" }"));
        Assert.Throws<FormatException>(() => ReleaseInfo.Parse("{ }"));
    }

    [Fact]
    public void Installed_means_the_uninstaller_is_next_to_the_program()
    {
        var updater = Create();
        Assert.False(updater.IsInstalled);

        File.WriteAllText(Path.Combine(_dir, "program", "unins000.exe"), "x");

        Assert.True(updater.IsInstalled);
    }

    [Fact]
    public async Task A_newer_release_is_offered_and_the_same_or_an_older_one_is_not()
    {
        _routes[ReleaseApi] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(ReleaseJson()));

        var offer = await Create("3.0.0").CheckAsync(CancellationToken.None);
        Assert.Equal("3.1.0", offer!.Version);
        Assert.Equal(Installer.Length, offer.SizeBytes);

        Assert.Null(await Create("3.1.0").CheckAsync(CancellationToken.None));
        Assert.Null(await Create("3.2.0").CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Missing_releases_and_releases_without_an_installer_are_reported_in_words()
    {
        var none = await Assert.ThrowsAsync<InvalidOperationException>(() => Create().CheckAsync(CancellationToken.None));
        Assert.Contains("No version has been published", none.Message);

        _routes[ReleaseApi] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(ReleaseJson(withInstaller: false)));
        var noInstaller = await Assert.ThrowsAsync<InvalidOperationException>(() => Create().CheckAsync(CancellationToken.None));
        Assert.Contains("no installer", noInstaller.Message);
    }

    [Fact]
    public async Task The_installer_is_downloaded_checked_against_its_checksum_and_started_with_silent_arguments()
    {
        _routes[ReleaseApi] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(ReleaseJson()));
        _routes[InstallerUrl] = (HttpStatusCode.OK, Installer);
        _routes[ChecksumUrl] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(Sha(Installer) + "  SOWBroadcast-Setup-3.1.0.exe\n"));
        var updater = Create();
        var offer = (await updater.CheckAsync(CancellationToken.None))!;
        var progress = new List<int>();

        await updater.DownloadAsync(offer, progress.Add, CancellationToken.None);
        updater.ApplyAndRestart(offer);

        var file = Path.Combine(_dir, "download", "SOWBroadcast-Setup-3.1.0.exe");
        Assert.Equal(Installer, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".part"));
        Assert.Equal(0, progress.First());
        Assert.Equal(100, progress.Last());
        var started = Assert.Single(_started);
        Assert.Equal(file, started.File);
        Assert.Contains("/SILENT", started.Arguments);
        Assert.Contains("/CLOSEAPPLICATIONS", started.Arguments);
    }

    [Fact]
    public async Task A_download_that_does_not_match_its_checksum_or_size_is_thrown_away_and_never_started()
    {
        _routes[ReleaseApi] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(ReleaseJson()));
        _routes[InstallerUrl] = (HttpStatusCode.OK, Installer);
        _routes[ChecksumUrl] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(new string('0', 64)));
        var updater = Create();
        var offer = (await updater.CheckAsync(CancellationToken.None))!;

        var wrong = await Assert.ThrowsAsync<InvalidOperationException>(() => updater.DownloadAsync(offer, _ => { }, CancellationToken.None));
        Assert.Contains("checksum", wrong.Message);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "download")));
        Assert.Throws<InvalidOperationException>(() => updater.ApplyAndRestart(offer));

        _routes[ReleaseApi] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(ReleaseJson(withChecksum: false, size: Installer.Length + 10)));
        var shortUpdater = Create();
        var shortOffer = (await shortUpdater.CheckAsync(CancellationToken.None))!;
        var incomplete = await Assert.ThrowsAsync<InvalidOperationException>(() => shortUpdater.DownloadAsync(shortOffer, _ => { }, CancellationToken.None));
        Assert.Contains("incomplete", incomplete.Message);
        Assert.Empty(_started);
    }

    [Fact]
    public async Task A_release_that_points_to_another_server_is_not_downloaded()
    {
        _routes[ReleaseApi] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(ReleaseJson(withChecksum: false, installerUrl: "https://evil.example/SOWBroadcast-Setup-3.1.0.exe")));
        var updater = Create();
        var offer = (await updater.CheckAsync(CancellationToken.None))!;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => updater.DownloadAsync(offer, _ => { }, CancellationToken.None));

        Assert.Contains("not GitHub's", failure.Message);
        Assert.DoesNotContain(_requests, r => r.Contains("evil.example"));
    }

    [Fact]
    public async Task A_release_without_a_checksum_is_accepted_when_the_size_is_right()
    {
        _routes[ReleaseApi] = (HttpStatusCode.OK, Encoding.UTF8.GetBytes(ReleaseJson(withChecksum: false)));
        _routes[InstallerUrl] = (HttpStatusCode.OK, Installer);
        var updater = Create();
        var offer = (await updater.CheckAsync(CancellationToken.None))!;

        await updater.DownloadAsync(offer, _ => { }, CancellationToken.None);
        updater.ApplyAndRestart(offer);

        Assert.Single(_started);
    }
}

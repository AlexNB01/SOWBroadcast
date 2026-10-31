using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace SowBroadcast.App.Services;

/// <summary>A file attached to a GitHub release.</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>What the program needs to know about the latest GitHub release.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, ReleaseAsset? Installer, ReleaseAsset? Checksum, string Notes = "")
{
    private const int MaxNotes = 4000;

    /// <summary>Reads GitHub's "latest release" answer. The installer is <c>SOWBroadcast-Setup-&lt;version&gt;.exe</c>, its checksum <c>….exe.sha256</c>.</summary>
    /// <exception cref="FormatException">The text is not a release with a usable version tag.</exception>
    public static ReleaseInfo Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (System.Text.Json.JsonException ex) { throw new FormatException("The answer from GitHub could not be read.", ex); }

        var tag = root?["tag_name"]?.GetValue<string>() ?? throw new FormatException("The release has no version tag.");
        if (!TryParseVersion(tag, out var version)) throw new FormatException($"The release tag \"{tag}\" is not a version.");

        ReleaseAsset? installer = null, checksum = null;
        foreach (var asset in root!["assets"]?.AsArray() ?? new JsonArray())
        {
            var name = asset?["name"]?.GetValue<string>() ?? "";
            var url = asset?["browser_download_url"]?.GetValue<string>() ?? "";
            var size = asset?["size"]?.GetValue<long>() ?? 0;
            if (name.StartsWith("SOWBroadcast-Setup-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                installer = new ReleaseAsset(name, url, size);
            else if (name.StartsWith("SOWBroadcast-Setup-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase))
                checksum = new ReleaseAsset(name, url, size);
        }

        // What the release says is shown to the operator as plain text; a very long text is cut.
        var notes = (root["body"] is JsonValue body && body.TryGetValue<string>(out var text) ? text : "").Replace("\r\n", "\n").Trim();
        if (notes.Length > MaxNotes) notes = notes[..MaxNotes].TrimEnd() + "…";
        return new ReleaseInfo(version, tag, installer, checksum, notes);
    }

    /// <summary>"v3.1.0" or "3.1.0-beta" to 3.1.0 (a pre-release suffix is ignored).</summary>
    public static bool TryParseVersion(string text, out Version version)
    {
        var core = text.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(core, out version!);
    }
}

/// <summary>
/// Updates the installed program by fetching the installer of the newest GitHub release and running it (EHD-15). The installer replaces the
/// program in its own folder and starts it again; nothing is downloaded before the user asks for it.
/// </summary>
public sealed class InstallerUpdater : IAppUpdater
{
    private const string InstallerArguments = "/SILENT /SUPPRESSMSGBOXES /NOCANCEL /CLOSEAPPLICATIONS";

    private readonly string _repository;
    private readonly Version _current;
    private readonly string _programFolder;
    private readonly HttpClient _http;
    private readonly Action<string, string> _start;
    private readonly string _downloadFolder;
    private string? _downloaded;

    /// <param name="start">Starts a program with arguments (tests record the call instead of running an installer).</param>
    public InstallerUpdater(string repository, string currentVersion, string programFolder, HttpClient? http = null,
        Action<string, string>? start = null, string? downloadFolder = null)
    {
        _repository = repository;
        _current = ReleaseInfo.TryParseVersion(currentVersion, out var v) ? v : new Version(0, 0);
        _programFolder = programFolder;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"SOWBroadcast/{currentVersion} (+https://suomiow.fi)");
        _start = start ?? ((file, arguments) => Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true }));
        _downloadFolder = downloadFolder ?? Path.Combine(Path.GetTempPath(), "SOWBroadcast-update");
    }

    /// <summary>True when the program was put here by the installer (which leaves its uninstaller next to it).</summary>
    public bool IsInstalled => File.Exists(Path.Combine(_programFolder, "unins000.exe"));

    public async Task<UpdateOffer?> CheckAsync(CancellationToken cancellation)
    {
        using var response = await _http.GetAsync($"https://api.github.com/repos/{_repository}/releases/latest", cancellation).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw new InvalidOperationException("No version has been published yet.");
        response.EnsureSuccessStatusCode();
        var release = ReleaseInfo.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));

        if (release.Version <= _current) return null;
        if (release.Installer is null) throw new InvalidOperationException($"Version {release.Version} has no installer attached.");
        return new UpdateOffer(release.Version.ToString(), release.Installer.Size, release, release.Notes);
    }

    public async Task DownloadAsync(UpdateOffer offer, Action<int> progress, CancellationToken cancellation)
    {
        var release = (ReleaseInfo)offer.Handle;
        var installer = release.Installer ?? throw new InvalidOperationException("The release has no installer.");
        EnsureGitHub(installer.Url);

        Directory.CreateDirectory(_downloadFolder);
        var target = Path.Combine(_downloadFolder, Path.GetFileName(installer.Name));
        var temp = target + ".part";
        try
        {
            using (var response = await _http.GetAsync(installer.Url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? installer.Size;
                await using var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
                await using var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[256 * 1024];
                long done = 0;
                int read, last = -1;
                progress(0);
                while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                    done += read;
                    var percent = total > 0 ? (int)Math.Min(100, done * 100 / total) : 0;
                    if (percent != last) { last = percent; progress(percent); }
                }
            }

            if (installer.Size > 0 && new FileInfo(temp).Length != installer.Size)
                throw new InvalidOperationException("The download is incomplete.");
            await VerifyChecksumAsync(release, temp, cancellation).ConfigureAwait(false);

            File.Move(temp, target, overwrite: true);
            _downloaded = target;
        }
        catch
        {
            try { File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
            throw;
        }
    }

    private async Task VerifyChecksumAsync(ReleaseInfo release, string file, CancellationToken cancellation)
    {
        if (release.Checksum is null) return;   // an older release without one: the size check has to do
        EnsureGitHub(release.Checksum.Url);
        var text = await _http.GetStringAsync(release.Checksum.Url, cancellation).ConfigureAwait(false);
        var expected = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        await using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation).ConfigureAwait(false));
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The downloaded installer does not match its checksum, so it was not used.");
    }

    // Only GitHub's own addresses: a release cannot point the program at some other server.
    private static void EnsureGitHub(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !(uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The release points to an address that is not GitHub's, so it was not downloaded.");
    }

    /// <summary>Starts the downloaded installer (it closes this program, replaces it and starts it again).</summary>
    public void ApplyAndRestart(UpdateOffer offer)
    {
        if (_downloaded is null || !File.Exists(_downloaded)) throw new InvalidOperationException("The installer has not been downloaded.");
        _start(_downloaded, InstallerArguments);
    }
}

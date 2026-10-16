using System.Text.Json.Nodes;
using SowBroadcast.Core.Assets;
using SowBroadcast.Integrations.Link;

namespace SowBroadcast.Integrations.Assets;

/// <summary>What an update of the hero and map list did.</summary>
public sealed record ManifestUpdateResult(bool Ok, string Message, IReadOnlyList<string> Added);

/// <summary>
/// Fetches the newest asset manifest from the latest GitHub release (the file <c>manifest.json</c> attached to it) and, when it is
/// newer than the one in use, downloads the pictures it names that are not installed and brings the catalogue in line (EHD-10).
/// Only ever started by the user: the program makes no network calls of its own accord.
/// </summary>
public sealed class ManifestUpdater
{
    public const string ManifestAssetName = "manifest.json";
    private const int MaxManifestBytes = 1024 * 1024;
    private const int MaxImageBytes = 5 * 1024 * 1024;

    private readonly IHttpFetcher _http;
    private readonly AssetService _assets;
    private readonly string _repository;

    /// <param name="repository">"owner/name" of the GitHub repository whose releases carry the manifest.</param>
    public ManifestUpdater(IHttpFetcher http, AssetService assets, string repository)
    {
        _http = http;
        _assets = assets;
        _repository = repository;
    }

    public async Task<ManifestUpdateResult> UpdateAsync(CancellationToken cancellation = default)
    {
        var release = await _http.GetAsync(new Uri($"https://api.github.com/repos/{_repository}/releases/latest"), MaxManifestBytes, cancellation);
        if (release.Status == 404) return Fail("No release with a hero and map list has been published yet.");
        if (release.Status != 200) return Fail(release.Error ?? $"GitHub answered {release.Status}.");

        string? manifestUrl;
        try
        {
            manifestUrl = (JsonNode.Parse(release.Text)?["assets"] as JsonArray)?
                .FirstOrDefault(a => a?["name"]?.GetValue<string>() == ManifestAssetName)?["browser_download_url"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return Fail("The answer from GitHub could not be read.");
        }
        if (manifestUrl is null) return Fail($"The latest release has no {ManifestAssetName}.");
        if (!IsGitHubUrl(manifestUrl)) return Fail("The release points the manifest to an address that is not GitHub's; it was not downloaded.");

        var file = await _http.GetAsync(new Uri(manifestUrl), MaxManifestBytes, cancellation);
        if (file.Status != 200) return Fail(file.Error ?? $"The manifest could not be downloaded ({file.Status}).");

        AssetManifest manifest;
        try { manifest = AssetManifest.Parse(file.Text); }
        catch (FormatException ex) { return Fail("The manifest in the release is not usable: " + ex.Message); }

        if (manifest.Revision <= _assets.ManifestRevision)
            return new ManifestUpdateResult(true, $"The hero and map list is up to date (revision {_assets.ManifestRevision}).", Array.Empty<string>());

        // Pictures first, so the index files written when the manifest is applied can point at them.
        var missing = 0;
        foreach (var category in AssetCategories.All)
            foreach (var entry in manifest.Of(category))
            {
                if (!await FetchPictureAsync(entry.Asset.ImagePath, entry.ImageUrl, cancellation)) missing++;
                if (!await FetchPictureAsync(entry.Asset.BodyImagePath, entry.BodyImageUrl, cancellation)) missing++;
            }

        ManifestInstallResult installed;
        try { installed = _assets.InstallManifest(file.Text); }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            return Fail("The new list could not be saved: " + ex.Message);
        }

        var message = installed.Added.Count == 0
            ? $"Updated to revision {installed.Revision}; no new heroes or maps."
            : $"Updated to revision {installed.Revision}. New: {string.Join(", ", installed.Added)}.";
        if (missing > 0) message += $" {missing} picture(s) could not be downloaded and stay missing.";
        return new ManifestUpdateResult(true, message, installed.Added);
    }

    // Downloads a picture that is not installed yet (when the manifest says where it is). True when nothing is missing afterwards.
    private async Task<bool> FetchPictureAsync(string? relative, string? url, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(relative)) return true;
        if (_assets.HasFile(relative)) return true;
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;

        var target = Path.GetFullPath(Path.Combine(_assets.UserScoreboardRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(Path.GetFullPath(_assets.UserScoreboardRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;   // a path that leads out of the Scoreboard folder

        var response = await _http.GetAsync(uri, MaxImageBytes, cancellation);
        if (response.Status != 200 || !LooksLikeImage(response.Body)) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, response.Body, cancellation);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool LooksLikeImage(byte[] b) =>
        b.Length > 12 && (
            (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) ||            // PNG
            (b[0] == 0xFF && b[1] == 0xD8) ||                                              // JPEG
            (b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P'));

    private static bool IsGitHubUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    private static ManifestUpdateResult Fail(string message) => new(false, message, Array.Empty<string>());
}

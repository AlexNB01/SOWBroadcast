using System.Security.Cryptography;
using System.Text;

namespace SowBroadcast.Integrations.Link;

/// <summary>
/// Team logos fetched from SOWDraft into a local folder (SOWDraft refuses to let other sites show them directly, and OBS needs a file
/// anyway). A file is named after a hash of its address, so a changed logo address becomes a new file. Downloads check size and content;
/// an address that failed is not tried again in the same session.
/// </summary>
public sealed class LogoCache
{
    public const int MaxBytes = 2 * 1024 * 1024;

    private readonly string _folder;
    private readonly IHttpFetcher _http;
    private readonly HashSet<string> _failed = new();

    public LogoCache(string folder, IHttpFetcher http)
    {
        _folder = folder;
        _http = http;
    }

    /// <summary>The local file of a logo address when it has been downloaded, else null.</summary>
    public string? PathOf(string url)
    {
        if (!Directory.Exists(_folder)) return null;
        var stem = Stem(url);
        return Directory.EnumerateFiles(_folder, stem + ".*").FirstOrDefault();
    }

    /// <summary>Downloads the logos that are not here yet. Returns how many new files were written.</summary>
    public async Task<int> EnsureAsync(IEnumerable<string> urls, CancellationToken cancellation = default)
    {
        var count = 0;
        foreach (var url in urls.Distinct())
        {
            if (PathOf(url) is not null || _failed.Contains(url)) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) { _failed.Add(url); continue; }

            var result = await _http.GetAsync(uri, MaxBytes, cancellation).ConfigureAwait(false);
            var extension = result.Status == 200 && result.Error is null ? ExtensionOf(result.Body) : null;
            if (extension is null) { _failed.Add(url); continue; }

            Directory.CreateDirectory(_folder);
            var target = Path.Combine(_folder, Stem(url) + extension);
            var temp = target + ".tmp";
            await File.WriteAllBytesAsync(temp, result.Body, cancellation).ConfigureAwait(false);
            File.Move(temp, target, overwrite: true);
            count++;
        }
        return count;
    }

    private static string Stem(string url) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant()[..16];

    /// <summary>The file extension for a body that really is a PNG, JPEG, WebP or SVG picture; null for anything else.</summary>
    internal static string? ExtensionOf(byte[] body)
    {
        if (body.Length < 12 || body.Length > MaxBytes) return null;
        if (body[0] == 0x89 && body[1] == 0x50 && body[2] == 0x4E && body[3] == 0x47) return ".png";
        if (body[0] == 0xFF && body[1] == 0xD8 && body[2] == 0xFF) return ".jpg";
        if (body[0] == 'R' && body[1] == 'I' && body[2] == 'F' && body[3] == 'F' && body[8] == 'W' && body[9] == 'E' && body[10] == 'B' && body[11] == 'P') return ".webp";
        var head = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 512)).TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (head.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
            (head.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && head.Contains("<svg", StringComparison.OrdinalIgnoreCase))) return ".svg";
        return null;
    }
}

using SowBroadcast.Core.Export;

namespace SowBroadcast.Core.Assets;

/// <summary>
/// Writes a category's files into the user's <c>Scoreboard/</c> tree: <c>index.txt</c>, <c>index.json</c> and the
/// images as <c>&lt;slug&gt;.png</c> (plus <c>HeroesBody/</c> for heroes). Images that already come from a shipped
/// folder under the same name are not copied: the server serves them from there.
/// </summary>
public sealed class AssetExporter
{
    private readonly string _root;
    private readonly IReadOnlyList<string> _shippedRoots;

    public AssetExporter(string userScoreboardRoot, IReadOnlyList<string> shippedScoreboardRoots)
    {
        _root = Path.GetFullPath(userScoreboardRoot);
        _shippedRoots = shippedScoreboardRoots.Select(Path.GetFullPath).ToArray();
    }

    public void Export(AssetCategory category, IReadOnlyList<Asset> assets)
    {
        var folder = Path.Combine(_root, category.Folder());
        Directory.CreateDirectory(folder);

        FileWriter.WriteTextIfChanged(Path.Combine(folder, "index.txt"), AssetFiles.BuildIndexText(assets));

        foreach (var asset in assets)
        {
            var relative = $"{category.Folder()}/{Slug.Make(asset.Name)}.png";
            var source = asset.SourcePath is { } s && File.Exists(s) ? s : AssetFiles.Resolve(asset.ImagePath, AllRoots());
            CopyImage(source, relative);
        }

        if (category == AssetCategory.Heroes)
        {
            foreach (var asset in assets)
            {
                if (asset.BodySourcePath is not { } body || !File.Exists(body)) continue;
                CopyImage(body, asset.BodyImagePath ?? $"HeroesBody/{Slug.HeroBodyFile(asset.Name)}.png");
            }
        }

        FileWriter.WriteTextIfChanged(Path.Combine(folder, "index.json"),
            AssetFiles.BuildIndexJson(category, assets, rel => ImageExists(rel)));
    }

    /// <summary>Removes the exported image of a deleted asset (never touches shipped files).</summary>
    public void DeleteImage(AssetCategory category, string name) =>
        FileWriter.DeleteIfExists(Path.Combine(_root, category.Folder(), Slug.Make(name) + ".png"));

    private IReadOnlyList<string> AllRoots() => new[] { _root }.Concat(_shippedRoots).ToArray();

    private bool ImageExists(string relative) => AssetFiles.Resolve(relative, AllRoots()) is not null;

    private void CopyImage(string? source, string relativeTarget)
    {
        if (source is null) return;
        var target = Path.GetFullPath(Path.Combine(_root, relativeTarget.Replace('/', Path.DirectorySeparatorChar)));
        var sourceFull = Path.GetFullPath(source);
        if (string.Equals(sourceFull, target, StringComparison.OrdinalIgnoreCase)) return;

        // Already served from a shipped folder under the same relative name.
        foreach (var shipped in _shippedRoots)
        {
            var shippedTarget = Path.GetFullPath(Path.Combine(shipped, relativeTarget.Replace('/', Path.DirectorySeparatorChar)));
            if (string.Equals(sourceFull, shippedTarget, StringComparison.OrdinalIgnoreCase)) return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            var src = new FileInfo(sourceFull);
            var dst = new FileInfo(target);
            if (dst.Length == src.Length && ToUnixSeconds(dst.LastWriteTimeUtc) >= ToUnixSeconds(src.LastWriteTimeUtc)) return;
        }

        try { File.Copy(sourceFull, target, overwrite: true); }
        catch (IOException) { /* in use; the next export retries */ }
    }

    private static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeSeconds();
}

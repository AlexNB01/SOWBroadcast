namespace SowBroadcast.Core.Assets;

/// <summary>What the Add / Update button sends. Paths are files the user picked; empty keeps what exists.</summary>
public sealed record AssetEdit
{
    public AssetCategory Category { get; init; }
    public string Name { get; init; } = "";
    public string? ImageSourcePath { get; init; }
    public string? Mode { get; init; }
    public string? Role { get; init; }
    public string? BodySourcePath { get; init; }
}

public sealed record AssetResult(bool Ok, string? ErrorTitle, string? ErrorMessage, Asset? Asset)
{
    public static AssetResult Success(Asset asset) => new(true, null, null, asset);
    public static AssetResult Failure(string title, string message) => new(false, title, message, null);
}

/// <summary>A picture found in the Heroes or Maps folder that is not in the catalogue yet.</summary>
public sealed record ImportCandidate(AssetCategory Category, string FilePath, string Name, string? Mode = null);

/// <summary>
/// The heroes, maps and game modes the app knows. Loads from the user's index files, else the shipped ones, else a
/// folder scan; every change is written to the user's <c>Scoreboard/</c> tree straight away.
/// </summary>
public sealed class AssetService
{
    public static readonly IReadOnlyList<string> Roles = new[] { "Tank", "Damage", "Support" };

    private readonly string _userRoot;
    private readonly IReadOnlyList<string> _shippedRoots;
    private readonly AssetExporter _exporter;
    private readonly Dictionary<AssetCategory, List<Asset>> _items = AssetCategories.All.ToDictionary(c => c, _ => new List<Asset>());

    public AssetService(string userScoreboardRoot, IReadOnlyList<string> shippedScoreboardRoots)
    {
        _userRoot = Path.GetFullPath(userScoreboardRoot);
        _shippedRoots = shippedScoreboardRoots.Select(Path.GetFullPath).ToArray();
        _exporter = new AssetExporter(_userRoot, _shippedRoots);
    }

    /// <summary>Raised after a category changed and was written to disk.</summary>
    public event Action<AssetCategory>? Changed;

    public IReadOnlyList<Asset> Items(AssetCategory category) => _items[category];

    /// <summary>Names in code-point order, as the combo boxes list them.</summary>
    public IReadOnlyList<string> Names(AssetCategory category) =>
        _items[category].Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    public Asset? Find(AssetCategory category, string? name) =>
        string.IsNullOrEmpty(name) ? null : _items[category].FirstOrDefault(a => a.Name == name);

    public string? HeroRole(string? name) => Find(AssetCategory.Heroes, name)?.Role;

    /// <summary>The picture file of an entry (user folder first, then the shipped one, else the file it was imported from).</summary>
    public string? ImageFile(AssetCategory category, string? name)
    {
        var asset = Find(category, name);
        if (asset is null) return null;
        return AssetFiles.Resolve(asset.ImagePath, AllRoots) ?? (asset.SourcePath is { } s && File.Exists(s) ? s : null);
    }

    private IReadOnlyList<string> AllRoots => new[] { _userRoot }.Concat(_shippedRoots).ToArray();

    // ---- loading -----------------------------------------------------------------------------------------

    public void Load()
    {
        foreach (var category in AssetCategories.All)
        {
            var folder = category.Folder();
            var loaded = AssetFiles.ReadIndex(Path.Combine(_userRoot, folder, "index.json"), category, AllRoots);
            foreach (var shipped in _shippedRoots)
            {
                if (loaded is not null) break;
                loaded = AssetFiles.ReadIndex(Path.Combine(shipped, folder, "index.json"), category, AllRoots);
            }

            if (loaded is null)
            {
                loaded = new List<Asset>();
                foreach (var root in AllRoots.Reverse())
                    foreach (var asset in AssetFiles.ScanFolder(Path.Combine(root, folder), category))
                    {
                        var existing = loaded.FindIndex(a => a.Name == asset.Name);
                        if (existing >= 0) loaded[existing] = asset; else loaded.Add(asset);
                    }
            }

            _items[category] = loaded;
        }
    }

    // ---- editing -----------------------------------------------------------------------------------------

    public AssetResult AddOrUpdate(AssetEdit edit)
    {
        var name = edit.Name.Trim();
        if (name.Length == 0) return AssetResult.Failure("Missing name", "Type a name first.");

        var existing = Find(edit.Category, name);
        var slug = Slug.Make(name);
        var asset = new Asset
        {
            Name = name,
            ImagePath = $"{edit.Category.Folder()}/{slug}.png",
            SourcePath = Existing(edit.ImageSourcePath) ?? existing?.SourcePath,
            Mode = edit.Category == AssetCategory.Maps ? NullIfBlank(edit.Mode) : null,
            Role = edit.Category == AssetCategory.Heroes ? NullIfBlank(edit.Role) : null,
        };

        if (edit.Category == AssetCategory.Heroes)
        {
            var bodyRelative = $"HeroesBody/{Slug.HeroBodyFile(name)}.png";
            // A picked image, else a file that is already in place, else what the hero had before.
            var bodySource = Existing(edit.BodySourcePath)
                             ?? AssetFiles.Resolve(bodyRelative, AllRoots)
                             ?? existing?.BodySourcePath;
            if (bodySource is null || !File.Exists(bodySource))
                return AssetResult.Failure("Missing body image", "Heroes need a full-body image (used by the map draft overlay).");
            asset = asset with { BodyImagePath = bodyRelative, BodySourcePath = bodySource };
        }

        var list = _items[edit.Category];
        var index = list.FindIndex(a => a.Name == name);
        if (index >= 0) list[index] = asset; else list.Add(asset);

        Persist(edit.Category);
        return AssetResult.Success(asset);
    }

    public bool Delete(AssetCategory category, string name)
    {
        var list = _items[category];
        var index = list.FindIndex(a => a.Name == name);
        if (index < 0) return false;

        list.RemoveAt(index);
        _exporter.DeleteImage(category, name);
        Persist(category);
        return true;
    }

    // ---- bulk import -------------------------------------------------------------------------------------

    /// <summary>Pictures dropped into the user's Heroes / Maps folders that are not in the catalogue.</summary>
    public IReadOnlyList<ImportCandidate> ScanForImport()
    {
        var result = new List<ImportCandidate>();
        foreach (var category in new[] { AssetCategory.Heroes, AssetCategory.Maps })
        {
            var knownSlugs = _items[category].Select(a => Slug.Make(a.Name)).ToHashSet();
            foreach (var asset in AssetFiles.ScanFolder(Path.Combine(_userRoot, category.Folder()), category))
            {
                var stem = Path.GetFileNameWithoutExtension(asset.SourcePath!);
                if (Find(category, asset.Name) is not null || knownSlugs.Contains(stem) || knownSlugs.Contains(Slug.Make(asset.Name))) continue;
                result.Add(new ImportCandidate(category, asset.SourcePath!, asset.Name));
            }
        }
        return result;
    }

    /// <summary>Adds the candidates (names already in the catalogue are skipped). Returns how many heroes and maps were added.</summary>
    public (int Heroes, int Maps) BulkImport(IEnumerable<ImportCandidate> candidates)
    {
        var added = new Dictionary<AssetCategory, int> { [AssetCategory.Heroes] = 0, [AssetCategory.Maps] = 0 };
        foreach (var candidate in candidates)
        {
            var name = candidate.Name.Trim();
            if (name.Length == 0 || Find(candidate.Category, name) is not null) continue;
            if (candidate.Category is not (AssetCategory.Heroes or AssetCategory.Maps)) continue;

            _items[candidate.Category].Add(new Asset
            {
                Name = name,
                ImagePath = $"{candidate.Category.Folder()}/{Slug.Make(name)}.png",
                SourcePath = candidate.FilePath,
                Mode = candidate.Category == AssetCategory.Maps ? NullIfBlank(candidate.Mode) : null,
            });
            added[candidate.Category]++;
        }

        foreach (var category in new[] { AssetCategory.Heroes, AssetCategory.Maps })
            if (added[category] > 0) Persist(category);
        return (added[AssetCategory.Heroes], added[AssetCategory.Maps]);
    }

    private void Persist(AssetCategory category)
    {
        _exporter.Export(category, _items[category]);
        Changed?.Invoke(category);
    }

    private static string? Existing(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

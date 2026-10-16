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

/// <summary>What installing a manifest did: whether it was newer than the one in use, its revision and the names it added.</summary>
public sealed record ManifestInstallResult(bool Installed, int Revision, IReadOnlyList<string> Added);

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

    /// <summary>
    /// The catalogue name for a name as someone else spells it: the name itself or one of its aliases, compared exactly (ignoring only
    /// letter case and extra spaces). Returns null when nothing in the catalogue goes by that name; there is no guessing.
    /// </summary>
    public string? Resolve(AssetCategory category, string? name)
    {
        var key = Squeeze(name);
        if (key.Length == 0) return null;
        var items = _items[category];
        foreach (var asset in items)
            if (string.Equals(Squeeze(asset.Name), key, StringComparison.OrdinalIgnoreCase)) return asset.Name;
        foreach (var asset in items)
            if (asset.Aliases.Any(alias => string.Equals(Squeeze(alias), key, StringComparison.OrdinalIgnoreCase))) return asset.Name;
        return null;
    }

    private static string Squeeze(string? text) => string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The picture file of an entry (user folder first, then the shipped one, else the file it was imported from).</summary>
    public string? ImageFile(AssetCategory category, string? name)
    {
        var asset = Find(category, name);
        if (asset is null) return null;
        return AssetFiles.Resolve(asset.ImagePath, AllRoots) ?? (asset.SourcePath is { } s && File.Exists(s) ? s : null);
    }

    /// <summary>The per-user Scoreboard folder (where added pictures and fetched manifests are kept).</summary>
    public string UserScoreboardRoot => _userRoot;

    /// <summary>Whether a picture (a path like <c>Heroes/ana.png</c>) is installed, in the user folder or a shipped one.</summary>
    public bool HasFile(string? relative) => AssetFiles.Resolve(relative, AllRoots) is not null;

    private IReadOnlyList<string> AllRoots => new[] { _userRoot }.Concat(_shippedRoots).ToArray();

    // ---- loading -----------------------------------------------------------------------------------------

    public void Load()
    {
        LoadLocalChoices();
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

        // The manifest (shipped, or a newer one the user fetched) is the authority on roles, game modes, picture files and aliases,
        // and brings heroes and maps this installation does not have yet.
        if (BestManifest() is { } manifest)
        {
            ManifestRevision = manifest.Revision;
            ApplyManifest(manifest);
        }
    }

    // ---- manifest ----------------------------------------------------------------------------------------

    public const string ManifestFileName = "manifest.json";

    /// <summary>Revision of the manifest the catalogue was last brought in line with (0 = none).</summary>
    public int ManifestRevision { get; private set; }

    /// <summary>Where a manifest fetched by the user is kept (it wins over the shipped one when its revision is higher).</summary>
    public string UserManifestPath => Path.Combine(_userRoot, ManifestFileName);

    private AssetManifest? BestManifest() =>
        new[] { UserManifestPath }.Concat(_shippedRoots.Select(r => Path.Combine(r, ManifestFileName)))
            .Select(AssetManifest.TryLoad).Where(m => m is not null).Select(m => m!)
            .OrderByDescending(m => m.Revision).FirstOrDefault();

    /// <summary>
    /// Brings the catalogue in line with <paramref name="manifest"/>: known entries take its role, game mode, picture files and
    /// aliases; entries it lists that are missing are added (unless the user deleted them); entries the user added themselves stay.
    /// Changed categories are written to disk. Returns the names that were added.
    /// </summary>
    public IReadOnlyList<string> ApplyManifest(AssetManifest manifest)
    {
        var added = new List<string>();
        foreach (var category in AssetCategories.All)
        {
            var list = _items[category];
            var changed = false;
            foreach (var entry in manifest.Of(category))
            {
                var wanted = entry.Asset;
                var index = list.FindIndex(a => string.Equals(a.Name, wanted.Name, StringComparison.OrdinalIgnoreCase));
                if (index < 0) index = list.FindIndex(a => wanted.Aliases.Any(x => string.Equals(x, a.Name, StringComparison.OrdinalIgnoreCase)));

                if (index >= 0)
                {
                    var current = list[index];
                    var merged = current with
                    {
                        Aliases = wanted.Aliases,
                        Role = wanted.Role ?? current.Role,
                        Mode = wanted.Mode ?? current.Mode,
                        ImagePath = wanted.ImagePath ?? current.ImagePath,
                        BodyImagePath = wanted.BodyImagePath ?? current.BodyImagePath,
                    };
                    merged = merged with
                    {
                        SourcePath = AssetFiles.Resolve(merged.ImagePath, AllRoots) ?? merged.SourcePath,
                        BodySourcePath = AssetFiles.Resolve(merged.BodyImagePath, AllRoots) ?? merged.BodySourcePath,
                    };
                    if (!SameData(current, merged))
                    {
                        list[index] = merged;
                        changed = true;
                    }
                }
                else if (!_removed[category].Contains(wanted.Name))
                {
                    list.Add(wanted with
                    {
                        SourcePath = AssetFiles.Resolve(wanted.ImagePath, AllRoots),
                        BodySourcePath = AssetFiles.Resolve(wanted.BodyImagePath, AllRoots),
                    });
                    added.Add(wanted.Name);
                    changed = true;
                }
            }
            if (changed) Persist(category);
        }
        return added;
    }

    /// <summary>
    /// Takes a manifest fetched from outside: it is checked, and when its revision is newer than the one in use it is kept in
    /// the user folder and applied. Returns what happened; throws <see cref="FormatException"/> when the text is not a usable manifest.
    /// </summary>
    public ManifestInstallResult InstallManifest(string json)
    {
        var manifest = AssetManifest.Parse(json);
        if (manifest.Revision <= ManifestRevision) return new ManifestInstallResult(false, manifest.Revision, Array.Empty<string>());

        Directory.CreateDirectory(_userRoot);
        File.WriteAllText(UserManifestPath, json, new System.Text.UTF8Encoding(false));
        ManifestRevision = manifest.Revision;
        return new ManifestInstallResult(true, manifest.Revision, ApplyManifest(manifest));
    }

    private static bool SameData(Asset a, Asset b) =>
        a.Name == b.Name && a.ImagePath == b.ImagePath && a.Mode == b.Mode && a.Role == b.Role &&
        a.BodyImagePath == b.BodyImagePath && a.Aliases.SequenceEqual(b.Aliases);

    // ---- what the user deleted from the shipped lists (so the manifest does not bring it back) ------------

    private readonly Dictionary<AssetCategory, HashSet<string>> _removed =
        AssetCategories.All.ToDictionary(c => c, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private string LocalChoicesPath => Path.Combine(_userRoot, "assets.local.json");

    private void LoadLocalChoices()
    {
        foreach (var set in _removed.Values) set.Clear();
        try
        {
            if (!File.Exists(LocalChoicesPath)) return;
            if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(LocalChoicesPath))?["removed"] is not System.Text.Json.Nodes.JsonObject removed) return;
            foreach (var category in AssetCategories.All)
                if (removed[category.Key()] is System.Text.Json.Nodes.JsonArray names)
                    foreach (var name in names)
                        if (name?.GetValue<string>() is { Length: > 0 } text) _removed[category].Add(text);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or InvalidOperationException) { /* a damaged file: nothing was deleted */ }
    }

    private void SaveLocalChoices()
    {
        var removed = new System.Text.Json.Nodes.JsonObject();
        foreach (var category in AssetCategories.All)
            removed[category.Key()] = new System.Text.Json.Nodes.JsonArray(
                _removed[category].OrderBy(n => n, StringComparer.Ordinal).Select(n => (System.Text.Json.Nodes.JsonNode)System.Text.Json.Nodes.JsonValue.Create(n)!).ToArray());
        try
        {
            Directory.CreateDirectory(_userRoot);
            File.WriteAllText(LocalChoicesPath, new System.Text.Json.Nodes.JsonObject { ["removed"] = removed }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
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
            // A hero the catalogue already knows keeps its picture file; a new one gets the conventional name.
            var bodyRelative = existing?.BodyImagePath ?? $"HeroesBody/{Slug.HeroBodyFile(name)}.png";
            // A picked image, else a file that is already in place, else what the hero had before.
            var bodySource = Existing(edit.BodySourcePath)
                             ?? AssetFiles.Resolve(bodyRelative, AllRoots)
                             ?? existing?.BodySourcePath;
            if (bodySource is null || !File.Exists(bodySource))
                return AssetResult.Failure("Missing body image", "Heroes need a full-body image (used by the map draft overlay).");
            asset = asset with { BodyImagePath = bodyRelative, BodySourcePath = bodySource };
        }
        if (existing is not null) asset = asset with { Aliases = existing.Aliases };

        if (_removed[edit.Category].Remove(name)) SaveLocalChoices();   // adding it again undoes an earlier delete

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
        if (_removed[category].Add(name)) SaveLocalChoices();           // the manifest must not bring it back
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

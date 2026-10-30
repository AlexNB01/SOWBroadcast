using System.Text.Json;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Link;
using SowBroadcast.Core.Match;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Core.Teams;

/// <summary>A team kept in the library. The logo is a file of the library's own (<see cref="LogoFile"/>, relative to the library folder).</summary>
public sealed record LibraryTeam
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Abbr { get; init; } = "";
    public string? ColorHex { get; init; }
    /// <summary>E.g. <c>logos/3fa9c2e41b7d.png</c>; null = no logo.</summary>
    public string? LogoFile { get; init; }
    public IReadOnlyList<Player> Players { get; init; } = Array.Empty<Player>();
    /// <summary>Where the entry came from: "manual", "file" or "sowdraft".</summary>
    public string Source { get; init; } = "manual";
    public DateTime Updated { get; init; }
}

public sealed record LibrarySaveResult(LibraryTeam Team, bool Added, string? Warning);

/// <summary>
/// The teams the producer works with again and again (EHD-8): name, abbreviation, colour, logo and players, found by a search box
/// instead of a file dialog. Everything lives in one folder: <c>library.json</c> and one PNG per team in <c>logos/</c>. The library
/// owns its logos (a team's logo is copied in, as PNG, and replaced or deleted with the team), so entries never point at files
/// elsewhere on the disk and no stale logo is left behind.
/// </summary>
public sealed class TeamLibrary
{
    private const string FileName = "library.json";
    private const string LogoFolder = "logos";

    private readonly string _folder;
    private readonly ILogoConverter _logos;
    private readonly Func<DateTime> _now;
    private List<LibraryTeam> _teams = new();

    private sealed record FileData
    {
        public int Version { get; init; } = 1;
        public IReadOnlyList<LibraryTeam> Teams { get; init; } = Array.Empty<LibraryTeam>();
    }

    public TeamLibrary(string folder, ILogoConverter logos, Func<DateTime>? now = null)
    {
        _folder = Path.GetFullPath(folder);
        _logos = logos;
        _now = now ?? (() => DateTime.Now);
    }

    public string Folder => _folder;

    /// <summary>Every team, by name.</summary>
    public IReadOnlyList<LibraryTeam> All => _teams.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    public LibraryTeam? Find(string? id) => string.IsNullOrEmpty(id) ? null : _teams.FirstOrDefault(t => t.Id == id);

    public LibraryTeam? FindByName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : _teams.FirstOrDefault(t => SameName(t.Name, name));

    public static bool SameName(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.CurrentCultureIgnoreCase);

    /// <summary>The absolute logo file of a team, when it has one and the file is there.</summary>
    public string? LogoPathOf(LibraryTeam team)
    {
        if (string.IsNullOrEmpty(team.LogoFile)) return null;
        var path = Path.Combine(_folder, team.LogoFile.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    // ---- reading -----------------------------------------------------------------------------------------

    /// <summary>Reads the library (a missing or damaged file is an empty library; the damaged one is kept as <c>.bad</c>).</summary>
    public void Load()
    {
        _teams = new List<LibraryTeam>();
        var path = Path.Combine(_folder, FileName);
        if (!File.Exists(path)) return;
        try
        {
            var data = JsonSerializer.Deserialize<FileData>(File.ReadAllText(path, System.Text.Encoding.UTF8), StateStore.Options);
            if (data is null) return;
            _teams = data.Teams.Where(t => !string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrEmpty(t.Id)).ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            try { File.Copy(path, path + ".bad", overwrite: true); }
            catch (Exception copy) when (copy is IOException or UnauthorizedAccessException) { /* best effort */ }
        }
    }

    /// <summary>Teams whose name, abbreviation or a player's name contains every word of <paramref name="query"/> (any case).</summary>
    public IReadOnlyList<LibraryTeam> Search(string? query)
    {
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return All;
        return All.Where(t =>
        {
            var haystack = string.Join(' ', new[] { t.Name, t.Abbr }.Concat(t.Players.Select(p => p.Name)));
            return words.All(w => haystack.Contains(w, StringComparison.CurrentCultureIgnoreCase));
        }).ToArray();
    }

    // ---- writing -----------------------------------------------------------------------------------------

    /// <summary>
    /// Keeps <paramref name="team"/> (a team panel as it is now): a team of the same name is updated, otherwise a new entry is made.
    /// The logo is copied into the library as PNG; a team without logo loses the library's one. Score and banned hero are not kept.
    /// </summary>
    /// <exception cref="ArgumentException">The team has no name.</exception>
    public LibrarySaveResult Save(Team team, string source = "manual")
    {
        var name = team.Name.Trim();
        if (name.Length == 0) throw new ArgumentException("A team needs a name to be kept in the library.", nameof(team));

        var existing = FindByName(name);
        var id = existing?.Id ?? NewId();
        string? warning = null;
        var logoFile = existing?.LogoFile;

        var libraryLogo = existing is null ? null : LogoPathOf(existing);
        if (string.IsNullOrEmpty(team.LogoPath))
        {
            DeleteLogoFile(existing?.LogoFile);
            logoFile = null;
        }
        else if (libraryLogo is not null && SamePath(libraryLogo, team.LogoPath))
        {
            // The panel shows the library's own logo already: nothing to copy.
        }
        else
        {
            var png = _logos.ToPng(team.LogoPath);
            if (png is null)
            {
                warning = $"The logo {team.LogoPath} could not be read, so the library keeps the logo it had.";
            }
            else
            {
                logoFile = $"{LogoFolder}/{id}.png";
                Directory.CreateDirectory(Path.Combine(_folder, LogoFolder));
                FileWriter.WriteBytesIfChanged(Path.Combine(_folder, LogoFolder, id + ".png"), png);
            }
        }

        var entry = new LibraryTeam
        {
            Id = id,
            Name = name,
            Abbr = team.Abbr.Trim(),
            ColorHex = string.IsNullOrWhiteSpace(team.ColorHex) ? null : team.ColorHex,
            LogoFile = logoFile,
            Players = Eight(team.Players),
            Source = source,
            Updated = _now(),
        };
        if (existing is null) _teams.Add(entry);
        else _teams[_teams.IndexOf(existing)] = entry;
        Write();
        return new LibrarySaveResult(entry, existing is null, warning);
    }

    /// <summary>Removes a team and its logo.</summary>
    public bool Delete(string id)
    {
        var team = Find(id);
        if (team is null) return false;
        _teams.Remove(team);
        DeleteLogoFile(team.LogoFile);
        Write();
        return true;
    }

    /// <summary>The team as a panel wants it: the library's data with the panel's current score kept (the logo path is the library's file).</summary>
    public Team ToTeam(LibraryTeam entry, int keepScore, string defaultColour) => new()
    {
        Name = entry.Name,
        Abbr = entry.Abbr,
        LogoPath = LogoPathOf(entry),
        Score = keepScore,
        ColorHex = string.IsNullOrWhiteSpace(entry.ColorHex) ? defaultColour : entry.ColorHex,
        Players = Eight(entry.Players),
        BannedHero = "",
    };

    /// <summary>Adds the team of a <c>*.sowteam.json</c> file (the old way of keeping teams) to the library.</summary>
    /// <exception cref="StateLoadException">The file cannot be read or is not a team file.</exception>
    public LibrarySaveResult ImportFile(string jsonPath) =>
        Save(TeamFile.Import(jsonPath, keepScore: 0, defaultColour: ""), "file");

    /// <summary>
    /// Takes the teams of a SOWDraft tournament: SOWDraft's values replace what the library has for a team of the same name, except that
    /// an abbreviation or colour SOWDraft does not have stays. <paramref name="logoPathOf"/> gives the downloaded file of a logo address.
    /// </summary>
    public (int Added, int Updated, IReadOnlyList<string> Warnings) ImportFromLink(IEnumerable<LinkTeam> teams, Func<string, string?> logoPathOf, Func<string, string?>? resolveHero = null)
    {
        int added = 0, updated = 0;
        var warnings = new List<string>();
        foreach (var linked in teams)
        {
            if (string.IsNullOrWhiteSpace(linked.Name)) continue;
            var existing = FindByName(linked.Name);
            var players = LinkNaming.InRoleOrder(linked.Players).Take(Limits.PlayersPerTeam)
                .Select(p => new Player { Name = LinkNaming.PlayerName(new LinkPlayer(p.Name, p.Battletag, p.Discord, p.Role, p.SecondaryRoles)), Hero = p.Hero is { Length: > 0 } hero ? resolveHero?.Invoke(hero) ?? "" : "", Role = LinkNaming.RoleOf(p.Role) })
                .ToArray();
            var team = new Team
            {
                Name = linked.Name,
                Abbr = !string.IsNullOrWhiteSpace(linked.Abbr) ? linked.Abbr : existing?.Abbr is { Length: > 0 } keep ? keep : LinkNaming.SuggestAbbreviation(linked.Name),
                ColorHex = !string.IsNullOrWhiteSpace(linked.Color) ? linked.Color : existing?.ColorHex ?? "",
                LogoPath = linked.LogoUrl is { Length: > 0 } url ? logoPathOf(url) ?? (existing is null ? null : LogoPathOf(existing)) : null,
                Players = players,
            };
            var result = Save(team, "sowdraft");
            if (result.Warning is not null) warnings.Add($"{linked.Name}: {result.Warning}");
            if (result.Added) added++; else updated++;
        }
        return (added, updated, warnings);
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    private static IReadOnlyList<Player> Eight(IEnumerable<Player> players)
    {
        var list = players.Take(Limits.PlayersPerTeam).ToList();
        while (list.Count < Limits.PlayersPerTeam) list.Add(new Player());
        return list;
    }

    private string NewId()
    {
        string id;
        do { id = Guid.NewGuid().ToString("N")[..12]; } while (_teams.Any(t => t.Id == id));
        return id;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private void DeleteLogoFile(string? logoFile)
    {
        if (string.IsNullOrEmpty(logoFile)) return;
        FileWriter.DeleteIfExists(Path.Combine(_folder, logoFile.Replace('/', Path.DirectorySeparatorChar)));
    }

    private void Write()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, FileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new FileData { Teams = All }, StateStore.Options), new System.Text.UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null, ignoreMetadataErrors: true);
        else File.Move(temp, path);
    }
}

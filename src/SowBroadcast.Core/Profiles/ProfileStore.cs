using System.Text.Json;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.History;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Core.Profiles;

/// <summary>A saved profile as the list shows it.</summary>
public sealed record ProfileInfo(string Id, string Name, DateTime Saved, string Summary);

/// <summary>
/// Named profiles (EHD-18): a snapshot of the broadcast setup for one tournament or one match — status text, colours, logos, casters,
/// waiting-screen texts, standings, bracket, the tournament address and the match itself — that can be put back in one step.
/// Each profile is a folder (<c>profile.json</c> and the logo files it uses, copied in so the profile does not depend on files that
/// may move or be deleted). Countdown and the running SOWDraft connection are never part of it.
/// </summary>
public sealed class ProfileStore
{
    private const string ProfileFile = "profile.json";
    private const string IndexFile = "index.json";
    private const string LogoFolder = "logos";

    private readonly string _folder;
    private readonly Func<DateTime> _now;

    private sealed record Data
    {
        public int Version { get; init; } = 1;
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public DateTime Saved { get; init; }
        public AppState State { get; init; } = new();
    }

    private sealed record Index
    {
        public string Active { get; init; } = "";
    }

    public ProfileStore(string folder, Func<DateTime>? now = null)
    {
        _folder = Path.GetFullPath(folder);
        _now = now ?? (() => DateTime.Now);
    }

    public string Folder => _folder;

    /// <summary>The profile that was applied or saved last (empty when none, or when it was deleted).</summary>
    public string ActiveId
    {
        get
        {
            var id = ReadIndex().Active;
            return id.Length > 0 && File.Exists(Path.Combine(_folder, id, ProfileFile)) ? id : "";
        }
    }

    public void SetActive(string? id) => WriteText(Path.Combine(_folder, IndexFile), JsonSerializer.Serialize(new Index { Active = id ?? "" }, StateStore.Options));

    /// <summary>All profiles by name.</summary>
    public IReadOnlyList<ProfileInfo> List()
    {
        var result = new List<ProfileInfo>();
        if (!Directory.Exists(_folder)) return result;
        foreach (var dir in Directory.EnumerateDirectories(_folder))
        {
            if (Read(Path.Combine(dir, ProfileFile)) is not { } data) continue;
            result.Add(new ProfileInfo(data.Id, data.Name, data.Saved, Summarise(data.State)));
        }
        return result.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public ProfileInfo? FindByName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : List().FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    /// <summary>
    /// Keeps <paramref name="state"/> as the profile <paramref name="name"/> (a profile of that name is replaced) and makes it the active one.
    /// The logos the state points at are copied into the profile's folder.
    /// </summary>
    /// <exception cref="ArgumentException">The name is empty.</exception>
    public ProfileInfo Save(string name, AppState state)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("A profile needs a name.", nameof(name));

        var id = FindByName(name)?.Id ?? NewId();
        var dir = Path.Combine(_folder, id);
        var logos = Path.Combine(dir, LogoFolder);
        Directory.CreateDirectory(dir);

        // Logos first (the profile owns copies), then the file that refers to them.
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? Own(string? path, string slot)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var fileName = slot + Path.GetExtension(path).ToLowerInvariant();
            Directory.CreateDirectory(logos);
            var target = Path.Combine(logos, fileName);
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(path, target, overwrite: true);
            used.Add(fileName);
            return LogoFolder + "/" + fileName;
        }

        var kept = Normalise(state) with
        {
            Team1 = state.Team1 with { LogoPath = Own(state.Team1.LogoPath, "team1") },
            Team2 = state.Team2 with { LogoPath = Own(state.Team2.LogoPath, "team2") },
            General = state.General with
            {
                OverlayLogoPath = Own(state.General.OverlayLogoPath, "overlay"),
                TransitionLogoPath = Own(state.General.TransitionLogoPath, "transition"),
            },
        };
        if (Directory.Exists(logos))
            foreach (var file in Directory.GetFiles(logos))
                if (!used.Contains(Path.GetFileName(file))) FileWriter.DeleteIfExists(file);   // no stale logo from an earlier save

        var data = new Data { Id = id, Name = name, Saved = _now(), State = kept };
        WriteText(Path.Combine(dir, ProfileFile), JsonSerializer.Serialize(data, StateStore.Options));
        SetActive(id);
        return new ProfileInfo(id, name, data.Saved, Summarise(kept));
    }

    /// <summary>The state of a profile, with its logos as the files in the profile's folder.</summary>
    /// <exception cref="StateLoadException">The profile does not exist or cannot be read.</exception>
    public AppState Load(string id)
    {
        var dir = Path.Combine(_folder, id);
        var data = Read(Path.Combine(dir, ProfileFile)) ?? throw new StateLoadException("That profile is no longer there.");
        string? Resolve(string? path) => string.IsNullOrEmpty(path) ? null : Path.GetFullPath(Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar)));
        var s = data.State;
        return s with
        {
            Team1 = s.Team1 with { LogoPath = Resolve(s.Team1.LogoPath) },
            Team2 = s.Team2 with { LogoPath = Resolve(s.Team2.LogoPath) },
            General = s.General with { OverlayLogoPath = Resolve(s.General.OverlayLogoPath), TransitionLogoPath = Resolve(s.General.TransitionLogoPath) },
        };
    }

    public bool Delete(string id)
    {
        var dir = Path.Combine(_folder, id);
        if (id.Length == 0 || !Directory.Exists(dir)) return false;
        Directory.Delete(dir, recursive: true);
        if (ReadIndex().Active == id) SetActive(null);
        return true;
    }

    // What a profile does not keep: the countdown (it belongs to the moment) and the live SOWDraft connection (only the address stays).
    private static AppState Normalise(AppState state) => state with
    {
        Waiting = state.Waiting with { TimerSeconds = 0, TimerRunning = false, TimerEndsAtMs = 0 },
        Link = new LinkSettings { TournamentUrl = state.Link.TournamentUrl },
        DraftLinkUrl = "",
        TournamentLinkUrl = "",
        CurrentMap = null,
    };

    private static string Summarise(AppState state)
    {
        var parts = new List<string>();
        if (state.General.StatusText.Trim().Length > 0) parts.Add(state.General.StatusText.Trim());
        if (state.Standings.Title.Trim().Length > 0) parts.Add(state.Standings.Title.Trim());
        if (!MatchArchive.IsBlank(state)) parts.Add(MatchArchive.Describe(state));
        return parts.Count == 0 ? "settings only" : string.Join(" · ", parts);
    }

    private string NewId()
    {
        string id;
        do { id = Guid.NewGuid().ToString("N")[..10]; } while (Directory.Exists(Path.Combine(_folder, id)));
        return id;
    }

    private static Data? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(path, System.Text.Encoding.UTF8), StateStore.Options) is { Id.Length: > 0 } d ? d : null : null; }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
    }

    private Index ReadIndex()
    {
        try
        {
            var path = Path.Combine(_folder, IndexFile);
            return File.Exists(path) ? JsonSerializer.Deserialize<Index>(File.ReadAllText(path), StateStore.Options) ?? new Index() : new Index();
        }
        catch (Exception ex) when (ex is JsonException or IOException) { return new Index(); }
    }

    private static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null, ignoreMetadataErrors: true);
        else File.Move(temp, path);
    }
}

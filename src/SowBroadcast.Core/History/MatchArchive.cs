using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Core.History;

/// <summary>A match kept in the archive: the file, when it was archived and a one-line description.</summary>
public sealed record ArchivedMatch(string Path, DateTime Time, string Title);

/// <summary>
/// The matches the producer has worked on, kept as files so "restore the previous match" works after a reset, a load, a change of
/// the linked match or a restart (EHD-17). One file per match state, newest 30 kept; an identical state is not archived twice.
/// </summary>
public sealed class MatchArchive
{
    public const int DefaultKeep = 30;

    private readonly string _folder;
    private readonly int _keep;

    public MatchArchive(string folder, int keep = DefaultKeep)
    {
        _folder = folder;
        _keep = Math.Max(1, keep);
    }

    public string Folder => _folder;

    /// <summary>True when there is nothing worth keeping: no team names, no map, no score.</summary>
    public static bool IsBlank(AppState state) =>
        state.Team1.Name.Trim().Length == 0 && state.Team2.Name.Trim().Length == 0 &&
        state.Team1.Score == 0 && state.Team2.Score == 0 &&
        state.Maps.All(m => string.IsNullOrEmpty(m.Map) && m.T1 == 0 && m.T2 == 0 && !m.Completed) &&
        state.Team1.Players.Concat(state.Team2.Players).All(p => p.Name.Trim().Length == 0);

    /// <summary>Keeps <paramref name="state"/> unless it is blank or the same as the newest archived one. Returns the file, or null when nothing was written.</summary>
    public string? Archive(AppState state, DateTime now)
    {
        if (IsBlank(state)) return null;
        var content = Comparable(state);

        var newest = Files().FirstOrDefault();
        if (newest is not null && TryRead(newest) is { } last && Comparable(last) == content) return null;

        Directory.CreateDirectory(_folder);
        // The name sorts after every earlier one, also for several archives in the same second (and after older ones were trimmed).
        var stem = $"match-{now:yyyyMMdd-HHmmss}";
        var newestStem = newest is null ? "" : System.IO.Path.GetFileNameWithoutExtension(newest);
        var name = stem;
        for (var n = 2; string.CompareOrdinal(name, newestStem) <= 0 || File.Exists(System.IO.Path.Combine(_folder, name + ".json")); n++)
            name = $"{stem}-{n:00}";
        var path = System.IO.Path.Combine(_folder, name + ".json");
        StateStore.Save(path, state);
        Trim();
        return path;
    }

    /// <summary>The archived matches, newest first. Files that cannot be read are skipped.</summary>
    public IReadOnlyList<ArchivedMatch> List()
    {
        var result = new List<ArchivedMatch>();
        foreach (var file in Files())
        {
            if (TryRead(file) is not { } state) continue;
            result.Add(new ArchivedMatch(file, TimeOf(file), Describe(state)));
        }
        return result;
    }

    // The time is in the name (match-20261016-224500[-02].json); the file date is only the fallback.
    private static DateTime TimeOf(string file)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(file);
        return name.Length >= 20 && DateTime.TryParseExact(name.AsSpan(6, 15), "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var time)
            ? time
            : File.GetLastWriteTime(file);
    }

    public static string Describe(AppState state)
    {
        var one = state.Team1.Name.Trim().Length > 0 ? state.Team1.Name.Trim() : "Team 1";
        var two = state.Team2.Name.Trim().Length > 0 ? state.Team2.Name.Trim() : "Team 2";
        return $"{one} {state.Team1.Score}–{state.Team2.Score} {two}";
    }

    // What makes two states "the same match state": everything except the countdown and the link bookkeeping.
    private static string Comparable(AppState state) => StateStore.Serialize(state with
    {
        Waiting = state.Waiting with { TimerSeconds = 0, TimerRunning = false, TimerEndsAtMs = 0 },
        Link = new LinkSettings(),
        CurrentMap = null,
    });

    private IEnumerable<string> Files() =>
        Directory.Exists(_folder)
            ? Directory.EnumerateFiles(_folder, "match-*.json").OrderByDescending(f => System.IO.Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal)   // "-02" suffixes sort after the plain name
            : Enumerable.Empty<string>();

    private static AppState? TryRead(string path)
    {
        try { return StateStore.Load(path); }
        catch (StateLoadException) { return null; }
    }

    private void Trim()
    {
        foreach (var old in Files().Skip(_keep))
        {
            try { File.Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
        }
    }
}

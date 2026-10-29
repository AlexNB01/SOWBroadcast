using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SowBroadcast.Core.Replay;

/// <summary>One saved replay clip: the file in the playlist plus what the operator did with it (a title, a trim, a speed, categories).</summary>
public sealed record ReplayClip
{
    public string Id { get; init; } = "";

    /// <summary>The file name in <c>Replay/Playlist</c>.</summary>
    public string File { get; init; } = "";

    public string Title { get; init; } = "";

    /// <summary>When the clip arrived (Unix milliseconds).</summary>
    public long AddedAt { get; init; }

    /// <summary>The length of the whole file; 0 while unknown (the preview fills it in).</summary>
    public long DurationMs { get; init; }

    /// <summary>Where the shown part begins and ends in the file (0 for the end = to the end). The file itself is never cut.</summary>
    public long StartMs { get; init; }
    public long EndMs { get; init; }

    /// <summary>Playback speed on the stream (1 = normal, 0.5 = slow motion).</summary>
    public double Speed { get; init; } = 1;

    /// <summary>The match the clip was saved in ("Alpha vs Bravo"), set when it arrived; the operator can change it.</summary>
    public string Match { get; init; } = "";

    /// <summary>The tournament (the status text of the General tab when the clip arrived).</summary>
    public string Tournament { get; init; } = "";

    /// <summary>Categories the operator made.</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>How long the shown part is (the whole file when no trim is set; 0 while the length is unknown).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public long ShownMs
    {
        get
        {
            var end = EndMs > 0 ? EndMs : DurationMs;
            return end > 0 ? Math.Max(0, end - StartMs) : 0;
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public double ShownSeconds => Speed > 0 ? ShownMs / 1000.0 / Speed : ShownMs / 1000.0;
}

public enum ReplayCategoryKind { Match, Tournament, Custom }

/// <summary>A group of clips the stream can show: the clips of a match, of the tournament, or a tag the operator made.</summary>
public sealed record ReplayCategory(ReplayCategoryKind Kind, string Name, int Count)
{
    public string Label => Kind switch
    {
        ReplayCategoryKind.Match => $"Match · {Name}",
        ReplayCategoryKind.Tournament => $"Tournament · {Name}",
        _ => Name,
    };
}

/// <summary>What the replay overlay is asked to show: clips played once in order, after which it goes back to looping the newest clip.</summary>
public sealed record ReplayRequest(long Nonce, long AtMs, IReadOnlyList<ReplayClip> Clips);

/// <summary>
/// The replay clips, kept in <c>Replay/clips.json</c> next to the playlist. A clip arrives with the match and tournament that were on
/// the screen; the operator can name it, trim it (the file is never changed: the overlay plays the chosen part), slow it down and
/// put it in categories. Safe to use from several threads.
/// </summary>
public sealed class ReplayLibrary
{
    public const string FileName = "clips.json";
    public const int SchemaVersion = 1;

    private static readonly Regex Numbered = new(@"^(\d+)-Replay-Replay\.mp4$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip,
    };

    private readonly string _folder;
    private readonly Func<long> _nowMs;
    private readonly object _lock = new();
    private readonly List<ReplayClip> _clips = new();
    private readonly List<string> _categories = new();   // the custom ones, also when they hold no clip yet
    private ReplayRequest? _request;
    private long _lastNonce;

    public ReplayLibrary(string replayFolder, Func<long>? nowMs = null)
    {
        _folder = Path.GetFullPath(replayFolder);
        _nowMs = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Load();
    }

    public string PlaylistFolder => Path.Combine(_folder, "Playlist");
    public string FilePath => Path.Combine(_folder, FileName);
    public string PathOf(ReplayClip clip) => Path.Combine(PlaylistFolder, clip.File);

    /// <summary>The clips or the categories changed.</summary>
    public event Action? Changed;

    // ---- reading -----------------------------------------------------------------------------------------

    /// <summary>All clips, the newest first.</summary>
    public IReadOnlyList<ReplayClip> Clips()
    {
        lock (_lock) return _clips.OrderByDescending(c => c.AddedAt).ThenByDescending(c => NumberOf(c.File)).ToArray();
    }

    public ReplayClip? Find(string id)
    {
        lock (_lock) return _clips.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public ReplayClip? Latest() => Clips().FirstOrDefault();

    /// <summary>Every category that has a clip (or that the operator made): the newest matches first, then tournaments, then the custom ones.</summary>
    public IReadOnlyList<ReplayCategory> Categories()
    {
        lock (_lock)
        {
            var newest = _clips.OrderByDescending(c => c.AddedAt).ThenByDescending(c => NumberOf(c.File)).ToArray();
            var list = new List<ReplayCategory>();
            foreach (var name in newest.Select(c => c.Match).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                list.Add(new ReplayCategory(ReplayCategoryKind.Match, name, _clips.Count(c => Same(c.Match, name))));
            foreach (var name in newest.Select(c => c.Tournament).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                list.Add(new ReplayCategory(ReplayCategoryKind.Tournament, name, _clips.Count(c => Same(c.Tournament, name))));
            foreach (var name in _categories.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                list.Add(new ReplayCategory(ReplayCategoryKind.Custom, name, _clips.Count(c => c.Tags.Any(t => Same(t, name)))));
            return list;
        }
    }

    /// <summary>The clips of a category (a match, a tournament or a tag, by name), in the order they were saved.</summary>
    public IReadOnlyList<ReplayClip> ClipsIn(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return Array.Empty<ReplayClip>();
        lock (_lock)
            return _clips.Where(c => Same(c.Match, name) || Same(c.Tournament, name) || c.Tags.Any(t => Same(t, name)))
                .OrderBy(c => c.AddedAt).ThenBy(c => NumberOf(c.File)).ToArray();
    }

    // ---- changing ----------------------------------------------------------------------------------------

    /// <summary>Adds the clip that just reached the playlist, with the match and tournament of the moment.</summary>
    public ReplayClip Add(string file, string match, string tournament)
    {
        ReplayClip clip;
        lock (_lock)
        {
            var existing = _clips.FirstOrDefault(c => string.Equals(c.File, file, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) return existing;
            clip = New(file, _nowMs(), match.Trim(), tournament.Trim());
            _clips.Add(clip);
            Save();
        }
        Changed?.Invoke();
        return clip;
    }

    /// <summary>
    /// Makes the library agree with the playlist folder: clips that are in the folder but not in the library are added (without a match),
    /// entries whose file is gone are dropped. Returns whether anything changed.
    /// </summary>
    public bool Sync()
    {
        bool changed;
        lock (_lock)
        {
            changed = _clips.RemoveAll(c => !System.IO.File.Exists(PathOf(c))) > 0;
            if (Directory.Exists(PlaylistFolder))
            {
                foreach (var path in Directory.EnumerateFiles(PlaylistFolder, "*.mp4"))
                {
                    var file = Path.GetFileName(path);
                    if (!Numbered.IsMatch(file) || _clips.Any(c => string.Equals(c.File, file, StringComparison.OrdinalIgnoreCase))) continue;
                    _clips.Add(New(file, new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(path), TimeSpan.Zero).ToUnixTimeMilliseconds(), "", ""));
                    changed = true;
                }
            }
            if (changed) Save();
        }
        if (changed) Changed?.Invoke();
        return changed;
    }

    /// <summary>Replaces the clip with the same id (the edits of the tab: title, trim, speed, categories).</summary>
    public bool Update(ReplayClip clip)
    {
        lock (_lock)
        {
            var index = _clips.FindIndex(c => c.Id == clip.Id);
            if (index < 0) return false;
            var tags = clip.Tags.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var tag in tags) EnsureCategory(tag);
            _clips[index] = Normalise(clip with { Tags = tags });
            Save();
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>A path in the playlist folder for a video that is being written (not an <c>.mp4</c>, so nothing mistakes it for a clip).</summary>
    public string TempExportPath() => Path.Combine(PlaylistFolder, "export-" + Guid.NewGuid().ToString("N")[..8] + ".tmp");

    /// <summary>
    /// Takes the finished video at <paramref name="tempFile"/> into the playlist as a new clip with the title, match, tournament and
    /// categories of <paramref name="source"/> (untrimmed, normal speed). It is filed just below the source, so it never becomes the
    /// newest clip and the stream keeps showing what it showed. Null when the file could not be moved.
    /// </summary>
    public ReplayClip? AdoptCopy(ReplayClip source, string tempFile, string title)
    {
        ReplayClip clip;
        lock (_lock)
        {
            var number = _clips.Select(c => NumberOf(c.File)).DefaultIfEmpty(0).Max();
            if (Directory.Exists(PlaylistFolder))
                foreach (var path in Directory.EnumerateFiles(PlaylistFolder, "*.mp4")) number = Math.Max(number, NumberOf(Path.GetFileName(path)));

            var file = $"{number + 1}-Replay-Replay.mp4";
            try { System.IO.File.Move(tempFile, Path.Combine(PlaylistFolder, file)); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }

            clip = Normalise(new ReplayClip
            {
                Id = Guid.NewGuid().ToString("N")[..10],
                File = file,
                Title = title,
                AddedAt = source.AddedAt - 1,
                DurationMs = Mp4Info.ReadDurationMs(Path.Combine(PlaylistFolder, file)),
                Match = source.Match,
                Tournament = source.Tournament,
                Tags = source.Tags.ToArray(),
            });
            _clips.Add(clip);
            Save();
        }
        Changed?.Invoke();
        return clip;
    }

    /// <summary>Removes the clip from the library and deletes its file.</summary>
    public bool Remove(string id)
    {
        ReplayClip? clip;
        lock (_lock)
        {
            clip = _clips.FirstOrDefault(c => c.Id == id);
            if (clip is null) return false;
            _clips.Remove(clip);
            if (_request is not null) _request = _request with { Clips = _request.Clips.Where(c => c.Id != id).ToArray() };
            Save();
        }
        try { System.IO.File.Delete(PathOf(clip)); }
        catch (IOException) { /* in use (the overlay is playing it): the entry is gone, the file goes with the next clean-up */ }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Makes a custom category (empty until clips are put in it). False when the name is empty or already used.</summary>
    public bool AddCategory(string name)
    {
        name = name.Trim();
        lock (_lock)
        {
            if (name.Length == 0 || _categories.Any(c => Same(c, name))) return false;
            _categories.Add(name);
            Save();
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Deletes a custom category: the tag is taken off all clips (the clips stay).</summary>
    public bool RemoveCategory(string name)
    {
        lock (_lock)
        {
            var removed = _categories.RemoveAll(c => Same(c, name)) > 0;
            for (var i = 0; i < _clips.Count; i++)
            {
                if (!_clips[i].Tags.Any(t => Same(t, name))) continue;
                _clips[i] = _clips[i] with { Tags = _clips[i].Tags.Where(t => !Same(t, name)).ToArray() };
                removed = true;
            }
            if (!removed) return false;
            Save();
        }
        Changed?.Invoke();
        return true;
    }

    // ---- what the overlay shows --------------------------------------------------------------------------

    /// <summary>The overlay plays these clips once, in this order, then goes back to the newest clip.</summary>
    public ReplayRequest Play(IEnumerable<ReplayClip> clips)
    {
        lock (_lock)
        {
            _lastNonce = Math.Max(_lastNonce + 1, _nowMs());
            return _request = new ReplayRequest(_lastNonce, _nowMs(), clips.ToArray());
        }
    }

    /// <summary>Takes whatever is playing off the stream (the overlay goes back to the newest clip).</summary>
    public ReplayRequest Stop() => Play(Array.Empty<ReplayClip>());

    public ReplayRequest? Request
    {
        get { lock (_lock) return _request; }
    }

    /// <summary>The <c>replay</c> part of <c>/api/state</c>: the newest clip (looped when nothing else is asked) and the play request.</summary>
    public JsonObject BuildOverlayJson()
    {
        lock (_lock)
        {
            var newest = _clips.OrderByDescending(c => c.AddedAt).ThenByDescending(c => NumberOf(c.File)).FirstOrDefault();
            return new JsonObject
            {
                ["latest"] = newest is null ? null : ClipJson(newest),
                ["play"] = _request is null ? null : new JsonObject
                {
                    ["nonce"] = _request.Nonce,
                    ["at"] = _request.AtMs,
                    ["clips"] = new JsonArray(_request.Clips.Select(c => (JsonNode)ClipJson(c)).ToArray()),
                },
            };
        }
    }

    private static JsonObject ClipJson(ReplayClip c) => new()
    {
        ["id"] = c.Id,
        ["file"] = c.File,
        ["title"] = c.Title,
        ["start_ms"] = c.StartMs,
        ["end_ms"] = c.EndMs,
        ["speed"] = c.Speed,
    };

    // ---- the file ----------------------------------------------------------------------------------------

    private ReplayClip New(string file, long addedAt, string match, string tournament)
    {
        var number = NumberOf(file);
        return new ReplayClip
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            File = file,
            Title = number > 0 ? $"Clip {number}" : Path.GetFileNameWithoutExtension(file),
            AddedAt = addedAt,
            DurationMs = Mp4Info.ReadDurationMs(Path.Combine(PlaylistFolder, file)),
            Match = match,
            Tournament = tournament,
        };
    }

    // A trim that cannot be played (end before start, a negative start, a speed outside what a video can do) is put right.
    private static ReplayClip Normalise(ReplayClip c)
    {
        var start = Math.Max(0, c.StartMs);
        var end = Math.Max(0, c.EndMs);
        if (end > 0 && end <= start) end = 0;
        var speed = double.IsFinite(c.Speed) ? Math.Clamp(c.Speed, 0.1, 4) : 1;
        return c with { StartMs = start, EndMs = end, Speed = speed, Title = c.Title.Trim(), Match = c.Match.Trim(), Tournament = c.Tournament.Trim() };
    }

    private void EnsureCategory(string name)
    {
        if (!_categories.Any(c => Same(c, name))) _categories.Add(name);
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static int NumberOf(string file) =>
        Numbered.Match(file) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;

    private sealed record Document(int Schema, List<ReplayClip> Clips, List<string> Categories);

    private void Load()
    {
        try
        {
            if (!System.IO.File.Exists(FilePath)) return;
            var doc = JsonSerializer.Deserialize<Document>(System.IO.File.ReadAllText(FilePath), Json);
            if (doc is null) return;
            _clips.AddRange((doc.Clips ?? new()).Where(c => c.Id.Length > 0 && c.File.Length > 0).Select(Normalise));
            _categories.AddRange((doc.Categories ?? new()).Where(c => c.Length > 0));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A damaged list is kept aside and the clips are found again from the folder (titles and trims are lost, the files are not).
            try { System.IO.File.Move(FilePath, FilePath + ".bad", overwrite: true); }
            catch (IOException) { /* nothing more to do */ }
        }
    }

    // Called under the lock. Written to a temporary file first, so a crash never leaves a half-written list.
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            var text = JsonSerializer.Serialize(new Document(SchemaVersion, _clips, _categories), Json);
            var temp = FilePath + ".tmp";
            System.IO.File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false));
            System.IO.File.Move(temp, FilePath, overwrite: true);
        }
        catch (IOException) { /* the next change tries again */ }
        catch (UnauthorizedAccessException) { /* same */ }
    }
}

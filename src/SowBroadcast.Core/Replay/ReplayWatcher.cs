using System.Text.RegularExpressions;

namespace SowBroadcast.Core.Replay;

/// <summary>When a file was last written and how big it was: if both are the same, nothing happened to it.</summary>
public readonly record struct FileIdentity(long LastWriteTicks, long Length);

/// <summary>
/// Turns the clip OBS' replay buffer saves (<c>Replay/Replay Replay.mp4</c>) into the next numbered file of the playlist
/// (<c>Replay/Playlist/&lt;n&gt;-Replay-Replay.mp4</c>) and points <c>replaypath.txt</c> at it (CLAUDE.md 10). A clip is only taken when it has
/// stopped changing (OBS writes it for a moment), never twice, and a clip that was already there when the program started is not copied.
/// </summary>
public sealed class ReplayWatcher : IDisposable
{
    public const string SourceName = "Replay Replay.mp4";
    public const string PointerName = "replaypath.txt";
    public const int StableChecks = 3;

    private static readonly Regex NumberedClip = new(@"^(\d+)-Replay-Replay\.mp4$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string _folder;
    private readonly int _keep;
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _poll;
    private FileIdentity? _lastCopied;
    private FileIdentity? _candidate;
    private int _stable;

    /// <param name="replayFolder">The <c>Scoreboard/Replay</c> folder OBS saves into.</param>
    /// <param name="keep">How many numbered clips stay in the playlist (older ones are removed; 0 keeps everything).</param>
    public ReplayWatcher(string replayFolder, int keep = 25)
    {
        _folder = Path.GetFullPath(replayFolder);
        _keep = keep;
    }

    public string SourcePath => Path.Combine(_folder, SourceName);
    public string PlaylistFolder => Path.Combine(_folder, "Playlist");
    public string PointerPath => Path.Combine(_folder, PointerName);

    /// <summary>A new clip is in the playlist; the argument is its file name (what <c>replaypath.txt</c> holds).</summary>
    public event Action<string>? ReplayReady;

    /// <summary>Something went wrong that the user may want to know (a clip that could not be copied).</summary>
    public event Action<string>? Problem;

    // ---- the rules (one step at a time, so they can be tested without waiting) ---------------------------

    /// <summary>Remembers the clip that is already there so it is not copied again at startup.</summary>
    public void Baseline()
    {
        lock (_lock)
        {
            _lastCopied = IdentityOf(SourcePath);
            _candidate = null;
            _stable = 0;
        }
    }

    /// <summary>
    /// One look at the clip. Returns the new playlist file name when this look completed a copy, else null.
    /// A clip is copied after it has looked the same <see cref="StableChecks"/> times in a row.
    /// </summary>
    public string? Check()
    {
        lock (_lock)
        {
            var identity = IdentityOf(SourcePath);
            if (identity is null || identity == _lastCopied)
            {
                _candidate = null;
                _stable = 0;
                return null;
            }

            if (identity == _candidate) _stable++;
            else { _candidate = identity; _stable = 1; }
            if (_stable < StableChecks) return null;

            try
            {
                var name = Copy();
                _lastCopied = identity;
                _candidate = null;
                _stable = 0;
                return name;
            }
            catch (IOException)
            {
                _stable = 0;   // still being written or in use: look again later
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                _stable = 0;
                Problem?.Invoke($"The replay clip could not be copied: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>True while a clip is waiting to be taken (the polling should go on).</summary>
    public bool IsPending
    {
        get
        {
            lock (_lock)
            {
                var identity = IdentityOf(SourcePath);
                return identity is not null && identity != _lastCopied;
            }
        }
    }

    private string Copy()
    {
        Directory.CreateDirectory(PlaylistFolder);
        var next = NumbersInPlaylist().DefaultIfEmpty(0).Max() + 1;
        var name = $"{next}-Replay-Replay.mp4";
        var target = Path.Combine(PlaylistFolder, name);
        var temp = target + ".tmp";
        File.Copy(SourcePath, temp, overwrite: true);
        File.Move(temp, target, overwrite: true);
        File.WriteAllText(PointerPath, name, new System.Text.UTF8Encoding(false));
        Trim(next);
        return name;
    }

    private IEnumerable<int> NumbersInPlaylist() =>
        !Directory.Exists(PlaylistFolder)
            ? Enumerable.Empty<int>()
            : Directory.EnumerateFiles(PlaylistFolder)
                .Select(f => NumberedClip.Match(Path.GetFileName(f)))
                .Where(m => m.Success && int.TryParse(m.Groups[1].Value, out _))
                .Select(m => int.Parse(m.Groups[1].Value));

    /// <summary>Removes the oldest numbered clips beyond the limit; the clip just added is never removed.</summary>
    private void Trim(int current)
    {
        if (_keep <= 0) return;
        var clips = Directory.EnumerateFiles(PlaylistFolder)
            .Select(f => (Path: f, Match: NumberedClip.Match(Path.GetFileName(f))))
            .Where(x => x.Match.Success)
            .Select(x => (x.Path, Number: int.Parse(x.Match.Groups[1].Value)))
            .OrderByDescending(x => x.Number)
            .ToList();
        foreach (var old in clips.Skip(_keep).Where(c => c.Number != current))
        {
            try { File.Delete(old.Path); }
            catch (IOException) { /* a viewer still has it open; removed next time */ }
        }
    }

    private static FileIdentity? IdentityOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileIdentity(info.LastWriteTimeUtc.Ticks, info.Length) : null;
        }
        catch (IOException) { return null; }
    }

    // ---- running -----------------------------------------------------------------------------------------

    /// <summary>Starts watching. A clip that is already there is taken as seen (no copy at startup).</summary>
    public void Start()
    {
        Directory.CreateDirectory(PlaylistFolder);
        Baseline();

        _watcher = new FileSystemWatcher(_folder, "*.mp4") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
        _watcher.Created += (_, _) => Wake();
        _watcher.Changed += (_, _) => Wake();
        _watcher.Renamed += (_, _) => Wake();
        _watcher.EnableRaisingEvents = true;
        Wake();
    }

    // The watcher only says "something happened"; the checks themselves are 0.5 s apart until the clip is taken (or gone).
    private void Wake()
    {
        lock (_lock) _poll ??= new Timer(_ => Tick(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    private void Tick()
    {
        var name = Check();
        if (name is not null) ReplayReady?.Invoke(name);
        lock (_lock)
        {
            // Decided under the lock, so a file event that arrives meanwhile starts a new timer instead of being lost.
            if (IsPending) return;
            _poll?.Dispose();
            _poll = null;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        lock (_lock)
        {
            _poll?.Dispose();
            _poll = null;
        }
    }
}

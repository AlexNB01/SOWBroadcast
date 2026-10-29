namespace SowBroadcast.App.Services;

/// <summary>Plays a replay clip in the Replay tab so the operator can see it before it goes on the stream.</summary>
public interface IClipPlayer
{
    /// <summary>False when the video library could not be loaded (see <see cref="Problem"/>); everything else then does nothing.</summary>
    bool Available { get; }

    string? Problem { get; }

    /// <summary>Loads the file and shows its first frame, paused.</summary>
    void Open(string path);

    /// <summary>Stops and lets go of the file (so it can be deleted).</summary>
    void Close();

    void Play();
    void Pause();
    bool IsPlaying { get; }

    long PositionMs { get; }

    /// <summary>The length of the loaded file, 0 while unknown.</summary>
    long LengthMs { get; }

    void Seek(long positionMs);
    void SetRate(double rate);
    bool Muted { get; set; }
}

/// <summary>Used when there is no player (tests, or the video library is missing).</summary>
public sealed class NoClipPlayer : IClipPlayer
{
    public NoClipPlayer(string? problem = null) => Problem = problem ?? "Video preview is not available.";

    public bool Available => false;
    public string? Problem { get; }
    public bool IsPlaying => false;
    public long PositionMs => 0;
    public long LengthMs => 0;
    public bool Muted { get; set; } = true;
    public void Open(string path) { }
    public void Close() { }
    public void Play() { }
    public void Pause() { }
    public void Seek(long positionMs) { }
    public void SetRate(double rate) { }
}

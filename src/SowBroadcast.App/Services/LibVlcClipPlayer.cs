using LibVLCSharp.Shared;

namespace SowBroadcast.App.Services;

/// <summary>The Replay tab's preview, on LibVLC. When the native library is not there the player says so and does nothing.</summary>
public sealed class LibVlcClipPlayer : IClipPlayer, IDisposable
{
    private readonly LibVLC? _vlc;
    private Media? _media;
    private volatile bool _pauseWhenPlaying;

    public LibVlcClipPlayer()
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _vlc = new LibVLC("--no-video-title-show", "--no-osd", "--quiet");
            MediaPlayer = new MediaPlayer(_vlc) { Mute = true };
            MediaPlayer.Playing += (_, _) =>
            {
                if (!_pauseWhenPlaying) return;
                _pauseWhenPlaying = false;
                // LibVLC must not be called from inside its own event.
                Task.Run(() => MediaPlayer?.SetPause(true));
            };
            Available = true;
        }
        catch (Exception ex)
        {
            Problem = "Video preview is not available: " + FirstLine(ex.Message);
            Serilog.Log.Warning(ex, "LibVLC could not be loaded");
        }
    }

    /// <summary>What the view's video surface shows (null when LibVLC could not be loaded).</summary>
    public MediaPlayer? MediaPlayer { get; }

    public bool Available { get; }
    public string? Problem { get; }

    public bool IsPlaying => MediaPlayer?.IsPlaying == true;
    public long PositionMs => MediaPlayer is null ? 0 : Math.Max(0, MediaPlayer.Time);
    public long LengthMs => MediaPlayer is null ? 0 : Math.Max(0, MediaPlayer.Length);

    public bool Muted
    {
        get => MediaPlayer?.Mute ?? true;
        set { if (MediaPlayer is not null) MediaPlayer.Mute = value; }
    }

    public void Open(string path)
    {
        if (MediaPlayer is null || _vlc is null) return;
        Release();
        if (!File.Exists(path)) return;
        _media = new Media(_vlc, new Uri(path));
        _pauseWhenPlaying = true;   // shows the first frame, then waits
        MediaPlayer.Play(_media);
    }

    public void Close()
    {
        if (MediaPlayer is null) return;
        _pauseWhenPlaying = false;
        Task.Run(() => MediaPlayer.Stop()).Wait(TimeSpan.FromSeconds(2));
        Release();
    }

    public void Play()
    {
        if (MediaPlayer is null) return;
        _pauseWhenPlaying = false;
        // After the end the player is stopped, and Play starts the clip again.
        if (!MediaPlayer.IsPlaying) MediaPlayer.Play();
    }

    public void Pause()
    {
        if (MediaPlayer is { IsPlaying: true }) MediaPlayer.SetPause(true);
    }

    public void Seek(long positionMs)
    {
        if (MediaPlayer is null) return;
        MediaPlayer.Time = Math.Max(0, positionMs);
    }

    public void SetRate(double rate)
    {
        MediaPlayer?.SetRate((float)Math.Clamp(rate, 0.1, 4));
    }

    private void Release()
    {
        _media?.Dispose();
        _media = null;
    }

    public void Dispose()
    {
        _pauseWhenPlaying = false;
        try
        {
            Task.Run(() => MediaPlayer?.Stop()).Wait(TimeSpan.FromSeconds(2));
            Release();
            MediaPlayer?.Dispose();
            _vlc?.Dispose();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "LibVLC did not shut down cleanly");
        }
    }

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();
}

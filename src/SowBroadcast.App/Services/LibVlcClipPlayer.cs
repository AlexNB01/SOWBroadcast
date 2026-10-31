using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace SowBroadcast.App.Services;

/// <summary>
/// The Replay tab's preview, on LibVLC. When the native library is not there the player says so and does nothing.
/// The picture is not drawn by a native video window: LibVLC hands every frame to <see cref="FrameReady"/> and the view shows it as an
/// ordinary image, so the clip can only ever appear inside the preview box (a native window could end up as a window of its own).
/// </summary>
public sealed class LibVlcClipPlayer : IClipPlayer, IDisposable
{
    // The frame size the picture is delivered in (clips come from OBS, always 16:9); LibVLC scales the video to it.
    private const int FrameWidth = 960;
    private const int FrameHeight = 540;
    private const int FramePitch = FrameWidth * 4;

    private readonly IntPtr _frame = Marshal.AllocHGlobal(FramePitch * FrameHeight);
    private readonly object _frameLock = new();
    private WriteableBitmap? _bitmap;
    private int _frameQueued;
    private volatile bool _disposed;

    private readonly LibVLC? _vlc;
    private Media? _media;
    private volatile bool _pauseWhenPlaying;
    private volatile bool _surface;
    private string? _wanted;
    private float _rate = 1;
    private long _seekWhenPlaying = -1;   // where to go once a restarted clip is playing (-1 = nowhere)

    // A clip that played to its end (or was stopped) is not started again by Play or moved by Seek: it has to be opened again.
    private bool NeedsRestart =>
        _wanted is not null && _surface && MediaPlayer is { State: VLCState.Ended or VLCState.Stopped or VLCState.NothingSpecial or VLCState.Error };

    public LibVlcClipPlayer()
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _vlc = new LibVLC("--no-video-title-show", "--no-osd", "--quiet");
            MediaPlayer = new MediaPlayer(_vlc) { Mute = true };
            MediaPlayer.SetVideoFormat("RV32", FrameWidth, FrameHeight, FramePitch);
            MediaPlayer.SetVideoCallbacks(
                (opaque, planes) => { Monitor.Enter(_frameLock); Marshal.WriteIntPtr(planes, _frame); return IntPtr.Zero; },
                (opaque, picture, planes) => Monitor.Exit(_frameLock),
                (opaque, picture) => QueueFrame());
            MediaPlayer.Playing += (_, _) =>
            {
                var seek = Interlocked.Exchange(ref _seekWhenPlaying, -1);
                var pause = _pauseWhenPlaying;
                _pauseWhenPlaying = false;
                // LibVLC must not be called from inside its own event, and the first picture needs a moment before it can be held.
                Task.Run(async () =>
                {
                    if (MediaPlayer is null) return;
                    if (Math.Abs(_rate - 1) > 0.001f) MediaPlayer.SetRate(_rate);   // a restarted clip forgets the speed
                    if (seek >= 0) MediaPlayer.Time = seek;
                    if (!pause) return;
                    await Task.Delay(250);
                    MediaPlayer?.SetPause(true);
                });
            };
            Available = true;
        }
        catch (Exception ex)
        {
            Problem = "Video preview is not available: " + FirstLine(ex.Message);
            Serilog.Log.Warning(ex, "LibVLC could not be loaded");
        }
    }

    /// <summary>The player itself (null when LibVLC could not be loaded).</summary>
    public MediaPlayer? MediaPlayer { get; }

    /// <summary>The picture shown now; the same bitmap is updated in place, so a view only has to redraw when <see cref="FrameReady"/> fires.</summary>
    public WriteableBitmap? Frame => _bitmap;

    /// <summary>A new picture is in <see cref="Frame"/> (raised on the UI thread).</summary>
    public event Action? FrameReady;

    // Called on LibVLC's thread for every picture: at most one copy to the UI thread is waiting at a time.
    private void QueueFrame()
    {
        if (Interlocked.Exchange(ref _frameQueued, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _frameQueued, 0);
            if (_disposed) return;
            _bitmap ??= new WriteableBitmap(new PixelSize(FrameWidth, FrameHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var target = _bitmap.Lock())
            {
                lock (_frameLock)
                {
                    for (var y = 0; y < FrameHeight; y++)
                        unsafe { Buffer.MemoryCopy((byte*)_frame + y * FramePitch, (byte*)target.Address + y * target.RowBytes, target.RowBytes, FramePitch); }
                }
            }
            FrameReady?.Invoke();
        }, DispatcherPriority.Render);
    }

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

    /// <summary>
    /// Playing before the video surface exists makes LibVLC open a window of its own, so a clip is only started while the view's surface is
    /// there; until then the clip is remembered and opened when the surface arrives.
    /// </summary>
    public void Open(string path)
    {
        if (MediaPlayer is null || _vlc is null) return;
        _wanted = path;
        _seekWhenPlaying = -1;
        if (!_surface) { StopPlayback(); return; }
        Start(path);
    }

    /// <summary>The view's video surface is in a window: the clip that was asked for can be shown.</summary>
    public void SurfaceReady()
    {
        if (MediaPlayer is null) return;
        _surface = true;
        if (_wanted is not null) Start(_wanted);
    }

    /// <summary>The surface is gone (another tab is shown): stop, so LibVLC does not make a window of its own.</summary>
    public void SurfaceLost()
    {
        _surface = false;
        StopPlayback();
    }

    private void Start(string path, bool hold = true)
    {
        if (MediaPlayer is null || _vlc is null) return;
        Release();
        if (!File.Exists(path)) return;
        _media = new Media(_vlc, new Uri(path));
        _pauseWhenPlaying = hold;   // hold: shows the first frame, then waits
        MediaPlayer.Play(_media);
    }

    private void StopPlayback()
    {
        if (MediaPlayer is null) return;
        _pauseWhenPlaying = false;
        Task.Run(() => MediaPlayer.Stop()).Wait(TimeSpan.FromSeconds(2));
        Release();
    }

    public void Close()
    {
        if (MediaPlayer is null) return;
        _wanted = null;
        _pauseWhenPlaying = false;
        Task.Run(() => MediaPlayer.Stop()).Wait(TimeSpan.FromSeconds(2));
        Release();
    }

    public void Play()
    {
        if (MediaPlayer is null) return;
        _pauseWhenPlaying = false;
        if (NeedsRestart) Start(_wanted!, hold: false);                        // it played to the end: from the beginning again
        else if (MediaPlayer.State == VLCState.Paused) MediaPlayer.Play();   // carries on
    }

    public void Pause()
    {
        if (MediaPlayer is { IsPlaying: true }) MediaPlayer.SetPause(true);
    }

    public void Seek(long positionMs)
    {
        if (MediaPlayer is null) return;
        positionMs = Math.Max(0, positionMs);
        if (NeedsRestart)
        {
            // Nothing is playing to move: open the clip again, hold it, and go there once it runs.
            _seekWhenPlaying = positionMs;
            _pauseWhenPlaying = true;
            Start(_wanted!);
            return;
        }
        MediaPlayer.Time = positionMs;
    }

    public void SetRate(double rate)
    {
        _rate = (float)Math.Clamp(rate, 0.1, 4);
        MediaPlayer?.SetRate(_rate);
    }

    private void Release()
    {
        _media?.Dispose();
        _media = null;
    }

    public void Dispose()
    {
        _disposed = true;
        _pauseWhenPlaying = false;
        try
        {
            Task.Run(() => MediaPlayer?.Stop()).Wait(TimeSpan.FromSeconds(2));
            Release();
            MediaPlayer?.Dispose();
            _vlc?.Dispose();
            Marshal.FreeHGlobal(_frame);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "LibVLC did not shut down cleanly");
        }
    }

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();
}

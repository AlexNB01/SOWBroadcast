using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;

namespace SowBroadcast.App.Views;

public partial class ReplayView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _dragging;

    public ReplayView()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => (DataContext as ReplayViewModel)?.Tick(followPosition: !_dragging);
        // handledEventsToo: the slider's own handling marks the pointer events as done
        Seek.AddHandler(PointerPressedEvent, (_, _) => _dragging = true, handledEventsToo: true);
        Seek.AddHandler(PointerReleasedEvent, (_, _) => EndDrag(), handledEventsToo: true);
        Seek.PointerCaptureLost += (_, _) => EndDrag();
        DataContextChanged += (_, _) => ConnectVideo();
    }

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        (DataContext as ReplayViewModel)?.SeekTo(Seek.Value);
    }

    private LibVlcClipPlayer? _connected;

    // The picture is a bitmap the player updates in place; the image only has to be told to draw it again.
    private void ConnectVideo()
    {
        if (DataContext is not ReplayViewModel { Player: LibVlcClipPlayer player } || ReferenceEquals(player, _connected)) return;
        DisconnectVideo();
        _connected = player;
        player.FrameReady += ShowFrame;
        ShowFrame();
    }

    private void DisconnectVideo()
    {
        if (_connected is null) return;
        _connected.FrameReady -= ShowFrame;
        _connected = null;
    }

    private void ShowFrame()
    {
        if (_connected?.Frame is not { } frame) return;
        if (!ReferenceEquals(Video.Source, frame)) Video.Source = frame;
        Video.InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
        // The video surface gets its window handle after this tab is shown; playing only then keeps LibVLC from opening a window of its own.
        Dispatcher.UIThread.Post(() =>
        {
            ConnectVideo();
            if (DataContext is ReplayViewModel { Player: LibVlcClipPlayer player }) player.SurfaceReady();
        }, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        DisconnectVideo();
        if (DataContext is ReplayViewModel { Player: LibVlcClipPlayer player }) player.SurfaceLost();
        base.OnDetachedFromVisualTree(e);
    }
}

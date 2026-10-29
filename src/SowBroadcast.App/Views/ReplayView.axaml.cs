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

    private void ConnectVideo()
    {
        if (DataContext is ReplayViewModel { Player: LibVlcClipPlayer { MediaPlayer: { } player } })
            Video.MediaPlayer = player;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }
}

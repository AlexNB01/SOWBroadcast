using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SowBroadcast.App.Controls;

/// <summary>
/// How far the clips on the stream have got: one block per clip, as wide as the clip is long, filled as time passes, with a line at
/// the moment that is on the stream now. The remaining blocks show what is still to come.
/// </summary>
public sealed class ReplayRunBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<long>?> SegmentsProperty =
        AvaloniaProperty.Register<ReplayRunBar, IReadOnlyList<long>?>(nameof(Segments));

    public static readonly StyledProperty<long> ElapsedMsProperty = AvaloniaProperty.Register<ReplayRunBar, long>(nameof(ElapsedMs));

    /// <summary>The last seconds: the filled part turns to the warning colour.</summary>
    public static readonly StyledProperty<bool> UrgentProperty = AvaloniaProperty.Register<ReplayRunBar, bool>(nameof(Urgent));

    private const double Gap = 3;

    static ReplayRunBar()
    {
        AffectsRender<ReplayRunBar>(SegmentsProperty, ElapsedMsProperty, UrgentProperty);
    }

    public IReadOnlyList<long>? Segments { get => GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public long ElapsedMs { get => GetValue(ElapsedMsProperty); set => SetValue(ElapsedMsProperty, value); }
    public bool Urgent { get => GetValue(UrgentProperty); set => SetValue(UrgentProperty, value); }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 200 : availableSize.Width, 22);

    private IBrush Brush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var segments = Segments;
        if (segments is null || segments.Count == 0) return;

        var total = segments.Sum();
        if (total <= 0) return;

        var empty = Brush("SowLionBrush", Brushes.DarkBlue);
        var done = Urgent ? Brush("SowErrorBrush", Brushes.IndianRed) : Brush("SowCrownBrush", Brushes.SteelBlue);
        var marker = Brush("SowWhiteBrush", Brushes.White);

        var room = Math.Max(0, Bounds.Width - Gap * (segments.Count - 1));
        var x = 0.0;
        long start = 0;
        foreach (var length in segments)
        {
            var width = room * length / total;
            var block = new Rect(x, 0, Math.Max(width, 1), Bounds.Height);
            context.DrawRectangle(empty, null, block, 4, 4);

            var filled = Math.Clamp(ElapsedMs - start, 0, length);
            if (filled > 0)
            {
                var part = new Rect(x, 0, Math.Max(width * filled / length, 1), Bounds.Height);
                using (context.PushClip(block)) context.DrawRectangle(done, null, part, 4, 4);
            }

            if (ElapsedMs >= start && ElapsedMs < start + length)
            {
                var at = x + width * (ElapsedMs - start) / length;
                context.DrawRectangle(marker, null, new Rect(Math.Max(0, at - 1), -2, 2, Bounds.Height + 4));
            }

            x += width + Gap;
            start += length;
        }
    }
}

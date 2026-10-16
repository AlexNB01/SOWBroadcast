using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls;

namespace SowBroadcast.App.Controls;

/// <summary>
/// A colour button: shows the colour, and opens a small panel with palette swatches and a hex field.
/// (A first, plain version of the planned in-app colour picker; the OS colour dialog does not follow the theme.)
/// </summary>
public sealed class ColorField : UserControl
{
    public static readonly StyledProperty<string?> HexProperty =
        AvaloniaProperty.Register<ColorField, string?>(nameof(Hex), "#ffffff", defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private static readonly string[] Palette =
    {
        "#001445", "#134380", "#1e8ab9", "#0f356b", "#23c5d7", "#a9b8cc",
        "#f2f6fa", "#ffffff", "#000000", "#55aaff", "#ff557f", "#c80013",
        "#27aae1", "#ffcc00", "#2ecc71", "#ff8a00",
    };

    private static readonly Regex HexPattern = new("^#?([0-9a-fA-F]{6})$", RegexOptions.Compiled);

    private readonly Border _swatch = new() { Width = 20, Height = 20, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1) };
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _hexBox = new() { Watermark = "#rrggbb", MaxLength = 7, MinWidth = 120 };
    private readonly Button _button;
    private readonly Flyout _flyout;

    public ColorField()
    {
        FrostBorder(_swatch, Border.BorderBrushProperty);
        _text.Classes.Add("muted");

        var face = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        face.Children.Add(_swatch);
        face.Children.Add(_text);

        _button = new Button { Content = face, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        _button.Classes.Add("comboLike");

        var swatches = new WrapPanel { Width = 200 };
        foreach (var hex in Palette)
        {
            var pick = new Button
            {
                Width = 28, Height = 28, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(2),
                Background = new SolidColorBrush(Color.Parse(hex)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
            };
            FrostBorder(pick, Button.BorderBrushProperty);
            ToolTip.SetTip(pick, hex);
            pick.Click += (_, _) => { Hex = hex; _flyout!.Hide(); };
            swatches.Children.Add(pick);
        }

        _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitHex(); e.Handled = true; } };
        _hexBox.LostFocus += (_, _) => CommitHex();

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(swatches);
        panel.Children.Add(_hexBox);

        _flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft, Content = panel };
        _flyout.Opened += (_, _) => { _hexBox.Text = Hex; };
        _button.Flyout = _flyout;

        Content = _button;
        HexProperty.Changed.AddClassHandler<ColorField>((c, _) => c.UpdateFace());
        UpdateFace();
    }

    /// <summary>Opens the palette panel (also used by the screenshot helper).</summary>
    public void OpenPanel() => _flyout.ShowAt(_button);

    // The theme brush is resolved once the control is in the tree (a lookup in the constructor finds nothing yet).
    private void FrostBorder(AvaloniaObject target, AvaloniaProperty property) =>
        target.Bind(property, this.GetResourceObservable("SowFrostBrush"));

    /// <summary>The colour as <c>#rrggbb</c>.</summary>
    public string? Hex { get => GetValue(HexProperty); set => SetValue(HexProperty, value); }

    private void CommitHex()
    {
        var match = HexPattern.Match((_hexBox.Text ?? "").Trim());
        if (match.Success) Hex = "#" + match.Groups[1].Value.ToLowerInvariant();
        else _hexBox.Text = Hex;
    }

    private void UpdateFace()
    {
        var ok = Color.TryParse(Hex, out var color);
        _swatch.Background = ok ? new SolidColorBrush(color) : Brushes.Transparent;
        _text.Text = Hex ?? "";
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Control;

namespace SowBroadcast.App.Views;

public partial class ControlSettingsWindow : Window
{
    private readonly ControlSettingsViewModel _viewModel;

    public ControlSettingsWindow() : this(new ControlSettingsViewModel(new ControlSettings(), null)) { }

    public ControlSettingsWindow(ControlSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>The key combination of a key press, or null while only modifiers are down or the key is not one we can register.</summary>
    public static HotkeyGesture? GestureOf(Key key, KeyModifiers modifiers)
    {
        var name = HotkeyGesture.CanonicalKey(key.ToString());
        if (name is null) return null;
        var mods = HotkeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control)) mods |= HotkeyModifiers.Ctrl;
        if (modifiers.HasFlag(KeyModifiers.Alt)) mods |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift)) mods |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Meta)) mods |= HotkeyModifiers.Win;
        return new HotkeyGesture(mods, name);
    }

    private void OnGestureKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: HotkeyRowViewModel row }) return;
        if (e.Key == Key.Tab) return;   // keep keyboard navigation working
        e.Handled = true;
        if (e.Key is Key.Back or Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            row.Gesture = "";
            return;
        }
        if (GestureOf(e.Key, e.KeyModifiers) is { } gesture) row.Gesture = gesture.ToString();
    }

    private async void OnCopyToken(object? sender, RoutedEventArgs e) => await CopyAsync(_viewModel.Token);

    private async void OnCopyExample(object? sender, RoutedEventArgs e) => await CopyAsync(_viewModel.ExampleUrl);

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnSave(object? sender, RoutedEventArgs e) => Close(_viewModel.ToSettings());
}

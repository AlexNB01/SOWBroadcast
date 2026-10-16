using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.Core.Control;

namespace SowBroadcast.App.ViewModels;

/// <summary>One command in the hotkey list.</summary>
public sealed partial class HotkeyRowViewModel : ObservableObject
{
    public HotkeyRowViewModel(ControlCommandInfo command, string? gesture)
    {
        Command = command;
        _gesture = gesture ?? "";
    }

    public ControlCommandInfo Command { get; }
    public string Label => Command.Label;
    public string Id => Command.Id;
    public string DefaultText => Command.DefaultGesture ?? "none";

    /// <summary>The key combination as text; empty means no hotkey.</summary>
    [ObservableProperty] private string _gesture;

    [RelayCommand]
    private void Clear() => Gesture = "";

    [RelayCommand]
    private void UseDefault() => Gesture = Command.DefaultGesture ?? "";
}

/// <summary>The "Hotkeys and remote control" window: hotkeys, whether they work everywhere, and the token + URLs for Stream Deck / Companion.</summary>
public sealed partial class ControlSettingsViewModel : ObservableObject
{
    private readonly string _baseUrl;

    public ControlSettingsViewModel(ControlSettings settings, string? baseUrl)
    {
        _baseUrl = baseUrl ?? "http://127.0.0.1:8324";
        Rows = ControlCatalog.All.Select(c => new HotkeyRowViewModel(c, settings.GestureFor(c.Id))).ToArray();
        foreach (var row in Rows) row.PropertyChanged += (_, _) => Refresh();
        _globalHotkeys = settings.GlobalHotkeys;
        _token = settings.Token;
        Refresh();
    }

    public IReadOnlyList<HotkeyRowViewModel> Rows { get; }

    [ObservableProperty] private bool _globalHotkeys;
    [ObservableProperty] private string _token;
    [ObservableProperty] private string _problems = "";
    [ObservableProperty] private bool _canSave = true;

    public string BaseUrl => _baseUrl;
    public string ExampleUrl => $"{_baseUrl}/api/control/{ControlCatalog.Team1ScoreUp}?token={Token}";
    public string ListUrl => $"{_baseUrl}/api/control?token={Token}";

    partial void OnGlobalHotkeysChanged(bool value) => Refresh();

    partial void OnTokenChanged(string value)
    {
        OnPropertyChanged(nameof(ExampleUrl));
        OnPropertyChanged(nameof(ListUrl));
    }

    [RelayCommand]
    private void NewToken() => Token = ControlSettings.NewToken();

    [RelayCommand]
    private void ResetAll()
    {
        foreach (var row in Rows) row.Gesture = row.Command.DefaultGesture ?? "";
    }

    /// <summary>What the window shows, as settings to keep.</summary>
    public ControlSettings ToSettings()
    {
        var settings = new ControlSettings { Token = Token, GlobalHotkeys = GlobalHotkeys };
        foreach (var row in Rows)
        {
            // Normalised when it reads as a gesture; anything else is kept as typed so Problems can name it.
            var text = HotkeyGesture.TryParse(row.Gesture, out var gesture) ? gesture.ToString() : row.Gesture.Trim();
            settings.Bindings[row.Id] = text;
        }
        return settings;
    }

    private void Refresh()
    {
        var problems = ToSettings().Problems();
        Problems = string.Join(Environment.NewLine, problems);
        CanSave = problems.Count == 0;
    }
}

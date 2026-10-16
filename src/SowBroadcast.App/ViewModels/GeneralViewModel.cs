using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Model;

namespace SowBroadcast.App.ViewModels;

/// <summary>One of the eight overlay colours: its key in <c>colors.txt</c>, a name, what it colours, and the current value.</summary>
public sealed partial class OverlayColourViewModel : ObservableObject
{
    public OverlayColourViewModel(string key, string title, string description, string hex)
    {
        Key = key;
        Title = title;
        Description = description;
        _hex = hex;
    }

    public string Key { get; }
    public string Title { get; }
    public string Description { get; }

    [ObservableProperty] private string _hex;
}

/// <summary>The General tab: casters and host, the two broadcast logos, the status text and the overlay colours.</summary>
public sealed partial class GeneralViewModel : ObservableObject
{
    private static readonly (string Key, string Title, string Description)[] ColourFields =
    {
        ("primary", "Primary", "Background colour behind all text"),
        ("secondary", "Secondary", "Colour of most text"),
        ("tertiary", "Tertiary", "Accents, scores and behind “vs” in the scenes outside the game"),
        ("quaternary", "Quaternary", "Background behind scores in the scenes outside the game"),
        ("quinary", "Quinary", "Matchup labels in the bracket scenes, “Playoffs” text, away screen labels and social media text"),
        ("senary", "Senary", "Text colour of the message on the away screen and bracket scenes"),
        ("septenary", "Septenary", "Primary background colour during the stinger transitions"),
        ("octonary", "Octonary", "Secondary (trailing) background colour during the stinger transitions"),
    };

    private readonly IImagePicker _images;
    private readonly string? _logoFolder;
    private bool _loading;

    public GeneralViewModel(IImagePicker images, string? logoFolder)
    {
        _images = images;
        _logoFolder = logoFolder;
        var defaults = GeneralSettings.DefaultColors();
        Colours = ColourFields.Select(f => new OverlayColourViewModel(f.Key, f.Title, f.Description, defaults[f.Key])).ToArray();
        foreach (var colour in Colours) colour.PropertyChanged += (_, _) => RaiseEdited();
    }

    public IReadOnlyList<OverlayColourViewModel> Colours { get; }

    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _caster1 = "";
    [ObservableProperty] private string _caster2 = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string? _overlayLogoPath;
    [ObservableProperty] private string? _transitionLogoPath;

    /// <summary>Raised when the user changed something (not while a state is being loaded).</summary>
    public event Action? Edited;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        RaiseEdited();
    }

    private void RaiseEdited()
    {
        if (!_loading) Edited?.Invoke();
    }

    [RelayCommand]
    private async Task LoadOverlayLogoAsync()
    {
        var path = await _images.PickImageAsync("Select overlay logo", _logoFolder);
        if (path is not null) OverlayLogoPath = path;
    }

    [RelayCommand]
    private async Task LoadTransitionLogoAsync()
    {
        var path = await _images.PickImageAsync("Select transition logo", _logoFolder);
        if (path is not null) TransitionLogoPath = path;
    }

    public void Load(AppState state) => Show(state.General);

    /// <summary>Back to the defaults. The series length and player count belong to the Match tab and are not touched.</summary>
    public void Reset() => Show(new GeneralSettings(), raiseEdited: true);

    private void Show(GeneralSettings g, bool raiseEdited = false)
    {
        _loading = !raiseEdited;
        try
        {
            Host = g.Host;
            Caster1 = g.Caster1;
            Caster2 = g.Caster2;
            StatusText = g.StatusText;
            OverlayLogoPath = string.IsNullOrEmpty(g.OverlayLogoPath) ? null : g.OverlayLogoPath;
            TransitionLogoPath = string.IsNullOrEmpty(g.TransitionLogoPath) ? null : g.TransitionLogoPath;
            var defaults = GeneralSettings.DefaultColors();
            foreach (var colour in Colours)
                colour.Hex = g.Colors.TryGetValue(colour.Key, out var hex) && !string.IsNullOrWhiteSpace(hex) ? hex : defaults[colour.Key];
        }
        finally { _loading = false; }
    }

    public AppState ApplyTo(AppState state) => state with
    {
        General = state.General with
        {
            Host = Host.Trim(),
            Caster1 = Caster1.Trim(),
            Caster2 = Caster2.Trim(),
            StatusText = StatusText.Trim(),
            OverlayLogoPath = string.IsNullOrEmpty(OverlayLogoPath) ? null : OverlayLogoPath,
            TransitionLogoPath = string.IsNullOrEmpty(TransitionLogoPath) ? null : TransitionLogoPath,
            Colors = Colours.ToDictionary(c => c.Key, c => c.Hex),
        },
    };
}

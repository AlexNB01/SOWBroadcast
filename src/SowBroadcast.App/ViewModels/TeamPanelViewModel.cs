using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Model;

namespace SowBroadcast.App.ViewModels;

/// <summary>One team panel of the Match tab: name, abbreviation, score, colour, logo and eight player rows.</summary>
public sealed partial class TeamPanelViewModel : ObservableObject
{
    private readonly IImagePicker _images;
    private readonly string? _logoFolder;

    public TeamPanelViewModel(int number, string defaultColour, Func<string?, string?> roleOfHero, IImagePicker images, string? logoFolder)
    {
        Number = number;
        DefaultColour = defaultColour;
        _colourHex = defaultColour;
        _images = images;
        _logoFolder = logoFolder;
        Players = Enumerable.Range(1, Limits.PlayersPerTeam).Select(i => new PlayerRowViewModel(i, roleOfHero)).ToArray();
    }

    public int Number { get; }
    public string Title => $"TEAM {Number}";
    public string DefaultColour { get; }
    public IReadOnlyList<PlayerRowViewModel> Players { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _abbr = "";
    [ObservableProperty] private int _score;
    [ObservableProperty] private string _colourHex;
    [ObservableProperty] private string? _logoPath;

    /// <summary>Shown in the pick lists of the map cards: the team name, or "T1"/"T2" while it is empty.</summary>
    public string PickLabel => string.IsNullOrWhiteSpace(Name) ? $"T{Number}" : Name.Trim();

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(PickLabel));

    // Advice on the team colour against the overlay's text and background colours (EHD-12); empty when the colour is fine.
    private string _overlayText = "#000000";
    private string _overlayBackground = "#FFFFFF";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasColourWarning))]
    private string _colourWarning = "";

    public bool HasColourWarning => ColourWarning.Length > 0;

    partial void OnColourHexChanged(string value) => UpdateColourWarning();

    /// <summary>The overlay colours of the General tab: the text colour ("secondary") and the plate behind text ("primary").</summary>
    public void SetOverlayColours(string text, string background)
    {
        _overlayText = text;
        _overlayBackground = background;
        UpdateColourWarning();
    }

    private void UpdateColourWarning() =>
        ColourWarning = SowBroadcast.Core.Colour.ColourContrast.TeamColourAdvice(ColourHex, _overlayText, _overlayBackground);

    [RelayCommand]
    private async Task LoadLogoAsync()
    {
        var path = await _images.PickImageAsync("Select team logo", _logoFolder);
        if (path is not null) LogoPath = path;
    }

    public void Load(Team team)
    {
        Name = team.Name;
        Abbr = team.Abbr;
        Score = team.Score;
        ColourHex = string.IsNullOrWhiteSpace(team.ColorHex) ? DefaultColour : team.ColorHex;
        LogoPath = string.IsNullOrEmpty(team.LogoPath) ? null : team.LogoPath;
        for (var i = 0; i < Players.Count; i++)
            Players[i].Load(i < team.Players.Count ? team.Players[i] : new Player());
    }

    /// <summary>The team as edited; fields the panel does not edit (the legacy banned hero) come from <paramref name="current"/>.</summary>
    public Team ToTeam(Team current) => current with
    {
        Name = Name.Trim(),
        Abbr = Abbr.Trim(),
        LogoPath = string.IsNullOrEmpty(LogoPath) ? null : LogoPath,
        Score = Math.Clamp(Score, 0, 200),
        ColorHex = ColourHex,
        Players = Players.Select(p => p.ToPlayer()).ToArray(),
    };
}

using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Model;

namespace SowBroadcast.App.ViewModels;

/// <summary>
/// The editable side of the Match tab: two team panels, seven map cards and the format bar. It mirrors the match part of an
/// <see cref="AppState"/> (<see cref="Load"/> / <see cref="ApplyTo"/>) and says when the user changed something (<see cref="Edited"/>).
/// </summary>
public sealed partial class MatchViewModel : ObservableObject
{
    // Properties that follow from others and are not user edits.
    private static readonly HashSet<string> NotEdits = new() { "IsShown", "MapImagePath", "PickLabel", "Label", "Title", "HeroNames", "MapNames" };

    private readonly AssetService? _assets;
    private bool _loading;

    public MatchViewModel(AssetService? assets, IImagePicker images, string? logoFolder)
    {
        _assets = assets;
        PickOptions = new[] { new PickOption(Picks.Tba, "TBA"), new PickOption(Picks.Team1, "T1"), new PickOption(Picks.Team2, "T2") };
        Func<string?, string?> roleOfHero = hero => _assets?.HeroRole(hero);
        Team1 = new TeamPanelViewModel(1, Defaults.Team1Color, roleOfHero, images, logoFolder);
        Team2 = new TeamPanelViewModel(2, Defaults.Team2Color, roleOfHero, images, logoFolder);
        Maps = Enumerable.Range(1, Limits.MapsPerSeries)
            .Select(i => new MapCardViewModel(i, PickOptions, map => _assets?.ImageFile(AssetCategory.Maps, map)))
            .ToArray();

        RefreshLists();
        UpdatePickLabels();
        UpdateVisibility();

        foreach (var source in new INotifyPropertyChanged[] { this, Team1, Team2 }.Concat(Team1.Players).Concat(Team2.Players).Concat(Maps))
            source.PropertyChanged += OnChildChanged;
        Team1.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TeamPanelViewModel.PickLabel)) UpdatePickLabels(); };
        Team2.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TeamPanelViewModel.PickLabel)) UpdatePickLabels(); };
        if (assets is not null) assets.Changed += _ => RefreshLists();
    }

    public TeamPanelViewModel Team1 { get; }
    public TeamPanelViewModel Team2 { get; }
    public IReadOnlyList<MapCardViewModel> Maps { get; }
    public IReadOnlyList<PickOption> PickOptions { get; }

    /// <summary>How many map cards are shown (the series length).</summary>
    [ObservableProperty] private int _mapsCount = Limits.MapsPerSeries;

    /// <summary>How many player rows are shown in each panel.</summary>
    [ObservableProperty] private int _playersCount = 5;

    /// <summary>Raised when the user changed something (not while a state is being loaded).</summary>
    public event Action? Edited;

    partial void OnMapsCountChanged(int value) => UpdateVisibility();

    partial void OnPlayersCountChanged(int value) => UpdateVisibility();

    private void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading || e.PropertyName is null || NotEdits.Contains(e.PropertyName)) return;
        Edited?.Invoke();
    }

    private void RefreshLists()
    {
        var heroes = _assets?.Names(AssetCategory.Heroes) ?? Array.Empty<string>();
        var maps = _assets?.Names(AssetCategory.Maps) ?? Array.Empty<string>();
        foreach (var row in Team1.Players.Concat(Team2.Players)) row.HeroNames = heroes;
        foreach (var card in Maps)
        {
            card.HeroNames = heroes;
            card.MapNames = maps;
            card.RefreshImage();
        }
    }

    private void UpdatePickLabels()
    {
        PickOptions[1].Label = Team1.PickLabel;
        PickOptions[2].Label = Team2.PickLabel;
    }

    private void UpdateVisibility()
    {
        var maps = Math.Clamp(MapsCount, 1, Limits.MapsPerSeries);
        var players = Math.Clamp(PlayersCount, 1, Limits.PlayersPerTeam);
        foreach (var card in Maps) card.IsShown = card.Index <= maps;
        foreach (var team in new[] { Team1, Team2 })
            foreach (var row in team.Players)
                row.IsShown = row.Number <= players;
    }

    /// <summary>Shows the match part of <paramref name="state"/>. Raises no <see cref="Edited"/>.</summary>
    public void Load(AppState state)
    {
        _loading = true;
        try
        {
            MapsCount = Math.Clamp(state.General.MapsCount, 1, Limits.MapsPerSeries);
            PlayersCount = Math.Clamp(state.General.NumPlayers, 1, Limits.PlayersPerTeam);
            Team1.Load(state.Team1);
            Team2.Load(state.Team2);
            foreach (var card in Maps)
                card.Load(state.Maps.FirstOrDefault(m => m.Index == card.Index) ?? new MapEntry { Index = card.Index });
        }
        finally { _loading = false; }
    }

    /// <summary>Returns <paramref name="state"/> with the match part replaced by what is on screen (derived fields recomputed).</summary>
    public AppState ApplyTo(AppState state) => StateDerivations.Apply(state with
    {
        Team1 = Team1.ToTeam(state.Team1),
        Team2 = Team2.ToTeam(state.Team2),
        Maps = Maps.Select(c => c.ToEntry()).ToArray(),
        // The legacy "first to" value is a copy of the series length.
        General = state.General with { MapsCount = MapsCount, FirstTo = MapsCount, NumPlayers = PlayersCount },
    });
}

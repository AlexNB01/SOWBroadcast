using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.Core.Teams;

namespace SowBroadcast.App.ViewModels;

/// <summary>One team in the library list.</summary>
public sealed class LibraryItemViewModel
{
    public LibraryItemViewModel(LibraryTeam team, string? logoPath)
    {
        Id = team.Id;
        Name = team.Name;
        Abbr = team.Abbr;
        LogoPath = logoPath;
        var players = team.Players.Count(p => p.Name.Trim().Length > 0);
        var source = team.Source switch { "sowdraft" => "from SOWDraft", "file" => "from a team file", _ => "" };
        Details = string.Join(" · ", new[] { players == 0 ? "no players" : players == 1 ? "1 player" : $"{players} players", source }.Where(s => s.Length > 0));
    }

    public string Id { get; }
    public string Name { get; }
    public string Abbr { get; }
    public string? LogoPath { get; }
    public string Details { get; }
    public string Title => Abbr.Length == 0 ? Name : $"{Name}  ({Abbr})";
}

/// <summary>The team library window: search, load into Home / Away, keep the panels, delete, bring teams in from files or SOWDraft.</summary>
public sealed partial class TeamLibraryViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;

    public TeamLibraryViewModel(MainWindowViewModel main)
    {
        _main = main;
        Refresh();
    }

    public ObservableCollection<LibraryItemViewModel> Items { get; } = new();

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private LibraryItemViewModel? _selected;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _isEmpty;

    public bool CanImportFromLink => _main.LinkedTeamCount > 0;

    partial void OnQueryChanged(string value) => Refresh();

    private void Refresh()
    {
        var library = _main.Library;
        var keep = Selected?.Id;
        Items.Clear();
        if (library is not null)
            foreach (var team in library.Search(Query)) Items.Add(new LibraryItemViewModel(team, library.LogoPathOf(team)));
        Selected = Items.FirstOrDefault(i => i.Id == keep) ?? Items.FirstOrDefault();
        IsEmpty = library is null || library.All.Count == 0;
        OnPropertyChanged(nameof(CanImportFromLink));
    }

    [RelayCommand]
    private void LoadHome() => Load(1);

    [RelayCommand]
    private void LoadAway() => Load(2);

    private void Load(int slot)
    {
        if (Selected is { } item) Message = _main.LoadTeamFromLibrary(slot, item.Id);
    }

    [RelayCommand]
    private void SaveHome()
    {
        Message = _main.SaveTeamToLibrary(1);
        Refresh();
    }

    [RelayCommand]
    private void SaveAway()
    {
        Message = _main.SaveTeamToLibrary(2);
        Refresh();
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is not { } item) return;
        Message = _main.DeleteLibraryTeam(item.Id);
        Refresh();
    }

    [RelayCommand]
    private async Task ImportFileAsync()
    {
        Message = await _main.ImportTeamFileToLibraryAsync();
        Refresh();
    }

    [RelayCommand]
    private async Task ImportFromLinkAsync()
    {
        Message = await _main.ImportTeamsFromLinkAsync();
        Refresh();
    }
}

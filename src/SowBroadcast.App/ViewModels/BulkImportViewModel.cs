using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Assets;

namespace SowBroadcast.App.ViewModels;

public sealed partial class BulkImportRow : ObservableObject
{
    public BulkImportRow(ImportCandidate candidate)
    {
        Candidate = candidate;
        Name = candidate.Name;
        Mode = candidate.Mode;
    }

    public ImportCandidate Candidate { get; }
    public string FileName => Path.GetFileName(Candidate.FilePath);
    public bool IsMap => Candidate.Category == AssetCategory.Maps;
    public string Kind => Candidate.Category == AssetCategory.Maps ? "Map" : "Hero";

    [ObservableProperty] private bool _include = true;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string? _mode;

    public ImportCandidate ToCandidate() => Candidate with { Name = Name, Mode = Mode };
}

/// <summary>Adds pictures dropped into the Heroes and Maps folders to the catalogue in one go.</summary>
public sealed partial class BulkImportViewModel : ObservableObject
{
    private readonly AssetService _assets;
    private readonly IMessages _messages;

    public BulkImportViewModel(AssetService assets, IMessages messages)
    {
        _assets = assets;
        _messages = messages;
        ModeNames = assets.Names(AssetCategory.Modes);
        foreach (var candidate in assets.ScanForImport()) Rows.Add(new BulkImportRow(candidate));
    }

    public IReadOnlyList<string> ModeNames { get; }
    public ObservableCollection<BulkImportRow> Rows { get; } = new();
    public bool HasRows => Rows.Count > 0;

    /// <summary>Raised after an import so the window can close.</summary>
    public event Action? Imported;

    [RelayCommand]
    private async Task ImportAsync()
    {
        var (heroes, maps) = _assets.BulkImport(Rows.Where(r => r.Include).Select(r => r.ToCandidate()));
        await _messages.ShowInfoAsync("Bulk import", $"Imported {heroes} heroes and {maps} maps.");
        Imported?.Invoke();
    }
}

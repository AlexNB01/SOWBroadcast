using CommunityToolkit.Mvvm.ComponentModel;
using SowBroadcast.Core.Model;

namespace SowBroadcast.App.ViewModels;

/// <summary>Entry of the pick list: the stored value ("—", "T1" or "T2") with a label that follows the team name.</summary>
public sealed partial class PickOption : ObservableObject
{
    public PickOption(string value, string label)
    {
        Value = value;
        _label = label;
    }

    public string Value { get; }

    [ObservableProperty] private string _label;
}

/// <summary>One of the seven map cards of the Match tab.</summary>
public sealed partial class MapCardViewModel : ObservableObject
{
    private readonly Func<string?, string?> _imageOf;
    private readonly IReadOnlyList<PickOption> _pickOptions;

    public MapCardViewModel(int index, IReadOnlyList<PickOption> pickOptions, Func<string?, string?> imageOf)
    {
        Index = index;
        _pickOptions = pickOptions;
        _selectedPick = pickOptions[0];
        _imageOf = imageOf;
    }

    public int Index { get; }
    public string Label => $"M{Index}";
    public IReadOnlyList<PickOption> PickOptions => _pickOptions;

    /// <summary>The maps and heroes to choose from (set by the Match view model whenever the catalogue changes).</summary>
    [ObservableProperty] private IReadOnlyList<string> _mapNames = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> _heroNames = Array.Empty<string>();

    [ObservableProperty] private string? _map;
    [ObservableProperty] private int _t1;
    [ObservableProperty] private int _t2;
    [ObservableProperty] private bool _completed;
    [ObservableProperty] private PickOption _selectedPick;
    [ObservableProperty] private string? _t1Ban;
    [ObservableProperty] private string? _t2Ban;
    [ObservableProperty] private bool _isShown = true;
    [ObservableProperty] private string? _mapImagePath;

    partial void OnMapChanged(string? value) => RefreshImage();

    public void RefreshImage() => MapImagePath = _imageOf(Map);

    public void Load(MapEntry entry)
    {
        Map = string.IsNullOrEmpty(entry.Map) ? null : entry.Map;
        T1 = entry.T1;
        T2 = entry.T2;
        Completed = entry.Completed;
        SelectedPick = _pickOptions.FirstOrDefault(p => p.Value == entry.Pick) ?? _pickOptions[0];
        T1Ban = string.IsNullOrEmpty(entry.T1Ban) ? null : entry.T1Ban;
        T2Ban = string.IsNullOrEmpty(entry.T2Ban) ? null : entry.T2Ban;
    }

    public MapEntry ToEntry() => StateDerivations.WithWinner(new MapEntry
    {
        Index = Index,
        Map = Map ?? "",
        T1 = Math.Clamp(T1, 0, 200),
        T2 = Math.Clamp(T2, 0, 200),
        Completed = Completed,
        Pick = SelectedPick.Value,
        T1Ban = T1Ban ?? "",
        T2Ban = T2Ban ?? "",
    });
}

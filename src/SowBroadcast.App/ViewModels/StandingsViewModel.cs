using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Standings;

namespace SowBroadcast.App.ViewModels;

/// <summary>An observable object that tells its owner when one of its values was edited (not while it is being loaded).</summary>
public abstract class EditableObject : ObservableObject
{
    protected bool Loading;

    public event Action? Edited;

    protected void RaiseEdited()
    {
        if (!Loading) Edited?.Invoke();
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!IsDerived(e.PropertyName)) RaiseEdited();
    }

    /// <summary>Properties that follow from others and are not edits themselves.</summary>
    protected virtual bool IsDerived(string? propertyName) => false;
}

/// <summary>One line of a standings table.</summary>
public sealed partial class StandingsRowViewModel : EditableObject
{
    private readonly IImagePicker _images;
    private readonly string? _logoFolder;

    // Not typed by hand: kept so a round trip does not drop the figures a tournament link filled in.
    private readonly int? _played, _draws, _sf, _sa;

    public StandingsRowViewModel(StandingsRow row, IImagePicker images, string? logoFolder)
    {
        _images = images;
        _logoFolder = logoFolder;
        Loading = true;
        Rank = row.Rank;
        TeamName = row.TeamName;
        Abbr = row.Abbr;
        Wins = row.Wins;
        Losses = row.Losses;
        MapDiff = row.MapDiff;
        Points = row.Points;
        Status = string.IsNullOrWhiteSpace(row.Status) ? null : row.Status.Trim().ToLowerInvariant();
        LogoPath = row.LogoPath ?? "";
        Loading = false;
        (_played, _draws, _sf, _sa) = (row.Played, row.Draws, row.Sf, row.Sa);
    }

    public IReadOnlyList<string> StatusNames { get; } = new[] { "qualified", "eliminated" };

    [ObservableProperty] private int _rank;
    [ObservableProperty] private string _teamName = "";
    [ObservableProperty] private string _abbr = "";
    [ObservableProperty] private int _wins;
    [ObservableProperty] private int _losses;
    [ObservableProperty] private int _mapDiff;
    [ObservableProperty] private int _points;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _logoPath = "";

    /// <summary>The user asked to remove this line.</summary>
    public event Action<StandingsRowViewModel>? RemoveRequested;

    [RelayCommand]
    private void Remove() => RemoveRequested?.Invoke(this);

    [RelayCommand]
    private async Task BrowseLogoAsync()
    {
        var path = await _images.PickImageAsync("Select team logo", _logoFolder);
        if (path is not null) LogoPath = path;
    }

    public StandingsRow ToRow() => new()
    {
        Rank = Math.Clamp(Rank, 0, 99),
        TeamName = TeamName.Trim(),
        Abbr = Abbr.Trim(),
        Wins = Math.Clamp(Wins, 0, 99),
        Losses = Math.Clamp(Losses, 0, 99),
        MapDiff = Math.Clamp(MapDiff, -99, 99),
        Points = Math.Clamp(Points, 0, 999),
        Status = Status ?? "",
        LogoPath = string.IsNullOrWhiteSpace(LogoPath) ? null : LogoPath.Trim(),
        Played = _played,
        Draws = _draws,
        Sf = _sf,
        Sa = _sa,
    };
}

/// <summary>An entry of the "Show in standings.html" list: all groups, or one group by its key.</summary>
public sealed partial class DisplayOption : ObservableObject
{
    public const string AllKey = "__all__";

    public DisplayOption(string key, string label)
    {
        Key = key;
        _label = label;
    }

    public string Key { get; }

    [ObservableProperty] private string _label;
}

/// <summary>A group of the standings: a name and its table.</summary>
public sealed partial class StandingsGroupViewModel : EditableObject
{
    private readonly IImagePicker _images;
    private readonly string? _logoFolder;

    public StandingsGroupViewModel(string key, string name, IEnumerable<StandingsRow> rows, IImagePicker images, string? logoFolder)
    {
        Key = key;
        _images = images;
        _logoFolder = logoFolder;
        Option = new DisplayOption(key, LabelFor(name, key));
        Loading = true;
        Name = name;
        Loading = false;
        SetRows(rows);
        if (Rows.Count == 0) AddRow();
        Rows.CollectionChanged += (_, _) => RaiseEdited();
    }

    public string Key { get; }
    public DisplayOption Option { get; }
    public ObservableCollection<StandingsRowViewModel> Rows { get; } = new();

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private bool _canRemove;

    /// <summary>The user asked to remove this group.</summary>
    public event Action<StandingsGroupViewModel>? RemoveRequested;

    protected override bool IsDerived(string? propertyName) => propertyName == nameof(CanRemove);

    partial void OnNameChanged(string value) => Option.Label = LabelFor(value, Key);

    private static string LabelFor(string name, string key) => string.IsNullOrWhiteSpace(name) ? key : name.Trim();

    [RelayCommand]
    private void AddRow() => Rows.Add(NewRow(new StandingsRow()));

    [RelayCommand]
    private void RemoveGroup() => RemoveRequested?.Invoke(this);

    private StandingsRowViewModel NewRow(StandingsRow row)
    {
        var vm = new StandingsRowViewModel(row, _images, _logoFolder);
        vm.Edited += RaiseEdited;
        vm.RemoveRequested += r => Rows.Remove(r);
        return vm;
    }

    /// <summary>Replaces the table's lines (used by loading and by Update + sort).</summary>
    public void SetRows(IEnumerable<StandingsRow> rows)
    {
        var wasLoading = Loading;
        Loading = true;
        try
        {
            Rows.Clear();
            foreach (var row in rows) Rows.Add(NewRow(row));
        }
        finally { Loading = wasLoading; }
    }

    public void SortAndRank()
    {
        var sorted = StandingsOperations.SortAndRank(Rows.Select(r => r.ToRow()));
        SetRows(sorted);
        if (Rows.Count == 0) AddRow();
        RaiseEdited();
    }

    public StandingsGroup ToGroup() => new()
    {
        Key = Key,
        Name = Name.Trim(),
        Rows = ScoreboardExporter.ApplyRanks(StandingsOperations.WithoutBlanks(Rows.Select(r => r.ToRow()))),
    };
}

/// <summary>The Standings tab: groups with editable tables, which group the overlay shows, title and subtitle.</summary>
public sealed partial class StandingsViewModel : EditableObject
{
    private readonly IImagePicker _images;
    private readonly string? _logoFolder;
    private int _groupCounter = 1;

    public StandingsViewModel(IImagePicker images, string? logoFolder)
    {
        _images = images;
        _logoFolder = logoFolder;
        AllOption = new DisplayOption(DisplayOption.AllKey, "All groups");
        DisplayOptions.Add(AllOption);
        _selectedDisplay = AllOption;
        ShowGroups(new[] { NewGroup() });
        Groups.CollectionChanged += (_, _) => { RaiseEdited(); OnPropertyChanged(nameof(GroupColumns)); };
    }

    /// <summary>Set by the view: true when the window is wide enough for two group tables side by side.</summary>
    public bool WideLayout
    {
        get => _wideLayout;
        set { if (SetProperty(ref _wideLayout, value)) OnPropertyChanged(nameof(GroupColumns)); }
    }

    private bool _wideLayout;

    /// <summary>Groups sit side by side when there are two or more and the window is wide (otherwise one column).</summary>
    public int GroupColumns => Groups.Count >= 2 && WideLayout ? 2 : 1;

    protected override bool IsDerived(string? propertyName) => propertyName is nameof(GroupColumns) or nameof(WideLayout);

    public ObservableCollection<StandingsGroupViewModel> Groups { get; } = new();
    public ObservableCollection<DisplayOption> DisplayOptions { get; } = new();
    public DisplayOption AllOption { get; }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private DisplayOption _selectedDisplay;

    // Choosing a group in the list while the list is being rebuilt is not an edit.
    partial void OnSelectedDisplayChanged(DisplayOption value)
    {
        if (value is null) SelectedDisplay = AllOption;
    }

    [RelayCommand]
    private void AddGroup()
    {
        var group = NewGroup();
        Groups.Add(group);
        Attach(group);
        DisplayOptions.Add(group.Option);
        UpdateRemovable();
    }

    private StandingsGroupViewModel NewGroup(string? key = null, string? name = null, IEnumerable<StandingsRow>? rows = null) =>
        new(key ?? $"group_{_groupCounter++}", name ?? $"Group {Groups.Count + 1}", rows ?? Array.Empty<StandingsRow>(), _images, _logoFolder);

    private void Attach(StandingsGroupViewModel group)
    {
        group.Edited += RaiseEdited;
        group.RemoveRequested += RemoveGroup;
    }

    private void RemoveGroup(StandingsGroupViewModel group)
    {
        if (Groups.Count <= 1) return;
        Groups.Remove(group);
        DisplayOptions.Remove(group.Option);
        if (SelectedDisplay == group.Option || !DisplayOptions.Contains(SelectedDisplay)) SelectedDisplay = AllOption;
        UpdateRemovable();
    }

    private void UpdateRemovable()
    {
        foreach (var group in Groups) group.CanRemove = Groups.Count > 1;
    }

    private void ShowGroups(IEnumerable<StandingsGroupViewModel> groups)
    {
        var wasLoading = Loading;
        Loading = true;
        try
        {
            Groups.Clear();
            while (DisplayOptions.Count > 1) DisplayOptions.RemoveAt(1);
            foreach (var group in groups)
            {
                Groups.Add(group);
                Attach(group);
                DisplayOptions.Add(group.Option);
            }
            SelectedDisplay = AllOption;
            UpdateRemovable();
        }
        finally { Loading = wasLoading; }
    }

    /// <summary>Update + sort: every table is ordered and ranked.</summary>
    public void SortAll()
    {
        foreach (var group in Groups) group.SortAndRank();
    }

    /// <summary>Back to one empty group with no title. Not sent.</summary>
    public void ResetTab()
    {
        Title = "";
        Subtitle = "";
        _groupCounter = 1;
        ShowGroups(new[] { NewGroup() });
        RaiseEdited();
    }

    public void Load(AppState state)
    {
        var s = state.Standings;
        Loading = true;
        try
        {
            Title = s.Title;
            Subtitle = s.Subtitle;
            _groupCounter = 1;
            var source = s.Groups.Count > 0
                ? s.Groups
                : new[] { new StandingsGroup { Key = "group_1", Name = "Group 1", Rows = s.Rows } };

            var groups = new List<StandingsGroupViewModel>();
            for (var i = 0; i < source.Count; i++)
            {
                var key = string.IsNullOrEmpty(source[i].Key) ? $"group_{i + 1}" : source[i].Key;
                var name = string.IsNullOrEmpty(source[i].Name) ? $"Group {i + 1}" : source[i].Name;
                groups.Add(new StandingsGroupViewModel(key, name, source[i].Rows, _images, _logoFolder));
                _groupCounter = Math.Max(_groupCounter, i + 2);
            }
            ShowGroups(groups);
            SelectedDisplay = DisplayOptions.FirstOrDefault(o => o.Key == s.DisplayGroup) ?? AllOption;
        }
        finally { Loading = false; }
    }

    public AppState ApplyTo(AppState state)
    {
        var groups = Groups.Select(g => g.ToGroup()).ToArray();
        var display = groups.Any(g => g.Key == SelectedDisplay.Key) ? SelectedDisplay.Key : DisplayOption.AllKey;
        var shown = display != DisplayOption.AllKey ? groups.First(g => g.Key == display) : groups.FirstOrDefault();
        return state with
        {
            Standings = state.Standings with
            {
                Title = Title.Trim(),
                Subtitle = Subtitle.Trim(),
                Rows = shown?.Rows ?? Array.Empty<StandingsRow>(),
                Groups = groups,
                DisplayGroup = display,
            },
        };
    }
}

namespace SowBroadcast.App.Controls;

/// <summary>
/// The behaviour behind <see cref="SearchableComboBox"/>, free of UI: a case-insensitive substring filter over a list,
/// a current row that arrow keys move through the visible rows, and an optional "none" entry such as "— Hero —".
/// </summary>
public sealed class SearchableListModel
{
    public const int MinPopupHeight = 120;
    public const int MaxPopupHeight = 320;
    public const double MinPopupWidth = 220;

    private IReadOnlyList<string> _all = Array.Empty<string>();
    private string _query = "";

    /// <summary>When set, the list starts with this entry; choosing it means "no value".</summary>
    public string? NoneText { get; set; }

    public IReadOnlyList<string> Visible { get; private set; } = Array.Empty<string>();

    public int CurrentIndex { get; private set; } = -1;

    public string? CurrentRow => CurrentIndex >= 0 && CurrentIndex < Visible.Count ? Visible[CurrentIndex] : null;

    public string Query
    {
        get => _query;
        set
        {
            _query = value ?? "";
            Rebuild();
            CurrentIndex = Visible.Count > 0 ? 0 : -1;
        }
    }

    public void SetItems(IEnumerable<string>? items)
    {
        _all = items?.ToArray() ?? Array.Empty<string>();
        Rebuild();
    }

    /// <summary>Clears the filter and puts the current row on the selected item (or the first row).</summary>
    public void Open(string? selected)
    {
        _query = "";
        Rebuild();
        var row = selected ?? NoneText;
        var index = row is null ? -1 : IndexOf(row);
        CurrentIndex = index >= 0 ? index : Visible.Count > 0 ? 0 : -1;
    }

    /// <summary>Moves the current row through the visible rows (stops at the ends).</summary>
    public void Move(int delta)
    {
        if (Visible.Count == 0) return;
        CurrentIndex = Math.Clamp(CurrentIndex + delta, 0, Visible.Count - 1);
    }

    public void SetCurrent(int index)
    {
        if (index >= 0 && index < Visible.Count) CurrentIndex = index;
    }

    /// <summary>The value the current row stands for: null for the "none" entry or when nothing is current.</summary>
    public string? ValueOf(string? row) => row is null || row == NoneText ? null : row;

    public static IEnumerable<string> Filter(IEnumerable<string> items, string query) =>
        string.IsNullOrEmpty(query) ? items : items.Where(i => i.Contains(query, StringComparison.OrdinalIgnoreCase));

    public static double PopupWidth(double controlWidth) => Math.Max(controlWidth, MinPopupWidth);

    /// <summary>Popup height for the number of visible rows: room for the filter box plus 22 px per row, clamped.</summary>
    public static double PopupHeight(int rows) => Math.Clamp(40 + 22 * rows, MinPopupHeight, MaxPopupHeight);

    private int IndexOf(string row)
    {
        for (var i = 0; i < Visible.Count; i++)
            if (Visible[i] == row) return i;
        return -1;
    }

    private void Rebuild()
    {
        var source = NoneText is null ? _all : _all.Prepend(NoneText);
        Visible = Filter(source, _query).ToArray();
        if (CurrentIndex >= Visible.Count) CurrentIndex = Visible.Count - 1;
    }
}

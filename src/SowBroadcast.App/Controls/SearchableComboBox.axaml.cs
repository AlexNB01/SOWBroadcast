using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SowBroadcast.App.Controls;

public partial class SearchableComboBox : UserControl
{
    public static readonly StyledProperty<IEnumerable<string>?> ItemsSourceProperty =
        AvaloniaProperty.Register<SearchableComboBox, IEnumerable<string>?>(nameof(ItemsSource));

    public static readonly StyledProperty<string?> SelectedItemProperty =
        AvaloniaProperty.Register<SearchableComboBox, string?>(nameof(SelectedItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string?> NoneTextProperty =
        AvaloniaProperty.Register<SearchableComboBox, string?>(nameof(NoneText));

    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<SearchableComboBox, string>(nameof(Placeholder), "");

    private readonly SearchableListModel _model = new();
    private bool _updatingRows;

    public SearchableComboBox()
    {
        InitializeComponent();
        ItemsSourceProperty.Changed.AddClassHandler<SearchableComboBox>((c, _) => c.OnItemsChanged());
        SelectedItemProperty.Changed.AddClassHandler<SearchableComboBox>((c, _) => c.UpdateDisplay());
        NoneTextProperty.Changed.AddClassHandler<SearchableComboBox>((c, _) => { c._model.NoneText = c.NoneText; c.OnItemsChanged(); });
        PlaceholderProperty.Changed.AddClassHandler<SearchableComboBox>((c, _) => c.UpdateDisplay());
        UpdateDisplay();
    }

    public IEnumerable<string>? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public string? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }

    /// <summary>Text of an entry at the top of the list that means "nothing chosen" (e.g. "— Hero —").</summary>
    public string? NoneText { get => GetValue(NoneTextProperty); set => SetValue(NoneTextProperty, value); }

    public string Placeholder { get => GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

    public bool IsDropDownOpen => Dropdown.IsOpen;

    public void Open()
    {
        if (Dropdown.IsOpen) return;
        _model.NoneText = NoneText;
        _model.SetItems(ItemsSource);
        _model.Open(SelectedItem);

        Filter.Text = "";
        RefreshRows();
        var width = SearchableListModel.PopupWidth(Bounds.Width);
        PopupBorder.Width = width;
        Rows.MaxHeight = SearchableListModel.PopupHeight(_model.Visible.Count) - 40;
        Dropdown.IsOpen = true;
        Dispatcher.UIThread.Post(() => Filter.Focus(), DispatcherPriority.Loaded);
    }

    public void Close()
    {
        Dropdown.IsOpen = false;
        Display.Focus();
    }

    private void OnItemsChanged()
    {
        _model.NoneText = NoneText;
        _model.SetItems(ItemsSource);
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        var text = SelectedItem;
        if (string.IsNullOrEmpty(text)) text = NoneText ?? Placeholder;
        DisplayText.Text = text;
        DisplayText.Classes.Set("placeholder", string.IsNullOrEmpty(SelectedItem));
    }

    private void RefreshRows()
    {
        _updatingRows = true;
        Rows.ItemsSource = _model.Visible;
        Rows.SelectedIndex = _model.CurrentIndex;
        _updatingRows = false;
        if (_model.CurrentIndex >= 0) Rows.ScrollIntoView(_model.CurrentIndex);
        Rows.MaxHeight = SearchableListModel.PopupHeight(_model.Visible.Count) - 40;
    }

    private void Commit(string? row)
    {
        if (row is null) return;
        SelectedItem = _model.ValueOf(row);
        Close();
    }

    // ---- display button ----------------------------------------------------------------------------------

    private void OnDisplayClick(object? sender, RoutedEventArgs e) => Open();

    private void OnDisplayKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Up or Key.F4 or Key.Space or Key.Enter)
        {
            Open();
            e.Handled = true;
        }
    }

    /// <summary>The mouse wheel still steps through the values while the list is closed.</summary>
    private void OnDisplayWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Dropdown.IsOpen || ItemsSource is null) return;
        var rows = (NoneText is null ? ItemsSource : ItemsSource.Prepend(NoneText)).ToList();
        if (rows.Count == 0) return;
        var current = rows.IndexOf(SelectedItem ?? NoneText ?? "");
        var next = Math.Clamp((current < 0 ? 0 : current) + (e.Delta.Y > 0 ? -1 : 1), 0, rows.Count - 1);
        SelectedItem = _model.ValueOf(rows[next]);
        e.Handled = true;
    }

    // ---- popup -------------------------------------------------------------------------------------------

    private void OnFilterChanged(object? sender, TextChangedEventArgs e)
    {
        if (!Dropdown.IsOpen) return;
        _model.Query = Filter.Text ?? "";
        RefreshRows();
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: _model.Move(+1); SyncSelection(); e.Handled = true; break;
            case Key.Up: _model.Move(-1); SyncSelection(); e.Handled = true; break;
            case Key.Enter: Commit(_model.CurrentRow); e.Handled = true; break;
            case Key.Escape: Close(); e.Handled = true; break;
        }
    }

    private void SyncSelection()
    {
        _updatingRows = true;
        Rows.SelectedIndex = _model.CurrentIndex;
        _updatingRows = false;
        if (_model.CurrentIndex >= 0) Rows.ScrollIntoView(_model.CurrentIndex);
    }

    private void OnRowsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_updatingRows) return;
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (Rows.SelectedItem is string row) Commit(row);
    }

    private void OnPopupClosed(object? sender, EventArgs e) => Display.Focus();
}

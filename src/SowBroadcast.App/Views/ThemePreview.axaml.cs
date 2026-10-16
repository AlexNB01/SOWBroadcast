using Avalonia.Controls;

namespace SowBroadcast.App.Views;

public partial class ThemePreview : UserControl
{
    public ThemePreview()
    {
        InitializeComponent();
        SampleCombo.ItemsSource = new[]
        {
            "Ana", "Ashe", "Baptiste", "Bastion", "Brigitte", "Cassidy", "D.Va", "Doomfist", "Echo", "Freja", "Genji", "Hanzo",
            "Illari", "Junker Queen", "Junkrat", "Juno", "Kiriko", "Lúcio", "Mei", "Mercy", "Moira", "Orisa", "Pharah", "Reaper",
        };
        SampleMapCombo.ItemsSource = new[] { "Aatlis", "Busan", "Circuit Royal", "Ilios", "King's Row", "Lijiang Tower", "Watchpoint: Gibraltar" };
    }

    public void FocusSample() => SampleBox.Focus();

    public bool IsSampleComboOpen => SampleCombo.IsDropDownOpen;

    /// <summary>Dev aid for screenshots: opens the hero list with a filter typed in.</summary>
    public void OpenSampleCombo(string filter)
    {
        SampleCombo.Open();
        if (!string.IsNullOrEmpty(filter)) SampleCombo.FindControl<TextBox>("Filter")!.Text = filter;
    }
}

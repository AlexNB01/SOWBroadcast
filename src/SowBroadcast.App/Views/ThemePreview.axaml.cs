using Avalonia.Controls;

namespace SowBroadcast.App.Views;

public partial class ThemePreview : UserControl
{
    public ThemePreview() => InitializeComponent();

    public void FocusSample() => SampleBox.Focus();
}

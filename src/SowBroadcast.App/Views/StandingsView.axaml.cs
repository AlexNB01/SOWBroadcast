using Avalonia.Controls;

namespace SowBroadcast.App.Views;

public partial class StandingsView : UserControl
{
    public StandingsView()
    {
        InitializeComponent();
        // Two groups side by side only when the window is wide enough for the tables.
        SizeChanged += (_, e) =>
        {
            if (DataContext is ViewModels.MainWindowViewModel vm) vm.Standings.WideLayout = e.NewSize.Width >= 1700;
        };
    }
}

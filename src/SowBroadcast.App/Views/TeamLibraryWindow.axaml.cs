using Avalonia.Controls;
using Avalonia.Interactivity;
using SowBroadcast.App.ViewModels;

namespace SowBroadcast.App.Views;

public partial class TeamLibraryWindow : Window
{
    private readonly TeamLibraryViewModel? _viewModel;

    public TeamLibraryWindow()
    {
        InitializeComponent();
    }

    public TeamLibraryWindow(TeamLibraryViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Opened += (_, _) => SearchBox.Focus();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    // Double-click loads the team as Home; the buttons cover Away.
    private void OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => _viewModel?.LoadHomeCommand.Execute(null);
}

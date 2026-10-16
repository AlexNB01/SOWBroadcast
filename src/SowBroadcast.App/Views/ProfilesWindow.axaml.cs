using Avalonia.Controls;
using Avalonia.Interactivity;
using SowBroadcast.App.ViewModels;

namespace SowBroadcast.App.Views;

public partial class ProfilesWindow : Window
{
    private readonly ProfilesViewModel? _viewModel;

    public ProfilesWindow()
    {
        InitializeComponent();
    }

    public ProfilesWindow(ProfilesViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    // Double-click is the quick switch: the event settings, never the teams on screen.
    private void OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => _viewModel?.ApplySettingsCommand.Execute(null);
}

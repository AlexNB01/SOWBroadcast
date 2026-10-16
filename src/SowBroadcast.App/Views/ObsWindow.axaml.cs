using Avalonia.Controls;
using Avalonia.Interactivity;
using SowBroadcast.App.ViewModels;

namespace SowBroadcast.App.Views;

public partial class ObsWindow : Window
{
    private readonly ObsViewModel? _viewModel;

    public ObsWindow()
    {
        InitializeComponent();
    }

    public ObsWindow(ObsViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnSceneClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name }) _viewModel?.SwitchSceneCommand.Execute(name);
    }
}

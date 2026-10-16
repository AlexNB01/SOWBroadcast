using Avalonia.Controls;
using Avalonia.Interactivity;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Model;

namespace SowBroadcast.App.Views;

public partial class TeamImportWindow : Window
{
    private readonly TeamImportViewModel _viewModel;

    public TeamImportWindow() : this(new TeamImportViewModel(Array.Empty<TeamRef>())) { }

    public TeamImportWindow(TeamImportViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnImport(object? sender, RoutedEventArgs e) => Close(_viewModel.Selected());
}

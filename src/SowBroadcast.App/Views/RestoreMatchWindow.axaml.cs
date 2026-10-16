using Avalonia.Controls;
using Avalonia.Interactivity;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.History;

namespace SowBroadcast.App.Views;

public partial class RestoreMatchWindow : Window
{
    private readonly RestoreMatchViewModel _viewModel;

    public RestoreMatchWindow() : this(new RestoreMatchViewModel(Array.Empty<ArchivedMatch>())) { }

    public RestoreMatchWindow(RestoreMatchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnRestore(object? sender, RoutedEventArgs e) => Close(_viewModel.Selected?.Path);

    private void OnDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (_viewModel.Selected is { } item) Close(item.Path);
    }
}

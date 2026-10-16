using Avalonia.Controls;
using Avalonia.Interactivity;
using SowBroadcast.App.ViewModels;

namespace SowBroadcast.App.Views;

public partial class BulkImportWindow : Window
{
    public BulkImportWindow()
    {
        InitializeComponent();
    }

    public BulkImportWindow(BulkImportViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.Imported += Close;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}

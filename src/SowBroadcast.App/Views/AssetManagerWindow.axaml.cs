using Avalonia.Controls;
using SowBroadcast.App.ViewModels;

namespace SowBroadcast.App.Views;

public partial class AssetManagerWindow : Window
{
    public AssetManagerWindow()
    {
        InitializeComponent();
    }

    public AssetManagerWindow(AssetManagerViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Height = viewModel.IsHeroes ? 760 : 560;
    }
}

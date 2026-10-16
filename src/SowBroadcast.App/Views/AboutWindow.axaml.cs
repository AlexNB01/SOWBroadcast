using Avalonia.Controls;
using Avalonia.Interactivity;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {AppInfo.Version}";
        DataText.Text = AppPaths.DataRoot;
        LogText.Text = AppLog.Folder;
    }

    private void OnOpenData(object? sender, RoutedEventArgs e) => FolderOpener.Open(AppPaths.DataRoot);

    private void OnOpenLogs(object? sender, RoutedEventArgs e) => FolderOpener.Open(AppLog.Folder);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

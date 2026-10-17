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
        ProgramText.Text = AppPaths.ProgramFolder;
        DataText.Text = AppPaths.DataRoot;
        LogText.Text = AppLog.Folder;
    }

    private void OnOpenProgram(object? sender, RoutedEventArgs e) => FolderOpener.Open(AppPaths.ProgramFolder);

    private void OnOpenData(object? sender, RoutedEventArgs e) => FolderOpener.Open(AppPaths.DataRoot);

    private void OnOpenLogs(object? sender, RoutedEventArgs e) => FolderOpener.Open(AppLog.Folder);

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.App;

public partial class MainWindow : Window, IFileDialogs, IMessages
{
    private static readonly FilePickerFileType StateFiles = new("SOW Broadcast state")
    {
        Patterns = new[] { "*.json" },
    };

    private readonly MainWindowViewModel _viewModel;
    private readonly AppServices _services;

    public MainWindow()
    {
        InitializeComponent();
        _services = AppServices.Start();
        _viewModel = new MainWindowViewModel(this, this, _services.Publisher, AppPaths.AutosavePath);
        DataContext = _viewModel;
#if DEBUG
        ThemeTab.IsVisible = true;
        DebugMenu.IsVisible = true;
        WireDevScreenshot();
        WireDemoLive();
#endif
        Opened += async (_, _) =>
        {
            await _viewModel.LoadAutosaveAsync();
            _viewModel.PublishStartup();
            if (_services.StartupError is { } error)
                await ShowErrorAsync("Overlay server could not start",
                    error + "\nOverlay files are still written, but browser overlays will not update.");
        };
        Closing += (_, _) =>
        {
            _viewModel.Autosave();
            _services.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        };
    }

#if DEBUG
    // Dev aid: `--demo-live` bumps team 1's score a few seconds after start and publishes it, to watch an open overlay update.
    private void WireDemoLive()
    {
        if (!Environment.GetCommandLineArgs().Contains("--demo-live")) return;
        Opened += async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(12));
            _viewModel.State = _viewModel.State with { Team1 = _viewModel.State.Team1 with { Score = _viewModel.State.Team1.Score + 5 } };
            _viewModel.PublishNowCommand.Execute(null);
        };
    }

    private void OnOpenScoreboardOverlay(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.OverlayBaseUrl is { } url)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url + "/HTML/scoreboard.html") { UseShellExecute = true });
    }
#endif

#if DEBUG
    // Dev aid: `--screenshot <file.png> [--tab <index>] [--size WxH]` renders the window to a PNG and exits.
    private void WireDevScreenshot()
    {
        var args = Environment.GetCommandLineArgs();
        var shot = Array.IndexOf(args, "--screenshot");
        if (shot < 0 || shot + 1 >= args.Length) return;
        var path = args[shot + 1];
        var tab = Array.IndexOf(args, "--tab");
        if (tab >= 0 && tab + 1 < args.Length && int.TryParse(args[tab + 1], out var index)) Tabs.SelectedIndex = index;

        var dim = Array.IndexOf(args, "--size");
        if (dim >= 0 && dim + 1 < args.Length)
        {
            var parts = args[dim + 1].Split('x');
            if (parts.Length == 2 && double.TryParse(parts[0], out var w) && double.TryParse(parts[1], out var h))
            {
                Width = w;
                Height = h;
            }
        }

        Opened += async (_, _) =>
        {
            if (args.Contains("--focus")) ThemeView.FocusSample();
            await Task.Delay(1200);
            var size = new Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(96, 96));
            bitmap.Render(this);
            bitmap.Save(path);
            Close();
        };
    }
#endif

    private void OnQuit(object? sender, RoutedEventArgs e) => Close();

    public async Task<string?> PickOpenStateFileAsync(string? initialDirectory)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load match state",
            AllowMultiple = false,
            FileTypeFilter = new[] { StateFiles, FilePickerFileTypes.All },
            SuggestedStartLocation = await StartFolderAsync(initialDirectory),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveStateFileAsync(string? initialDirectory, string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save match state",
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = new[] { StateFiles },
            SuggestedStartLocation = await StartFolderAsync(initialDirectory),
        });
        return file?.TryGetLocalPath();
    }

    private async Task<IStorageFolder?> StartFolderAsync(string? directory) =>
        string.IsNullOrEmpty(directory) || !Directory.Exists(directory)
            ? null
            : await StorageProvider.TryGetFolderFromPathAsync(directory);

    public async Task ShowErrorAsync(string title, string message)
    {
        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 88 };
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = title, Classes = { "h2" } },
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    ok,
                },
            },
        };
        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;
using SowBroadcast.App.Views;
using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.App;

public partial class MainWindow : Window, IFileDialogs, IImagePicker, IMessages, ITeamPicker
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
        var teamsFolder = Path.Combine(AppPaths.ScoreboardDir, "Teams");
        var logoFolder = Path.Combine(AppPaths.ScoreboardDir, "Temp", "Team Logos");
        var broadcastLogoFolder = Path.Combine(AppPaths.ScoreboardDir, "Temp", "Broadcast Logos");
        Directory.CreateDirectory(teamsFolder);
        Directory.CreateDirectory(logoFolder);
        Directory.CreateDirectory(broadcastLogoFolder);
        _viewModel = new MainWindowViewModel(this, this, _services.Publisher, AppPaths.AutosavePath,
            _services.Assets, this, new SkiaLogoConverter(), teamsFolder, logoFolder, broadcastLogoFolder, AppPaths.DefaultHighlightsDir,
            standingsLogoFolder: teamsFolder, teamPicker: this,
            linkService: _services.Link, logoCache: _services.Logos, uiPost: action => Avalonia.Threading.Dispatcher.UIThread.Post(action));
        DataContext = _viewModel;
        _services.Assets.Changed += _ => _viewModel.MarkUnsent("assets");

        _services.Replay.ReplayReady += name => Avalonia.Threading.Dispatcher.UIThread.Post(() => { Serilog.Log.Information("Replay clip saved: {Name}", name); _viewModel.StatusText = $"Replay clip saved: {name}"; });
        _services.Replay.Problem += message => Avalonia.Threading.Dispatcher.UIThread.Post(() => _viewModel.StatusText = message);
        try { _services.Replay.Start(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _viewModel.StatusText = $"The replay folder cannot be watched: {ex.Message}";
        }

        // The countdown counts on a monotonic clock; this only refreshes what is shown (and the timer files) a few times a second.
        var clock = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_, _) => _viewModel.Waiting.Tick();
        clock.Start();
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
            await _viewModel.RestoreLinkAsync();
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

        var dialogArg = Array.IndexOf(args, "--dialog");
        if (dialogArg >= 0 && dialogArg + 1 < args.Length)
        {
            // `--dialog heroes|maps|modes|bulk` renders that dialog instead of the main window.
            Opened += async (_, _) =>
            {
                Window dialog = args[dialogArg + 1] switch
                {
                    "heroes" => new AssetManagerWindow(new AssetManagerViewModel(AssetCategory.Heroes, _services.Assets, this, this)),
                    "about" => new AboutWindow(),
                    "maps" => new AssetManagerWindow(new AssetManagerViewModel(AssetCategory.Maps, _services.Assets, this, this)),
                    "modes" => new AssetManagerWindow(new AssetManagerViewModel(AssetCategory.Modes, _services.Assets, this, this)),
                    _ => new BulkImportWindow(new BulkImportViewModel(_services.Assets, this)),
                };
                var selectArg = Array.IndexOf(args, "--select");
                if (selectArg >= 0 && selectArg + 1 < args.Length && dialog.DataContext is AssetManagerViewModel manager)
                    manager.SelectedName = args[selectArg + 1];
                dialog.Show();
                await Task.Delay(1200);
                var dialogSize = new Avalonia.PixelSize((int)dialog.Bounds.Width, (int)dialog.Bounds.Height);
                using var dialogBitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(dialogSize, new Avalonia.Vector(96, 96));
                dialogBitmap.Render(dialog);
                dialogBitmap.Save(path);
                dialog.Close();
                Close();
            };
            return;
        }

        Opened += async (_, _) =>
        {
            if (args.Contains("--sample"))
            {
                await Task.Delay(400); // after the autosave restore
                _viewModel.LoadSampleCommand.Execute(null);
            }
            var linkArg = Array.IndexOf(args, "--link");
            if (linkArg >= 0 && linkArg + 1 < args.Length)
            {
                // `--link <tournament url> [--select-live]` links a tournament (and its live match) before the picture is taken.
                _viewModel.Link.TournamentUrl = args[linkArg + 1];
                await _viewModel.Link.LinkCommand.ExecuteAsync(null);
                await Task.Delay(1500);
                if (args.Contains("--select-live") && _viewModel.Link.Matches.FirstOrDefault(m => m.IsLive) is { } live)
                {
                    _viewModel.Link.SelectedMatch = live;
                    await Task.Delay(2500);
                }
            }
            if (args.Contains("--open-colour"))
            {
                await Task.Delay(300);
                Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(this).OfType<Controls.ColorField>().FirstOrDefault()?.OpenPanel();
            }
            if (args.Contains("--focus")) ThemeView.FocusSample();
            var openCombo = Array.IndexOf(args, "--open-combo");
            if (openCombo >= 0) ThemeView.OpenSampleCombo(openCombo + 1 < args.Length && !args[openCombo + 1].StartsWith("--") ? args[openCombo + 1] : "");
            await Task.Delay(1200);
            if (openCombo >= 0) Console.Error.WriteLine($"combo open at capture: {ThemeView.IsSampleComboOpen}");
            var size = new Avalonia.PixelSize((int)Bounds.Width, (int)Bounds.Height);
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(96, 96));
            bitmap.Render(this);
            bitmap.Save(path);
            Close();
        };
    }
#endif

    private void OnQuit(object? sender, RoutedEventArgs e) => Close();

    private async void OnAbout(object? sender, RoutedEventArgs e) => await new AboutWindow().ShowDialog(this);

    private void OnOpenLogs(object? sender, RoutedEventArgs e) => FolderOpener.Open(AppLog.Folder);

    private void OnOpenData(object? sender, RoutedEventArgs e) => FolderOpener.Open(AppPaths.DataRoot);

    private void OnManageHeroes(object? sender, RoutedEventArgs e) => OpenAssetManager(AssetCategory.Heroes);
    private void OnManageMaps(object? sender, RoutedEventArgs e) => OpenAssetManager(AssetCategory.Maps);
    private void OnManageModes(object? sender, RoutedEventArgs e) => OpenAssetManager(AssetCategory.Modes);

    private async void OpenAssetManager(AssetCategory category)
    {
        var window = new AssetManagerWindow(new AssetManagerViewModel(category, _services.Assets, this, this));
        await window.ShowDialog(this);
    }

    private async void OnBulkImport(object? sender, RoutedEventArgs e)
    {
        var window = new BulkImportWindow(new BulkImportViewModel(_services.Assets, this));
        await window.ShowDialog(this);
    }

    public async Task<IReadOnlyList<Core.Model.TeamRef>?> PickTeamsAsync(IReadOnlyList<Core.Model.TeamRef> teams)
    {
        var window = new TeamImportWindow(new TeamImportViewModel(teams));
        return await window.ShowDialog<IReadOnlyList<Core.Model.TeamRef>?>(this);
    }

    public async Task<string?> PickImageAsync(string title, string? initialDirectory = null)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" } },
                FilePickerFileTypes.All,
            },
            SuggestedStartLocation = await StartFolderAsync(initialDirectory),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

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

    private static readonly FilePickerFileType TeamFiles = new("SOW team")
    {
        Patterns = new[] { "*.json" },
    };

    public async Task<string?> PickOpenTeamFileAsync(string? initialDirectory)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import team",
            AllowMultiple = false,
            FileTypeFilter = new[] { TeamFiles, FilePickerFileTypes.All },
            SuggestedStartLocation = await StartFolderAsync(initialDirectory),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveTeamFileAsync(string? initialDirectory, string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export team",
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = new[] { TeamFiles },
            SuggestedStartLocation = await StartFolderAsync(initialDirectory),
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title, string? initialDirectory)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolderAsync(initialDirectory),
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task<IStorageFolder?> StartFolderAsync(string? directory) =>
        string.IsNullOrEmpty(directory) || !Directory.Exists(directory)
            ? null
            : await StorageProvider.TryGetFolderFromPathAsync(directory);

    public Task ShowErrorAsync(string title, string message) => ShowMessageAsync(title, message);

    public Task ShowInfoAsync(string title, string message) => ShowMessageAsync(title, message);

    private async Task ShowMessageAsync(string title, string message)
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

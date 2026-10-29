using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using SowBroadcast.App.Services;
using SowBroadcast.App.ViewModels;
using SowBroadcast.App.Views;
using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Control;
using SowBroadcast.Integrations.Obs;
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
    private bool _skipAutosave;
    private readonly AppServices _services;
    private readonly GlobalHotkeys _hotkeys = new();
    private readonly AppSettings _appSettings = AppSettings.Load(AppPaths.AppSettingsPath);
    private readonly UpdateService _updates = new(new InstallerUpdater(AppInfo.Repository, AppInfo.Version, AppPaths.ProgramFolder), AppInfo.Version);
    private readonly SowBroadcast.Integrations.Obs.ObsController _obsController = new();
    private Views.ObsWindow? _obsWindow;
    private readonly LibVlcClipPlayer _clipPlayer;
    private Dictionary<HotkeyGesture, string> _windowKeys = new();

    public MainWindow()
    {
        InitializeComponent();
        _services = AppServices.Start();
        // Visible folders next to the program (C:\SOW Broadcast\Logos ...), where the file dialogs open
        var teamsFolder = AppPaths.TeamFilesDir;
        var logoFolder = AppPaths.TeamLogosDir;
        var broadcastLogoFolder = AppPaths.BroadcastLogosDir;
        Directory.CreateDirectory(teamsFolder);
        Directory.CreateDirectory(logoFolder);
        Directory.CreateDirectory(broadcastLogoFolder);
        SowBroadcast.Core.Colour.RecentColours.Shared.UseFile(AppPaths.RecentColoursPath);
        var teamLibrary = new SowBroadcast.Core.Teams.TeamLibrary(Path.Combine(AppPaths.ScoreboardDir, "TeamLibrary"), new SkiaLogoConverter());
        teamLibrary.Load();
        _clipPlayer = new LibVlcClipPlayer();
        _viewModel = new MainWindowViewModel(this, this, _services.Publisher, AppPaths.AutosavePath,
            _services.Assets, this, new SkiaLogoConverter(), teamsFolder, logoFolder, broadcastLogoFolder, AppPaths.DefaultHighlightsDir,
            standingsLogoFolder: logoFolder, teamPicker: this,
            linkService: _services.Link, logoCache: _services.Logos, uiPost: action => Avalonia.Threading.Dispatcher.UIThread.Post(action),
            archive: new SowBroadcast.Core.History.MatchArchive(Path.Combine(AppPaths.DataRoot, "History")),
            library: teamLibrary,
            profiles: new SowBroadcast.Core.Profiles.ProfileStore(Path.Combine(AppPaths.DataRoot, "Profiles")),
            replayClips: _services.Clips, clipPlayer: _clipPlayer);
        DataContext = _viewModel;
        UpdateStartupItem.IsChecked = _appSettings.CheckForUpdatesAtStartup;
        _viewModel.UseAutomation(_services.ControlSettings.Automation);
        StartObs();
        StartControl();
        _services.Assets.Changed += _ => _viewModel.MarkUnsent("assets");

        _services.Replay.ReplayReady += name => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Serilog.Log.Information("Replay clip saved: {Name}", name);
            _viewModel.StatusText = $"Replay clip saved: {name}";
            _viewModel.Replay.OnClipArrived(name);
        });
        _services.Replay.Problem += message => Avalonia.Threading.Dispatcher.UIThread.Post(() => _viewModel.StatusText = message);
        try { _services.Replay.Start(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _viewModel.StatusText = $"The replay folder cannot be watched: {ex.Message}";
        }

        // The countdown counts on a monotonic clock; this only refreshes what is shown (and the timer files) a few times a second.
        var clock = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_, _) => _viewModel.Waiting.Tick();
        // What was edited since the last look becomes one undo step once the typing pauses.
        var steps = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        steps.Tick += (_, _) => _viewModel.FlushHistory();
        steps.Start();
        clock.Start();
#if DEBUG
        ThemeTab.IsVisible = true;
        DebugMenu.IsVisible = true;
        WireDevScreenshot();
        WireDemoLive();
        WireServeSample();
#endif
        Opened += async (_, _) =>
        {
            await _viewModel.LoadAutosaveAsync();
            _viewModel.PublishStartup();
            await _viewModel.RestoreLinkAsync();
            _ = _viewModel.Obs?.ConnectAtStartupAsync();   // quietly: OBS may not be running yet
            if (SowBroadcast.Core.Diagnostics.CrashReports.TakePending(AppLog.Folder) is { } crash)
                await ShowInfoAsync("The program closed unexpectedly last time",
                    $"A report was saved:\n{crash}\n\nYour match was saved as far as the autosave goes. Send the report to whoever looks after the program (Help > Open log folder).");
            if (_appSettings.CheckForUpdatesAtStartup) _ = CheckForUpdatesQuietlyAsync();
            if (_services.StartupError is { } error)
                await ShowErrorAsync("Overlay server could not start",
                    error + "\nOverlay files are still written, but browser overlays will not update.");
        };
        Closing += (_, _) =>
        {
            _hotkeys.Dispose();
            _clipPlayer.Dispose();
            _obsController.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            if (!_skipAutosave)
            {
                _viewModel.Autosave();
                _viewModel.ArchiveOnClose();
            }
            _services.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        };
    }

#if DEBUG
    // Dev aid: `--serve-sample <seconds>` loads the sample match, sends it to the overlays and keeps running for that long
    // (so a headless browser can take pictures of the overlays), then closes.
    private void WireServeSample()
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.IndexOf(args, "--serve-sample");
        if (at < 0 || at + 1 >= args.Length || !double.TryParse(args[at + 1], out var seconds)) return;
        Opened += async (_, _) =>
        {
            await Task.Delay(1500);
            _viewModel.LoadSampleCommand.Execute(null);
            _viewModel.UpdateMatchCommand.Execute(null);
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            _skipAutosave = true; // the sample is not the user's match
            Close();
        };
    }

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

#endif

    // ---- Hotkeys and the local control interface (EHD-7) ---------------------------------------------------

    private void StartControl()
    {
        // The HTTP interface and the hotkeys run the same commands, on the UI thread.
        var host = new ControlHost(_viewModel, _services.ControlSettings,
            work => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(work));
        _services.ControlGateway.Handler = host.HandleAsync;

        _hotkeys.Pressed += id => Avalonia.Threading.Dispatcher.UIThread.Post(async () => await _viewModel.RunControlAsync(id));
        AddHandler(KeyDownEvent, OnControlKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ApplyHotkeys();
    }

    // Registers the hotkeys with Windows (when they should work everywhere) and remembers them for this window, so a combination
    // Windows would not give us still works while this window has the focus.
    private void ApplyHotkeys()
    {
        var settings = _services.ControlSettings;
        var active = settings.ActiveBindings();
        _windowKeys = active.ToDictionary(b => b.Gesture, b => b.Id);

        var failed = _hotkeys.Apply(settings.GlobalHotkeys ? active : Array.Empty<(string, HotkeyGesture)>());
        if (failed.Count > 0)
        {
            var names = string.Join(", ", failed.Select(id => $"{ControlCatalog.Find(id)?.Label} ({settings.GestureFor(id)})"));
            Serilog.Log.Warning("Hotkeys another program already uses: {Names}", names);
            _viewModel.StatusText = $"These hotkeys are used by another program and only work in this window: {names}";
        }
    }

    private void OnControlKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (Views.ControlSettingsWindow.GestureOf(e.Key, e.KeyModifiers) is { } gesture && _windowKeys.TryGetValue(gesture, out var id))
        {
            e.Handled = true;
            _ = _viewModel.RunControlAsync(id);
        }
    }

    private async void OnUpdateAssetList(object? sender, RoutedEventArgs e)
    {
        _viewModel.StatusText = "Checking for a newer hero and map list…";
        var result = await _services.AssetUpdater.UpdateAsync();
        _viewModel.StatusText = result.Message;
        if (result.Ok) await ShowInfoAsync("Hero and map list", result.Message);
        else await ShowErrorAsync("Hero and map list", result.Message);
    }

    // ---- updates (EHD-15) --------------------------------------------------------------------------------

    private async void OnCheckUpdates(object? sender, RoutedEventArgs e)
    {
        _viewModel.StatusText = "Checking for updates…";
        var result = await _updates.CheckAsync();
        _viewModel.StatusText = result.Message;
        switch (result.Status)
        {
            case UpdateCheckStatus.Available when result.Offer is { } offer:
                if (!await ConfirmAsync("Update available",
                        result.Message + "\n\nThe program closes and starts again in the new version. Do not update during a broadcast. Install now?")) return;
                _viewModel.Autosave();
                _viewModel.ArchiveOnClose();
                _viewModel.StatusText = "Downloading the update… 0 %";
                var problem = await _updates.InstallAsync(offer, percent => Avalonia.Threading.Dispatcher.UIThread.Post(() => _viewModel.StatusText = $"Downloading the update… {percent} %"));
                if (problem is not null)
                {
                    _viewModel.StatusText = problem;
                    await ShowErrorAsync("Update", problem);
                }
                else
                {
                    // The installer is running: close the program so it can replace it (it starts the new version afterwards).
                    _viewModel.StatusText = "Installing the update…";
                    Close();
                }
                break;
            case UpdateCheckStatus.Failed:
                await ShowErrorAsync("Update", result.Message);
                break;
            default:
                await ShowInfoAsync("Update", result.Message);
                break;
        }
    }

    // At startup: no dialog, only a line in the status bar (the producer may be busy setting up a show).
    private async Task CheckForUpdatesQuietlyAsync()
    {
        var result = await _updates.CheckAsync();
        if (result.Status == UpdateCheckStatus.Available)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _viewModel.StatusText = result.Message + " Help > Check for updates installs it.");
    }

    private void OnUpdateStartupToggled(object? sender, RoutedEventArgs e)
    {
        _appSettings.CheckForUpdatesAtStartup = UpdateStartupItem.IsChecked == true;
        try { _appSettings.Save(AppPaths.AppSettingsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _viewModel.StatusText = $"The setting could not be saved: {ex.Message}"; }
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var answer = false;
        var yes = new Button { Content = "Yes", MinWidth = 88 };
        var no = new Button { Content = "No", MinWidth = 88 };
        var dialog = new Window
        {
            Title = title,
            Width = 480,
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
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } },
                },
            },
        };
        yes.Click += (_, _) => { answer = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return answer;
    }

    private async void OnProfiles(object? sender, RoutedEventArgs e) =>
        await new Views.ProfilesWindow(new ProfilesViewModel(_viewModel)).ShowDialog(this);

    private async void OnTeamLibrary(object? sender, RoutedEventArgs e) =>
        await new Views.TeamLibraryWindow(new TeamLibraryViewModel(_viewModel)).ShowDialog(this);

    // ---- OBS (EHD-6) -------------------------------------------------------------------------------------

    private void StartObs()
    {
        var replayFolder = Path.Combine(AppPaths.ScoreboardDir, "Replay");
        ChecklistInput Checklist()
        {
            var pages = Directory.Exists(Path.Combine(AppPaths.ContentRoot, "HTML"))
                ? Directory.GetFiles(Path.Combine(AppPaths.ContentRoot, "HTML"), "*.html").Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToArray()
                : Array.Empty<string>();
            return new ChecklistInput(_services.Publisher.BaseUrl, SowBroadcast.Core.Obs.ObsSceneCollection.SceneNames(), pages, replayFolder);
        }

        var obs = new ObsViewModel(_obsController, SowBroadcast.Core.Obs.ObsSettings.Load(AppPaths.ObsSettingsPath), AppPaths.ObsSettingsPath, new DpapiProtector(),
            Checklist, () => _services.Publisher.BaseUrl, AppPaths.ContentRoot, AppPaths.DataRoot,
            action => Avalonia.Threading.Dispatcher.UIThread.Post(action), PickSaveJsonAsync);
        obs.Said += text => _viewModel.StatusText = text;
        _viewModel.Obs = obs;
    }

    private async Task<string?> PickSaveJsonAsync(string title, string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } },
        });
        return file?.TryGetLocalPath();
    }

    private void OnObsPanel(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.Obs is not { } obs) return;
        if (_obsWindow is { IsVisible: true })
        {
            _obsWindow.Activate();
            return;
        }
        // Not a dialog: the producer keeps it open next to the main window during the show.
        _obsWindow = new Views.ObsWindow(obs);
        _obsWindow.Show(this);
    }

    private async void OnRestoreMatch(object? sender, RoutedEventArgs e)
    {
        var window = new Views.RestoreMatchWindow(new RestoreMatchViewModel(_viewModel.ArchivedMatches()));
        if (await window.ShowDialog<string?>(this) is { } path) await _viewModel.RestoreArchivedAsync(path);
    }

    private async void OnControlSettings(object? sender, RoutedEventArgs e)
    {
        var window = new Views.ControlSettingsWindow(new ControlSettingsViewModel(_services.ControlSettings, _viewModel.OverlayBaseUrl));
        if (await window.ShowDialog<ControlSettings?>(this) is not { } chosen) return;

        _services.ControlSettings.CopyFrom(chosen);
        _services.ControlGateway.Token = chosen.Token;
        try { _services.ControlSettings.Save(AppPaths.ControlSettingsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Settings could not be saved", ex.Message);
        }
        ApplyHotkeys();
        _viewModel.StatusText = "Hotkeys, remote control and automation settings saved";
    }

    // ---- overlay preview (EHD-13): the overlays in the default browser, no WebView in the program ----------------

    private void OnPreviewAll(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.OverlayBaseUrl is { } url) BrowserOpener.Open(SowBroadcast.Core.Overlays.OverlayCatalog.OverviewUrl(url));
        else _viewModel.StatusText = "The overlay server is not running, so there is nothing to preview.";
    }

    private void OnPreview(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path }) return;
        var page = SowBroadcast.Core.Overlays.OverlayCatalog.All.FirstOrDefault(p => p.Path == path);
        if (page is null) return;
        if (_viewModel.OverlayBaseUrl is { } url) BrowserOpener.Open(SowBroadcast.Core.Overlays.OverlayCatalog.PreviewUrl(url, page));
        else _viewModel.StatusText = "The overlay server is not running, so there is nothing to preview.";
    }

    private void OnOpenScoreboardOverlay(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.OverlayBaseUrl is { } url)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url + "/HTML/scoreboard.html") { UseShellExecute = true });
    }

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
            // `--dialog heroes|maps|modes|bulk|about|control|restore` renders that dialog instead of the main window.
            Opened += async (_, _) =>
            {
                Window dialog = args[dialogArg + 1] switch
                {
                    "heroes" => new AssetManagerWindow(new AssetManagerViewModel(AssetCategory.Heroes, _services.Assets, this, this)),
                    "about" => new AboutWindow(),
                    "obs" => new ObsWindow(_viewModel.Obs!),
                    "profiles" => new ProfilesWindow(new ProfilesViewModel(_viewModel)),
                    "library" => new TeamLibraryWindow(new TeamLibraryViewModel(_viewModel)),
                    "restore" => new RestoreMatchWindow(new RestoreMatchViewModel(_viewModel.ArchivedMatches())),
                    "control" => new ControlSettingsWindow(new ControlSettingsViewModel(_services.ControlSettings, _viewModel.OverlayBaseUrl)),
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

    private void OnOpenProgram(object? sender, RoutedEventArgs e) => FolderOpener.Open(AppPaths.ProgramFolder);

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

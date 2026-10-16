using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.Core.Obs;
using SowBroadcast.Integrations.Obs;

namespace SowBroadcast.App.ViewModels;

/// <summary>A scene button.</summary>
public sealed partial class ObsSceneItem : ObservableObject
{
    public ObsSceneItem(string name, bool isCurrent)
    {
        Name = name;
        _isCurrent = isCurrent;
    }

    public string Name { get; }

    [ObservableProperty] private bool _isCurrent;
}

/// <summary>A line of the go-live checklist, ready to show.</summary>
public sealed class CheckItem
{
    public CheckItem(CheckResult result)
    {
        Glyph = result.Level switch { CheckLevel.Ok => "✔", CheckLevel.Warning => "!", _ => "✘" };
        Level = result.Level;
        Title = result.Title;
        Detail = result.Detail;
    }

    public string Glyph { get; }
    public CheckLevel Level { get; }
    public string Title { get; }
    public string Detail { get; }
}

/// <summary>The OBS panel (EHD-6): connection, scene buttons, replay buffer, go-live checklist and the scene collection export.</summary>
public sealed partial class ObsViewModel : ObservableObject
{
    private readonly ObsController _obs;
    private readonly ObsSettings _settings;
    private readonly string _settingsPath;
    private readonly ISecretProtector _protector;
    private readonly Func<ChecklistInput> _checklist;
    private readonly Func<string?> _baseUrl;
    private readonly string _contentRoot;
    private readonly string _dataRoot;
    private readonly Action<Action> _uiPost;
    private readonly Func<string, string, Task<string?>> _pickSave;

    public ObsViewModel(ObsController obs, ObsSettings settings, string settingsPath, ISecretProtector protector, Func<ChecklistInput> checklist,
        Func<string?> baseUrl, string contentRoot, string dataRoot, Action<Action> uiPost, Func<string, string, Task<string?>>? pickSave = null)
    {
        _obs = obs;
        _settings = settings;
        _settingsPath = settingsPath;
        _protector = protector;
        _checklist = checklist;
        _baseUrl = baseUrl;
        _contentRoot = contentRoot;
        _dataRoot = dataRoot;
        _uiPost = uiPost;
        _pickSave = pickSave ?? ((_, _) => Task.FromResult<string?>(null));

        _host = settings.Host;
        _port = settings.Port;
        _autoConnect = settings.AutoConnect;
        _password = settings.ProtectedPassword.Length > 0 ? PasswordPlaceholder : "";
        _obs.Changed += () => _uiPost(Refresh);
        _obs.ReplaySaved += path => _uiPost(() => Say($"OBS saved a replay: {Path.GetFileName(path)}"));
        Refresh();
    }

    // Shown in the password box instead of the stored password (which is never shown); typing replaces it.
    private const string PasswordPlaceholder = "••••••••";

    public ObsController Controller => _obs;
    public ObservableCollection<ObsSceneItem> Scenes { get; } = new();
    public ObservableCollection<CheckItem> CheckResults { get; } = new();

    [ObservableProperty] private string _host;
    [ObservableProperty] private int _port;
    [ObservableProperty] private string _password;
    [ObservableProperty] private bool _autoConnect;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Not connected";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _replayText = "";
    [ObservableProperty] private bool _replayRunning;

    /// <summary>Said to the main window's status bar as well.</summary>
    public event Action<string>? Said;

    public bool CanEditConnection => !IsConnected;

    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(CanEditConnection));

    private string Say(string text)
    {
        Message = text;
        Said?.Invoke(text);
        return text;
    }

    private void Refresh()
    {
        IsConnected = _obs.IsConnected;
        Status = _obs.IsConnected
            ? $"Connected to OBS Studio {_obs.ObsVersion}{(_obs.IsStreaming ? " · streaming" : "")}{(_obs.IsRecording ? " · recording" : "")}"
            : _obs.LastProblem ?? "Not connected";
        ReplayRunning = _obs.ReplayBufferActive;
        ReplayText = !_obs.IsConnected ? "" : _obs.ReplayBufferActive ? "Replay buffer running" : "Replay buffer stopped";

        var scenes = _obs.Scenes;
        if (!Scenes.Select(s => s.Name).SequenceEqual(scenes))
        {
            Scenes.Clear();
            foreach (var name in scenes) Scenes.Add(new ObsSceneItem(name, name == _obs.CurrentScene));
        }
        else
        {
            foreach (var scene in Scenes) scene.IsCurrent = scene.Name == _obs.CurrentScene;
        }
    }

    // ---- connection --------------------------------------------------------------------------------------

    private string? PasswordToUse() =>
        Password == PasswordPlaceholder ? _settings.GetPassword(_protector) : string.IsNullOrEmpty(Password) ? null : Password;

    /// <summary>Connects with the settings as they are now; on success the settings are kept.</summary>
    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (IsBusy || _obs.IsConnected) return;
        IsBusy = true;
        Status = "Connecting…";
        try
        {
            await _obs.ConnectAsync(Host.Trim(), Port, PasswordToUse());
            _settings.Host = Host.Trim();
            _settings.Port = Port;
            _settings.AutoConnect = AutoConnect;
            if (Password != PasswordPlaceholder) _settings.SetPassword(Password, _protector);
            Password = _settings.ProtectedPassword.Length > 0 ? PasswordPlaceholder : "";
            Save();
            Say($"Connected to OBS Studio {_obs.ObsVersion}");
        }
        catch (ObsException ex)
        {
            Say(ex.Message);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    /// <summary>Connects when the program starts, if the user asked for that (no message when OBS is not there).</summary>
    public async Task ConnectAtStartupAsync()
    {
        if (!_settings.AutoConnect) return;
        await ConnectAsync();
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await _obs.DisconnectAsync();
        Say("Disconnected from OBS");
        Refresh();
    }

    partial void OnAutoConnectChanged(bool value)
    {
        _settings.AutoConnect = value;
        Save();
    }

    private void Save()
    {
        try { _settings.Save(_settingsPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Message = $"The OBS settings could not be saved: {ex.Message}"; }
    }

    // ---- actions (buttons here, hotkeys and the HTTP interface) ------------------------------------------

    /// <summary>Puts a scene on air. The result says whether OBS did it, in words (also shown in the panel and the status bar).</summary>
    public Task<(bool Ok, string Message)> SwitchToSceneAsync(string? name) => Run(async () =>
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ObsException("Say which scene.");
        await _obs.SwitchSceneAsync(name);
        return $"OBS scene: {name}";
    });

    public Task<(bool Ok, string Message)> StartReplayAsync() => Run(async () => { await _obs.StartReplayBufferAsync(); return "Replay buffer started"; });

    public Task<(bool Ok, string Message)> StopReplayAsync() => Run(async () => { await _obs.StopReplayBufferAsync(); return "Replay buffer stopped"; });

    public Task<(bool Ok, string Message)> SaveReplayNowAsync() => Run(async () => { await _obs.SaveReplayAsync(); return "Replay saved: OBS is writing the clip"; });

    [RelayCommand]
    private async Task SwitchScene(string? name) => await SwitchToSceneAsync(name);

    [RelayCommand]
    private async Task StartReplayBuffer() => await StartReplayAsync();

    [RelayCommand]
    private async Task StopReplayBuffer() => await StopReplayAsync();

    [RelayCommand]
    private async Task SaveReplay() => await SaveReplayNowAsync();

    /// <summary>Starts the replay buffer when it is stopped and stops it when it runs (the hotkey for it).</summary>
    public Task<(bool Ok, string Message)> ToggleReplayBufferAsync() =>
        Run(async () =>
        {
            if (_obs.ReplayBufferActive) { await _obs.StopReplayBufferAsync(); return "Replay buffer stopped"; }
            await _obs.StartReplayBufferAsync();
            return "Replay buffer started";
        });

    // Runs an OBS action and turns a refusal into a message (never an exception): the operator is busy with a live show.
    private async Task<(bool Ok, string Message)> Run(Func<Task<string>> action)
    {
        if (!_obs.IsConnected) return (false, Say("Not connected to OBS."));
        try
        {
            return (true, Say(await action()));
        }
        catch (ObsException ex)
        {
            return (false, Say(ex.Message));
        }
    }

    // ---- go-live checklist -------------------------------------------------------------------------------

    [RelayCommand]
    private async Task CheckAsync()
    {
        CheckResults.Clear();
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            foreach (var result in await _obs.RunChecklistAsync(_checklist())) CheckResults.Add(new CheckItem(result));
            var problems = CheckResults.Count(c => c.Level == CheckLevel.Problem);
            var warnings = CheckResults.Count(c => c.Level == CheckLevel.Warning);
            Say(problems > 0 ? $"Go-live check: {problems} problem(s) to fix" : warnings > 0 ? $"Go-live check: ready, {warnings} thing(s) to look at" : "Go-live check: all good");
        }
        catch (ObsException ex)
        {
            Say(ex.Message);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    // ---- scene collection --------------------------------------------------------------------------------

    /// <summary>Writes the scene collection for this installation to a file OBS can import (Scene Collection > Import).</summary>
    [RelayCommand]
    private async Task ExportCollectionAsync()
    {
        if (_baseUrl() is not { } baseUrl)
        {
            Say("The overlay server is not running, so there is no address to put in the scenes.");
            return;
        }
        var path = await _pickSave("Export OBS scene collection", "SOW Broadcast.json");
        if (path is null) return;
        try
        {
            var json = ObsSceneCollection.Build(baseUrl, _contentRoot, _dataRoot);
            await File.WriteAllTextAsync(path, json, new System.Text.UTF8Encoding(false));
            Say($"Scene collection saved: {Path.GetFileName(path)}. In OBS: Scene Collection > Import, then pick your NDI senders and camera.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say($"The scene collection could not be saved: {ex.Message}");
        }
    }
}

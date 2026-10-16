using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileDialogs _dialogs;
    private readonly IMessages _messages;
    private readonly IOverlayPublisher _publisher;
    private readonly string _autosavePath;
    private string? _currentSavePath;

    public MainWindowViewModel(IFileDialogs dialogs, IMessages messages, IOverlayPublisher publisher, string autosavePath)
    {
        _dialogs = dialogs;
        _messages = messages;
        _publisher = publisher;
        _autosavePath = autosavePath;
        ServerStatus = publisher.BaseUrl is null ? "Overlay server unavailable" : $"Overlays: {publisher.BaseUrl}";
        State = AppState.CreateDefault();
        Version = typeof(MainWindowViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "3.0.0";
    }

    public string Version { get; }
    public string ServerStatus { get; }
    public string? OverlayBaseUrl => _publisher.BaseUrl;

    [ObservableProperty] private AppState _state;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _hasUnsentChanges;
    [ObservableProperty] private string _linkStatus = "Not linked";

    public string? CurrentSavePath => _currentSavePath;

    /// <summary>Restores the autosave (if any) at startup.</summary>
    public async Task LoadAutosaveAsync()
    {
        try
        {
            var loaded = StateStore.LoadWithRecovery(_autosavePath);
            if (loaded is not null)
            {
                State = loaded;
                StatusText = $"Restored autosave from {_autosavePath}";
            }
        }
        catch (StateLoadException ex)
        {
            StatusText = "Autosave could not be read; starting with an empty match";
            await _messages.ShowErrorAsync("Autosave could not be read", ex.Message);
        }
    }

    /// <summary>Startup: send the restored state to the overlays (every key, so already-open overlays refresh).</summary>
    public void PublishStartup() => TryPublish(updateStatus: false);

    [RelayCommand]
    private void PublishNow() => TryPublish(updateStatus: true);

    private void TryPublish(bool updateStatus)
    {
        try
        {
            var result = _publisher.Publish(State);
            HasUnsentChanges = false;
            if (updateStatus)
                StatusText = result.ChangedKeys.Count == 0
                    ? "Overlays are up to date"
                    : $"Published: {string.Join(", ", result.ChangedKeys.Take(6))}{(result.ChangedKeys.Count > 6 ? "…" : "")}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not write overlay files: {ex.Message}";
        }
    }

#if DEBUG
    [RelayCommand]
    private void LoadSample()
    {
        State = SampleState.Create();
        HasUnsentChanges = true;
        StatusText = "Loaded the sample match (not published yet)";
    }
#endif

    public void Autosave()
    {
        try
        {
            StateStore.Save(_autosavePath, State);
        }
        catch (IOException ex)
        {
            StatusText = $"Autosave failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusText = $"Autosave failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var path = await _dialogs.PickOpenStateFileAsync(Path.GetDirectoryName(_currentSavePath));
        if (path is null) return;
        try
        {
            State = StateStore.Load(path);
        }
        catch (StateLoadException ex)
        {
            await _messages.ShowErrorAsync("Import failed", ex.Message);
            return;
        }
        _currentSavePath = path;
        HasUnsentChanges = true;
        Autosave();
        StatusText = $"Loaded {Path.GetFileName(path)}";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        // Same rule as the legacy app: save to the current file if one is set, otherwise ask.
        if (_currentSavePath is not null && !SamePath(_currentSavePath, _autosavePath))
        {
            await WriteAsync(_currentSavePath);
            return;
        }
        await SaveAsAsync();
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        var path = await _dialogs.PickSaveStateFileAsync(Path.GetDirectoryName(_currentSavePath), "match_state.sowbroadcast.json");
        if (path is null) return;
        _currentSavePath = path;
        await WriteAsync(path);
    }

    private async Task WriteAsync(string path)
    {
        try
        {
            StateStore.Save(path, State);
            Autosave();
            StatusText = $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _messages.ShowErrorAsync("Save failed", ex.Message);
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}

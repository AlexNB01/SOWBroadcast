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
    private readonly string _autosavePath;
    private string? _currentSavePath;

    public MainWindowViewModel(IFileDialogs dialogs, IMessages messages, string autosavePath)
    {
        _dialogs = dialogs;
        _messages = messages;
        _autosavePath = autosavePath;
        State = AppState.CreateDefault();
        Version = typeof(MainWindowViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "3.0.0";
    }

    public string Version { get; }

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

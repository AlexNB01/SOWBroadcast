using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Match;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IFileDialogs _dialogs;
    private readonly IMessages _messages;
    private readonly IOverlayPublisher _publisher;
    private readonly string _autosavePath;
    private readonly ILogoConverter _logos;
    private readonly string? _teamsFolder;
    private string? _currentSavePath;
    private bool _collecting;

    public MainWindowViewModel(IFileDialogs dialogs, IMessages messages, IOverlayPublisher publisher, string autosavePath,
        AssetService? assets = null, IImagePicker? images = null, ILogoConverter? logos = null,
        string? teamsFolder = null, string? logoFolder = null)
    {
        _dialogs = dialogs;
        _messages = messages;
        _publisher = publisher;
        _autosavePath = autosavePath;
        _logos = logos ?? new SkiaLogoConverter();
        _teamsFolder = teamsFolder;
        ServerStatus = publisher.BaseUrl is null ? "Overlay server unavailable" : $"Overlays: {publisher.BaseUrl}";
        Match = new MatchViewModel(assets, images ?? new NoImagePicker(), logoFolder);
        Match.Edited += () => HasUnsentChanges = true;
        State = AppState.CreateDefault();
        Version = typeof(MainWindowViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "3.0.0";
    }

    public string Version { get; }
    public string ServerStatus { get; }
    public string? OverlayBaseUrl => _publisher.BaseUrl;

    /// <summary>The Match tab (team panels, map cards, format bar).</summary>
    public MatchViewModel Match { get; }

    [ObservableProperty] private AppState _state;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _hasUnsentChanges;
    [ObservableProperty] private string _linkStatus = "Not linked";

    public string? CurrentSavePath => _currentSavePath;

    // A state set from outside (load, restore, swap) is shown on the Match tab; collecting the tab back into the state is not.
    partial void OnStateChanged(AppState value)
    {
        if (!_collecting) Match.Load(value);
    }

    /// <summary>Takes what is on the Match tab into <see cref="State"/>.</summary>
    public void CollectMatch()
    {
        _collecting = true;
        try { State = Match.ApplyTo(State); }
        finally { _collecting = false; }
    }

    // ---- Match tab actions -------------------------------------------------------------------------------

    /// <summary>The Update button: send everything to the overlays.</summary>
    [RelayCommand]
    private void UpdateMatch()
    {
        CollectMatch();
        TryPublish(updateStatus: true);
        Autosave();
    }

    [RelayCommand]
    private void SwapTeams()
    {
        CollectMatch();
        State = MatchOperations.Swap(State);
        AfterLocalChange("Teams swapped (not published yet)");
    }

    [RelayCommand]
    private void ResetMatch()
    {
        CollectMatch();
        State = MatchOperations.Reset(State);
        AfterLocalChange("Match reset (not published yet)");
    }

    private void AfterLocalChange(string status)
    {
        HasUnsentChanges = true;
        StatusText = status;
        Autosave();
    }

    [RelayCommand]
    private Task ExportHomeAsync() => ExportAsync(1);

    [RelayCommand]
    private Task ExportAwayAsync() => ExportAsync(2);

    [RelayCommand]
    private Task ImportHomeAsync() => ImportAsync(1);

    [RelayCommand]
    private Task ImportAwayAsync() => ImportAsync(2);

    private async Task ExportAsync(int slot)
    {
        CollectMatch();
        var team = (slot == 1 ? State.Team1 : State.Team2) with { Score = 0, BannedHero = "" };
        var path = await _dialogs.PickSaveTeamFileAsync(_teamsFolder, TeamFile.SuggestedFileName(team));
        if (path is null) return;
        try
        {
            var warning = TeamFile.Export(team, path, _logos);
            StatusText = $"Exported {Path.GetFileName(path)}";
            if (warning is not null) await _messages.ShowErrorAsync("Export", warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _messages.ShowErrorAsync("Export failed", ex.Message);
        }
    }

    private async Task ImportAsync(int slot)
    {
        var path = await _dialogs.PickOpenTeamFileAsync(_teamsFolder);
        if (path is null) return;
        CollectMatch();
        var current = slot == 1 ? State.Team1 : State.Team2;
        Team imported;
        try
        {
            imported = TeamFile.Import(path, current.Score, slot == 1 ? Defaults.Team1Color : Defaults.Team2Color);
        }
        catch (StateLoadException ex)
        {
            await _messages.ShowErrorAsync("Import failed", ex.Message);
            return;
        }
        State = slot == 1 ? State with { Team1 = imported } : State with { Team2 = imported };
        AfterLocalChange($"Imported {Path.GetFileName(path)} (not published yet)");
    }

    private sealed class NoImagePicker : IImagePicker
    {
        public Task<string?> PickImageAsync(string title, string? initialDirectory = null) => Task.FromResult<string?>(null);
    }

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
        CollectMatch();
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
        CollectMatch();
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
        CollectMatch();
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

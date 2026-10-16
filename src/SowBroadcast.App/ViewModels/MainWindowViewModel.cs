using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Bracket;
using SowBroadcast.Core.Standings;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Match;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;
using SowBroadcast.Integrations.Link;

namespace SowBroadcast.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, ILinkHost
{
    private readonly IFileDialogs _dialogs;
    private readonly IMessages _messages;
    private readonly IOverlayPublisher _publisher;
    private readonly string _autosavePath;
    private readonly ILogoConverter _logos;
    private readonly string? _teamsFolder;
    private readonly HashSet<string> _unsentSources = new();
    private readonly object _autosaveLock = new();
    private readonly AssetService? _assets;
    private string? _currentSavePath;
    private bool _collecting;

    public MainWindowViewModel(IFileDialogs dialogs, IMessages messages, IOverlayPublisher publisher, string autosavePath,
        AssetService? assets = null, IImagePicker? images = null, ILogoConverter? logos = null,
        string? teamsFolder = null, string? logoFolder = null, string? broadcastLogoFolder = null,
        string? defaultVideosDir = null, Func<long>? clock = null, string? standingsLogoFolder = null,
        ITeamPicker? teamPicker = null, MatchIdSource? matchIds = null,
        LinkService? linkService = null, LogoCache? logoCache = null, Action<Action>? uiPost = null)
    {
        _assets = assets;
        _dialogs = dialogs;
        _messages = messages;
        _publisher = publisher;
        _autosavePath = autosavePath;
        _logos = logos ?? new SkiaLogoConverter();
        _teamsFolder = teamsFolder;
        ServerStatus = publisher.BaseUrl is null ? "Overlay server unavailable" : $"Overlays: {publisher.BaseUrl}";
        var picker = images ?? new NoImagePicker();
        Match = new MatchViewModel(assets, picker, logoFolder);
        General = new GeneralViewModel(picker, broadcastLogoFolder);
        Waiting = new WaitingViewModel(dialogs, defaultVideosDir, clock);
        Standings = new StandingsViewModel(picker, standingsLogoFolder);
        Bracket = new BracketViewModel(picker, standingsLogoFolder,
            () => StandingsOperations.TeamsFromStandings(Standings.ApplyTo(State).Standings), teamPicker, messages, matchIds);
        Link = new LinkViewModel(linkService ?? UnavailableLink(), logoCache, this, messages, uiPost ?? (action => action()));
        Match.Edited += () => MarkUnsent("match");
        General.Edited += () => MarkUnsent("general");
        Waiting.Edited += () => MarkUnsent("waiting");
        Standings.Edited += () => MarkUnsent("standings");
        Bracket.Edited += () => MarkUnsent("bracket");
        Waiting.TimerCommanded += () => PublishTimer(announce: true);
        Waiting.TimerTicked += () => PublishTimer(announce: false);
        State = AppState.CreateDefault();
        Version = AppInfo.Version;
    }

    public string Version { get; }
    public string ServerStatus { get; }
    public string? OverlayBaseUrl => _publisher.BaseUrl;

    /// <summary>The Match tab (team panels, map cards, format bar).</summary>
    public MatchViewModel Match { get; }

    /// <summary>The General tab (casters, logos, status text, overlay colours).</summary>
    public GeneralViewModel General { get; }

    /// <summary>The Waiting Screen tab (videos, countdown, texts, ticker, socials).</summary>
    public WaitingViewModel Waiting { get; }

    /// <summary>The SOWDraft link (tournament, match list, sides, status).</summary>
    public LinkViewModel Link { get; }

    /// <summary>Connects again with the link the last session saved (EHD-5).</summary>
    public Task RestoreLinkAsync() => Link.RestoreAsync(State.Link);

    // A link that cannot work (tests and tools that do not need one): every attempt ends in an error message.
    private static LinkService UnavailableLink() => new(new NoHttp(), () => throw new InvalidOperationException("Linking is not available."));

    private sealed class NoHttp : IHttpFetcher
    {
        public Task<HttpResult> GetAsync(Uri url, int maxBytes, CancellationToken cancellation) =>
            Task.FromResult(new HttpResult(0, Array.Empty<byte>(), null, "Linking is not available."));
    }

    // ---- ILinkHost ---------------------------------------------------------------------------------------

    AppState ILinkHost.CollectState()
    {
        CollectTabs();
        return State;
    }

    IReadOnlyList<string> ILinkHost.MapNames => _assets?.Names(AssetCategory.Maps) ?? Array.Empty<string>();

    void ILinkHost.ApplyLinked(AppState state)
    {
        _collecting = true;
        try { State = state; }
        finally { _collecting = false; }
        Match.Load(state);
        Standings.Load(state);
        Bracket.Load(state);
        TryPublish(updateStatus: false);
        Autosave();
    }

    /// <summary>The Standings tab (groups, tables, display group).</summary>
    public StandingsViewModel Standings { get; }

    /// <summary>The Bracket tab (template, team list, matches).</summary>
    public BracketViewModel Bracket { get; }

    [ObservableProperty] private AppState _state;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _hasUnsentChanges;

    public string? CurrentSavePath => _currentSavePath;

    // A state set from outside (swap, reset, import) is shown on the Match tab; collecting the tabs back into the state is not.
    // General and Waiting are only reloaded by ShowState (loading a whole state), so a running countdown is never disturbed.
    partial void OnStateChanged(AppState value)
    {
        if (!_collecting) Match.Load(value);
    }

    partial void OnHasUnsentChangesChanged(bool value)
    {
        if (!value) _unsentSources.Clear();
    }

    /// <summary>Notes that <paramref name="source"/> (a tab, assets, a loaded file) has changes the overlays do not have yet.</summary>
    public void MarkUnsent(string source)
    {
        _unsentSources.Add(source);
        HasUnsentChanges = true;
    }

    private void ClearUnsent(string source)
    {
        _unsentSources.Remove(source);
        HasUnsentChanges = _unsentSources.Count > 0;
    }

    /// <summary>Takes what is on the Match, General and Waiting tabs into <see cref="State"/>.</summary>
    public void CollectTabs()
    {
        _collecting = true;
        try { State = Bracket.ApplyTo(Standings.ApplyTo(Waiting.ApplyTo(General.ApplyTo(Match.ApplyTo(State))))); }
        finally { _collecting = false; }
    }

    /// <summary>Shows a whole state on every tab (a loaded file, the restored autosave).</summary>
    private void ShowState(AppState state)
    {
        State = state;
        General.Load(state);
        Waiting.Load(state);
        Standings.Load(state);
        Bracket.Load(state);
    }

    // ---- General and Waiting tab actions -----------------------------------------------------------------

    [RelayCommand]
    private void UpdateGeneral()
    {
        CollectTabs();
        PublishPart(PublishScope.General, "general", "General");
    }

    [RelayCommand]
    private void UpdateWaiting()
    {
        CollectTabs();
        PublishPart(PublishScope.Waiting, "waiting", "Waiting screen");
    }

    private void PublishPart(PublishScope scope, string source, string name)
    {
        try
        {
            var result = _publisher.PublishPart(State, scope);
            ClearUnsent(source);
            StatusText = result.ChangedKeys.Count == 0
                ? $"{name}: overlays are up to date"
                : $"{name} published: {string.Join(", ", result.ChangedKeys)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not write overlay files: {ex.Message}";
            return;
        }
        Autosave();
    }

    /// <summary>Update + sort: orders and ranks every table, then publishes the standings.</summary>
    [RelayCommand]
    private void UpdateStandings()
    {
        Standings.SortAll();
        CollectTabs();
        PublishPart(PublishScope.Standings, "standings", "Standings");
    }

    [RelayCommand]
    private void UpdateBracket()
    {
        CollectTabs();
        PublishPart(PublishScope.Bracket, "bracket", "Bracket");
    }

    [RelayCommand]
    private void ResetStandings()
    {
        Standings.ResetTab();
        MarkUnsent("standings");
        StatusText = "Standings tab reset (not published yet)";
        Autosave();
    }

    [RelayCommand]
    private void ResetBracket()
    {
        Bracket.ResetTab();
        MarkUnsent("bracket");
        StatusText = "Bracket tab reset (not published yet)";
        Autosave();
    }

    [RelayCommand]
    private void ResetGeneral()
    {
        General.Reset();
        MarkUnsent("general");
        StatusText = "General tab reset (not published yet)";
        Autosave();
    }

    [RelayCommand]
    private void ResetWaiting()
    {
        Waiting.ResetTab();
        MarkUnsent("waiting");
        StatusText = "Waiting screen tab reset (not published yet)";
        Autosave();
    }

    /// <summary>A countdown command goes out at once; the running clock only refreshes the timer files.</summary>
    private void PublishTimer(bool announce)
    {
        _collecting = true;
        try { State = Waiting.ApplyTimerTo(State); }
        finally { _collecting = false; }
        try
        {
            _publisher.PublishPart(State, announce ? PublishScope.Timer : PublishScope.TimerTick);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Could not write timer files: {ex.Message}";
        }
    }

    // ---- Match tab actions -------------------------------------------------------------------------------

    /// <summary>The Update button: send everything to the overlays.</summary>
    [RelayCommand]
    private void UpdateMatch()
    {
        CollectTabs();
        TryPublish(updateStatus: true);
        Autosave();
    }

    [RelayCommand]
    private void SwapTeams()
    {
        if (Link.IsLinked)
        {
            // The teams come from SOWDraft: swapping them means choosing the other sides, which writes everything again.
            Link.FlipSides();
            StatusText = "Sides swapped";
            return;
        }
        CollectTabs();
        State = MatchOperations.Swap(State);
        AfterLocalChange("Teams swapped (not published yet)");
    }

    [RelayCommand]
    private void ResetMatch()
    {
        CollectTabs();
        State = MatchOperations.Reset(State);
        AfterLocalChange("Match reset (not published yet)");
        if (Link.IsLinked) Link.Reapply(clearReceived: true);   // a linked match is filled in again at once
    }

    private void AfterLocalChange(string status)
    {
        MarkUnsent("match");
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
        CollectTabs();
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
        CollectTabs();
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
                ShowState(loaded);
                StatusText = $"Restored autosave from {_autosavePath}";
            }
        }
        catch (StateLoadException ex)
        {
            Serilog.Log.Error(ex, "The autosave could not be read");
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
        CollectTabs();
        try
        {
            var result = _publisher.Publish(State);
            HasUnsentChanges = false; // clears every source
            if (updateStatus)
                StatusText = result.ChangedKeys.Count == 0
                    ? "Overlays are up to date"
                    : $"Published: {string.Join(", ", result.ChangedKeys.Take(6))}{(result.ChangedKeys.Count > 6 ? "…" : "")}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "Could not write overlay files");
            StatusText = $"Could not write overlay files: {ex.Message}";
        }
    }

#if DEBUG
    [RelayCommand]
    private void LoadSample()
    {
        ShowState(SampleState.Create());
        MarkUnsent("file");
        StatusText = "Loaded the sample match (not published yet)";
    }
#endif

    public void Autosave()
    {
        CollectTabs();
        try
        {
            lock (_autosaveLock) StateStore.Save(_autosavePath, State);
        }
        catch (IOException ex)
        {
            Serilog.Log.Warning(ex, "Autosave failed");
            StatusText = $"Autosave failed: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            Serilog.Log.Warning(ex, "Autosave failed");
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
            ShowState(StateStore.Load(path));
        }
        catch (StateLoadException ex)
        {
            await _messages.ShowErrorAsync("Import failed", ex.Message);
            return;
        }
        _currentSavePath = path;
        MarkUnsent("file");
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
        CollectTabs();
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

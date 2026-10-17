using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Link;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;
using SowBroadcast.Integrations.Link;

namespace SowBroadcast.App.ViewModels;

/// <summary>What the link needs from the rest of the app.</summary>
public interface ILinkHost
{
    /// <summary>The state with everything on screen collected into it.</summary>
    AppState CollectState();

    /// <summary>The maps SOWDraft's map names are matched against.</summary>
    IReadOnlyList<string> MapNames { get; }

    /// <summary>The catalogue name for a name or alias SOWDraft sends (null when unknown, or when the host has no catalogue).</summary>
    string? ResolveName(SowBroadcast.Core.Assets.AssetCategory category, string name) => null;

    /// <summary>Shows linked data on the Match, Standings and Bracket tabs and sends it to the overlays at once (KYS-11a).</summary>
    void ApplyLinked(AppState state);

    /// <summary>The user is about to follow another match: the one on screen can be archived first.</summary>
    void BeforeMatchChange() { }
}

/// <summary>An entry of the match list: a tournament match the user can choose to follow.</summary>
public sealed record MatchChoice(string? Id, string Label, bool IsLive);

/// <summary>
/// The SOWDraft link of the app: the tournament address, the match list, the "Swap sides" switch, the status line and the advanced
/// manual draft room link. Applies what the <see cref="LinkService"/> reports to the tabs (CLAUDE.md 8.4).
/// </summary>
public sealed partial class LinkViewModel : ObservableObject
{
    public static readonly MatchChoice NoMatch = new(null, "— Select a match —", false);

    private readonly LinkService _service;
    private readonly LogoCache? _logos;
    private readonly ILinkHost _host;
    private readonly IMessages _messages;
    private readonly Action<Action> _post;
    private readonly Func<DateTime> _now;
    private LinkUpdate? _last;
    private bool _rebuilding;
    private string _lastUpdateTime = "";
    private string _loggedStatus = "";

    public LinkViewModel(LinkService service, LogoCache? logos, ILinkHost host, IMessages messages, Action<Action> post, Func<DateTime>? now = null)
    {
        _service = service;
        _logos = logos;
        _host = host;
        _messages = messages;
        _post = post;
        _now = now ?? (() => DateTime.Now);
        _selectedMatch = NoMatch;
        Matches.Add(NoMatch);

        service.Updated += update => _post(() => HandleUpdate(update));
        service.StatusChanged += _ => _post(RefreshStatus);
    }

    public ObservableCollection<MatchChoice> Matches { get; } = new();

    [ObservableProperty] private string _tournamentUrl = "";
    [ObservableProperty] private string _manualUrl = "";
    [ObservableProperty] private string _statusText = "Not linked";
    [ObservableProperty] private string _warningText = "";
    [ObservableProperty] private bool _isLinked;
    [ObservableProperty] private bool _isTournamentLinked;
    [ObservableProperty] private MatchChoice? _selectedMatch;
    [ObservableProperty] private bool _sidesSwapped;

    /// <summary>The link controls can be used (nothing is linked).</summary>
    public bool CanLink => !IsLinked;

    /// <summary>The short text of the header pill.</summary>
    public string PillText => IsLinked ? "SOWDraft linked" : "Not linked";

    partial void OnIsLinkedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanLink));
        OnPropertyChanged(nameof(PillText));
    }

    // ---- user actions ------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task LinkAsync()
    {
        var url = TournamentUrl.Trim();
        if (url.Length == 0)
        {
            await _messages.ShowErrorAsync("Link Tournament", "Paste a SOWDraft link first.");
            return;
        }
        try
        {
            await _service.LinkTournamentAsync(url);
            TournamentUrl = url;
        }
        catch (LinkException ex)
        {
            await _messages.ShowErrorAsync("Link Tournament", ex.Message);
        }
        RefreshStatus();
    }

    [RelayCommand]
    private async Task LinkManualAsync()
    {
        var url = ManualUrl.Trim();
        if (url.Length == 0)
        {
            await _messages.ShowErrorAsync("Link Draft Room", "Paste a SOWDraft link first.");
            return;
        }
        try
        {
            await _service.LinkDraftRoomAsync(url);
        }
        catch (LinkException ex)
        {
            await _messages.ShowErrorAsync("Link Draft Room", ex.Message);
        }
        RefreshStatus();
    }

    /// <summary>Unlink: the connection ends, every map card is cleared and the result goes to the overlays (as in the legacy app).</summary>
    [RelayCommand]
    private async Task UnlinkAsync()
    {
        await _service.UnlinkAsync();
        _last = null;
        _lastUpdateTime = "";
        _rebuilding = true;
        Matches.Clear();
        Matches.Add(NoMatch);
        SelectedMatch = NoMatch;
        _rebuilding = false;
        IsTournamentLinked = false;
        WarningText = "";

        var state = _host.CollectState();
        _host.ApplyLinked(state with
        {
            Maps = state.Maps.Select(m => new MapEntry { Index = m.Index }).ToArray(),
            Link = new LinkSettings(),
        });
        RefreshStatus();
    }

    /// <summary>The teams of the linked tournament (empty when none is linked); the team library can take them over.</summary>
    public IReadOnlyList<LinkTeam> LinkedTeams => _service.IsLinked && _last is { IsManualDraftRoom: false } last ? last.Snapshot.Teams : Array.Empty<LinkTeam>();

    /// <summary>Downloads logos that are not here yet (nothing to do without a logo cache).</summary>
    public Task<int> EnsureLogosAsync(IEnumerable<string> urls) => _logos is null ? Task.FromResult(0) : _logos.EnsureAsync(urls);

    /// <summary>The downloaded file of a logo address, or null.</summary>
    public string? LogoPathOf(string url) => _logos?.PathOf(url);

    partial void OnSelectedMatchChanged(MatchChoice? value)
    {
        if (_rebuilding || !_service.IsLinked) return;
        _host.BeforeMatchChange();
        _ = _service.SelectMatchAsync(value?.Id);
    }

    partial void OnSidesSwappedChanged(bool value)
    {
        if (_rebuilding) return;
        // New sides: everything that comes from SOWDraft is written again, so nothing stays on the wrong team.
        Reapply(clearReceived: true);
    }

    /// <summary>Applies the last data again (after Reset or a change of sides). Without a link there is nothing to apply.</summary>
    public void Reapply(bool clearReceived)
    {
        if (_last is null) return;
        HandleUpdate(_last, forceWrite: clearReceived);
    }

    /// <summary>The "Swap Teams" button while linked: same as the Swap sides switch.</summary>
    public void FlipSides() => SidesSwapped = !SidesSwapped;

    // ---- restoring ---------------------------------------------------------------------------------------

    /// <summary>Connects again with what the last session saved (a tournament and its chosen match, or a manual draft room).</summary>
    public async Task RestoreAsync(LinkSettings saved)
    {
        if (!saved.IsLinked) return;
        _rebuilding = true;
        SidesSwapped = saved.SidesSwapped;
        _rebuilding = false;
        try
        {
            if (saved.ManualDraftUrl.Length > 0)
            {
                ManualUrl = saved.ManualDraftUrl;
                await _service.LinkDraftRoomAsync(saved.ManualDraftUrl);
            }
            else
            {
                TournamentUrl = saved.TournamentUrl;
                await _service.LinkTournamentAsync(saved.TournamentUrl, saved.SelectedMatchId);
            }
        }
        catch (LinkException ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        RefreshStatus();
    }

    // ---- what the service reports ------------------------------------------------------------------------

    private void RefreshStatus()
    {
        var status = _service.Status;
        if (status.Text != _loggedStatus)
        {
            _loggedStatus = status.Text;
            if (status.State == LinkState.Error) Serilog.Log.Warning("SOWDraft link: {Status}", status.Text);
            else Serilog.Log.Information("SOWDraft link: {Status}", status.Text);
        }
        IsLinked = _service.IsLinked;
        var linkedStates = status.State is LinkState.Linked or LinkState.WaitingForDraft or LinkState.MatchComplete or LinkState.NoMatchSelected;
        StatusText = linkedStates && _lastUpdateTime.Length > 0 ? $"{status.Text}  ·  last update {_lastUpdateTime}" : status.Text;
        if (!IsLinked) IsTournamentLinked = false;
    }

    private void HandleUpdate(LinkUpdate update, bool forceWrite = false)
    {
        if (!_service.IsLinked) return;
        _last = update;
        _lastUpdateTime = _now().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        var state = _host.CollectState();
        var link = state.Link;
        var snapshot = update.Snapshot;
        var received = link.Received;
        var warnings = new List<string>();
        var toFetch = new List<string>();
        var next = state;

        // Another match means another set of values: write everything again.
        if (forceWrite || (!update.IsManualDraftRoom && update.SelectedMatchId != link.SelectedMatchId)) received = new Dictionary<string, string>();

        if (update.SelectedMatchId.Length > 0)
        {
            var options = new LinkApplyOptions(SidesSwapped, _host.MapNames, url => _logos?.PathOf(url))
            {
                ResolveMap = name => _host.ResolveName(SowBroadcast.Core.Assets.AssetCategory.Maps, name),
                ResolveHero = name => _host.ResolveName(SowBroadcast.Core.Assets.AssetCategory.Heroes, name),
            };
            var applied = MatchLinkMapper.Apply(next, snapshot, update.SelectedMatchId, options, received);
            next = applied.State;
            received = applied.Received;
            warnings.AddRange(applied.Warnings);
            toFetch.AddRange(applied.LogosToFetch);
        }

        if (!update.IsManualDraftRoom)
        {
            string? LogoOf(string? url) => url is null ? null : _logos?.PathOf(url);
            next = next with { Standings = TournamentMapper.Standings(snapshot, LogoOf), Bracket = TournamentMapper.Bracket(snapshot, LogoOf) };
            toFetch.AddRange(snapshot.Teams.Select(t => t.LogoUrl).Where(u => !string.IsNullOrEmpty(u))!);
        }

        next = next with
        {
            Link = link with
            {
                TournamentUrl = update.IsManualDraftRoom ? "" : TournamentUrl,
                ManualDraftUrl = update.IsManualDraftRoom ? ManualUrl.Trim() : "",
                Origin = snapshot.Origin,
                TournamentId = update.IsManualDraftRoom ? "" : snapshot.Id,
                SelectedMatchId = update.IsManualDraftRoom ? "" : update.SelectedMatchId,
                SidesSwapped = SidesSwapped,
                Received = received,
            },
        };

        IsTournamentLinked = !update.IsManualDraftRoom;
        WarningText = warnings.Count == 0 ? "" : $"Warning: {warnings[0]}" + (warnings.Count > 1 ? $" (+{warnings.Count - 1} more)" : "");
        if (!update.IsManualDraftRoom) RebuildMatches(snapshot, update.SelectedMatchId);

        // Only touch the tabs when something differs (the 30 s refetch usually changes nothing).
        if (Signature(next) != Signature(state)) _host.ApplyLinked(next);
        RefreshStatus();

        if (_logos is not null && toFetch.Count > 0) _ = FetchLogosAsync(toFetch.Distinct().ToArray());
    }

    private async Task FetchLogosAsync(IReadOnlyList<string> urls)
    {
        try
        {
            var downloaded = await _logos!.EnsureAsync(urls).ConfigureAwait(false);
            if (downloaded > 0) _post(() => { if (_last is not null) HandleUpdate(_last); });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _post(() => WarningText = $"Warning: could not save a logo ({ex.Message})");
        }
    }

    private static string Signature(AppState state) =>
        StateStore.Serialize(state with { General = new GeneralSettings(), Waiting = new WaitingSettings() });

    // ---- match list --------------------------------------------------------------------------------------

    private void RebuildMatches(TournamentSnapshot snapshot, string selectedId)
    {
        string Name(string? id) => snapshot.TeamById(id)?.Name is { Length: > 0 } n ? n : "TBD";
        static int Rank(LinkMatch m) =>
            m.Status == "in_progress" ? 0 : m.Status == "completed" ? 3 : string.IsNullOrEmpty(m.Team1Id) || string.IsNullOrEmpty(m.Team2Id) ? 2 : 1;

        // Matches in play first, then the next ones to be played (soonest first), then the finished ones, newest first. While any match is
        // in play the finished ones are left out (the one chosen stays, so the selection does not vanish).
        var anyInPlay = snapshot.Matches.Any(m => m.Status == "in_progress");
        var visible = snapshot.Matches
            .Where(m => m.Status != "bye" && !(anyInPlay && m.Status == "completed" && m.Id != selectedId))
            .ToArray();
        var choices = visible.Where(m => Rank(m) < 3)
            .OrderBy(Rank)
            .ThenBy(m => m.ScheduledAt ?? long.MaxValue)
            .ThenBy(m => m.Round)
            .ThenBy(m => m.MatchNumber)
            .Concat(visible.Where(m => Rank(m) == 3)
                .OrderByDescending(m => m.ScheduledAt ?? 0L)
                .ThenByDescending(m => m.Round)
                .ThenByDescending(m => m.MatchNumber))
            .Select(m =>
            {
                var live = m.Status == "in_progress" && !string.IsNullOrEmpty(m.DraftMatchId);
                var where = string.IsNullOrEmpty(m.GroupName) ? $"round {m.Round}" : $"group {m.GroupName}, round {m.Round}";
                var status = m.Status.Replace('_', ' ');
                return new MatchChoice(m.Id, $"{(live ? "● " : "")}{Name(m.Team1Id)} vs {Name(m.Team2Id)} – {where} / {status}", live);
            })
            .ToArray();

        // Keep the list as it is when nothing about it changed, so an open drop-down is not disturbed.
        var wanted = new[] { NoMatch }.Concat(choices).ToArray();
        if (!Matches.SequenceEqual(wanted))
        {
            _rebuilding = true;
            Matches.Clear();
            foreach (var choice in wanted) Matches.Add(choice);
            _rebuilding = false;
        }

        var selected = Matches.FirstOrDefault(c => c.Id == (selectedId.Length == 0 ? null : selectedId)) ?? NoMatch;
        if (!ReferenceEquals(SelectedMatch, selected) && SelectedMatch != selected)
        {
            _rebuilding = true;
            SelectedMatch = selected;
            _rebuilding = false;
        }
    }
}

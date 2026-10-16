using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SowBroadcast.App.Services;
using SowBroadcast.Core.Bracket;
using SowBroadcast.Core.Control;
using SowBroadcast.Core.Model;

namespace SowBroadcast.App.ViewModels;

/// <summary>One of the 16 lines of the bracket's team list: a name and a logo.</summary>
public sealed partial class BracketTeamRowViewModel : EditableObject
{
    private readonly IImagePicker _images;
    private readonly string? _logoFolder;

    public BracketTeamRowViewModel(int number, IImagePicker images, string? logoFolder)
    {
        Number = number;
        _images = images;
        _logoFolder = logoFolder;
    }

    public int Number { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _logoPath;

    [RelayCommand]
    private async Task BrowseLogoAsync()
    {
        var path = await _images.PickImageAsync("Select team logo", _logoFolder);
        if (path is not null) LogoPath = path;
    }

    public void Set(TeamRef? team)
    {
        Name = team?.Name ?? "";
        LogoPath = string.IsNullOrEmpty(team?.LogoPath) ? null : team!.LogoPath;
    }

    /// <summary>The abbreviation of a bracket team is always empty (as in the legacy app).</summary>
    public TeamRef ToRef() => new() { Name = Name.Trim(), Abbr = "", LogoPath = string.IsNullOrEmpty(LogoPath) ? null : LogoPath };
}

/// <summary>A match of the bracket: two teams picked from the team list, and their scores.</summary>
public sealed partial class BracketMatchViewModel : EditableObject
{
    private readonly Func<string, TeamRef?> _lookup;
    private TeamRef _ref1;
    private TeamRef _ref2;

    // Carried through unchanged (they come from a tournament link or the template).
    private readonly string _id, _boLabel, _status;
    private readonly long _scheduledAt;

    public BracketMatchViewModel(BracketMatch match, Func<string, TeamRef?> lookup, IReadOnlyList<string> teamNames)
    {
        _lookup = lookup;
        _id = match.Id;
        _boLabel = match.BoLabel;
        _status = match.Status;
        _scheduledAt = match.ScheduledAt;
        _ref1 = match.Team1;
        _ref2 = match.Team2;
        Loading = true;
        TeamNames = teamNames;
        Team1 = string.IsNullOrEmpty(match.Team1.Name) ? null : match.Team1.Name;
        Team2 = string.IsNullOrEmpty(match.Team2.Name) ? null : match.Team2.Name;
        Score1 = match.Score1;
        Score2 = match.Score2;
        Loading = false;
    }

    [ObservableProperty] private IReadOnlyList<string> _teamNames;
    [ObservableProperty] private string? _team1;
    [ObservableProperty] private string? _team2;
    [ObservableProperty] private int _score1;
    [ObservableProperty] private int _score2;

    protected override bool IsDerived(string? propertyName) => propertyName == nameof(TeamNames);

    // Picking a team copies its name, abbreviation and logo from the team list.
    partial void OnTeam1Changed(string? value)
    {
        if (!Loading) _ref1 = Resolve(value);
    }

    partial void OnTeam2Changed(string? value)
    {
        if (!Loading) _ref2 = Resolve(value);
    }

    private TeamRef Resolve(string? name) =>
        string.IsNullOrEmpty(name) ? new TeamRef() : _lookup(name) ?? new TeamRef { Name = name };

    /// <summary>Puts <paramref name="team"/> in place 1 or 2 (the winner who moved up), keeping its logo and abbreviation.</summary>
    public void Assign(int slot, TeamRef team)
    {
        Loading = true;
        try
        {
            if (slot == 1) { _ref1 = team; Team1 = team.Name; }
            else { _ref2 = team; Team2 = team.Name; }
        }
        finally { Loading = false; }
        RaiseEdited();
    }

    /// <summary>After the team list changed: teams that are still in it take the list's current logo and name data.</summary>
    public void Resync()
    {
        if (Team1 is not null && _lookup(Team1) is { } a) _ref1 = a;
        if (Team2 is not null && _lookup(Team2) is { } b) _ref2 = b;
    }

    public BracketMatch ToMatch() => new()
    {
        Id = _id,
        BoLabel = _boLabel,
        Status = _status,
        Team1 = _ref1 with { Name = Team1 ?? "" },
        Team2 = _ref2 with { Name = Team2 ?? "" },
        Score1 = Math.Clamp(Score1, 0, 99),
        Score2 = Math.Clamp(Score2, 0, 99),
        ScheduledAt = _scheduledAt,
    };
}

public sealed class BracketRoundViewModel
{
    public BracketRoundViewModel(BracketRound round, IReadOnlyList<BracketMatchViewModel> matches)
    {
        Name = round.Name;
        Side = round.Side;
        Matches = matches;
    }

    public string Name { get; }
    public string Side { get; }
    public IReadOnlyList<BracketMatchViewModel> Matches { get; }

    public BracketRound ToRound() => new() { Name = Name, Side = Side, Matches = Matches.Select(m => m.ToMatch()).ToArray() };
}

public sealed class BracketSectionViewModel
{
    public BracketSectionViewModel(string title, IReadOnlyList<BracketRoundViewModel> rounds)
    {
        Title = title;
        Rounds = rounds;
    }

    public string Title { get; }
    public IReadOnlyList<BracketRoundViewModel> Rounds { get; }
}

/// <summary>The Bracket tab: title and stage, a template, the team list and the matches.</summary>
public sealed partial class BracketViewModel : EditableObject
{
    public const string UpperView = "Upper Bracket";
    public const string LowerView = "Lower Bracket";

    private readonly Func<IReadOnlyList<TeamRef>> _standingsTeams;
    private readonly ITeamPicker? _picker;
    private readonly IMessages _messages;
    private readonly MatchIdSource _ids;
    private List<BracketRoundViewModel> _rounds = new();
    private bool _advancing;

    /// <summary>The automations the producer has switched on (EHD-4); null = none.</summary>
    public AutomationSettings? Automation { get; set; }

    /// <summary>Whether the winners may be moved up now; false while a tournament link fills (and locks) the bracket.</summary>
    public Func<bool>? AutomationAllowed { get; set; }

    public BracketViewModel(IImagePicker images, string? logoFolder, Func<IReadOnlyList<TeamRef>> standingsTeams,
        ITeamPicker? picker, IMessages messages, MatchIdSource? ids = null)
    {
        _standingsTeams = standingsTeams;
        _picker = picker;
        _messages = messages;
        _ids = ids ?? new MatchIdSource();
        Teams = Enumerable.Range(1, Limits.BracketTeams).Select(i => new BracketTeamRowViewModel(i, images, logoFolder)).ToArray();
        foreach (var row in Teams) row.Edited += OnTeamListEdited;
        TemplateNames = BracketTemplates.All.Select(t => t.Label).ToArray();
        _selectedTemplate = TemplateNames[0];
    }

    public IReadOnlyList<BracketTeamRowViewModel> Teams { get; }
    public IReadOnlyList<string> TemplateNames { get; }
    public IReadOnlyList<string> DoubleViewNames { get; } = new[] { UpperView, LowerView };
    public ObservableCollection<BracketSectionViewModel> Sections { get; } = new();

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _stage = "";
    [ObservableProperty] private string? _selectedTemplate;
    [ObservableProperty] private bool _showDoubleView;
    [ObservableProperty] private string? _doubleView = UpperView;
    [ObservableProperty] private bool _showBronze;
    [ObservableProperty] private bool _bronzeEnabled;
    [ObservableProperty] private bool _hasRounds;

    protected override bool IsDerived(string? propertyName) =>
        propertyName is nameof(SelectedTemplate) or nameof(ShowDoubleView) or nameof(ShowBronze) or nameof(HasRounds);

    partial void OnBronzeEnabledChanged(bool value)
    {
        if (Loading || !ShowBronze) return;
        SetRounds(BracketTemplates.WithBronze(CurrentRounds(), value, _ids));
    }

    // ---- team list ---------------------------------------------------------------------------------------

    private IReadOnlyList<string> TeamNameList() =>
        Teams.Select(t => t.Name.Trim()).Where(n => n.Length > 0).ToArray();

    private TeamRef? Lookup(string name) =>
        Teams.FirstOrDefault(t => t.Name.Trim() == name)?.ToRef();

    private void OnTeamListEdited()
    {
        RefreshTeamOptions();
        RaiseEdited();
    }

    private void RefreshTeamOptions()
    {
        var names = TeamNameList();
        foreach (var match in _rounds.SelectMany(r => r.Matches))
        {
            match.TeamNames = names;
            match.Resync();
        }
    }

    [RelayCommand]
    private void ClearTeams()
    {
        foreach (var row in Teams) row.Set(null);
        RefreshTeamOptions();
        RaiseEdited();
    }

    [RelayCommand]
    private async Task ImportFromStandingsAsync()
    {
        var teams = _standingsTeams();
        if (teams.Count == 0)
        {
            await _messages.ShowInfoAsync("Import Teams", "No teams found in standings.");
            return;
        }
        var selected = _picker is null ? teams : await _picker.PickTeamsAsync(teams);
        if (selected is null) return;

        for (var i = 0; i < Teams.Count; i++) Teams[i].Set(i < selected.Count ? selected[i] : null);
        RefreshTeamOptions();
        RaiseEdited();
    }

    // ---- rounds and matches ------------------------------------------------------------------------------

    private IReadOnlyList<BracketRound> CurrentRounds() => _rounds.Select(r => r.ToRound()).ToArray();

    private void SetRounds(IReadOnlyList<BracketRound> rounds)
    {
        var names = TeamNameList();
        _rounds = rounds.Select(round =>
        {
            var matches = round.Matches
                .Select(m => m with { Id = string.IsNullOrEmpty(m.Id) ? _ids.Next() : m.Id })
                .Select(m =>
                {
                    var vm = new BracketMatchViewModel(m, Lookup, names);
                    vm.Edited += OnMatchEdited;
                    return vm;
                })
                .ToArray();
            return new BracketRoundViewModel(round, matches);
        }).ToList();

        Sections.Clear();
        foreach (var (key, title) in new[] { ("upper", "Upper Bracket"), ("lower", "Lower Bracket"), ("grand", "Grand Final") })
        {
            var rounds2 = _rounds.Where(r => SectionOf(r.Side) == key).ToArray();
            if (rounds2.Length > 0) Sections.Add(new BracketSectionViewModel(title, rounds2));
        }
        HasRounds = _rounds.Count > 0;
        RaiseEdited();
    }

    private void OnMatchEdited()
    {
        if (!_advancing && !Loading && Automation is { AdvanceBracketWinners: true } automation && AutomationAllowed?.Invoke() != false)
            AdvanceWinners(automation);
        RaiseEdited();
    }

    /// <summary>EHD-4: the winner of every decided match takes its place in the next round (see <see cref="BracketAdvance"/>).</summary>
    public int AdvanceWinners(AutomationSettings automation)
    {
        _advancing = true;
        try
        {
            var plan = BracketAdvance.Plan(CurrentRounds(), automation.DefaultBracketBestOf);
            foreach (var step in plan) _rounds[step.Round].Matches[step.Match].Assign(step.Slot, step.Team);
            return plan.Count;
        }
        finally { _advancing = false; }
    }

    private static string SectionOf(string? side) => side is "lower" or "grand" ? side : "upper";

    private BracketTemplate SelectedTemplateValue =>
        BracketTemplates.All.Where(t => t.Label == SelectedTemplate).Select(t => (BracketTemplate?)t.Template).FirstOrDefault()
        ?? BracketTemplate.Single4;

    /// <summary>Replaces the bracket with the chosen template (matches typed so far are lost, as in the legacy app).</summary>
    [RelayCommand]
    private void LoadTemplate()
    {
        var template = SelectedTemplateValue;
        var rounds = BracketTemplates.Build(template, _ids);
        Loading = true;
        try
        {
            ShowDoubleView = !BracketTemplates.IsSingle(template) && template != BracketTemplate.Double4;
            DoubleView = UpperView;
            ShowBronze = BracketTemplates.IsSingle(template);
            BronzeEnabled = false;
        }
        finally { Loading = false; }
        SetRounds(rounds);
    }

    /// <summary>Back to an empty bracket with no title, stage or teams. Not sent.</summary>
    public void ResetTab()
    {
        Title = "";
        Stage = "";
        foreach (var row in Teams) row.Set(null);
        Loading = true;
        try
        {
            ShowDoubleView = false;
            ShowBronze = false;
            BronzeEnabled = false;
        }
        finally { Loading = false; }
        SetRounds(Array.Empty<BracketRound>());
    }

    // ---- state -------------------------------------------------------------------------------------------

    public void Load(AppState state)
    {
        var b = state.Bracket;
        Loading = true;
        try
        {
            Title = b.Title;
            Stage = b.Stage;
            for (var i = 0; i < Teams.Count; i++) Teams[i].Set(i < b.Teams.Count ? b.Teams[i] : null);
            SetRounds(b.Rounds);
            ShowDoubleView = BracketTemplates.SupportsDoubleElimView(b.Rounds);
            DoubleView = b.DoubleElimView == "lower" ? LowerView : UpperView;
            ShowBronze = BracketTemplates.IsSingleElimination(b.Rounds);
            BronzeEnabled = ShowBronze && BracketTemplates.HasBronze(b.Rounds);
        }
        finally { Loading = false; }
    }

    public AppState ApplyTo(AppState state) => state with
    {
        Bracket = state.Bracket with
        {
            Title = Title.Trim(),
            Stage = Stage.Trim(),
            Rounds = CurrentRounds(),
            DoubleElimView = ShowDoubleView ? (DoubleView == LowerView ? "lower" : "upper") : "",
            Teams = Teams.Select(t => t.ToRef()).ToArray(),
        },
    };
}

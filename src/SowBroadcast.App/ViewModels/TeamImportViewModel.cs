using CommunityToolkit.Mvvm.ComponentModel;
using SowBroadcast.Core.Model;

namespace SowBroadcast.App.ViewModels;

/// <summary>A team in the import list; everything starts selected.</summary>
public sealed partial class TeamChoice : ObservableObject
{
    public TeamChoice(TeamRef team)
    {
        Team = team;
        Label = string.IsNullOrWhiteSpace(team.Name) ? "Unnamed team" : team.Name;
    }

    public TeamRef Team { get; }
    public string Label { get; }

    [ObservableProperty] private bool _isSelected = true;
}

public sealed class TeamImportViewModel
{
    public TeamImportViewModel(IEnumerable<TeamRef> teams) => Choices = teams.Select(t => new TeamChoice(t)).ToArray();

    public IReadOnlyList<TeamChoice> Choices { get; }

    /// <summary>The ticked teams in list order.</summary>
    public IReadOnlyList<TeamRef> Selected() => Choices.Where(c => c.IsSelected).Select(c => c.Team).ToArray();
}

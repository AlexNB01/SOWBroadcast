using CommunityToolkit.Mvvm.ComponentModel;
using SowBroadcast.Core.History;

namespace SowBroadcast.App.ViewModels;

/// <summary>One line of the "Restore previous match" list.</summary>
public sealed class ArchivedMatchItem
{
    public ArchivedMatchItem(ArchivedMatch match)
    {
        Path = match.Path;
        Title = match.Title;
        When = match.Time.ToString("ddd d MMM yyyy, HH:mm");
    }

    public string Path { get; }
    public string Title { get; }
    public string When { get; }
}

public sealed partial class RestoreMatchViewModel : ObservableObject
{
    public RestoreMatchViewModel(IEnumerable<ArchivedMatch> matches)
    {
        Matches = matches.Select(m => new ArchivedMatchItem(m)).ToArray();
        _selected = Matches.FirstOrDefault();
    }

    public IReadOnlyList<ArchivedMatchItem> Matches { get; }
    public bool IsEmpty => Matches.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ArchivedMatchItem? _selected;

    public bool HasSelection => Selected is not null;
}

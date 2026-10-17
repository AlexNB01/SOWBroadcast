using CommunityToolkit.Mvvm.Input;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;
using SowBroadcast.Core.Teams;

namespace SowBroadcast.App.ViewModels;

// The team library (EHD-8): teams are kept and found by name instead of being saved to and loaded from files.
public sealed partial class MainWindowViewModel
{
    private readonly TeamLibrary? _library;

    /// <summary>The library; null when the program runs without one (tests).</summary>
    public TeamLibrary? Library => _library;

    private static string SlotName(int slot) => slot == 1 ? "Home" : "Away";

    /// <summary>Keeps the team of a panel in the library (a team of the same name is updated).</summary>
    public string SaveTeamToLibrary(int slot)
    {
        if (_library is null) return Say("The team library is not available.");
        CollectTabs();
        var team = slot == 1 ? State.Team1 : State.Team2;
        if (string.IsNullOrWhiteSpace(team.Name)) return Say($"Give the {SlotName(slot)} team a name before keeping it in the library.");
        try
        {
            var result = _library.Save(team);
            var text = result.Added ? $"{result.Team.Name} added to the team library" : $"{result.Team.Name} updated in the team library";
            return Say(result.Warning is null ? text : $"{text}. {result.Warning}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Say($"The team library could not be written: {ex.Message}");
        }
    }

    /// <summary>Shows a library team in the Home or Away panel; the panel's score stays. Nothing is sent until Update.</summary>
    public string LoadTeamFromLibrary(int slot, string id)
    {
        if (_library?.Find(id) is not { } entry) return Say("That team is no longer in the library.");
        FlushHistory();
        CollectTabs();
        var current = slot == 1 ? State.Team1 : State.Team2;
        var team = _library.ToTeam(entry, current.Score, slot == 1 ? Defaults.Team1Color : Defaults.Team2Color);
        State = slot == 1 ? State with { Team1 = team } : State with { Team2 = team };
        AfterLocalChange($"Loaded {entry.Name} as {SlotName(slot)} (not published yet)");
        return StatusText;
    }

    public string DeleteLibraryTeam(string id)
    {
        if (_library?.Find(id) is not { } entry) return Say("That team is no longer in the library.");
        try
        {
            _library.Delete(id);
            return Say($"{entry.Name} removed from the team library");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Say($"The team library could not be written: {ex.Message}");
        }
    }

    /// <summary>Asks for a <c>*.sowteam.json</c> file (the old way of keeping teams) and adds its team to the library.</summary>
    public async Task<string> ImportTeamFileToLibraryAsync()
    {
        if (_library is null) return Say("The team library is not available.");
        var path = await _dialogs.PickOpenTeamFileAsync(_teamsFolder);
        if (path is null) return StatusText;
        try
        {
            var result = _library.ImportFile(path);
            return Say(result.Added ? $"{result.Team.Name} added to the team library" : $"{result.Team.Name} updated in the team library");
        }
        catch (StateLoadException ex)
        {
            await _messages.ShowErrorAsync("Import failed", ex.Message);
            return Say("Import failed");
        }
        catch (ArgumentException)
        {
            await _messages.ShowErrorAsync("Import failed", "The team file has no team name.");
            return Say("Import failed");
        }
    }

    /// <summary>How many teams the linked SOWDraft tournament has (0 when no tournament is linked).</summary>
    public int LinkedTeamCount => Link.LinkedTeams.Count;

    /// <summary>Takes every team of the linked SOWDraft tournament into the library (names, abbreviations, colours, logos, players).</summary>
    public async Task<string> ImportTeamsFromLinkAsync()
    {
        if (_library is null) return Say("The team library is not available.");
        var teams = Link.LinkedTeams;
        if (teams.Count == 0) return Say("Link a SOWDraft tournament first.");

        await Link.EnsureLogosAsync(teams.Where(t => !string.IsNullOrEmpty(t.LogoUrl)).Select(t => t.LogoUrl!));
        try
        {
            var (added, updated, warnings) = _library.ImportFromLink(teams, url => Link.LogoPathOf(url), hero => ((ILinkHost)this).ResolveName(SowBroadcast.Core.Assets.AssetCategory.Heroes, hero));
            var text = $"SOWDraft teams: {added} added, {updated} updated";
            return Say(warnings.Count == 0 ? text : $"{text}. {string.Join(" ", warnings)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Say($"The team library could not be written: {ex.Message}");
        }
    }

    [RelayCommand]
    private void SaveHomeToLibrary() => SaveTeamToLibrary(1);

    [RelayCommand]
    private void SaveAwayToLibrary() => SaveTeamToLibrary(2);

    private string Say(string text)
    {
        StatusText = text;
        return text;
    }
}

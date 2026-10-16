using CommunityToolkit.Mvvm.ComponentModel;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Profiles;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.App.ViewModels;

// Profiles (EHD-18): one tournament's or match's whole setup, saved by name and put back in one step.
public sealed partial class MainWindowViewModel
{
    private readonly ProfileStore? _profiles;

    /// <summary>The profile store; null when the program runs without one (tests).</summary>
    public ProfileStore? Profiles => _profiles;

    /// <summary>The name of the profile that was applied or saved last (empty when none); shown in the header.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveProfile))]
    private string _activeProfileName = "";

    public bool HasActiveProfile => ActiveProfileName.Length > 0;

    private void RefreshActiveProfile()
    {
        var id = _profiles?.ActiveId ?? "";
        ActiveProfileName = id.Length == 0 ? "" : _profiles!.List().FirstOrDefault(p => p.Id == id)?.Name ?? "";
    }

    /// <summary>Keeps everything on screen as the profile <paramref name="name"/> (a profile of that name is replaced).</summary>
    public string SaveProfile(string name)
    {
        if (_profiles is null) return Say("Profiles are not available.");
        if (string.IsNullOrWhiteSpace(name)) return Say("Give the profile a name first.");
        CollectTabs();
        try
        {
            var replaced = _profiles.FindByName(name) is not null;
            // The tournament address as typed in the link box (State has it only once a link has been made).
            var address = Link.TournamentUrl.Trim().Length > 0 ? Link.TournamentUrl.Trim() : State.Link.TournamentUrl;
            var info = _profiles.Save(name, State with { Link = State.Link with { TournamentUrl = address } });
            RefreshActiveProfile();
            return Say(replaced ? $"Profile {info.Name} updated" : $"Profile {info.Name} saved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Say($"The profile could not be saved: {ex.Message}");
        }
    }

    /// <summary>
    /// Puts a profile on screen. <paramref name="wholeMatch"/> false keeps the teams, maps and scores that are on screen and changes
    /// only the event settings (status text, colours, logos, casters, waiting texts, standings, bracket, tournament address);
    /// true brings the saved match too (the current one is archived first). Nothing is sent until Update; the countdown and a running
    /// SOWDraft connection are left alone.
    /// </summary>
    public string ApplyProfile(string id, bool wholeMatch)
    {
        if (_profiles is null) return Say("Profiles are not available.");
        AppState saved;
        try
        {
            saved = _profiles.Load(id);
        }
        catch (StateLoadException ex)
        {
            return Say(ex.Message);
        }

        FlushHistory();
        CollectTabs();
        if (wholeMatch) ArchiveCurrent("profile");

        var snapshot = wholeMatch
            ? saved
            : State with
            {
                General = saved.General,
                Waiting = saved.Waiting,
                Standings = saved.Standings,
                Bracket = saved.Bracket,
            };
        RestoreSlice(snapshot);

        // The tournament address is only filled in; connecting stays the user's own action (and a running link is not touched).
        var notes = "";
        if (!Link.IsLinked) Link.TournamentUrl = saved.Link.TournamentUrl;
        else if (saved.Link.TournamentUrl.Length > 0 && !string.Equals(saved.Link.TournamentUrl, State.Link.TournamentUrl, StringComparison.OrdinalIgnoreCase))
            notes = " (the SOWDraft link was left as it is: unlink to change the tournament)";

        _profiles.SetActive(id);
        RefreshActiveProfile();
        Autosave();
        return Say($"Profile {ActiveProfileName} applied{(wholeMatch ? "" : " (settings only)")}, not published yet{notes}");
    }

    public string DeleteProfile(string id)
    {
        if (_profiles is null) return Say("Profiles are not available.");
        var name = _profiles.List().FirstOrDefault(p => p.Id == id)?.Name;
        if (name is null || !_profiles.Delete(id)) return Say("That profile is no longer there.");
        RefreshActiveProfile();
        return Say($"Profile {name} deleted");
    }
}

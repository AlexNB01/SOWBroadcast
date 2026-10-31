using SowBroadcast.App.Services;

namespace SowBroadcast.App.ViewModels;

/// <summary>What the producer chose in the "update available" window.</summary>
public enum UpdateChoice { Later, InstallNow }

/// <summary>The texts of the window that tells the producer a newer version is available.</summary>
public sealed class UpdatePromptViewModel
{
    public UpdatePromptViewModel(UpdateOffer offer, string currentVersion)
    {
        VersionLine = $"Version {offer.Version} is available (you have {currentVersion}).";
        SizeLine = offer.SizeBytes is { } bytes and > 0 ? $"Download: {bytes / (1024.0 * 1024.0):0} MB" : "";
        Notes = offer.Notes;
    }

    public string VersionLine { get; }

    public string SizeLine { get; }

    public bool HasSize => SizeLine.Length > 0;

    /// <summary>What changed, as the release describes it.</summary>
    public string Notes { get; }

    public bool HasNotes => Notes.Length > 0;

    public string Warning => "Installing closes the program and starts it again in the new version. Your match is saved first. Do not update during a broadcast.";

    /// <summary>
    /// Whether the window may open on its own at startup. Never while OBS is streaming or recording (the producer is on air),
    /// and only once per run of the program.
    /// </summary>
    public static bool ShouldOpenAtStartup(bool alreadyShown, bool onAir) => !alreadyShown && !onAir;
}

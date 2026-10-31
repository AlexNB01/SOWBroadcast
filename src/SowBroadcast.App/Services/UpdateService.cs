
namespace SowBroadcast.App.Services;

/// <summary>A newer version that can be installed.</summary>
/// <param name="Notes">The release description written for the version (plain text, may be empty).</param>
public sealed record UpdateOffer(string Version, long? SizeBytes, object Handle, string Notes = "");

/// <summary>What the program needs from an updater, behind an interface so the check can be tested without GitHub or an installation.</summary>
public interface IAppUpdater
{
    /// <summary>False when the program was not installed with its installer (a developer's folder, a copied folder): it cannot update itself.</summary>
    bool IsInstalled { get; }

    /// <summary>The newer version, or null when this one is the latest.</summary>
    Task<UpdateOffer?> CheckAsync(CancellationToken cancellation);

    Task DownloadAsync(UpdateOffer offer, Action<int> progress, CancellationToken cancellation);

    /// <summary>Starts the installer of the downloaded version; the caller then closes the program (the installer replaces it and starts it again).</summary>
    void ApplyAndRestart(UpdateOffer offer);
}

public enum UpdateCheckStatus { NotInstalled, UpToDate, Available, Failed }

/// <summary>The answer of a check: what it found and a sentence to show.</summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, string Message, UpdateOffer? Offer = null);

/// <summary>
/// Looks for a newer version of the program on GitHub (EHD-15). Only when asked: nothing is sent or fetched until the user chooses
/// <i>Check for updates</i> (or has switched on the check at startup).
/// </summary>
public sealed class UpdateService
{
    private readonly IAppUpdater _updater;
    private readonly string _currentVersion;

    public UpdateService(IAppUpdater updater, string currentVersion)
    {
        _updater = updater;
        _currentVersion = currentVersion;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellation = default)
    {
        if (!_updater.IsInstalled)
            return new UpdateCheckResult(UpdateCheckStatus.NotInstalled,
                "Updates work only in the installed program (installed with SOWBroadcast-Setup-<version>.exe). This copy was not installed with it.");
        try
        {
            var offer = await _updater.CheckAsync(cancellation).ConfigureAwait(false);
            return offer is null
                ? new UpdateCheckResult(UpdateCheckStatus.UpToDate, $"SOW Broadcast {_currentVersion} is the latest version.")
                : new UpdateCheckResult(UpdateCheckStatus.Available, $"Version {offer.Version} is available (you have {_currentVersion}).{Size(offer)}", offer);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, "Could not check for updates: " + Reason(ex));
        }
    }

    /// <summary>Downloads the offered version and restarts the program in it. Returns an error text when it did not work, else null (the program is closing).</summary>
    public async Task<string?> InstallAsync(UpdateOffer offer, Action<int> progress, CancellationToken cancellation = default)
    {
        try
        {
            await _updater.DownloadAsync(offer, progress, cancellation).ConfigureAwait(false);
            _updater.ApplyAndRestart(offer);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return "The update could not be installed: " + Reason(ex);
        }
    }

    private static string Size(UpdateOffer offer) => offer.SizeBytes is { } bytes and > 0 ? $" Download: {bytes / (1024.0 * 1024.0):0} MB." : "";

    // The first line of the problem: a network error, a missing release. No stack traces for the operator.
    private static string Reason(Exception ex) => (ex.GetBaseException().Message.Split('\n')[0]).Trim();
}

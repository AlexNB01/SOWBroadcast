namespace SowBroadcast.Core.Storage;

/// <summary>
/// Where the app keeps things. <c>SOWB_ROOT</c> (as in the legacy app) puts everything in one folder;
/// otherwise generated data lives per user and the shipped overlay files live next to the program.
/// </summary>
public static class AppPaths
{
    public const string RootEnvironmentVariable = "SOWB_ROOT";

    private static string? Override
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
        }
    }

    /// <summary>Autosave, generated <c>Scoreboard/</c> files and user content (highlights, videos).</summary>
    public static string DataRoot =>
        Override ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SOWBroadcast");

    /// <summary>Shipped, read-only files: <c>HTML/</c> overlays and the bundled <c>Scoreboard/</c> images.</summary>
    public static string ContentRoot => Override ?? FindContentRoot(AppContext.BaseDirectory);

    public static string AutosavePath => Path.Combine(DataRoot, "autosave.json");

    /// <summary>Hotkeys and the token of the local control interface (belongs to the computer, not to a match).</summary>
    public static string ControlSettingsPath => Path.Combine(DataRoot, "control.json");

    /// <summary>The generated overlay data tree the overlays read.</summary>
    public static string ScoreboardDir => Path.Combine(DataRoot, "Scoreboard");

    public static string DefaultHighlightsDir => Path.Combine(DataRoot, "Highlights");

    /// <summary>Installed layout: next to the program. Development: the repository root, found by walking up.</summary>
    public static string FindContentRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        for (var i = 0; dir is not null && i < 10; i++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "HTML")) && Directory.Exists(Path.Combine(dir.FullName, "Scoreboard")))
                return dir.FullName;
        return start;
    }
}

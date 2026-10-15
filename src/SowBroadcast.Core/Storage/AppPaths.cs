namespace SowBroadcast.Core.Storage;

/// <summary>Where the app keeps its data. <c>SOWB_ROOT</c> overrides the default per-user folder.</summary>
public static class AppPaths
{
    public const string RootEnvironmentVariable = "SOWB_ROOT";

    public static string DataRoot
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overridden)) return overridden.Trim().Trim('"');
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SOWBroadcast");
        }
    }

    public static string AutosavePath => Path.Combine(DataRoot, "autosave.json");
}

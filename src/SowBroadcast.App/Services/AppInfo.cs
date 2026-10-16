using System.Reflection;

namespace SowBroadcast.App.Services;

/// <summary>What the program says about itself. The version lives in one place: <c>&lt;Version&gt;</c> in the project file.</summary>
public static class AppInfo
{
    public const string Name = "SOW Broadcast";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "3.0.0";
}

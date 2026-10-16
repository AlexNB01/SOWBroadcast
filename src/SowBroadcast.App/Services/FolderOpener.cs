using System.Diagnostics;

namespace SowBroadcast.App.Services;

/// <summary>Shows a folder in the file explorer (created first when it does not exist yet).</summary>
public static class FolderOpener
{
    public static void Open(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Serilog.Log.Warning(ex, "Could not open {Folder}", folder);
        }
    }
}

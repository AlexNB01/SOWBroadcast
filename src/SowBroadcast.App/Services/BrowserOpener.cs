using System.Diagnostics;

namespace SowBroadcast.App.Services;

/// <summary>Opens an address in the default browser (the overlay preview; the overlays themselves are only ever seen in OBS or here).</summary>
public static class BrowserOpener
{
    public static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Serilog.Log.Warning(ex, "Could not open {Url}", url);
        }
    }
}

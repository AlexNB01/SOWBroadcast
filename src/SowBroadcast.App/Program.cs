using Avalonia;
using SowBroadcast.App.Services;

namespace SowBroadcast.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // First of all: the installer and updater start the program with special arguments (install, update, uninstall hooks).
        Velopack.VelopackApp.Build().Run();

        AppLog.Init();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            AppLog.Shutdown();
        }
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Popups (combo lists, menus) are drawn inside the window instead of as separate native windows:
            // they theme and scale with the rest of the UI and can be captured in screenshots.
            .With(new Win32PlatformOptions { OverlayPopups = true })
            .LogToTrace();
}

using Avalonia;

namespace SowBroadcast.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Popups (combo lists, menus) are drawn inside the window instead of as separate native windows:
            // they theme and scale with the rest of the UI and can be captured in screenshots.
            .With(new Win32PlatformOptions { OverlayPopups = true })
            .LogToTrace();
}

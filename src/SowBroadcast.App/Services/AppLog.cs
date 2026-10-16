using SowBroadcast.Core.Storage;
using Serilog;

namespace SowBroadcast.App.Services;

/// <summary>
/// The program's log file: <c>Logs/sowbroadcast-YYYYMMDD.log</c> in the data folder, one file per day, the last 14 kept.
/// It holds what happened (start, server, link, errors), never match data such as player names.
/// </summary>
public static class AppLog
{
    public static string Folder => Path.Combine(AppPaths.DataRoot, "Logs");

    public static void Init()
    {
        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(Path.Combine(Folder, "sowbroadcast-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
                    shared: true, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Logger = Serilog.Core.Logger.None; // the program works without a log
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
            // A report the next start shows: the version, the system and the error.
            var report = SowBroadcast.Core.Diagnostics.CrashReports.Write(Folder, e.ExceptionObject as Exception, AppInfo.Version,
                Environment.OSVersion.ToString(), DateTime.Now, e.IsTerminating);
            if (report is not null) Log.Fatal("Crash report written: {Report}", report);
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        Log.Information("{Name} {Version} started on {Os}; data {Data}; content {Content}",
            AppInfo.Name, AppInfo.Version, Environment.OSVersion, AppPaths.DataRoot, AppPaths.ContentRoot);
    }

    public static void Shutdown()
    {
        Log.Information("{Name} closed", AppInfo.Name);
        Log.CloseAndFlush();
    }
}

using System.Text;

namespace SowBroadcast.Core.Diagnostics;

/// <summary>
/// A report for the case the program ends in an unhandled error (EHD-15): a text file with the version, the system and the error, saved
/// next to the log, and a note that the next start shows it. It holds what went wrong, never match data.
/// </summary>
public static class CrashReports
{
    private const string PendingFile = "crash-pending.txt";

    /// <summary>Writes the report and the note for the next start. Returns the report's path, or null when it could not be written.</summary>
    public static string? Write(string folder, Exception? error, string version, string system, DateTime now, bool terminating)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"crash-{now:yyyyMMdd-HHmmss}.txt");
            var text = new StringBuilder()
                .AppendLine("SOW Broadcast crash report")
                .AppendLine($"Version: {version}")
                .AppendLine($"System: {system}")
                .AppendLine($"Time: {now:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"Terminating: {terminating}")
                .AppendLine()
                .AppendLine(error?.ToString() ?? "(no exception object)")
                .ToString();
            File.WriteAllText(path, text, new UTF8Encoding(false));
            if (terminating) File.WriteAllText(Path.Combine(folder, PendingFile), Path.GetFileName(path), new UTF8Encoding(false));
            Trim(folder);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;   // the program was ending anyway
        }
    }

    /// <summary>The report of the last run when that run ended in a crash (the note is removed, so it is shown once); else null.</summary>
    public static string? TakePending(string folder)
    {
        var note = Path.Combine(folder, PendingFile);
        try
        {
            if (!File.Exists(note)) return null;
            var name = File.ReadAllText(note).Trim();
            File.Delete(note);
            var path = Path.Combine(folder, name);
            return name.Length > 0 && File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Reports are small, but a program that crashes at every start must not fill the disk: the newest ten stay.
    private static void Trim(string folder)
    {
        foreach (var old in Directory.GetFiles(folder, "crash-2*.txt").OrderByDescending(f => f, StringComparer.Ordinal).Skip(10))
        {
            try { File.Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best effort */ }
        }
    }
}

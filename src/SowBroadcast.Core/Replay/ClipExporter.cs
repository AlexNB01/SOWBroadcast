using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace SowBroadcast.Core.Replay;

/// <summary>The outcome of cutting a clip: <see cref="Error"/> is one short line of the reason when it did not work.</summary>
public sealed record ClipExportResult(bool Ok, string Error = "");

/// <summary>Writes the shown part of a clip (its trim and speed) as a new video file.</summary>
public interface IClipExporter
{
    /// <summary>The ffmpeg program the cutting uses, or null when it is not installed.</summary>
    string? FindTool();

    Task<ClipExportResult> ExportAsync(string input, string output, long startMs, long endMs, double speed, CancellationToken cancel = default);
}

/// <summary>
/// Cuts clips with ffmpeg: re-encoded (H.264 and AAC), so the cut is exact to the frame and the speed can change. ffmpeg is not shipped
/// with the program; it is found on the PATH (or next to the program).
/// </summary>
public sealed class FfmpegClipExporter : IClipExporter
{
    private readonly string? _explicitTool;
    private readonly IReadOnlyList<string> _folders;
    private readonly TimeSpan _timeout;

    public FfmpegClipExporter(string? tool = null, IEnumerable<string>? searchFolders = null, TimeSpan? timeout = null)
    {
        _explicitTool = tool;
        _folders = (searchFolders ?? DefaultFolders()).ToArray();
        _timeout = timeout ?? TimeSpan.FromMinutes(15);
    }

    private static IEnumerable<string> DefaultFolders()
    {
        yield return AppContext.BaseDirectory;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return folder.Trim().Trim('"');
    }

    public string? FindTool()
    {
        if (_explicitTool is not null) return File.Exists(_explicitTool) ? _explicitTool : null;
        foreach (var folder in _folders)
        {
            try
            {
                var candidate = Path.Combine(folder, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { /* a folder name that is not a path */ }
        }
        return null;
    }

    /// <summary>The ffmpeg arguments that write the part from <paramref name="startMs"/> to <paramref name="endMs"/> (0 = to the end) at <paramref name="speed"/>.</summary>
    public static IReadOnlyList<string> BuildArguments(string input, string output, long startMs, long endMs, double speed)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y" };
        // The cut is placed before the input: ffmpeg decodes from the keyframe before it and drops what comes before the start, so it is exact.
        if (startMs > 0) { args.Add("-ss"); args.Add(Seconds(startMs)); }
        if (endMs > startMs) { args.Add("-t"); args.Add(Seconds(endMs - startMs)); }
        args.Add("-i"); args.Add(input);
        args.AddRange(new[] { "-map", "0:v:0", "-map", "0:a:0?" });

        var slow = Math.Abs(speed - 1) > 0.001;
        if (slow)
        {
            args.Add("-vf"); args.Add("setpts=PTS/" + Number(speed));
            args.Add("-af"); args.Add(AudioTempo(speed));
        }

        args.AddRange(new[]
        {
            "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart",
            "-f", "mp4", output,
        });
        return args;
    }

    // atempo only takes 0.5 to 2, so a stronger change is several in a row (0.1x = 0.5 * 0.5 * 0.5 * 0.8).
    private static string AudioTempo(double speed)
    {
        var stages = new List<string>();
        var left = speed;
        while (left < 0.5) { stages.Add("atempo=0.5"); left /= 0.5; }
        while (left > 2) { stages.Add("atempo=2"); left /= 2; }
        stages.Add("atempo=" + Number(left));
        return string.Join(",", stages);
    }

    private static string Seconds(long ms) => (ms / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public async Task<ClipExportResult> ExportAsync(string input, string output, long startMs, long endMs, double speed, CancellationToken cancel = default)
    {
        var tool = FindTool();
        if (tool is null) return new ClipExportResult(false, "ffmpeg was not found.");

        var info = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in BuildArguments(input, output, startMs, endMs, Math.Clamp(speed, 0.1, 4))) info.ArgumentList.Add(argument);

        try
        {
            using var process = new Process { StartInfo = info };
            process.Start();
            var errors = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();   // nothing is expected; read so the pipe never fills

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(_timeout);
            try { await process.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { /* ended on its own */ }
                Discard(output);
                return new ClipExportResult(false, cancel.IsCancellationRequested ? "Cancelled." : "ffmpeg took too long.");
            }

            var text = await errors.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                Discard(output);
                return new ClipExportResult(false, LastLine(text) is { Length: > 0 } line ? line : $"ffmpeg stopped with code {process.ExitCode}.");
            }
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
            {
                Discard(output);
                return new ClipExportResult(false, "ffmpeg wrote no video (is the start after the end of the clip?).");
            }
            return new ClipExportResult(true);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            Discard(output);
            return new ClipExportResult(false, ex.Message.Split('\n', 2)[0].Trim());
        }
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";

    private static void Discard(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* left for the next clean-up */ }
    }
}

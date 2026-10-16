using System.Diagnostics;

namespace SowBroadcast.Tests;

/// <summary>tools/obs/make-obs-scenes.py: the generated OBS collection points at pages that exist and carries nothing from one machine. Needs Python; skipped without it.</summary>
public sealed class ObsSceneGeneratorTests
{
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tools", "obs", "make-obs-scenes.py"))) dir = dir.Parent;
        return dir?.FullName;
    }

    private static (int Exit, string Output)? RunPython(params string[] args)
    {
        var info = new ProcessStartInfo("python") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // no Python here
        }
    }

    [Fact]
    public void The_generated_collection_uses_only_pages_that_exist_and_nothing_machine_specific()
    {
        var repo = FindRepoRoot();
        if (repo is null) return;

        var result = RunPython(Path.Combine(repo, "tools", "obs", "make-obs-scenes.py"), "--check-only");

        if (result is null) return;
        Assert.True(result.Value.Exit == 0, result.Value.Output);
        Assert.Contains("18 scenes", result.Value.Output);
    }
}

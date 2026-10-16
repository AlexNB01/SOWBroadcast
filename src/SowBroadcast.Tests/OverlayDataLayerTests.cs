using System.Diagnostics;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Tests;

/// <summary>HTML/sow-data.js (what every overlay reads through) against the files the program used to write. Needs Node.js; skipped without it.</summary>
public sealed class OverlayDataLayerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sowb-data-" + Guid.NewGuid().ToString("N"));

    public OverlayDataLayerTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HTML", "sow-data.js"))) dir = dir.Parent;
        return dir?.FullName;
    }

    private static (int Exit, string Output)? RunNode(params string[] args)
    {
        var info = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
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
            return null; // no Node here
        }
    }

    [Theory]
    [InlineData("default")]
    [InlineData("full")]
    public void The_data_layer_gives_the_overlays_exactly_what_the_files_had(string name)
    {
        var repo = FindRepoRoot();
        if (repo is null) return;

        var golden = Path.Combine(AppContext.BaseDirectory, "Golden", name);
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var stateText = File.ReadAllText(Path.Combine(golden, "state.json")).Replace("{FIXTURES}", fixtures.Replace(Path.DirectorySeparatorChar, '/'));
        var state = StateStore.Deserialize(stateText);
        var exporter = new ScoreboardExporter(Path.Combine(_tmp, "Scoreboard"), new SkiaLogoConverter());
        exporter.ExportAll(state); // syncs the waiting playlist, which is part of the state
        var apiFile = Path.Combine(_tmp, "state.json");
        File.WriteAllText(apiFile, OverlayStateJson.Build(state, exporter, 1, 1_760_000_000_000));

        if (Environment.GetEnvironmentVariable("SOWB_DUMP") is { Length: > 0 } dump) File.Copy(apiFile, Path.Combine(dump, name + ".json"), true);
        var result = RunNode(Path.Combine(repo, "tools", "overlay-data.test.mjs"), apiFile, Path.Combine(golden, "Scoreboard"), fixtures);

        if (result is null) return;
        Assert.True(result.Value.Exit == 0, result.Value.Output);
    }

    [Fact]
    public void The_countdown_is_counted_from_its_end_time_not_from_files()
    {
        var repo = FindRepoRoot();
        if (repo is null) return;
        var script = Path.Combine(_tmp, "timer.mjs");
        File.WriteAllText(script, """
            import { readFileSync } from 'node:fs';
            import vm from 'node:vm';
            const src = readFileSync(process.argv[2], 'utf8');
            const window = {};
            const now = Date.now();
            const state = { schema_version: 1, version: 1, server_time: now, match: { waiting: {} }, timer: { seconds: 300, running: true, ends_at: now + 42_500 } };
            const ctx = vm.createContext({ window, Promise, JSON, Date, Math, Object, String, Array, console,
                fetch: async () => ({ ok: true, json: async () => state, text: async () => '' }) });
            vm.runInContext(src, ctx);
            const out = [];
            out.push(await window.SOW.read('../Scoreboard/Waiting/timer_seconds.txt'));
            out.push(await window.SOW.read('../Scoreboard/Waiting/timer_running.txt'));
            console.log(out.join(','));
            """);

        var result = RunNode(script, Path.Combine(repo, "HTML", "sow-data.js"));

        if (result is null) return;
        Assert.Equal("43,1", result.Value.Output.Trim()); // whole seconds, rounded up
    }
}

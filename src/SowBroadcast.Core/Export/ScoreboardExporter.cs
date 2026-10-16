using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SowBroadcast.Core.Model;

namespace SowBroadcast.Core.Export;

/// <summary>
/// Writes the <c>Scoreboard/</c> tree the overlays read. File names and formats follow the legacy contract
/// (CLAUDE.md chapter 6); golden tests compare the output with what the Python app wrote.
/// </summary>
public sealed class ScoreboardExporter
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".webm", ".mkv" };

    private readonly string _root;
    private readonly ILogoConverter _logos;
    private readonly string? _defaultVideosDir;

    /// <param name="scoreboardRoot">The <c>Scoreboard</c> folder.</param>
    /// <param name="defaultVideosDir">Folder used for the waiting-screen videos when none is chosen.</param>
    public ScoreboardExporter(string scoreboardRoot, ILogoConverter logos, string? defaultVideosDir = null)
    {
        _root = Path.GetFullPath(scoreboardRoot);
        _logos = logos;
        _defaultVideosDir = defaultVideosDir;
    }

    public string Root => _root;

    /// <summary>Everything a full Update writes (assets are handled separately).</summary>
    public IReadOnlyList<string> ExportAll(AppState state)
    {
        var written = new List<string>();
        var derived = StateDerivations.Apply(state);
        ExportGeneral(derived, written);
        ExportMatch(derived, written);
        ExportWaiting(derived, written);
        ExportStandings(derived, written);
        ExportBracket(derived, written);
        ExportStatus(derived, written);
        return written;
    }

    // ---- Match -------------------------------------------------------------------------------------------

    public void ExportMatch(AppState state, List<string> written)
    {
        WriteTeam("T1", state.Team1, written);
        WriteTeam("T2", state.Team2, written);

        Write("Match/CurrentMap.txt", state.CurrentMap?.ToString(CultureInfo.InvariantCulture) ?? "", written);

        foreach (var map in state.Maps.Where(m => m.Index > 0))
        {
            var body =
                $"Name={SingleLine(map.Map).Trim()}\r\n" +
                $"T1={map.T1.ToString(CultureInfo.InvariantCulture)}\r\n" +
                $"T2={map.T2.ToString(CultureInfo.InvariantCulture)}\r\n" +
                $"Completed={(map.Completed ? 1 : 0)}\r\n" +
                $"Pick={map.Pick}\r\n" +
                $"T1Ban={map.T1Ban}\r\n" +
                $"T2Ban={map.T2Ban}\r\n";
            Write($"Match/Map{map.Index}.txt", body, written);
        }

        Write("Match/matchtext.txt", MatchText.Build(state), written);
        Write("Match/match.json", LegacyMatchJson.Build(state), written);
    }

    private void WriteTeam(string prefix, Team team, List<string> written)
    {
        Write($"Match/{prefix}Name.txt", team.Name, written);
        Write($"Match/{prefix}Score.txt", team.Score.ToString(CultureInfo.InvariantCulture), written);
        Write($"Match/{prefix}Color.txt", team.ColorHex, written);
        Write($"Match/{prefix}Abbr.txt", team.Abbr, written);

        var lines = team.Players.Select((p, i) =>
            $"{i + 1}\t{p.Name.Replace('\t', ' ')}\t{p.Hero.Replace('\t', ' ')}\t{p.Role.Replace('\t', ' ')}").ToList();
        Write($"Match/{prefix}Players.txt", string.Join("\r\n", lines) + (lines.Count > 0 ? "\r\n" : ""), written);

        for (var i = 0; i < Limits.PlayersPerTeam; i++)
        {
            var p = i < team.Players.Count ? team.Players[i] : new Player();
            Write($"Match/{prefix}P{i + 1}Name.txt", p.Name.Trim(), written);
            Write($"Match/{prefix}P{i + 1}Role.txt", p.Role.Trim(), written);
            Write($"Match/{prefix}P{i + 1}Hero.txt", p.Hero.Trim(), written);
        }

        WriteLogo(team.LogoPath, $"Match/{prefix}Logo.png", written);
    }

    // ---- General -----------------------------------------------------------------------------------------

    public void ExportGeneral(AppState state, List<string> written)
    {
        var g = state.General;
        Write("General/host.txt", g.Host, written);
        Write("General/caster1.txt", g.Caster1, written);
        Write("General/caster2.txt", g.Caster2, written);
        // maps.html reads first_to.txt as the number of maps to show, so it holds the series length.
        Write("General/first_to.txt", g.MapsCount.ToString(CultureInfo.InvariantCulture), written);

        var colors = new StringBuilder();
        foreach (var (key, value) in g.Colors) colors.Append(key).Append('=').Append(value).Append("\r\n");
        Write("General/colors.txt", colors.ToString(), written);

        WriteLogo(g.OverlayLogoPath, "General/OverlayLogo.png", written);
        WriteLogo(g.TransitionLogoPath, "General/TransitionLogo.png", written);
    }

    public void ExportStatus(AppState state, List<string> written) =>
        Write("Match/status.txt", state.General.StatusText.Trim(), written);

    // ---- Waiting -----------------------------------------------------------------------------------------

    public void ExportWaiting(AppState state, List<string> written)
    {
        var w = state.Waiting;
        Write("Waiting/text_starting.txt", Fallback(w.TextStarting, "STARTING SOON!"), written);
        Write("Waiting/text_brb.txt", Fallback(w.TextBrb, "BE RIGHT BACK!"), written);
        Write("Waiting/text_end.txt", Fallback(w.TextEnd, "THANK YOU FOR WATCHING"), written);
        ExportTimer(state, written);

        var useOverride = w.TickerOverrideEnabled && w.TickerOverride.Trim().Length > 0;
        Write("Waiting/ticker_override.txt", w.TickerOverride, written);
        Write("Waiting/ticker_use_override.txt", useOverride ? "1" : "0", written);
        Write("Waiting/socials.json", PythonStyleJson(w.Socials), written);

        var videos = SyncPlaylist(w.VideosDir);
        Write("Waiting/videos.txt", string.Join("\r\n", videos) + (videos.Count > 0 ? "\r\n" : ""), written);
        Write("Waiting/videos_dir.txt", "", written);
    }

    /// <summary>Only the countdown files (what the timer commands and the running clock write).</summary>
    public void ExportTimer(AppState state, List<string> written)
    {
        Write("Waiting/timer_seconds.txt", state.Waiting.TimerSeconds.ToString(CultureInfo.InvariantCulture), written);
        Write("Waiting/timer_running.txt", state.Waiting.TimerRunning ? "1" : "0", written);
    }

    /// <summary>Mirrors the chosen video folder into <c>Waiting/Playlist</c> and returns the file names.</summary>
    private List<string> SyncPlaylist(string videosDir)
    {
        var playlist = Path.Combine(_root, "Waiting", "Playlist");
        Directory.CreateDirectory(playlist);

        var source = string.IsNullOrWhiteSpace(videosDir) ? _defaultVideosDir : videosDir.Trim();
        var names = new List<string>();
        if (!string.IsNullOrEmpty(source) && Directory.Exists(source))
        {
            foreach (var file in Directory.GetFiles(source).OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
            {
                var name = Path.GetFileName(file);
                if (!VideoExtensions.Contains(Path.GetExtension(name))) continue;
                names.Add(name);

                var target = Path.Combine(playlist, name);
                try
                {
                    if (!File.Exists(target) || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(target))
                        File.Copy(file, target, overwrite: true);
                }
                catch (IOException) { /* a video being copied by OBS; retry on the next Update */ }
            }
        }

        foreach (var existing in Directory.GetFileSystemEntries(playlist))
        {
            if (names.Contains(Path.GetFileName(existing), StringComparer.Ordinal)) continue;
            try { if (File.Exists(existing)) File.Delete(existing); }
            catch (IOException) { /* in use; removed on a later Update */ }
        }

        return names;
    }

    // ---- Standings ---------------------------------------------------------------------------------------

    public void ExportStandings(AppState state, List<string> written)
    {
        var s = state.Standings;
        var groups = s.Groups.Count > 0
            ? s.Groups
            : new[] { new StandingsGroup { Key = "group_1", Name = "Group 1", Rows = s.Rows } };
        groups = groups.Select(g => g with { Rows = ApplyRanks(g.Rows) }).ToArray();

        var displayGroup = string.IsNullOrEmpty(s.DisplayGroup) ? "__all__" : s.DisplayGroup;
        IReadOnlyList<StandingsRow> selected = Array.Empty<StandingsRow>();
        if (displayGroup != "__all__")
            selected = groups.FirstOrDefault(g => g.Key == displayGroup)?.Rows ?? Array.Empty<StandingsRow>();
        else if (groups.Count == 1)
            selected = groups[0].Rows;

        var payload = new JsonObject
        {
            ["title"] = s.Title,
            ["subtitle"] = s.Subtitle,
            ["columns"] = new JsonObject { ["mode"] = string.IsNullOrEmpty(s.Columns.Mode) ? "map_diff" : s.Columns.Mode },
            ["rows"] = RowsJson(selected),
            ["groups"] = new JsonArray(groups.Select(g => (JsonNode)new JsonObject
            {
                ["key"] = g.Key,
                ["name"] = g.Name,
                ["rows"] = RowsJson(g.Rows),
            }).ToArray()),
            ["display_group"] = displayGroup,
        };
        WriteJsonWithStamp("Standings/standings.json", "Standings/updated_at.txt", payload, written);
    }

    private JsonArray RowsJson(IReadOnlyList<StandingsRow> rows) =>
        new(rows.Select(r =>
        {
            var row = new JsonObject
            {
                ["team"] = r.TeamName,
                ["abbr"] = r.Abbr,
                ["logo"] = NormalizeLogoPath(r.LogoPath),
                ["wins"] = r.Wins,
                ["losses"] = r.Losses,
                ["map_diff"] = r.MapDiff,
                ["points"] = r.Points,
                ["status"] = r.Status,
                ["rank"] = r.Rank,
            };
            // Extended stats exist only for live-linked league rows; standings.html uses them to pick its layout.
            if (r.Played is not null) row["played"] = r.Played;
            if (r.Draws is not null) row["draws"] = r.Draws;
            if (r.Sf is not null) row["sf"] = r.Sf;
            if (r.Sa is not null) row["sa"] = r.Sa;
            return (JsonNode)row;
        }).ToArray());

    /// <summary>Rows without a rank get the unused ranks in points / map-diff / name order.</summary>
    public static IReadOnlyList<StandingsRow> ApplyRanks(IReadOnlyList<StandingsRow> rows)
    {
        var used = rows.Where(r => r.Rank > 0).Select(r => r.Rank).ToHashSet();
        var result = rows.ToList();
        var unranked = result.Select((row, index) => (row, index)).Where(x => x.row.Rank <= 0)
            .OrderByDescending(x => x.row.Points)
            .ThenByDescending(x => x.row.MapDiff)
            .ThenBy(x => x.row.TeamName.ToLowerInvariant(), StringComparer.Ordinal)
            .ToList();

        var next = 1;
        foreach (var (row, index) in unranked)
        {
            while (used.Contains(next)) next++;
            result[index] = row with { Rank = next };
            used.Add(next);
            next++;
        }
        return result;
    }

    // ---- Bracket -----------------------------------------------------------------------------------------

    public void ExportBracket(AppState state, List<string> written)
    {
        var b = state.Bracket;
        var payload = new JsonObject
        {
            ["title"] = b.Title,
            ["stage"] = b.Stage,
            ["rounds"] = new JsonArray(b.Rounds.Select(r => (JsonNode)new JsonObject
            {
                ["name"] = r.Name,
                ["side"] = r.Side,
                ["matches"] = new JsonArray(r.Matches.Select(m => (JsonNode)new JsonObject
                {
                    ["id"] = m.Id,
                    ["bo_label"] = m.BoLabel,
                    ["team1"] = TeamRefJson(m.Team1),
                    ["team2"] = TeamRefJson(m.Team2),
                    ["score1"] = m.Score1,
                    ["score2"] = m.Score2,
                    ["status"] = m.Status,
                    ["scheduled_at"] = m.ScheduledAt,
                }).ToArray()),
            }).ToArray()),
            ["double_elim_view"] = b.DoubleElimView,
        };
        WriteJsonWithStamp("Bracket/bracket.json", "Bracket/updated_at.txt", payload, written);
    }

    private JsonObject TeamRefJson(TeamRef team) => new()
    {
        ["name"] = team.Name,
        ["abbr"] = team.Abbr,
        ["logo"] = NormalizeLogoPath(team.LogoPath),
    };

    // ---- Helpers -----------------------------------------------------------------------------------------

    /// <summary>Paths inside the Scoreboard folder become relative; everything else is absolute. Always forward slashes.</summary>
    public string? NormalizeLogoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Trim();
        if (!Path.IsPathRooted(p)) return p.Replace('\\', '/');
        try
        {
            var relative = Path.GetRelativePath(_root, p);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                return relative.Replace('\\', '/');
        }
        catch (ArgumentException) { /* fall through */ }
        return p.Replace('\\', '/');
    }

    private void WriteJsonWithStamp(string relativePath, string stampPath, JsonObject payload, List<string> written)
    {
        var json = FileWriter.Crlf(payload.ToJsonString(PayloadOptions));
        if (Write(relativePath, json, written))
            Write(stampPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), written);
    }

    private void WriteLogo(string? source, string relativePath, List<string> written)
    {
        var png = _logos.ToPng(source);
        var target = Path.Combine(_root, relativePath);
        if (png is null)
        {
            if (FileWriter.DeleteIfExists(target)) written.Add(relativePath);
            return;
        }
        if (FileWriter.WriteBytesIfChanged(target, png)) written.Add(relativePath);
    }

    private bool Write(string relativePath, string content, List<string> written)
    {
        var changed = FileWriter.WriteTextIfChanged(Path.Combine(_root, relativePath), content);
        if (changed) written.Add(relativePath);
        return changed;
    }

    private static string Fallback(string value, string fallback) => string.IsNullOrEmpty(value) ? fallback : value;

    private static string SingleLine(string value) => value.Replace('\n', ' ');

    /// <summary>One-line JSON object with ", " and ": " separators, like Python's json.dump (byte parity with the legacy file).</summary>
    private static string PythonStyleJson(IReadOnlyDictionary<string, string> values) =>
        "{" + string.Join(", ", values.Select(kv => $"{Quote(kv.Key)}: {Quote(kv.Value)}")) + "}";

    private static string Quote(string value) => JsonSerializer.Serialize(value, PayloadOptions);
}

using System.Text.Json;
using SowBroadcast.Core.Assets;
using SowBroadcast.Core.Export;
using SowBroadcast.Core.Model;
using SowBroadcast.Core.Storage;

namespace SowBroadcast.Core.Match;

/// <summary>
/// The <c>*.sowteam.json</c> file of the Teams menu: <c>{version, name, abbr, color_hex, logo_png, players[8]}</c> with the logo
/// as <c>&lt;slug&gt;.png</c> next to it. Score and banned hero are never part of it.
/// </summary>
public static class TeamFile
{
    private sealed record Data
    {
        public int Version { get; init; } = 1;
        public string Name { get; init; } = "";
        public string Abbr { get; init; } = "";
        public string? ColorHex { get; init; }
        public string? LogoPng { get; init; }
        public IReadOnlyList<Player> Players { get; init; } = Array.Empty<Player>();
    }

    /// <summary>The file name the Export dialog suggests.</summary>
    public static string SuggestedFileName(Team team) => Slug.Make(string.IsNullOrWhiteSpace(team.Name) ? "team" : team.Name) + ".sowteam.json";

    /// <summary>Writes the team file (and its logo as PNG). Returns a warning when the logo could not be written, else null.</summary>
    public static string? Export(Team team, string jsonPath, ILogoConverter logos)
    {
        if (!jsonPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) jsonPath += ".sowteam.json";
        var dir = Path.GetDirectoryName(Path.GetFullPath(jsonPath))!;
        Directory.CreateDirectory(dir);

        var logoName = Slug.Make(string.IsNullOrWhiteSpace(team.Name) ? "team" : team.Name) + ".png";
        var hasLogo = !string.IsNullOrEmpty(team.LogoPath);
        var data = new Data
        {
            Name = team.Name,
            Abbr = team.Abbr,
            ColorHex = team.ColorHex,
            LogoPng = hasLogo ? logoName : null,
            Players = team.Players.Take(Limits.PlayersPerTeam).ToArray(),
        };
        FileWriter.WriteTextIfChanged(jsonPath, JsonSerializer.Serialize(data, StateStore.Options));

        if (!hasLogo) return null;
        var png = logos.ToPng(team.LogoPath);
        if (png is null) return $"The logo {team.LogoPath} could not be read, so it was not exported.";
        FileWriter.WriteBytesIfChanged(Path.Combine(dir, logoName), png);
        return null;
    }

    /// <summary>
    /// Reads a team file. The current score is kept and the banned hero cleared; a missing colour falls back to
    /// <paramref name="defaultColour"/>; the logo is looked for as <c>logo_png</c>, then <c>&lt;file&gt;.png</c>, then <c>Logo.png</c>.
    /// </summary>
    /// <exception cref="StateLoadException">The file cannot be read or is not a team file.</exception>
    public static Team Import(string jsonPath, int keepScore, string defaultColour)
    {
        Data? data;
        try
        {
            data = JsonSerializer.Deserialize<Data>(File.ReadAllText(jsonPath, System.Text.Encoding.UTF8), StateStore.Options);
        }
        catch (JsonException ex)
        {
            throw new StateLoadException("The file is not a valid team file.", ex);
        }
        catch (IOException ex)
        {
            throw new StateLoadException($"Could not read {jsonPath}.", ex);
        }
        if (data is null) throw new StateLoadException("The file is empty.");

        var players = data.Players.Take(Limits.PlayersPerTeam).ToList();
        while (players.Count < Limits.PlayersPerTeam) players.Add(new Player());

        return new Team
        {
            Name = data.Name,
            Abbr = data.Abbr,
            LogoPath = FindLogo(jsonPath, data.LogoPng),
            Score = keepScore,
            ColorHex = string.IsNullOrWhiteSpace(data.ColorHex) ? defaultColour : data.ColorHex,
            Players = players,
            BannedHero = "",
        };
    }

    private static string? FindLogo(string jsonPath, string? logoName)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(jsonPath))!;
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(logoName)) candidates.Add(Path.Combine(dir, logoName));
        candidates.Add(Path.Combine(dir, Path.GetFileNameWithoutExtension(jsonPath) + ".png"));
        candidates.Add(Path.Combine(dir, "Logo.png"));
        return candidates.FirstOrDefault(File.Exists);
    }
}

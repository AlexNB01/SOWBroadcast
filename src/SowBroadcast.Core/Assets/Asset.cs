using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SowBroadcast.Core.Assets;

public enum AssetCategory
{
    Heroes,
    Maps,
    /// <summary>Game modes (Control, Escort...). Their folder is <c>Gametypes</c>.</summary>
    Modes,
}

public static class AssetCategories
{
    public static readonly IReadOnlyList<AssetCategory> All = new[] { AssetCategory.Heroes, AssetCategory.Maps, AssetCategory.Modes };

    /// <summary>Folder under <c>Scoreboard/</c>.</summary>
    public static string Folder(this AssetCategory category) => category switch
    {
        AssetCategory.Heroes => "Heroes",
        AssetCategory.Maps => "Maps",
        _ => "Gametypes",
    };

    /// <summary>Array name in <c>index.json</c>, and the key overlays use in change events (<c>assets.heroes</c>).</summary>
    public static string Key(this AssetCategory category) => category switch
    {
        AssetCategory.Heroes => "heroes",
        AssetCategory.Maps => "maps",
        _ => "modes",
    };

    public static string Title(this AssetCategory category) => category switch
    {
        AssetCategory.Heroes => "Heroes",
        AssetCategory.Maps => "Maps",
        _ => "Game Modes",
    };
}

/// <summary>A hero, map or game mode. Image paths are relative to <c>Scoreboard/</c>, with forward slashes.</summary>
public sealed record Asset
{
    public string Name { get; init; } = "";
    /// <summary>Where overlays find the image, e.g. <c>Heroes/ana.png</c>.</summary>
    public string? ImagePath { get; init; }
    /// <summary>Maps only: Control, Escort, Hybrid, Push or Flashpoint.</summary>
    public string? Mode { get; init; }
    /// <summary>Absolute file the image was taken from (copied to <see cref="ImagePath"/> on export).</summary>
    public string? SourcePath { get; init; }
    /// <summary>Heroes only: full-body image, e.g. <c>HeroesBody/Ana.png</c>.</summary>
    public string? BodyImagePath { get; init; }
    public string? BodySourcePath { get; init; }
    /// <summary>Heroes only: Tank, Damage or Support.</summary>
    public string? Role { get; init; }
}

public static class Slug
{
    private static readonly Regex NonAlphanumeric = new("[^a-z0-9]+", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> HeroBodyOverrides = new(StringComparer.Ordinal)
    {
        ["d.va"] = "Dva", ["dva"] = "Dva",
        ["lúcio"] = "Lucio", ["lucio"] = "Lucio",
        ["soldier: 76"] = "Soldier_76", ["soldier 76"] = "Soldier_76", ["soldier:76"] = "Soldier_76",
        ["wrecking ball"] = "Wrecking_Ball",
        ["junker queen"] = "Junker_Queen",
        ["jetpack cat"] = "Jetpack_Cat",
        ["torbjörn"] = "Torbjörn", ["torbjorn"] = "Torbjörn",
    };

    /// <summary>
    /// File-name slug: lowercase, accents removed, anything else becomes "-". Names with no Latin letters or digits
    /// (which would all collapse to the same slug) get a short stable hash instead.
    /// </summary>
    public static string Make(string? name)
    {
        var normalized = (name ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormKD);
        var stripped = new StringBuilder();
        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) stripped.Append(ch);

        var slug = NonAlphanumeric.Replace(stripped.ToString(), "-").Trim('-');
        if (slug.Length > 0) return slug;

        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0) return "item";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(trimmed)))[..6].ToLowerInvariant();
        return "item-" + hash;
    }

    /// <summary>
    /// File name (without extension) of a hero's full-body image in <c>HeroesBody</c>. Must match <c>heroBodyFile()</c>
    /// in <c>MapDraft.html</c>.
    /// </summary>
    public static string HeroBodyFile(string? name)
    {
        var trimmed = (name ?? "").Trim();
        if (HeroBodyOverrides.TryGetValue(trimmed.ToLowerInvariant(), out var mapped)) return mapped;
        var words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Join("_", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        return joined.Length == 0 ? "Hero" : joined;
    }

    /// <summary>"d-va" / "king_s_row" -> "D Va" / "King S Row" (name guess for images found in a folder).</summary>
    public static string NameFromFileName(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var spaced = Regex.Replace(stem, "[-_]+", " ").Trim();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
    }
}

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
    /// <summary>Other spellings of the name (how SOWDraft or players write it); the match against them is exact, not fuzzy.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
}

public static class Slug
{
    private static readonly Regex NonAlphanumeric = new("[^a-z0-9]+", RegexOptions.Compiled);

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
    /// The file name (without extension) a NEW hero's full-body image gets in <c>HeroesBody</c>: words in Title Case joined with "_",
    /// accents removed and anything but letters, digits and "-" dropped ("Soldier: 76" becomes "Soldier_76"). Heroes that are already
    /// known keep the file their manifest entry names (<see cref="Asset.BodyImagePath"/>); there is no table of exceptions any more.
    /// </summary>
    public static string HeroBodyFile(string? name)
    {
        var folded = new StringBuilder();
        foreach (var ch in (name ?? "").Trim().Normalize(NormalizationForm.FormKD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) folded.Append(ch);

        var words = folded.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-').ToArray()))
            .Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        var joined = string.Join("_", words);
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

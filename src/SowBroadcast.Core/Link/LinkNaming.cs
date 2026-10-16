using System.Text;

namespace SowBroadcast.Core.Link;

/// <summary>The small naming rules of the link: roles, player names, abbreviations and map names (CLAUDE.md 8.4).</summary>
public static class LinkNaming
{
    private static readonly HashSet<string> TankRoles = new(StringComparer.OrdinalIgnoreCase) { "Tank" };
    private static readonly HashSet<string> DamageRoles = new(StringComparer.OrdinalIgnoreCase) { "Damage", "DPS", "Hitscan", "Flex DPS", "Projectile" };
    private static readonly HashSet<string> SupportRoles = new(StringComparer.OrdinalIgnoreCase) { "Support", "Main Support", "Flex Support" };

    /// <summary>
    /// The role shown for a player: Tank, Damage or Support from the main role; "Flex" when the main role and the secondary roles
    /// together cover all three categories (SOWDraft's own "plays all roles" rule) or the main role is Flex; otherwise empty.
    /// </summary>
    public static string RoleOf(string? role, IEnumerable<string>? secondaryRoles)
    {
        var all = new[] { role }.Concat(secondaryRoles ?? Array.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!.Trim()).ToArray();
        var covers = all.Any(TankRoles.Contains) && all.Any(DamageRoles.Contains) && all.Any(SupportRoles.Contains);
        if (covers || string.Equals(role?.Trim(), "Flex", StringComparison.OrdinalIgnoreCase)) return "Flex";
        var main = role?.Trim() ?? "";
        if (TankRoles.Contains(main)) return "Tank";
        if (DamageRoles.Contains(main)) return "Damage";
        if (SupportRoles.Contains(main)) return "Support";
        return "";
    }

    /// <summary>The name shown for a player: the battletag before "#" (at most 12 characters), else the name, else the Discord name.</summary>
    public static string PlayerName(LinkPlayer player)
    {
        string? pick = null;
        if (!string.IsNullOrWhiteSpace(player.Battletag))
        {
            var tag = player.Battletag.Trim();
            var hash = tag.IndexOf('#');
            var before = (hash >= 0 ? tag[..hash] : tag).Trim();
            if (before.Length > 0) pick = before;
        }
        pick ??= !string.IsNullOrWhiteSpace(player.Name) ? player.Name.Trim() : null;
        pick ??= !string.IsNullOrWhiteSpace(player.Discord) ? player.Discord.Trim() : "";
        return pick.Length > 12 ? pick[..12] : pick;
    }

    /// <summary>A suggested abbreviation: the first three letters or digits of the name, in capitals.</summary>
    public static string SuggestAbbreviation(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c)) continue;
            sb.Append(char.ToUpperInvariant(c));
            if (sb.Length == 3) break;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Finds a name in a plain list of names: exactly, else ignoring only letter case and extra spaces. Returns null when it is not
    /// there. Other spellings ("King's Row" for "King S Row") are the business of the aliases in the asset manifest
    /// (<c>AssetService.Resolve</c>); this fallback never guesses.
    /// </summary>
    public static string? ResolveName(string name, IEnumerable<string> catalogue)
    {
        var names = catalogue as IReadOnlyCollection<string> ?? catalogue.ToArray();
        var exact = names.FirstOrDefault(n => n == name);
        if (exact is not null) return exact;
        var key = Squeeze(name);
        return key.Length == 0 ? null : names.FirstOrDefault(n => string.Equals(Squeeze(n), key, StringComparison.OrdinalIgnoreCase));
    }

    private static string Squeeze(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

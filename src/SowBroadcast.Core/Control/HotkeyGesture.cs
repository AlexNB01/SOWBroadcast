using System.Diagnostics.CodeAnalysis;

namespace SowBroadcast.Core.Control;

/// <summary>A key plus modifiers, written like <c>Ctrl+Alt+F1</c>. Key names are the names of Avalonia's <c>Key</c> enum.</summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    // The numbers are Windows' MOD_ALT / MOD_CONTROL / MOD_SHIFT / MOD_WIN, so they go straight into RegisterHotKey.
    private static readonly Dictionary<string, int> VirtualKeys = BuildKeys();

    /// <summary>The Windows virtual-key code of the key.</summary>
    public int VirtualKey => VirtualKeys[Key];

    /// <summary>True when pressing it cannot be mistaken for typing: it has Ctrl, Alt or Win, or is a function / numpad key.</summary>
    public bool IsSafeAsGlobal =>
        (Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0 ||
        Key.StartsWith('F') && Key.Length > 1 && char.IsDigit(Key[1]) || Key.StartsWith("NumPad", StringComparison.Ordinal);

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & HotkeyModifiers.Ctrl) != 0) parts.Add("Ctrl");
        if ((Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & HotkeyModifiers.Win) != 0) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= HotkeyModifiers.Ctrl; break;
                case "alt": modifiers |= HotkeyModifiers.Alt; break;
                case "shift": modifiers |= HotkeyModifiers.Shift; break;
                case "win": case "windows": case "meta": modifiers |= HotkeyModifiers.Win; break;
                default:
                    if (key is not null) return false; // two keys
                    key = CanonicalKey(raw);
                    if (key is null) return false;
                    break;
            }
        }
        if (key is null) return false;
        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    /// <summary>The key's canonical name (case and a few aliases fixed), or null when it is not a key we know.</summary>
    public static string? CanonicalKey(string name)
    {
        if (name.Length == 1 && char.IsDigit(name[0])) name = "D" + name;
        foreach (var known in VirtualKeys.Keys)
            if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase)) return known;
        return name.ToLowerInvariant() switch
        {
            "pgup" => "PageUp",
            "pgdn" => "PageDown",
            "next" => "PageDown",
            "prior" => "PageUp",
            "plus" => "OemPlus",
            "minus" => "OemMinus",
            _ => null,
        };
    }

    /// <summary>Whether <paramref name="key"/> is a name this program can register.</summary>
    public static bool IsKnownKey([NotNullWhen(true)] string? key) => key is not null && VirtualKeys.ContainsKey(key);

    private static Dictionary<string, int> BuildKeys()
    {
        var keys = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var c = 'A'; c <= 'Z'; c++) keys[c.ToString()] = c;
        for (var i = 0; i <= 9; i++) keys["D" + i] = 0x30 + i;
        for (var i = 1; i <= 24; i++) keys["F" + i] = 0x6F + i;
        for (var i = 0; i <= 9; i++) keys["NumPad" + i] = 0x60 + i;
        keys["Multiply"] = 0x6A;
        keys["Add"] = 0x6B;
        keys["Subtract"] = 0x6D;
        keys["Decimal"] = 0x6E;
        keys["Divide"] = 0x6F;
        keys["Left"] = 0x25;
        keys["Up"] = 0x26;
        keys["Right"] = 0x27;
        keys["Down"] = 0x28;
        keys["PageUp"] = 0x21;
        keys["PageDown"] = 0x22;
        keys["End"] = 0x23;
        keys["Home"] = 0x24;
        keys["Insert"] = 0x2D;
        keys["Delete"] = 0x2E;
        keys["Space"] = 0x20;
        keys["OemPlus"] = 0xBB;
        keys["OemComma"] = 0xBC;
        keys["OemMinus"] = 0xBD;
        keys["OemPeriod"] = 0xBE;
        return keys;
    }
}

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

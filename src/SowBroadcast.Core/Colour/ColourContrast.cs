using System.Globalization;

namespace SowBroadcast.Core.Colour;

/// <summary>Colour arithmetic for the colour picker's advice: WCAG relative luminance and contrast ratio.</summary>
public static class ColourContrast
{
    /// <summary>Text on the team colour should reach this (WCAG's ratio for large text, which is what overlay names are).</summary>
    public const double ReadableRatio = 3.0;

    /// <summary>A colour closer than this to the background nearly disappears.</summary>
    public const double VisibleRatio = 1.5;

    /// <summary>Reads <c>#rrggbb</c> (the <c>#</c> optional). False for anything else.</summary>
    public static bool TryParse(string? hex, out (int R, int G, int B) colour)
    {
        colour = default;
        if (hex is null) return false;
        var text = hex.Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;
        colour = ((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
        return true;
    }

    /// <summary>WCAG relative luminance, 0 (black) to 1 (white).</summary>
    public static double Luminance((int R, int G, int B) c)
    {
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>Contrast ratio from 1 (same colour) to 21 (black on white); null when either colour is not a hex colour.</summary>
    public static double? Ratio(string? a, string? b)
    {
        if (!TryParse(a, out var first) || !TryParse(b, out var second)) return null;
        var la = Luminance(first);
        var lb = Luminance(second);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// Advice for a team colour against the overlay colours the producer chose: <paramref name="text"/> is the colour of most overlay
    /// text (the "secondary" colour), <paramref name="background"/> the plate behind text (the "primary" colour). Empty when all is well.
    /// </summary>
    public static string TeamColourAdvice(string? team, string? text, string? background)
    {
        var parts = new List<string>();
        if (Ratio(team, text) is { } onText && onText < ReadableRatio)
            parts.Add($"Overlay text ({text}) is hard to read on this colour ({onText:0.0}:1, 3:1 or more is easy to read).");
        if (Ratio(team, background) is { } onBackground && onBackground < VisibleRatio)
            parts.Add($"This colour almost disappears on the overlay background ({background}, {onBackground:0.0}:1).");
        return string.Join(' ', parts);
    }
}

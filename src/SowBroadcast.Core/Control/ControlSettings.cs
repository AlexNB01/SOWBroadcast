using System.Security.Cryptography;
using System.Text.Json;

namespace SowBroadcast.Core.Control;

/// <summary>
/// How this computer is set up for the local control interface: the secret Stream Deck / Companion send, whether the hotkeys work
/// while another program has the focus, and which gesture triggers which command. Kept in <c>control.json</c> in the data folder,
/// not in the match: it belongs to the machine, and a loaded match file must not change it.
/// </summary>
public sealed class ControlSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Sent by the caller as <c>X-SOWB-Token</c> (or <c>?token=</c>). Stable between runs so a Stream Deck profile keeps working.</summary>
    public string Token { get; set; } = NewToken();

    /// <summary>Hotkeys work in every program (Windows RegisterHotKey), not only while this window has the focus.</summary>
    public bool GlobalHotkeys { get; set; } = true;

    /// <summary>The optional automations (scores from maps, finished maps, bracket advancement).</summary>
    public AutomationSettings Automation { get; set; } = new();

    /// <summary>Command id to gesture text; an empty text means "no hotkey". Commands that are not listed use their default.</summary>
    public Dictionary<string, string> Bindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>The gesture text of <paramref name="id"/>: the user's, the default, or null for none.</summary>
    public string? GestureFor(string id)
    {
        if (Bindings.TryGetValue(id, out var text)) return string.IsNullOrWhiteSpace(text) ? null : text;
        return ControlCatalog.Find(id)?.DefaultGesture;
    }

    /// <summary>Every command with a valid gesture, by gesture. A gesture two commands share goes to the first one in catalogue order.</summary>
    public IReadOnlyList<(string Id, HotkeyGesture Gesture)> ActiveBindings()
    {
        var seen = new HashSet<HotkeyGesture>();
        var result = new List<(string, HotkeyGesture)>();
        foreach (var command in ControlCatalog.All)
        {
            if (HotkeyGesture.TryParse(GestureFor(command.Id), out var gesture) && seen.Add(gesture))
                result.Add((command.Id, gesture));
        }
        return result;
    }

    /// <summary>What is wrong with the bindings (unknown key, shared gesture, a bare letter as a global hotkey); empty when all is well.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        var used = new Dictionary<HotkeyGesture, string>();
        foreach (var command in ControlCatalog.All)
        {
            var text = GestureFor(command.Id);
            if (text is null) continue;
            if (!HotkeyGesture.TryParse(text, out var gesture))
            {
                problems.Add($"{command.Label}: \"{text}\" is not a key combination.");
                continue;
            }
            if (GlobalHotkeys && !gesture.IsSafeAsGlobal)
                problems.Add($"{command.Label}: {gesture} needs Ctrl, Alt or Win (or a function key) to work in every program.");
            if (used.TryGetValue(gesture, out var other))
                problems.Add($"{command.Label} and {other} both use {gesture}.");
            else
                used[gesture] = command.Label;
        }
        return problems;
    }

    /// <summary>Takes over everything from <paramref name="other"/> (the object itself stays, because the server and the window hold on to it).</summary>
    public void CopyFrom(ControlSettings other)
    {
        Token = other.Token;
        GlobalHotkeys = other.GlobalHotkeys;
        Bindings = new Dictionary<string, string>(other.Bindings, StringComparer.OrdinalIgnoreCase);
        Automation.CopyFrom(other.Automation);   // the same object stays, because the match and the bracket hold on to it
    }

    public static ControlSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<ControlSettings>(File.ReadAllText(path), Json);
                if (loaded is not null)
                {
                    if (string.IsNullOrWhiteSpace(loaded.Token)) loaded.Token = NewToken();
                    loaded.Bindings = new Dictionary<string, string>(loaded.Bindings ?? new(), StringComparer.OrdinalIgnoreCase);
                    loaded.Automation ??= new AutomationSettings();
                    loaded.Automation.MapWinPoints = new Dictionary<string, int>(loaded.Automation.MapWinPoints ?? new(), StringComparer.OrdinalIgnoreCase);
                    loaded.Automation.DefaultBracketBestOf = Math.Clamp(loaded.Automation.DefaultBracketBestOf, 1, 9);
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged file must not stop the program: start from the defaults (and a new token) and write them back.
        }
        return new ControlSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        if (File.Exists(path)) File.Replace(temp, path, null, ignoreMetadataErrors: true);
        else File.Move(temp, path);
    }
}

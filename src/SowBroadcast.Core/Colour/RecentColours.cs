using System.Text.Json;
using System.Text.RegularExpressions;

namespace SowBroadcast.Core.Colour;

/// <summary>
/// The colours the producer picked last, newest first, for the colour picker's "recent" row (EHD-12). One list for every colour field, kept
/// in <c>recent-colours.json</c> in the data folder, so the team colour used last week is still one click away.
/// </summary>
public sealed partial class RecentColours
{
    public const int Capacity = 8;

    [GeneratedRegex("^#?([0-9a-fA-F]{6})$")]
    private static partial Regex HexPattern();

    private readonly List<string> _items = new();
    private string? _path;

    /// <summary>The list every colour field of the program shares.</summary>
    public static RecentColours Shared { get; } = new();

    /// <summary>The colours as <c>#rrggbb</c>, newest first.</summary>
    public IReadOnlyList<string> Items => _items;

    /// <summary>Raised when the list changed.</summary>
    public event Action? Changed;

    /// <summary>Uses <paramref name="path"/> for keeping the list, and reads what is there.</summary>
    public void UseFile(string path)
    {
        _path = path;
        _items.Clear();
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) is { } stored)
                foreach (var hex in stored)
                    if (Normalise(hex) is { } valid && !_items.Contains(valid)) _items.Add(valid);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* a damaged list: start empty */ }
        if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
    }

    /// <summary>Puts a colour first (moving it up when it was there). Anything that is not a hex colour is ignored.</summary>
    public void Add(string? hex)
    {
        if (Normalise(hex) is not { } valid) return;
        if (_items.Count > 0 && _items[0] == valid) return;
        _items.Remove(valid);
        _items.Insert(0, valid);
        if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
        Save();
        Changed?.Invoke();
    }

    public static string? Normalise(string? hex)
    {
        var match = HexPattern().Match((hex ?? "").Trim());
        return match.Success ? "#" + match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_items));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the list only lives for this session then */ }
    }
}

using System.Text.Json;

namespace SowBroadcast.Core.Storage;

/// <summary>Small preferences of the program itself (not of a match), kept in <c>app-settings.json</c> in the data folder.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Look for a newer version when the program starts. Off by default: the program makes no network calls the user did not ask for.</summary>
    public bool CheckForUpdatesAtStartup { get; set; }

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) is { } loaded) return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* a damaged file: the defaults */ }
        return new AppSettings();
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

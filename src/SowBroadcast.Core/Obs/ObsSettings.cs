using System.Text.Json;

namespace SowBroadcast.Core.Obs;

/// <summary>Keeps a secret readable only by this user on this computer (Windows DPAPI in the program; a fake in tests).</summary>
public interface ISecretProtector
{
    string Protect(string plain);

    /// <summary>The secret, or null when it cannot be read (another user or computer, or damaged).</summary>
    string? Unprotect(string protectedText);
}

/// <summary>
/// How to reach OBS Studio's WebSocket server. Kept in <c>obs.json</c> in the data folder (it belongs to the computer, not to a match).
/// The password is stored protected for the current Windows user, never as plain text.
/// </summary>
public sealed class ObsSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string Host { get; set; } = "localhost";

    /// <summary>OBS Studio's default WebSocket port.</summary>
    public int Port { get; set; } = 4455;

    /// <summary>The password, protected with <see cref="ISecretProtector"/> (empty = none).</summary>
    public string ProtectedPassword { get; set; } = "";

    /// <summary>Connect to OBS when the program starts.</summary>
    public bool AutoConnect { get; set; }

    public string? GetPassword(ISecretProtector protector) =>
        ProtectedPassword.Length == 0 ? null : protector.Unprotect(ProtectedPassword);

    public void SetPassword(string? password, ISecretProtector protector) =>
        ProtectedPassword = string.IsNullOrEmpty(password) ? "" : protector.Protect(password);

    public static ObsSettings Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<ObsSettings>(File.ReadAllText(path), Json) is { } loaded)
            {
                if (string.IsNullOrWhiteSpace(loaded.Host)) loaded.Host = "localhost";
                if (loaded.Port is < 1 or > 65535) loaded.Port = 4455;
                loaded.ProtectedPassword ??= "";
                return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* a damaged file: the defaults */ }
        return new ObsSettings();
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

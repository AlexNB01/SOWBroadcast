using System.Text.Json;
using System.Text.Json.Serialization;
using SowBroadcast.Core.Model;

namespace SowBroadcast.Core.Storage;

public sealed class StateLoadException : Exception
{
    public StateLoadException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Reads and writes the persisted state as JSON. Writes are atomic (temp file + replace) and keep the
/// previous version as <c>.bak</c>, so a crash mid-write never leaves a broken autosave.
/// Loading tolerates unknown keys (legacy files carry <c>assets</c> and other leftovers).
/// </summary>
public sealed class StateStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static string Serialize(AppState state) => JsonSerializer.Serialize(state, Options);

    public static AppState Deserialize(string json)
    {
        AppState? state;
        try
        {
            state = JsonSerializer.Deserialize<AppState>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new StateLoadException("The file is not a valid SOW Broadcast state file.", ex);
        }
        if (state is null) throw new StateLoadException("The file is empty.");
        return StateMigrations.ToCurrent(state);
    }

    public static AppState Load(string path)
    {
        try
        {
            return Deserialize(File.ReadAllText(path, System.Text.Encoding.UTF8));
        }
        catch (IOException ex)
        {
            throw new StateLoadException($"Could not read {path}.", ex);
        }
    }

    /// <summary>Loads the file; if it is corrupt falls back to the <c>.bak</c> copy. Returns null when neither exists.</summary>
    public static AppState? LoadWithRecovery(string path)
    {
        var backup = path + ".bak";
        if (File.Exists(path))
        {
            try { return Load(path); }
            catch (StateLoadException) when (File.Exists(backup)) { return Load(backup); }
        }
        return File.Exists(backup) ? Load(backup) : null;
    }

    public static void Save(string path, AppState state)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, Path.GetFileName(path) + ".tmp");
        var json = Serialize(state);

        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(true);
        }

        if (File.Exists(path))
            File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(temp, path);
    }
}

public static class StateMigrations
{
    /// <summary>Brings a loaded state up to <see cref="StateSchema.Current"/> and re-derives computed fields.</summary>
    public static AppState ToCurrent(AppState state)
    {
        // v0 (legacy, no schema_version) -> v1: same shape; normalise lists the UI relies on.
        var maps = state.Maps.OrderBy(m => m.Index).ToList();
        while (maps.Count < Limits.MapsPerSeries)
            maps.Add(new MapEntry { Index = maps.Count + 1 });

        return StateDerivations.Apply(state with
        {
            SchemaVersion = StateSchema.Current,
            Team1 = NormalisePlayers(state.Team1),
            Team2 = NormalisePlayers(state.Team2),
            Maps = maps.Take(Limits.MapsPerSeries).ToArray(),
        });
    }

    private static Team NormalisePlayers(Team team)
    {
        var players = team.Players.Take(Limits.PlayersPerTeam).ToList();
        while (players.Count < Limits.PlayersPerTeam) players.Add(new Player());
        return team with { Players = players };
    }
}

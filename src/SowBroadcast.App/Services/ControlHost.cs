using System.Text.Json;
using SowBroadcast.App.ViewModels;
using SowBroadcast.Core.Control;
using SowBroadcast.Server;

namespace SowBroadcast.App.Services;

/// <summary>
/// Answers the calls of the local control interface (<c>/api/control[/command]</c>) by running the command on the UI thread
/// and describing the result as JSON. Status codes: 200 done, 404 no such command, 409 the command could not do anything
/// (a score already at 0, no open map), so a Stream Deck key can show that it did not work.
/// </summary>
public sealed class ControlHost
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly MainWindowViewModel _match;
    private readonly ControlSettings _settings;
    private readonly Func<Func<ControlReply>, Task<ControlReply>> _onUiThread;

    /// <param name="onUiThread">Runs a function on the UI thread (tests run it inline).</param>
    public ControlHost(MainWindowViewModel match, ControlSettings settings, Func<Func<ControlReply>, Task<ControlReply>>? onUiThread = null)
    {
        _match = match;
        _settings = settings;
        _onUiThread = onUiThread ?? (work => Task.FromResult(work()));
    }

    public Task<ControlReply> HandleAsync(string command, IReadOnlyDictionary<string, string> arguments) =>
        _onUiThread(() => string.IsNullOrEmpty(command) ? List() : Run(command, arguments));

    private ControlReply List()
    {
        var commands = ControlCatalog.All.Select(c => new { c.Id, c.Label, Hotkey = _settings.GestureFor(c.Id) });
        return new ControlReply(200, JsonSerializer.Serialize(new { commands, state = Describe(_match.ControlSnapshot(), null) }, Json));
    }

    private ControlReply Run(string command, IReadOnlyDictionary<string, string> arguments)
    {
        var outcome = _match.RunControl(command);
        if (ControlCatalog.Find(command) is null)
            return new ControlReply(404, JsonSerializer.Serialize(Describe(outcome, command), Json));

        // ?publish=1 is an Update pressed after the command, so one Stream Deck key can change and send in one go.
        if (outcome.Ok && arguments.TryGetValue("publish", out var publish) && publish is "1" or "true" &&
            command != ControlCatalog.Update)
            outcome = _match.RunControl(ControlCatalog.Update) with { Message = outcome.Message + ", updated" };

        return new ControlReply(outcome.Ok ? 200 : 409, JsonSerializer.Serialize(Describe(outcome, command), Json));
    }

    private static object Describe(ControlOutcome o, string? command) => new
    {
        ok = o.Ok,
        command,
        message = o.Message,
        team1 = new { score = o.Team1Score },
        team2 = new { score = o.Team2Score },
        current_map = o.CurrentMap,
        timer = new { text = o.Timer, running = o.TimerRunning },
        unsent = o.Unsent,
    };
}

namespace SowBroadcast.Core.History;

/// <summary>
/// Undo and redo over snapshots of the match (EHD-17). A snapshot is the state as text, so recording is cheap to compare and the
/// history never shares objects with the screen. The caller decides what a "step" is (a pause in typing, or one action such as a swap).
/// </summary>
public sealed class UndoHistory
{
    private readonly int _capacity;
    private readonly List<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private string? _current;

    public UndoHistory(int capacity = 100) => _capacity = Math.Max(1, capacity);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The snapshot the screen is supposed to show now.</summary>
    public string? Current => _current;

    /// <summary>Starts over with <paramref name="baseline"/> as the only snapshot (a freshly loaded match).</summary>
    public void Reset(string baseline)
    {
        _undo.Clear();
        _redo.Clear();
        _current = baseline;
    }

    /// <summary>Makes <paramref name="snapshot"/> the newest step. Returns false (and changes nothing) when it equals the current one.</summary>
    public bool Record(string snapshot)
    {
        if (_current is null)
        {
            _current = snapshot;
            return true;
        }
        if (string.Equals(snapshot, _current, StringComparison.Ordinal)) return false;

        _undo.Add(_current);
        if (_undo.Count > _capacity) _undo.RemoveAt(0);
        _redo.Clear();
        _current = snapshot;
        return true;
    }

    /// <summary>One step back; the snapshot to show, or null when there is nothing to undo.</summary>
    public string? Undo()
    {
        if (_undo.Count == 0 || _current is null) return null;
        _redo.Push(_current);
        _current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        return _current;
    }

    /// <summary>One step forward again; null when there is nothing to redo.</summary>
    public string? Redo()
    {
        if (_redo.Count == 0 || _current is null) return null;
        _undo.Add(_current);
        _current = _redo.Pop();
        return _current;
    }
}

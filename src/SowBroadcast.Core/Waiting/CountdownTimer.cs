namespace SowBroadcast.Core.Waiting;

/// <summary>
/// The waiting-screen countdown. While running it counts towards an end time on a monotonic clock, so it does not drift
/// and needs no per-second bookkeeping; <see cref="Remaining"/> is the whole seconds left (rounded up, so a 10 s timer reads
/// 10 until a full second has passed). The rules are the legacy ones: adjusting while stopped moves the preset too,
/// Reset returns to the preset, Start from zero restarts from the preset.
/// </summary>
public sealed class CountdownTimer
{
    private readonly Func<long> _nowMs;
    private long _endsAtMs;
    private int _remaining;

    /// <param name="nowMs">A monotonic clock in milliseconds (e.g. <c>Stopwatch</c>); tests pass a fake.</param>
    public CountdownTimer(Func<long> nowMs) => _nowMs = nowMs;

    public bool IsRunning { get; private set; }

    /// <summary>The value Reset returns to.</summary>
    public int Preset { get; private set; }

    /// <summary>Whole seconds left. Stops the timer when it has run out.</summary>
    public int Remaining
    {
        get
        {
            if (!IsRunning) return _remaining;
            var left = _endsAtMs - _nowMs();
            if (left <= 0)
            {
                IsRunning = false;
                _remaining = 0;
                return 0;
            }
            return (int)((left + 999) / 1000);
        }
    }

    /// <summary>Loads a saved value: stopped, with the value as both remaining time and preset.</summary>
    public void Set(int seconds)
    {
        IsRunning = false;
        _remaining = Preset = Math.Max(0, seconds);
    }

    public void Adjust(int deltaSeconds)
    {
        var next = Math.Max(0, Remaining + deltaSeconds);
        _remaining = next;
        if (IsRunning) _endsAtMs = _nowMs() + next * 1000L;
        else Preset = next;
    }

    /// <summary>Returns true when the timer is running afterwards.</summary>
    public bool Start()
    {
        if (IsRunning) return true;
        if (_remaining <= 0) _remaining = Preset;
        if (_remaining <= 0) return false;
        _endsAtMs = _nowMs() + _remaining * 1000L;
        IsRunning = true;
        return true;
    }

    /// <summary>Returns true when a running timer was stopped.</summary>
    public bool Pause()
    {
        if (!IsRunning) return false;
        _remaining = Remaining;
        IsRunning = false;
        return true;
    }

    public void Reset()
    {
        IsRunning = false;
        _remaining = Preset;
    }

    public static string Format(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }
}

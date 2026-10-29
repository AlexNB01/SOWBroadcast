namespace SowBroadcast.Core.Replay;

/// <summary>One clip of a request, as long as it is on the stream (the shown part at its speed).</summary>
public sealed record ReplayRunSegment(string Title, long LengthMs);

public enum ReplayRunState { Idle, Playing }

/// <summary>Where the clips that were asked for have got to on the stream.</summary>
/// <param name="Index">The clip on the stream now (0-based); -1 when nothing is playing.</param>
/// <param name="ElapsedMs">Since the start of the first clip.</param>
/// <param name="TotalMs">All clips together.</param>
/// <param name="LengthUnknown">The length of at least one clip is not known: the time left is then at least this much.</param>
public sealed record ReplayRunStatus(ReplayRunState State, int Index, IReadOnlyList<ReplayRunSegment> Segments, long ElapsedMs, long TotalMs, bool LengthUnknown)
{
    public static ReplayRunStatus None { get; } = new(ReplayRunState.Idle, -1, Array.Empty<ReplayRunSegment>(), 0, 0, false);

    public bool IsPlaying => State == ReplayRunState.Playing;
    public int Count => Segments.Count;
    public long RemainingMs => Math.Max(0, TotalMs - ElapsedMs);
    public ReplayRunSegment? Current => Index >= 0 && Index < Segments.Count ? Segments[Index] : null;
    public long ClipElapsedMs => Index < 0 ? 0 : Math.Max(0, ElapsedMs - Segments.Take(Index).Sum(s => s.LengthMs));
}

/// <summary>
/// How long the clips that were asked for stay on the stream. The overlay plays them one after another from the moment of the request, so
/// this is a sum of the shown lengths and the time that has passed since; nothing is reported back from the page. The page needs a moment
/// to start (and a moment between two clips), which is counted in. The result is an estimate to the second, not a frame-accurate clock.
/// </summary>
public static class ReplayRun
{
    /// <summary>The page has to see the request and start the first clip.</summary>
    public const long StartLagMs = 600;

    /// <summary>The page changes from one clip to the next.</summary>
    public const long ClipLagMs = 150;

    /// <summary>
    /// The state at <paramref name="nowMs"/> (Unix milliseconds) of a request. <paramref name="current"/> gives a clip as the library has it now:
    /// the length of a clip the request did not know may have been found since (the trim and speed are those of the request, which is what the page plays).
    /// </summary>
    public static ReplayRunStatus At(ReplayRequest? request, long nowMs, Func<ReplayClip, ReplayClip>? current = null)
    {
        if (request is null || request.Clips.Count == 0) return ReplayRunStatus.None;

        var segments = new List<ReplayRunSegment>();
        var unknown = false;
        foreach (var clip in request.Clips)
        {
            var known = current?.Invoke(clip) ?? clip;
            var shown = clip with { DurationMs = Math.Max(clip.DurationMs, known.DurationMs) };
            if (shown.ShownMs <= 0) unknown = true;
            var length = (long)Math.Round(shown.ShownSeconds * 1000) + ClipLagMs;
            segments.Add(new ReplayRunSegment(clip.Title.Length > 0 ? clip.Title : clip.File, length));
        }
        segments[0] = segments[0] with { LengthMs = segments[0].LengthMs + StartLagMs };

        var total = segments.Sum(s => s.LengthMs);
        var elapsed = Math.Max(0, nowMs - request.AtMs);
        if (elapsed >= total) return ReplayRunStatus.None;

        long at = 0;
        var index = 0;
        for (; index < segments.Count - 1 && elapsed >= at + segments[index].LengthMs; index++) at += segments[index].LengthMs;
        return new ReplayRunStatus(ReplayRunState.Playing, index, segments, elapsed, total, unknown);
    }
}

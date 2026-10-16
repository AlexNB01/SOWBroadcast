using SowBroadcast.Core.Model;

namespace SowBroadcast.Core.Bracket;

public enum BracketTemplate
{
    Single4,
    Single6,
    Single8,
    Double4,
    Double6,
    Double8,
}

/// <summary>Creates match ids like <c>m1760000000-3</c> (epoch seconds and a counter), unique within a session.</summary>
public sealed class MatchIdSource
{
    private readonly Func<long> _epochSeconds;
    private int _counter;

    public MatchIdSource(Func<long>? epochSeconds = null) =>
        _epochSeconds = epochSeconds ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public string Next() => $"m{_epochSeconds()}-{++_counter}";

    public BracketMatch NewMatch() => new() { Id = Next() };
}

/// <summary>The bracket templates, the double-elimination view rules and the bronze match (CLAUDE.md 4.6).</summary>
public static class BracketTemplates
{
    public const string BronzeName = "Bronze Match";

    public static readonly IReadOnlyList<(BracketTemplate Template, string Label)> All = new[]
    {
        (BracketTemplate.Single4, "4 team single elimination"),
        (BracketTemplate.Single6, "6 team single elimination (top 2 start round 2)"),
        (BracketTemplate.Single8, "8 team single elimination"),
        (BracketTemplate.Double4, "4 team double elimination"),
        (BracketTemplate.Double6, "6 team double elimination"),
        (BracketTemplate.Double8, "8 team double elimination"),
    };

    public static bool IsSingle(BracketTemplate template) => template <= BracketTemplate.Single8;

    public static IReadOnlyList<BracketRound> Build(BracketTemplate template, MatchIdSource ids)
    {
        (string Name, int Matches)[] upper, lower;
        switch (template)
        {
            case BracketTemplate.Single4:
                upper = new[] { ("Semifinals", 2), ("Final", 1) };
                lower = Array.Empty<(string, int)>();
                break;
            case BracketTemplate.Single6:
                upper = new[] { ("Round 1", 2), ("Semifinals", 2), ("Final", 1) };
                lower = Array.Empty<(string, int)>();
                break;
            case BracketTemplate.Single8:
                upper = new[] { ("Quarterfinals", 4), ("Semifinals", 2), ("Final", 1) };
                lower = Array.Empty<(string, int)>();
                break;
            case BracketTemplate.Double4:
                upper = new[] { ("Upper Semifinals", 2), ("Upper Final", 1) };
                lower = new[] { ("Lower Semifinal", 1), ("Lower Final", 1) };
                break;
            case BracketTemplate.Double6:
                upper = new[] { ("Upper Round 1", 2), ("Upper Semifinals", 2), ("Upper Final", 1) };
                lower = new[] { ("Lower Round 1", 2), ("Lower Semifinal", 1), ("Lower Final", 1) };
                break;
            default:
                upper = new[] { ("Upper Round 1", 4), ("Upper Semifinals", 2), ("Upper Final", 1) };
                lower = new[] { ("Lower Round 1", 2), ("Lower Round 2", 2), ("Lower Final", 1) };
                break;
        }

        var rounds = new List<BracketRound>();
        rounds.AddRange(upper.Select(r => Round(r.Name, "upper", r.Matches, ids)));
        rounds.AddRange(lower.Select(r => Round(r.Name, "lower", r.Matches, ids)));
        if (!IsSingle(template)) rounds.Add(Round("Grand Final", "grand", 1, ids));
        return rounds;
    }

    private static BracketRound Round(string name, string side, int matches, MatchIdSource ids) => new()
    {
        Name = name,
        Side = side,
        Matches = Enumerable.Range(0, matches).Select(_ => ids.NewMatch()).ToArray(),
    };

    /// <summary>The "upper / lower" view switch exists for the 6- and 8-team double eliminations (judged by the round sizes).</summary>
    public static bool SupportsDoubleElimView(IReadOnlyList<BracketRound> rounds)
    {
        var upper = Sizes(rounds, "upper");
        var lower = Sizes(rounds, "lower");
        return (upper.SequenceEqual(new[] { 4, 2, 1 }) && lower.SequenceEqual(new[] { 2, 2, 1 }))
            || (upper.SequenceEqual(new[] { 2, 2, 1 }) && lower.SequenceEqual(new[] { 2, 1, 1 }));
    }

    private static int[] Sizes(IReadOnlyList<BracketRound> rounds, string side) =>
        rounds.Where(r => Side(r) == side).Select(r => r.Matches.Count).ToArray();

    /// <summary>A single elimination: no grand final, and at most one lower round which must be the bronze match.</summary>
    public static bool IsSingleElimination(IReadOnlyList<BracketRound> rounds)
    {
        if (rounds.Count == 0 || rounds.Any(r => Side(r) == "grand")) return false;
        var lower = rounds.Where(r => Side(r) == "lower").ToArray();
        if (lower.Length > 1) return false;
        return lower.Length == 0 || (IsBronze(lower[0]));
    }

    public static bool HasBronze(IReadOnlyList<BracketRound> rounds) => rounds.Any(IsBronze);

    /// <summary>Adds or removes the bronze match round (a one-match "lower" round named "Bronze Match").</summary>
    public static IReadOnlyList<BracketRound> WithBronze(IReadOnlyList<BracketRound> rounds, bool enabled, MatchIdSource ids)
    {
        var existing = rounds.FirstOrDefault(IsBronze);
        if (enabled && existing is null)
            return rounds.Append(Round(BronzeName, "lower", 1, ids)).ToArray();
        if (!enabled && existing is not null)
            return rounds.Where(r => !ReferenceEquals(r, existing)).ToArray();
        return rounds;
    }

    private static bool IsBronze(BracketRound round) =>
        Side(round) == "lower" && round.Name.Trim().Equals(BronzeName, StringComparison.OrdinalIgnoreCase) && round.Matches.Count == 1;

    private static string Side(BracketRound round) => (round.Side ?? "").ToLowerInvariant();
}

using System.Text.RegularExpressions;
using SowBroadcast.Core.Model;

namespace SowBroadcast.Core.Bracket;

/// <summary>A team that should be put in a match slot because it won (or, for the bronze match, lost) the match before.</summary>
public sealed record BracketAssignment(int Round, int Match, int Slot, TeamRef Team);

/// <summary>
/// Moves the winner of a decided bracket match to its place in the next round (EHD-4). Single elimination is complete (the two
/// semifinal losers also go to the Bronze Match). In double elimination the winners move along their own side and the Upper
/// and Lower Final winners go to the Grand Final; where the losers of the upper bracket drop into the lower one depends on the
/// event, so those places are filled by hand.
/// </summary>
public static partial class BracketAdvance
{
    [GeneratedRegex(@"^\s*BO\s*(\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BoLabel();

    /// <summary>Wins needed in a match: from its BO label ("BO5" = 3), otherwise from the default best-of.</summary>
    public static int WinsNeeded(string? boLabel, int defaultBestOf)
    {
        var best = defaultBestOf;
        if (boLabel is not null && BoLabel().Match(boLabel) is { Success: true } m && int.TryParse(m.Groups[1].Value, out var parsed) && parsed > 0)
            best = parsed;
        return Math.Max(1, best) / 2 + 1;
    }

    /// <summary>The places to fill, in order, given the matches as they are now. Never overwrites a team the producer put there by hand.</summary>
    public static IReadOnlyList<BracketAssignment> Plan(IReadOnlyList<BracketRound> rounds, int defaultBestOf)
    {
        var working = rounds.Select(r => r.Matches.ToArray()).ToArray();
        var result = new List<BracketAssignment>();

        for (var r = 0; r < rounds.Count; r++)
        {
            for (var i = 0; i < working[r].Length; i++)
            {
                var match = working[r][i];
                var need = WinsNeeded(match.BoLabel, defaultBestOf);
                TeamRef winner, loser;
                if (match.Score1 >= need && match.Score1 > match.Score2) (winner, loser) = (match.Team1, match.Team2);
                else if (match.Score2 >= need && match.Score2 > match.Score1) (winner, loser) = (match.Team2, match.Team1);
                else continue;

                if (winner.Name.Trim().Length == 0) continue;
                if (WinnerTarget(rounds, r, i) is { } target)
                    Place(working, result, target.Round, target.Match, target.Slot, winner, loser);

                // Bronze match (single elimination): the semifinal losers meet there.
                if (BronzeTarget(rounds, r, i) is { } bronze && loser.Name.Trim().Length > 0)
                    Place(working, result, bronze.Round, bronze.Match, bronze.Slot, loser, winner);
            }
        }
        return result;
    }

    // Where the winner of match i of round r goes: (round, match, slot), with slot 0 meaning "the free place".
    private static (int Round, int Match, int Slot)? WinnerTarget(IReadOnlyList<BracketRound> rounds, int r, int i)
    {
        var side = Side(rounds[r]);
        if (side == "grand") return null;
        var n = rounds[r].Matches.Count;

        if (NextRound(rounds, r, side) is { } next)
        {
            var m = rounds[next].Matches.Count;
            if (n == 2 * m) return (next, i / 2, i % 2 + 1);
            return (next, Math.Min(i, m - 1), 0);
        }

        // The last round of a side: the Grand Final (upper winner takes place 1, lower winner place 2).
        var grand = IndexOfSide(rounds, "grand");
        if (grand >= 0 && rounds[grand].Matches.Count > 0) return (grand, 0, side == "upper" ? 1 : 2);
        return null;
    }

    private static (int Round, int Match, int Slot)? BronzeTarget(IReadOnlyList<BracketRound> rounds, int r, int i)
    {
        if (IndexOfSide(rounds, "grand") >= 0) return null;                         // double elimination: not automated
        var bronze = Enumerable.Range(0, rounds.Count).Where(k => rounds[k].Name == BracketTemplates.BronzeName && rounds[k].Matches.Count > 0).ToArray();
        if (bronze.Length == 0) return null;
        var upper = Enumerable.Range(0, rounds.Count).Where(k => Side(rounds[k]) == "upper").ToArray();
        if (upper.Length < 2) return null;
        // The semifinals: the round before the final.
        var semis = upper[^2];
        return r == semis && rounds[r].Matches.Count == 2 ? (bronze[0], 0, i + 1) : null;
    }

    private static void Place(BracketMatch[][] working, List<BracketAssignment> result, int round, int matchIndex, int slot, TeamRef team, TeamRef other)
    {
        var target = working[round][matchIndex];
        var current = new[] { target.Team1.Name.Trim(), target.Team2.Name.Trim() };
        var otherName = other.Name.Trim();
        var teamName = team.Name.Trim();

        if (slot == 0)
        {
            // The free place: the one that still holds the other team of the decided match (a corrected result), else an empty one.
            slot = otherName.Length > 0 && current[0] == otherName ? 1
                 : otherName.Length > 0 && current[1] == otherName ? 2
                 : current[0].Length == 0 ? 1
                 : current[1].Length == 0 ? 2
                 : 0;
            if (slot == 0) return;
        }

        var held = current[slot - 1];
        if (held == teamName) return;                                              // already there
        if (held.Length > 0 && held != otherName) return;                          // put there by hand: leave it

        working[round][matchIndex] = slot == 1 ? target with { Team1 = team } : target with { Team2 = team };
        result.Add(new BracketAssignment(round, matchIndex, slot, team));
    }

    private static string Side(BracketRound round) => round.Side is "lower" or "grand" ? round.Side : "upper";

    private static int IndexOfSide(IReadOnlyList<BracketRound> rounds, string side)
    {
        for (var k = 0; k < rounds.Count; k++)
            if (Side(rounds[k]) == side) return k;
        return -1;
    }

    private static int? NextRound(IReadOnlyList<BracketRound> rounds, int r, string side)
    {
        for (var k = r + 1; k < rounds.Count; k++)
        {
            if (Side(rounds[k]) != side) continue;
            // A single-elimination Bronze Match is a "lower" round of its own, not a continuation of anything.
            return rounds[k].Name == BracketTemplates.BronzeName ? null : k;
        }
        return null;
    }
}

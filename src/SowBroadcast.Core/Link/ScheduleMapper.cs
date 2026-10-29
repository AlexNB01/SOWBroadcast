using SowBroadcast.Core.Model;

namespace SowBroadcast.Core.Link;

/// <summary>Turns the matches of a linked tournament into the entries the waiting-screen schedule is made from.</summary>
public static class ScheduleMapper
{
    /// <param name="logoOf">The downloaded logo file of a team logo address, or null while it is not here (yet).</param>
    public static IReadOnlyList<ScheduleEntry> FromSnapshot(TournamentSnapshot snapshot, Func<string?, string?>? logoOf = null)
    {
        string? LogoOf(string? teamId) => snapshot.TeamById(teamId)?.LogoUrl is { } url ? logoOf?.Invoke(url) : null;

        // A stage's place in the tournament; inside one stage the group matches come before the bracket matches.
        int StageOrderOf(LinkMatch m)
        {
            var index = m.StageId is null ? -1 : snapshot.Stages.ToList().FindIndex(s => s.Id == m.StageId);
            return (index < 0 ? 0 : index) * 2 + (m.Bracket == "group" ? 0 : 1);
        }

        return snapshot.Matches
            .Where(m => m.Status != "bye")
            .Select(m => new ScheduleEntry
            {
                Id = m.Id,
                Team1 = snapshot.TeamById(m.Team1Id)?.Name ?? "",
                Team2 = snapshot.TeamById(m.Team2Id)?.Name ?? "",
                Team1Logo = LogoOf(m.Team1Id),
                Team2Logo = LogoOf(m.Team2Id),
                Score1 = m.Score1,
                Score2 = m.Score2,
                Status = m.Status switch
                {
                    "completed" => ScheduleStatus.Completed,
                    "in_progress" => ScheduleStatus.Live,
                    _ => ScheduleStatus.Upcoming,
                },
                ScheduledAt = m.ScheduledAt ?? 0,
                StartedAt = m.StartedAt ?? 0,
                CompletedAt = m.CompletedAt ?? 0,
                Asap = m.ScheduleAsap,
                TargetAt = m.TargetAt ?? 0,
                StageOrder = StageOrderOf(m),
                Round = m.Round,
                MatchNumber = m.MatchNumber,
                RoundName = string.IsNullOrEmpty(m.RoundName) ? $"Round {m.Round}" : m.RoundName,
                Group = m.GroupName ?? "",
                StreamUrl = m.StreamUrl ?? "",
            })
            .ToArray();
    }
}

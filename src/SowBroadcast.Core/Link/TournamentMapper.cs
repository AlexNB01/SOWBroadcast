using SowBroadcast.Core.Model;

namespace SowBroadcast.Core.Link;

/// <summary>Turns a tournament into the standings and bracket the two tabs show (the legacy app's mapping, CLAUDE.md 8.2).</summary>
public static class TournamentMapper
{
    private static readonly HashSet<string> StandingsFormats = new() { "round_robin", "swiss", "league", "group_stage" };
    private static readonly HashSet<string> BracketFormats = new() { "single_elimination", "double_elimination", "group_stage" };
    private static readonly HashSet<string> BracketKinds = new() { "main", "winners", "losers", "grand_final", "playoff" };

    // ---- standings ---------------------------------------------------------------------------------------

    public static StandingsSettings Standings(TournamentSnapshot snapshot, Func<string?, string?> logoOf)
    {
        var logo = snapshot.Teams.ToDictionary(t => t.Id, t => logoOf(t.LogoUrl));
        var groups = new List<StandingsGroup>();
        var stages = snapshot.Stages.Where(s => StandingsFormats.Contains(s.Format)).ToArray();

        if (stages.Length > 0)
        {
            foreach (var stage in stages)
            {
                var stageMatches = snapshot.Matches.Where(m => m.StageId == stage.Id).ToArray();
                if (stage.Format == "group_stage")
                {
                    var groupMatches = stageMatches.Where(m => m.Bracket == "group").ToArray();
                    foreach (var name in groupMatches.Select(m => m.GroupName).Where(n => !string.IsNullOrEmpty(n)).Distinct().OrderBy(n => n, StringComparer.Ordinal))
                    {
                        groups.Add(new StandingsGroup
                        {
                            Key = $"{stage.Id}_{name}",
                            Name = $"{(string.IsNullOrEmpty(stage.Name) ? "Stage" : stage.Name)} - Group {name}",
                            Rows = StageRows(groupMatches.Where(m => m.GroupName == name).ToArray(), stage, snapshot, logo),
                        });
                    }
                }
                else
                {
                    groups.Add(new StandingsGroup
                    {
                        Key = string.IsNullOrEmpty(stage.Id) ? $"stage_{groups.Count + 1}" : stage.Id,
                        Name = string.IsNullOrEmpty(stage.Name) ? "Standings" : stage.Name,
                        Rows = StageRows(stageMatches, stage, snapshot, logo),
                    });
                }
            }
        }
        else if (snapshot.Standings is { } server)
        {
            // Not staged: the server's own tiebreak-sorted table.
            var single = server.Count == 1 && server[0].Key == "group_1";
            foreach (var group in server)
            {
                groups.Add(new StandingsGroup
                {
                    Key = group.Key,
                    Name = single ? "Standings" : $"Group {group.Key}",
                    Rows = group.Entries.Select((e, i) => new StandingsRow
                    {
                        TeamName = e.Name,
                        LogoPath = logo.GetValueOrDefault(e.Id),
                        Wins = e.Wins,
                        Losses = e.Losses,
                        MapDiff = e.MapDiff,
                        Points = e.Points,
                        Rank = i + 1,
                    }).ToArray(),
                });
            }
        }

        return new StandingsSettings
        {
            Title = snapshot.Name,
            Subtitle = "",
            Rows = groups.Count > 0 ? groups[0].Rows : Array.Empty<StandingsRow>(),
            Groups = groups,
            DisplayGroup = groups.Count > 0 ? groups[0].Key : "",
        };
    }

    private sealed class Tally
    {
        public int Played, Wins, Draws, Losses, Sf, Sa, Points;
    }

    /// <summary>
    /// The table of a stage from its matches: a win scores the stage's win points, a drawn finished match (no winner) its draw points
    /// to both, a loss its loss points; ordered by points, then score difference, then score for.
    /// </summary>
    private static IReadOnlyList<StandingsRow> StageRows(IReadOnlyList<LinkMatch> matches, LinkStage stage, TournamentSnapshot snapshot,
        Dictionary<string, string?> logo)
    {
        var stats = new Dictionary<string, Tally>();
        var order = new List<string>();
        Tally Of(string id)
        {
            if (!stats.TryGetValue(id, out var t)) { stats[id] = t = new Tally(); order.Add(id); }
            return t;
        }

        var playable = matches.Where(m => !string.IsNullOrEmpty(m.Team1Id) && !string.IsNullOrEmpty(m.Team2Id) && m.Status != "bye").ToArray();
        foreach (var m in playable) { Of(m.Team1Id!); Of(m.Team2Id!); }

        foreach (var m in playable.Where(m => m.Status == "completed"))
        {
            var a = Of(m.Team1Id!);
            var b = Of(m.Team2Id!);
            var s1 = m.Score1 ?? 0;
            var s2 = m.Score2 ?? 0;
            a.Played++; b.Played++;
            a.Sf += s1; a.Sa += s2;
            b.Sf += s2; b.Sa += s1;
            if (string.IsNullOrEmpty(m.WinnerTeamId))
            {
                a.Draws++; b.Draws++;
                a.Points += stage.PtsDraw; b.Points += stage.PtsDraw;
            }
            else if (m.WinnerTeamId == m.Team1Id)
            {
                a.Wins++; a.Points += stage.PtsWin; b.Losses++; b.Points += stage.PtsLoss;
            }
            else
            {
                b.Wins++; b.Points += stage.PtsWin; a.Losses++; a.Points += stage.PtsLoss;
            }
        }

        return order
            .OrderByDescending(id => stats[id].Points)
            .ThenByDescending(id => stats[id].Sf - stats[id].Sa)
            .ThenByDescending(id => stats[id].Sf)
            .Select((id, index) =>
            {
                var t = stats[id];
                var team = snapshot.TeamById(id);
                return new StandingsRow
                {
                    TeamName = team?.Name ?? "",
                    Abbr = team?.Abbr ?? "",
                    LogoPath = logo.GetValueOrDefault(id),
                    Wins = t.Wins,
                    Losses = t.Losses,
                    MapDiff = t.Sf - t.Sa,
                    Points = t.Points,
                    Rank = index + 1,
                    Played = t.Played,
                    Draws = t.Draws,
                    Sf = t.Sf,
                    Sa = t.Sa,
                };
            }).ToArray();
    }

    // ---- bracket -----------------------------------------------------------------------------------------

    public static BracketSettings Bracket(TournamentSnapshot snapshot, Func<string?, string?> logoOf)
    {
        TeamRef RefOf(string? id)
        {
            var team = snapshot.TeamById(id);
            return new TeamRef { Name = team?.Name ?? "", Abbr = "", LogoPath = team is null ? null : logoOf(team.LogoUrl) };
        }

        var rounds = new List<BracketRound>();
        var stages = snapshot.Stages.Where(s => BracketFormats.Contains(s.Format)).ToArray();
        var multiStage = stages.Length > 1;

        void AddRounds(IEnumerable<LinkMatch> stageMatches, string stageLabel, int? bestOf)
        {
            var boLabel = bestOf is > 0 ? $"BO{bestOf}" : "";
            var bySide = stageMatches
                .Where(m => m.Status != "bye" && BracketKinds.Contains(string.IsNullOrEmpty(m.Bracket) ? "main" : m.Bracket!))
                .GroupBy(m => SideOf(m.Bracket))
                .ToDictionary(g => g.Key, g => g.GroupBy(m => m.Round <= 0 ? 1 : m.Round).ToDictionary(r => r.Key, r => r.ToArray()));

            foreach (var side in new[] { "upper", "lower", "grand" }.Where(bySide.ContainsKey))
            {
                var roundMap = bySide[side];
                var maxRound = roundMap.Keys.Max();
                foreach (var roundNo in roundMap.Keys.OrderBy(k => k))
                {
                    var matches = roundMap[roundNo].OrderBy(m => m.MatchNumber).Select(m => new BracketMatch
                    {
                        Id = m.Id,
                        BoLabel = boLabel,
                        Team1 = RefOf(m.Team1Id),
                        Team2 = RefOf(m.Team2Id),
                        Score1 = m.Score1 ?? 0,
                        Score2 = m.Score2 ?? 0,
                        Status = "",
                        ScheduledAt = m.ScheduledAt ?? 0,
                    }).ToArray();
                    var name = RoundName(side, roundNo, maxRound);
                    if (multiStage && stageLabel.Length > 0) name = $"{stageLabel}: {name}";
                    rounds.Add(new BracketRound { Name = name, Side = side, Matches = matches });
                }
            }
        }

        if (stages.Length > 0)
        {
            foreach (var stage in stages)
            {
                var stageMatches = snapshot.Matches.Where(m => m.StageId == stage.Id);
                if (stage.Format == "group_stage")
                    stageMatches = stageMatches.Where(m => m.Bracket is "playoff" or "main");
                AddRounds(stageMatches, stage.Name, stage.BestOf ?? snapshot.BestOf);
            }
        }
        else
        {
            // Not staged: matches carry the fixed bracket kinds directly.
            AddRounds(snapshot.Matches, "", snapshot.BestOf);
        }

        return new BracketSettings
        {
            Title = snapshot.Name,
            Stage = "",
            Rounds = rounds,
            DoubleElimView = "upper",
            Teams = snapshot.Teams.Take(Limits.BracketTeams).Select(t => RefOf(t.Id))
                .Concat(Enumerable.Repeat(new TeamRef(), Limits.BracketTeams)).Take(Limits.BracketTeams).ToArray(),
        };
    }

    private static string SideOf(string? bracket) => bracket switch
    {
        "losers" => "lower",
        "grand_final" => "grand",
        _ => "upper",
    };

    private static string RoundName(string side, int round, int maxRound)
    {
        if (side == "grand") return "Grand Final";
        var prefix = side == "upper" ? "Upper " : side == "lower" ? "Lower " : "";
        return (maxRound - round) switch
        {
            0 => $"{prefix}Final".Trim(),
            1 => $"{prefix}Semifinals".Trim(),
            2 => $"{prefix}Quarterfinals".Trim(),
            _ => $"{prefix}Round {round}".Trim(),
        };
    }
}

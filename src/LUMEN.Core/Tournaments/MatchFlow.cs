namespace Lumen.Core.Tournaments;

/// <summary>
/// What a match is allowed to do next (spec: Match進行).
///
/// A match moves through its states in one direction and skips nothing. That matters more
/// here than it looks: the states are the audit trail. A match that jumps from Waiting to
/// Complete has no song on it, no results, and nothing to show anybody who asks how it was
/// decided — so the jump is refused rather than tidied up afterwards.
///
/// Kept apart from storage on purpose. The rule is the same whether the match lives in
/// SQLite today or on a server later, and a rule that lives in a repository is a rule that
/// gets reimplemented, differently, the second time.
/// </summary>
public static class MatchFlow
{
    /// <summary>What may follow each state.</summary>
    private static readonly IReadOnlyDictionary<MatchStatus, MatchStatus[]> Allowed =
        new Dictionary<MatchStatus, MatchStatus[]>
        {
            // Waiting once both seats are filled; Void if it turns out to be a bye.
            [MatchStatus.Pending] = new[] { MatchStatus.Waiting, MatchStatus.Void },

            [MatchStatus.Waiting] = new[] { MatchStatus.SongSelected, MatchStatus.Void },

            // Back to Waiting is allowed: a pick can be changed before anybody is ready.
            [MatchStatus.SongSelected] = new[] { MatchStatus.Ready, MatchStatus.Waiting, MatchStatus.Void },

            [MatchStatus.Ready] = new[] { MatchStatus.Playing, MatchStatus.SongSelected, MatchStatus.Void },

            [MatchStatus.Playing] = new[] { MatchStatus.ResultPending, MatchStatus.Void },

            // A result can be sent back for another game of a series, or rejected outright,
            // which is what returning to Ready means.
            [MatchStatus.ResultPending] = new[] { MatchStatus.Complete, MatchStatus.Ready, MatchStatus.Void },

            // Terminal.
            [MatchStatus.Complete] = Array.Empty<MatchStatus>(),
            [MatchStatus.Void] = Array.Empty<MatchStatus>(),
        };

    public static bool CanTransition(MatchStatus from, MatchStatus to) =>
        from != to && Allowed.TryGetValue(from, out MatchStatus[]? next) && next.Contains(to);

    public static IReadOnlyList<MatchStatus> NextStates(MatchStatus from) =>
        Allowed.TryGetValue(from, out MatchStatus[]? next) ? next : Array.Empty<MatchStatus>();

    public static bool IsTerminal(MatchStatus status) =>
        status is MatchStatus.Complete or MatchStatus.Void;

    /// <summary>
    /// Moves a match on, or explains why it cannot go there.
    ///
    /// Throwing rather than returning a flag: an invalid transition is a bug in the caller,
    /// not a condition a tournament is expected to be in, and swallowing it would leave the
    /// bracket quietly wrong.
    /// </summary>
    public static TournamentMatch Transition(TournamentMatch match, MatchStatus to)
    {
        if (!CanTransition(match.Status, to))
        {
            throw new InvalidOperationException(
                $"A match cannot go from {match.Status} to {to}. " +
                $"From {match.Status} it may go to: " +
                $"{(NextStates(match.Status).Count == 0 ? "nowhere — it is finished" : string.Join(", ", NextStates(match.Status)))}.");
        }

        return match with
        {
            Status = to,
            StartedUtc = to == MatchStatus.Playing ? match.StartedUtc ?? DateTime.UtcNow : match.StartedUtc,
            CompletedUtc = IsTerminal(to) ? match.CompletedUtc ?? DateTime.UtcNow : match.CompletedUtc,
        };
    }

    /// <summary>
    /// Whether a match is in a state where somebody can actually sit down and play it.
    /// </summary>
    public static bool IsPlayable(TournamentMatch match) =>
        match.Status is MatchStatus.Ready or MatchStatus.Playing
        && match.SelectedChartIds.Count > 0
        && (match.HasBothPlayers || match.Player1Id is not null);

    /// <summary>
    /// How many games one side must win to take the match: over half of best-of.
    /// Best of 1 needs 1, best of 3 needs 2, best of 5 needs 3.
    /// </summary>
    public static int GamesToWin(int bestOf) => Math.Max(1, bestOf / 2 + 1);
}

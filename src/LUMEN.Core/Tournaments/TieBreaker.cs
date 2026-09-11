namespace Lumen.Core.Tournaments;

/// <summary>
/// Decides who won when two results have to be compared (spec: Tie Break).
///
/// The criteria are applied in the order the tournament set them, and the last one —
/// submission time — always decides, so a match can never be left level. That is not a
/// detail: a tournament that can produce a draw it has no rule for is a tournament that
/// stops, and somebody then has to make a decision that the rules did not authorise.
/// </summary>
public static class TieBreaker
{
    /// <summary>
    /// Compares two results under a tournament's rules. Negative means <paramref name="a"/>
    /// wins, positive means <paramref name="b"/> does, zero means genuinely indistinguishable
    /// — which only happens if the rules leave out the final criterion.
    /// </summary>
    public static int Compare(
        TournamentMatchResult a,
        TournamentMatchResult b,
        IReadOnlyList<TieBreakCriterion> criteria)
    {
        foreach (TieBreakCriterion criterion in criteria)
        {
            int verdict = CompareBy(a, b, criterion);
            if (verdict != 0)
            {
                return verdict;
            }
        }

        return 0;
    }

    /// <summary>
    /// One criterion. Negative means <paramref name="a"/> is better; the sign is chosen per
    /// criterion, because more is better for a score and worse for a miss count.
    /// </summary>
    public static int CompareBy(
        TournamentMatchResult a, TournamentMatchResult b, TieBreakCriterion criterion) =>
        criterion switch
        {
            TieBreakCriterion.Score => b.Score.CompareTo(a.Score),
            TieBreakCriterion.Accuracy => b.Accuracy.CompareTo(a.Accuracy),
            TieBreakCriterion.MissCount => a.Miss.CompareTo(b.Miss),
            TieBreakCriterion.MaxCombo => b.MaxCombo.CompareTo(a.MaxCombo),
            TieBreakCriterion.PerfectCount => b.Perfect.CompareTo(a.Perfect),
            TieBreakCriterion.SubmissionTime => a.SubmittedUtc.CompareTo(b.SubmittedUtc),
            _ => 0,
        };

    /// <summary>
    /// Which player won a single game between two results, or null when the rules cannot
    /// separate them.
    /// </summary>
    public static Guid? WinnerOf(
        TournamentMatchResult a,
        TournamentMatchResult b,
        IReadOnlyList<TieBreakCriterion> criteria)
    {
        int verdict = Compare(a, b, criteria);
        return verdict < 0 ? a.PlayerId : verdict > 0 ? b.PlayerId : null;
    }

    /// <summary>
    /// The winner of a whole match, from every confirmed result in it.
    ///
    /// Only confirmed results count. An unconfirmed result is a claim, and letting a claim
    /// decide a match is exactly the hole an organiser is there to close.
    /// </summary>
    public static MatchOutcome Decide(
        TournamentMatch match,
        IReadOnlyList<TournamentMatchResult> results,
        TournamentRules rules)
    {
        var confirmed = results.Where(r => r.IsConfirmed && r.MatchId == match.Id).ToList();

        if (confirmed.Count == 0)
        {
            return MatchOutcome.Undecided("Nothing has been confirmed for this match yet.");
        }

        // A bye: one seat, nobody to beat.
        if (match.IsBye)
        {
            Guid? only = match.Player1Id ?? match.Player2Id;
            return only is null
                ? MatchOutcome.Undecided("A bye with nobody in it.")
                : MatchOutcome.Won(only.Value, 0, 0);
        }

        if (!match.HasBothPlayers)
        {
            return MatchOutcome.Undecided("The match does not have two players.");
        }

        Guid one = match.Player1Id!.Value;
        Guid two = match.Player2Id!.Value;

        int wonByOne = 0;
        int wonByTwo = 0;

        foreach (var game in confirmed.GroupBy(r => r.GameIndex).OrderBy(g => g.Key))
        {
            TournamentMatchResult? first = game.FirstOrDefault(r => r.PlayerId == one);
            TournamentMatchResult? second = game.FirstOrDefault(r => r.PlayerId == two);

            // A game only one player has submitted is not a game either of them has won.
            if (first is null || second is null)
            {
                continue;
            }

            Guid? winner = WinnerOf(first, second, rules.TieBreak);
            if (winner == one)
            {
                wonByOne++;
            }
            else if (winner == two)
            {
                wonByTwo++;
            }
        }

        int needed = MatchFlow.GamesToWin(match.BestOf);

        if (wonByOne >= needed)
        {
            return MatchOutcome.Won(one, wonByOne, wonByTwo);
        }

        if (wonByTwo >= needed)
        {
            return MatchOutcome.Won(two, wonByTwo, wonByOne);
        }

        return MatchOutcome.Undecided(
            $"{wonByOne}-{wonByTwo}, and {needed} games are needed to win.");
    }

    /// <summary>
    /// The standings for a score attack, best first.
    ///
    /// One entry per player: their best confirmed result, compared under the tournament's
    /// own tie-break, so the ranking and the head-to-head rules cannot disagree.
    /// </summary>
    public static IReadOnlyList<StandingEntry> Standings(
        IReadOnlyList<TournamentMatchResult> results, TournamentRules rules)
    {
        var best = results
            .Where(r => r.IsConfirmed)
            .GroupBy(r => r.PlayerId)
            .Select(g => g.Aggregate((a, b) => Compare(a, b, rules.TieBreak) <= 0 ? a : b))
            .ToList();

        best.Sort((a, b) => Compare(a, b, rules.TieBreak));

        return best
            .Select((r, i) => new StandingEntry(i + 1, r.PlayerId, r))
            .ToList();
    }
}

/// <summary>Who won a match, and by how much.</summary>
/// <param name="WinnerPlayerId">Null when it is not decided yet.</param>
/// <param name="WinnerGames">Games won by the winner.</param>
/// <param name="LoserGames">Games won by the other player.</param>
/// <param name="Reason">Why it is undecided, when it is.</param>
public readonly record struct MatchOutcome(
    Guid? WinnerPlayerId, int WinnerGames, int LoserGames, string Reason)
{
    public bool IsDecided => WinnerPlayerId is not null;

    public static MatchOutcome Won(Guid winner, int winnerGames, int loserGames) =>
        new(winner, winnerGames, loserGames, "");

    public static MatchOutcome Undecided(string reason) => new(null, 0, 0, reason);
}

/// <summary>One line of a score-attack ranking.</summary>
public readonly record struct StandingEntry(int Rank, Guid PlayerId, TournamentMatchResult Best);

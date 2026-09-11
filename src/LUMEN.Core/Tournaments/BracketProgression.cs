namespace Lumen.Core.Tournaments;

/// <summary>A seat in a later match that a player has just earned.</summary>
/// <param name="MatchId">The match to put them in.</param>
/// <param name="AsPlayerOne">Which of the two seats.</param>
public readonly record struct BracketSeat(Guid MatchId, bool AsPlayerOne);

/// <summary>
/// Where a player goes when they win (spec: Match進行 / Next Match).
///
/// The bracket is built whole and empty, so advancing is filling in a seat that already
/// exists. That is the reason this can be a pure function of the bracket and one finished
/// match: there is nothing to create, only somewhere to put somebody.
///
/// The winners' side and the grand final are exact. The losers' side is not, and it is
/// worth being plain about that: a full double-elimination bracket cross-seeds the drop
/// from the winners' bracket so that two players who have already met are kept apart for
/// as long as possible. This fills the next free seat instead. Every player still needs
/// two losses to go out and the bracket still resolves, but an early rematch is possible
/// where a seeded drop would have avoided it.
/// </summary>
public static class BracketProgression
{
    /// <summary>
    /// Where the winner of <paramref name="completed"/> plays next, or null when there is
    /// nowhere left — they have won the tournament, or the match was in a format that does
    /// not advance anybody.
    /// </summary>
    public static BracketSeat? NextSeatForWinner(
        IReadOnlyList<TournamentRound> rounds,
        IReadOnlyList<TournamentMatch> matches,
        TournamentMatch completed)
    {
        TournamentRound? round = rounds.FirstOrDefault(r => r.Id == completed.RoundId);
        if (round is null)
        {
            return null;
        }

        return round.Side switch
        {
            BracketSide.Winners => WinnersAdvance(rounds, matches, round, completed),
            BracketSide.Losers => NextFreeSeat(rounds, matches, round, BracketSide.Losers)
                                  ?? GrandFinalSeat(rounds, matches, asPlayerOne: false),
            _ => null, // the grand final has nowhere to go
        };
    }

    /// <summary>
    /// Where the loser of <paramref name="completed"/> goes, in a double-elimination
    /// tournament. Null in every other format, where losing ends the run.
    /// </summary>
    public static BracketSeat? NextSeatForLoser(
        IReadOnlyList<TournamentRound> rounds,
        IReadOnlyList<TournamentMatch> matches,
        TournamentMatch completed,
        TournamentFormat format)
    {
        if (format != TournamentFormat.DoubleElimination)
        {
            return null;
        }

        TournamentRound? round = rounds.FirstOrDefault(r => r.Id == completed.RoundId);
        if (round is null || round.Side != BracketSide.Winners)
        {
            // Losing in the losers' bracket is the second loss: that is the end of it.
            return null;
        }

        return NextFreeSeat(rounds, matches, round, BracketSide.Losers, fromWinnersRound: true);
    }

    /// <summary>
    /// The winners'-side rule, which is exact: two matches feed one, and the lower slot
    /// takes the first seat. That is what makes a drawn bracket readable — the lines go
    /// where the picture says they go.
    /// </summary>
    private static BracketSeat? WinnersAdvance(
        IReadOnlyList<TournamentRound> rounds,
        IReadOnlyList<TournamentMatch> matches,
        TournamentRound round,
        TournamentMatch completed)
    {
        TournamentRound? next = rounds
            .Where(r => r.Side == BracketSide.Winners && r.Index > round.Index)
            .OrderBy(r => r.Index)
            .FirstOrDefault();

        if (next is null)
        {
            // The winners' bracket is finished. In a double-elimination tournament that
            // earns the grand final; in a single-elimination one it wins the whole thing.
            return GrandFinalSeat(rounds, matches, asPlayerOne: true);
        }

        TournamentMatch? target = matches
            .FirstOrDefault(m => m.RoundId == next.Id && m.Slot == completed.Slot / 2);

        return target is null ? null : new BracketSeat(target.Id, completed.Slot % 2 == 0);
    }

    private static BracketSeat? GrandFinalSeat(
        IReadOnlyList<TournamentRound> rounds,
        IReadOnlyList<TournamentMatch> matches,
        bool asPlayerOne)
    {
        TournamentRound? grand = rounds.FirstOrDefault(r => r.Side == BracketSide.Grand);
        if (grand is null)
        {
            return null;
        }

        TournamentMatch? match = matches.FirstOrDefault(m => m.RoundId == grand.Id);
        return match is null ? null : new BracketSeat(match.Id, asPlayerOne);
    }

    /// <summary>
    /// The first seat still empty in the next losers' round after this one.
    /// </summary>
    private static BracketSeat? NextFreeSeat(
        IReadOnlyList<TournamentRound> rounds,
        IReadOnlyList<TournamentMatch> matches,
        TournamentRound from,
        BracketSide side,
        bool fromWinnersRound = false)
    {
        // A player dropping out of the winners' bracket joins the losers' bracket at the
        // round that is waiting for that batch; a player already down there moves on to the
        // next one.
        int after = fromWinnersRound
            ? rounds.Where(r => r.Side == side).Select(r => r.Index).DefaultIfEmpty(-1).Min() - 1
            : from.Index;

        var candidates = rounds
            .Where(r => r.Side == side && r.Index > after)
            .OrderBy(r => r.Index)
            .ToList();

        foreach (TournamentRound round in candidates)
        {
            foreach (TournamentMatch match in matches
                         .Where(m => m.RoundId == round.Id)
                         .OrderBy(m => m.Slot))
            {
                if (match.Player1Id is null)
                {
                    return new BracketSeat(match.Id, true);
                }

                if (match.Player2Id is null)
                {
                    return new BracketSeat(match.Id, false);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Puts a player into a seat, and opens the match up once both seats are filled.
    /// </summary>
    public static TournamentMatch Seat(TournamentMatch match, BracketSeat seat, Guid playerId)
    {
        TournamentMatch filled = seat.AsPlayerOne
            ? match with { Player1Id = playerId }
            : match with { Player2Id = playerId };

        // Pending means "not everybody is here yet". Once they are, it is a match waiting
        // to be played, and moving it on here saves the organiser a click that could only
        // ever have one answer.
        if (filled.Status == MatchStatus.Pending && filled.HasBothPlayers)
        {
            filled = filled with { Status = MatchStatus.Waiting };
        }

        return filled;
    }

    /// <summary>
    /// The match that decides the tournament: the grand final if there is one, otherwise
    /// the last winners'-side match.
    /// </summary>
    public static TournamentMatch? DecidingMatch(
        IReadOnlyList<TournamentRound> rounds, IReadOnlyList<TournamentMatch> matches)
    {
        TournamentRound? last = rounds
            .OrderByDescending(r => r.Side == BracketSide.Grand)
            .ThenByDescending(r => r.Index)
            .FirstOrDefault(r => r.Side != BracketSide.Losers);

        return last is null ? null : matches.FirstOrDefault(m => m.RoundId == last.Id);
    }
}

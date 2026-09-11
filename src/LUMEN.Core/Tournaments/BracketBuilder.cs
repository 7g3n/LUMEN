namespace Lumen.Core.Tournaments;

/// <summary>A bracket, ready to be written down.</summary>
/// <param name="Rounds">In playing order.</param>
/// <param name="Matches">Every match of every round, already seated where it can be.</param>
public readonly record struct Bracket(
    IReadOnlyList<TournamentRound> Rounds,
    IReadOnlyList<TournamentMatch> Matches);

/// <summary>
/// Turns a seeded list of entrants into a bracket (spec: Tournament形式).
///
/// The whole bracket is built up front, empty seats and all, rather than a round at a
/// time. A player can then see the shape of their run before it starts — which is most of
/// the point of a bracket — and advancing somebody becomes filling in a seat that already
/// exists rather than inventing a match.
///
/// Seeding is the standard fold: 1 plays the lowest seed, 2 plays the next lowest, and so
/// on, arranged so the top two seeds can only meet in the final. Entrant counts that are
/// not a power of two are padded with byes, and the byes go to the top seeds, which is
/// both conventional and the only arrangement that does not punish seeding well.
/// </summary>
public static class BracketBuilder
{
    /// <summary>Below this there is no tournament to run.</summary>
    public const int MinimumParticipants = 2;

    public static Bracket Build(
        Guid tournamentId,
        TournamentFormat format,
        IReadOnlyList<TournamentParticipant> participants,
        int bestOf = 1)
    {
        var seeded = participants
            .Where(p => p.Status == ParticipantStatus.Active)
            .OrderBy(p => p.Seed == 0 ? int.MaxValue : p.Seed)
            .ThenBy(p => p.JoinedUtc)
            .ToList();

        if (seeded.Count < MinimumParticipants)
        {
            throw new InvalidOperationException(
                $"A tournament needs at least {MinimumParticipants} participants; this one has {seeded.Count}.");
        }

        return format switch
        {
            TournamentFormat.SingleElimination => SingleElimination(tournamentId, seeded, bestOf),
            TournamentFormat.DoubleElimination => DoubleElimination(tournamentId, seeded, bestOf),
            TournamentFormat.ScoreAttack => ScoreAttack(tournamentId, seeded, bestOf),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown tournament format."),
        };
    }

    // --- single elimination ---

    private static Bracket SingleElimination(
        Guid tournamentId, IReadOnlyList<TournamentParticipant> seeded, int bestOf)
    {
        int size = NextPowerOfTwo(seeded.Count);
        int roundCount = Log2(size);

        var rounds = new List<TournamentRound>();
        var matches = new List<TournamentMatch>();

        for (int index = 0; index < roundCount; index++)
        {
            int matchesInRound = size >> (index + 1);
            var round = new TournamentRound
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Index = index,
                Name = RoundName(matchesInRound, BracketSide.Winners),
                Side = BracketSide.Winners,
            };

            rounds.Add(round);

            for (int slot = 0; slot < matchesInRound; slot++)
            {
                matches.Add(new TournamentMatch
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournamentId,
                    RoundId = round.Id,
                    Slot = slot,
                    BestOf = bestOf,
                    Status = MatchStatus.Pending,
                });
            }
        }

        Seat(matches, rounds[0], SeedOrder(size), seeded);
        return new Bracket(rounds, matches);
    }

    // --- double elimination ---

    private static Bracket DoubleElimination(
        Guid tournamentId, IReadOnlyList<TournamentParticipant> seeded, int bestOf)
    {
        Bracket winners = SingleElimination(tournamentId, seeded, bestOf);

        var rounds = winners.Rounds.ToList();
        var matches = winners.Matches.ToList();

        int size = NextPowerOfTwo(seeded.Count);
        int winnerRounds = Log2(size);
        int index = winnerRounds;

        // The losers' bracket runs in pairs of rounds. The first of each pair puts the
        // players already down there against each other; the second feeds them the batch
        // that has just dropped out of the winners' bracket. That alternation is what keeps
        // the two halves in step, and it is why the counts go 2, 2, 1, 1 for eight entrants
        // rather than simply halving each time.
        int matchesInRound = size / 4;
        int losersRound = 1;

        while (matchesInRound >= 1)
        {
            for (int pair = 0; pair < 2; pair++)
            {
                var round = new TournamentRound
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournamentId,
                    Index = index++,
                    Name = $"Losers Round {losersRound++}",
                    Side = BracketSide.Losers,
                };

                rounds.Add(round);

                for (int slot = 0; slot < matchesInRound; slot++)
                {
                    matches.Add(new TournamentMatch
                    {
                        Id = Guid.NewGuid(),
                        TournamentId = tournamentId,
                        RoundId = round.Id,
                        Slot = slot,
                        BestOf = bestOf,
                        Status = MatchStatus.Pending,
                    });
                }
            }

            matchesInRound /= 2;
        }

        var grand = new TournamentRound
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            Index = index,
            Name = "Grand Final",
            Side = BracketSide.Grand,
        };

        rounds.Add(grand);
        matches.Add(new TournamentMatch
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            RoundId = grand.Id,
            Slot = 0,
            BestOf = bestOf,
            Status = MatchStatus.Pending,
        });

        return new Bracket(rounds, matches);
    }

    // --- score attack ---

    /// <summary>
    /// Everybody plays the same charts and the ranking is the result, so there is no
    /// bracket to build: one round, and one "match" per player holding their attempt.
    /// Modelling it this way rather than as a special case means the rest of the system —
    /// results, confirmation, the event log — works on it unchanged.
    /// </summary>
    private static Bracket ScoreAttack(
        Guid tournamentId, IReadOnlyList<TournamentParticipant> seeded, int bestOf)
    {
        var round = new TournamentRound
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            Index = 0,
            Name = "Score Attack",
            Side = BracketSide.Winners,
        };

        var matches = seeded.Select((participant, slot) => new TournamentMatch
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            RoundId = round.Id,
            Slot = slot,
            Player1Id = participant.PlayerId,
            BestOf = bestOf,
            Status = MatchStatus.Waiting,
        }).ToList();

        return new Bracket(new[] { round }, matches);
    }

    // --- seeding ---

    /// <summary>
    /// Seats the first round, pairing each seed with its opposite so that the top two can
    /// only meet at the end.
    /// </summary>
    private static void Seat(
        List<TournamentMatch> matches,
        TournamentRound firstRound,
        IReadOnlyList<int> order,
        IReadOnlyList<TournamentParticipant> seeded)
    {
        var firstRoundMatches = matches
            .Where(m => m.RoundId == firstRound.Id)
            .OrderBy(m => m.Slot)
            .ToList();

        for (int slot = 0; slot < firstRoundMatches.Count; slot++)
        {
            int seedA = order[slot * 2];
            int seedB = order[slot * 2 + 1];

            TournamentParticipant? a = seedA <= seeded.Count ? seeded[seedA - 1] : null;
            TournamentParticipant? b = seedB <= seeded.Count ? seeded[seedB - 1] : null;

            TournamentMatch match = firstRoundMatches[slot];
            int at = matches.IndexOf(match);

            matches[at] = match with
            {
                Player1Id = a?.PlayerId,
                Player2Id = b?.PlayerId,

                // A seat that will never be filled is a bye: the one player there advances
                // without playing, so the match is settled the moment it is created.
                Status = a is not null && b is not null ? MatchStatus.Waiting
                    : a is not null || b is not null ? MatchStatus.Void
                    : MatchStatus.Void,
                WinnerPlayerId = a is not null && b is null ? a.PlayerId
                    : b is not null && a is null ? b.PlayerId
                    : null,
            };
        }
    }

    /// <summary>
    /// The standard seeding fold for a bracket of <paramref name="size"/>: 1 v 16,
    /// 8 v 9, 5 v 12, 4 v 13, and so on, so that every round pairs adjacent strengths and
    /// the two best entrants are kept apart until the final.
    /// </summary>
    public static IReadOnlyList<int> SeedOrder(int size)
    {
        var order = new List<int> { 1, 2 };

        while (order.Count < size)
        {
            int rounds = order.Count * 2;
            var next = new List<int>(rounds);

            foreach (int seed in order)
            {
                next.Add(seed);
                next.Add(rounds + 1 - seed);
            }

            order = next;
        }

        return order;
    }

    /// <summary>"Final", "Semi Final", "Quarter Final", then counts.</summary>
    public static string RoundName(int matchesInRound, BracketSide side)
    {
        string name = matchesInRound switch
        {
            1 => "Final",
            2 => "Semi Final",
            4 => "Quarter Final",
            _ => $"Round of {matchesInRound * 2}",
        };

        return side == BracketSide.Losers ? "Losers " + name : name;
    }

    public static int NextPowerOfTwo(int n)
    {
        int size = 1;
        while (size < n)
        {
            size <<= 1;
        }

        return size;
    }

    private static int Log2(int powerOfTwo)
    {
        int bits = 0;
        while (powerOfTwo > 1)
        {
            powerOfTwo >>= 1;
            bits++;
        }

        return bits;
    }
}

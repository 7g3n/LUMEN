using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Tournaments;
using Xunit;

namespace Lumen.Tests.Core.Tournaments;

public class BracketBuilderTests
{
    private static readonly Guid TournamentId = Guid.NewGuid();

    private static IReadOnlyList<TournamentParticipant> Entrants(int count)
    {
        return Enumerable.Range(1, count).Select(seed => new TournamentParticipant
        {
            TournamentId = TournamentId,
            PlayerId = Guid.NewGuid(),
            DisplayName = $"Player {seed}",
            Seed = seed,
            JoinedUtc = new DateTime(2026, 1, 1).AddMinutes(seed),
        }).ToList();
    }

    private static Bracket Build(int entrants, TournamentFormat format = TournamentFormat.SingleElimination) =>
        BracketBuilder.Build(TournamentId, format, Entrants(entrants));

    // --- seeding ---

    /// <summary>
    /// The fold that keeps the two best entrants apart until the final. If this changes,
    /// seeding stops meaning anything.
    /// </summary>
    [Fact]
    public void Seed_order_pairs_each_seed_with_its_opposite()
    {
        BracketBuilder.SeedOrder(2).Should().Equal(1, 2);
        BracketBuilder.SeedOrder(4).Should().Equal(1, 4, 2, 3);
        BracketBuilder.SeedOrder(8).Should().Equal(1, 8, 4, 5, 2, 7, 3, 6);
    }

    [Fact]
    public void Seed_order_uses_every_seed_exactly_once()
    {
        foreach (int size in new[] { 2, 4, 8, 16, 32 })
        {
            IReadOnlyList<int> order = BracketBuilder.SeedOrder(size);

            order.Should().HaveCount(size);
            order.Should().OnlyHaveUniqueItems();
            order.Should().BeEquivalentTo(Enumerable.Range(1, size));
        }
    }

    /// <summary>
    /// Every first-round pairing sums to one more than the bracket size — that is what
    /// "strongest against weakest" means, and it is the property worth pinning.
    /// </summary>
    [Fact]
    public void Every_first_round_pairing_is_strongest_against_weakest()
    {
        foreach (int size in new[] { 4, 8, 16 })
        {
            IReadOnlyList<int> order = BracketBuilder.SeedOrder(size);

            for (int i = 0; i < order.Count; i += 2)
            {
                (order[i] + order[i + 1]).Should().Be(size + 1);
            }
        }
    }

    [Fact]
    public void The_top_two_seeds_start_in_opposite_halves()
    {
        IReadOnlyList<int> order = BracketBuilder.SeedOrder(16);

        int firstHalf = order.Take(8).ToList().IndexOf(1);
        int secondHalf = order.Skip(8).ToList().IndexOf(2);

        firstHalf.Should().BeGreaterThan(-1);
        secondHalf.Should().BeGreaterThan(-1, "seed 2 must be in the other half of the draw");
    }

    // --- single elimination ---

    [Fact]
    public void Eight_entrants_make_three_rounds_ending_in_a_final()
    {
        Bracket bracket = Build(8);

        bracket.Rounds.Should().HaveCount(3);
        bracket.Rounds.Select(r => r.Name).Should().Equal("Quarter Final", "Semi Final", "Final");
        bracket.Matches.Should().HaveCount(4 + 2 + 1);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 3)]
    [InlineData(16, 4)]
    [InlineData(32, 5)]
    public void A_power_of_two_field_has_as_many_rounds_as_it_needs(int entrants, int rounds)
    {
        Build(entrants).Rounds.Should().HaveCount(rounds);
    }

    [Fact]
    public void Every_entrant_is_seated_exactly_once_in_the_first_round()
    {
        IReadOnlyList<TournamentParticipant> entrants = Entrants(8);
        Bracket bracket = BracketBuilder.Build(TournamentId, TournamentFormat.SingleElimination, entrants);

        Guid firstRound = bracket.Rounds[0].Id;
        List<Guid> seated = bracket.Matches
            .Where(m => m.RoundId == firstRound)
            .SelectMany(m => new[] { m.Player1Id, m.Player2Id })
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();

        seated.Should().HaveCount(8);
        seated.Should().OnlyHaveUniqueItems();
        seated.Should().BeEquivalentTo(entrants.Select(e => e.PlayerId));
    }

    [Fact]
    public void Later_rounds_start_empty_because_nobody_has_played_yet()
    {
        Bracket bracket = Build(8);
        Guid firstRound = bracket.Rounds[0].Id;

        bracket.Matches
            .Where(m => m.RoundId != firstRound)
            .Should().OnlyContain(m => m.Player1Id == null && m.Player2Id == null);
    }

    // --- byes ---

    /// <summary>
    /// A field that is not a power of two is the normal case, not an edge one: six people
    /// turning up is more likely than eight.
    /// </summary>
    [Fact]
    public void Six_entrants_are_padded_to_eight_with_two_byes()
    {
        Bracket bracket = Build(6);
        Guid firstRound = bracket.Rounds[0].Id;

        var opening = bracket.Matches.Where(m => m.RoundId == firstRound).ToList();

        opening.Should().HaveCount(4);
        opening.Count(m => m.IsBye).Should().Be(2);
    }

    [Fact]
    public void A_bye_is_already_won_by_the_player_who_has_it()
    {
        Bracket bracket = Build(5);
        Guid firstRound = bracket.Rounds[0].Id;

        foreach (TournamentMatch bye in bracket.Matches.Where(m => m.RoundId == firstRound && m.IsBye))
        {
            bye.WinnerPlayerId.Should().NotBeNull("a bye is settled the moment it is created");
            bye.Status.Should().Be(MatchStatus.Void, "nobody plays a bye");
            bye.WinnerPlayerId.Should().Be(bye.Player1Id ?? bye.Player2Id);
        }
    }

    /// <summary>
    /// Byes go to the top seeds. Any other arrangement punishes seeding well, which is the
    /// opposite of what seeding is for.
    /// </summary>
    [Fact]
    public void The_byes_go_to_the_best_seeds()
    {
        IReadOnlyList<TournamentParticipant> entrants = Entrants(5);
        Bracket bracket = BracketBuilder.Build(TournamentId, TournamentFormat.SingleElimination, entrants);

        Guid firstRound = bracket.Rounds[0].Id;
        List<Guid> withByes = bracket.Matches
            .Where(m => m.RoundId == firstRound && m.IsBye)
            .Select(m => (m.Player1Id ?? m.Player2Id)!.Value)
            .ToList();

        // Five entrants in an eight bracket means three byes, and they belong to seeds 1-3.
        withByes.Should().HaveCount(3);
        withByes.Should().BeSubsetOf(entrants.Take(3).Select(e => e.PlayerId));
    }

    [Theory]
    [InlineData(3, 4)]
    [InlineData(5, 8)]
    [InlineData(9, 16)]
    [InlineData(17, 32)]
    public void An_awkward_field_is_rounded_up_to_a_bracket_that_fits(int entrants, int size)
    {
        BracketBuilder.NextPowerOfTwo(entrants).Should().Be(size);

        Bracket bracket = Build(entrants);
        bracket.Matches.Count(m => m.RoundId == bracket.Rounds[0].Id).Should().Be(size / 2);
    }

    // --- double elimination ---

    [Fact]
    public void Double_elimination_has_a_losers_bracket_and_a_grand_final()
    {
        Bracket bracket = Build(8, TournamentFormat.DoubleElimination);

        bracket.Rounds.Should().Contain(r => r.Side == BracketSide.Winners);
        bracket.Rounds.Should().Contain(r => r.Side == BracketSide.Losers);
        bracket.Rounds.Should().ContainSingle(r => r.Side == BracketSide.Grand);
        bracket.Rounds.Last().Name.Should().Be("Grand Final");
    }

    /// <summary>
    /// The standard shape: pairs of losers' rounds, halving. Eight entrants give 2, 2, 1, 1
    /// — not a straight halving, because every second round exists only to receive the
    /// players who have just dropped out of the winners' bracket.
    /// </summary>
    [Fact]
    public void The_losers_bracket_runs_in_pairs_of_rounds()
    {
        Bracket bracket = Build(8, TournamentFormat.DoubleElimination);

        var losers = bracket.Rounds.Where(r => r.Side == BracketSide.Losers).ToList();
        losers.Should().HaveCount(4);

        losers.Select(r => bracket.Matches.Count(m => m.RoundId == r.Id))
            .Should().Equal(2, 2, 1, 1);
    }

    [Fact]
    public void A_four_player_double_elimination_has_two_losers_rounds()
    {
        Bracket bracket = Build(4, TournamentFormat.DoubleElimination);

        var losers = bracket.Rounds.Where(r => r.Side == BracketSide.Losers).ToList();
        losers.Should().HaveCount(2);
        losers.Select(r => bracket.Matches.Count(m => m.RoundId == r.Id)).Should().Equal(1, 1);
    }

    /// <summary>
    /// Every entrant but the champion has to lose twice, and each match produces exactly
    /// one loss, so a double-elimination bracket holds 2n-2 matches in total. That single
    /// count is the strongest check there is that the two halves fit together.
    /// </summary>
    [Fact]
    public void The_bracket_holds_exactly_enough_matches_to_eliminate_everybody_twice()
    {
        foreach (int size in new[] { 4, 8, 16, 32 })
        {
            Build(size, TournamentFormat.DoubleElimination).Matches
                .Should().HaveCount(2 * size - 2, $"a {size}-player double elimination");
        }
    }

    /// <summary>And the single-elimination equivalent: one loss each, so n-1 matches.</summary>
    [Fact]
    public void A_single_elimination_bracket_holds_one_match_per_elimination()
    {
        foreach (int size in new[] { 4, 8, 16, 32 })
        {
            Build(size).Matches.Should().HaveCount(size - 1);
        }
    }

    [Fact]
    public void The_grand_final_is_a_single_match()
    {
        Bracket bracket = Build(8, TournamentFormat.DoubleElimination);
        TournamentRound grand = bracket.Rounds.Single(r => r.Side == BracketSide.Grand);

        bracket.Matches.Count(m => m.RoundId == grand.Id).Should().Be(1);
    }

    [Fact]
    public void Double_elimination_still_seats_everybody_in_the_first_round()
    {
        IReadOnlyList<TournamentParticipant> entrants = Entrants(8);
        Bracket bracket = BracketBuilder.Build(TournamentId, TournamentFormat.DoubleElimination, entrants);

        Guid firstRound = bracket.Rounds[0].Id;
        bracket.Matches
            .Where(m => m.RoundId == firstRound)
            .SelectMany(m => new[] { m.Player1Id, m.Player2Id })
            .Where(id => id is not null)
            .Should().HaveCount(8);
    }

    [Fact]
    public void Rounds_are_numbered_in_playing_order_with_no_gaps()
    {
        Bracket bracket = Build(8, TournamentFormat.DoubleElimination);

        bracket.Rounds.Select(r => r.Index).Should().Equal(
            Enumerable.Range(0, bracket.Rounds.Count));
    }

    // --- score attack ---

    /// <summary>
    /// Everybody plays the same charts, so there is nothing to pair. Modelling it as one
    /// match per player rather than as a special case means results, confirmation and the
    /// event log all work on it unchanged.
    /// </summary>
    [Fact]
    public void Score_attack_gives_every_player_their_own_slot()
    {
        IReadOnlyList<TournamentParticipant> entrants = Entrants(5);
        Bracket bracket = BracketBuilder.Build(TournamentId, TournamentFormat.ScoreAttack, entrants);

        bracket.Rounds.Should().ContainSingle();
        bracket.Matches.Should().HaveCount(5);
        bracket.Matches.Should().OnlyContain(m => m.Player2Id == null);
        bracket.Matches.Select(m => m.Player1Id).Should()
            .BeEquivalentTo(entrants.Select(e => (Guid?)e.PlayerId));
    }

    [Fact]
    public void Score_attack_slots_are_ready_to_play_immediately()
    {
        Build(4, TournamentFormat.ScoreAttack).Matches
            .Should().OnlyContain(m => m.Status == MatchStatus.Waiting);
    }

    // --- refusals ---

    [Fact]
    public void A_tournament_with_nobody_in_it_cannot_be_drawn()
    {
        Action build = () => BracketBuilder.Build(
            TournamentId, TournamentFormat.SingleElimination, Entrants(1));

        build.Should().Throw<InvalidOperationException>()
            .WithMessage("*at least 2*");
    }

    [Fact]
    public void Disqualified_entrants_are_left_out_of_the_draw()
    {
        var entrants = Entrants(5).ToList();
        entrants[2] = entrants[2] with { Status = ParticipantStatus.Disqualified };

        Bracket bracket = BracketBuilder.Build(TournamentId, TournamentFormat.SingleElimination, entrants);

        Guid firstRound = bracket.Rounds[0].Id;
        bracket.Matches
            .Where(m => m.RoundId == firstRound)
            .SelectMany(m => new[] { m.Player1Id, m.Player2Id })
            .Should().NotContain(entrants[2].PlayerId);
    }

    [Fact]
    public void Entrants_with_no_seed_go_to_the_back_in_the_order_they_joined()
    {
        var entrants = Entrants(4).Select(e => e with { Seed = 0 }).ToList();

        Bracket bracket = BracketBuilder.Build(TournamentId, TournamentFormat.SingleElimination, entrants);

        // Seed 1 in the fold is the first to have joined.
        Guid firstRound = bracket.Rounds[0].Id;
        TournamentMatch opener = bracket.Matches.First(m => m.RoundId == firstRound && m.Slot == 0);

        opener.Player1Id.Should().Be(entrants[0].PlayerId);
    }

    [Fact]
    public void Best_of_is_carried_onto_every_match()
    {
        Bracket bracket = BracketBuilder.Build(
            TournamentId, TournamentFormat.SingleElimination, Entrants(8), bestOf: 3);

        bracket.Matches.Should().OnlyContain(m => m.BestOf == 3);
    }

    [Fact]
    public void Every_match_belongs_to_a_round_of_the_tournament()
    {
        Bracket bracket = Build(8, TournamentFormat.DoubleElimination);
        var roundIds = bracket.Rounds.Select(r => r.Id).ToHashSet();

        bracket.Matches.Should().OnlyContain(m => roundIds.Contains(m.RoundId));
        bracket.Matches.Should().OnlyContain(m => m.TournamentId == TournamentId);
    }

    [Fact]
    public void Every_match_has_its_own_identity()
    {
        Bracket bracket = Build(16, TournamentFormat.DoubleElimination);

        bracket.Matches.Select(m => m.Id).Should().OnlyHaveUniqueItems();
        bracket.Rounds.Select(r => r.Id).Should().OnlyHaveUniqueItems();
    }
}

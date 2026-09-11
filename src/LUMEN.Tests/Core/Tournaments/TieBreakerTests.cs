using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Tournaments;
using Xunit;

namespace Lumen.Tests.Core.Tournaments;

public class TieBreakerTests
{
    private static readonly Guid MatchId = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static TournamentMatchResult Result(
        Guid player,
        long score = 900_000,
        double accuracy = 98.0,
        int miss = 0,
        int maxCombo = 500,
        int perfect = 400,
        int gameIndex = 0,
        DateTime? submitted = null,
        bool confirmed = true) => new()
        {
            Id = Guid.NewGuid(),
            MatchId = MatchId,
            PlayerId = player,
            ChartKey = "chart-key",
            GameIndex = gameIndex,
            Score = score,
            Accuracy = accuracy,
            Miss = miss,
            MaxCombo = maxCombo,
            Perfect = perfect,
            SubmittedUtc = submitted ?? new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            ConfirmedUtc = confirmed ? new DateTime(2026, 1, 1, 12, 5, 0, DateTimeKind.Utc) : null,
        };

    private static TournamentMatch Match(int bestOf = 1) => new()
    {
        Id = MatchId,
        TournamentId = Guid.NewGuid(),
        RoundId = Guid.NewGuid(),
        Slot = 0,
        Player1Id = Alice,
        Player2Id = Bob,
        BestOf = bestOf,
        Status = MatchStatus.ResultPending,
    };

    private static readonly TournamentRules Rules = TournamentRules.Official;

    // --- the criteria, one at a time ---

    [Fact]
    public void A_higher_score_wins()
    {
        TieBreaker.WinnerOf(
            Result(Alice, score: 980_000),
            Result(Bob, score: 970_000),
            Rules.TieBreak).Should().Be(Alice);
    }

    [Fact]
    public void With_equal_scores_the_better_accuracy_wins()
    {
        TieBreaker.WinnerOf(
            Result(Alice, score: 900_000, accuracy: 97.5),
            Result(Bob, score: 900_000, accuracy: 98.5),
            Rules.TieBreak).Should().Be(Bob);
    }

    /// <summary>Fewer misses is better, which is the one criterion that reads backwards.</summary>
    [Fact]
    public void With_equal_accuracy_fewer_misses_wins()
    {
        TieBreaker.WinnerOf(
            Result(Alice, miss: 3),
            Result(Bob, miss: 1),
            Rules.TieBreak).Should().Be(Bob);
    }

    [Fact]
    public void Then_the_longer_combo_wins()
    {
        TieBreaker.WinnerOf(
            Result(Alice, maxCombo: 600),
            Result(Bob, maxCombo: 500),
            Rules.TieBreak).Should().Be(Alice);
    }

    [Fact]
    public void Then_more_perfects_win()
    {
        TieBreaker.WinnerOf(
            Result(Alice, perfect: 410),
            Result(Bob, perfect: 400),
            Rules.TieBreak).Should().Be(Alice);
    }

    /// <summary>
    /// The last resort, and the reason a match can never be left level. A tournament that
    /// can produce a draw it has no rule for is a tournament that stops.
    /// </summary>
    [Fact]
    public void Two_identical_plays_are_settled_by_who_finished_first()
    {
        var early = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var late = early.AddMinutes(3);

        TieBreaker.WinnerOf(
            Result(Alice, submitted: late),
            Result(Bob, submitted: early),
            Rules.TieBreak).Should().Be(Bob);
    }

    [Fact]
    public void The_default_order_is_the_one_the_rules_document_names()
    {
        TournamentRules.DefaultTieBreak.Should().Equal(
            TieBreakCriterion.Score,
            TieBreakCriterion.Accuracy,
            TieBreakCriterion.MissCount,
            TieBreakCriterion.MaxCombo,
            TieBreakCriterion.PerfectCount,
            TieBreakCriterion.SubmissionTime);
    }

    /// <summary>An organiser can reorder them, and the reorder has to actually apply.</summary>
    [Fact]
    public void A_tournament_can_choose_a_different_order()
    {
        var accuracyFirst = new[] { TieBreakCriterion.Accuracy, TieBreakCriterion.Score };

        TournamentMatchResult higherScore = Result(Alice, score: 990_000, accuracy: 96.0);
        TournamentMatchResult betterAccuracy = Result(Bob, score: 950_000, accuracy: 99.0);

        TieBreaker.WinnerOf(higherScore, betterAccuracy, accuracyFirst).Should().Be(Bob);
        TieBreaker.WinnerOf(higherScore, betterAccuracy, Rules.TieBreak).Should().Be(Alice);
    }

    [Fact]
    public void With_no_criteria_left_there_is_no_winner()
    {
        TieBreaker.WinnerOf(Result(Alice), Result(Bob), Array.Empty<TieBreakCriterion>())
            .Should().BeNull();
    }

    // --- whole matches ---

    [Fact]
    public void A_single_game_match_is_decided_by_that_game()
    {
        MatchOutcome outcome = TieBreaker.Decide(
            Match(),
            new[] { Result(Alice, score: 990_000), Result(Bob, score: 900_000) },
            Rules);

        outcome.IsDecided.Should().BeTrue();
        outcome.WinnerPlayerId.Should().Be(Alice);
        outcome.WinnerGames.Should().Be(1);
        outcome.LoserGames.Should().Be(0);
    }

    [Fact]
    public void A_best_of_three_needs_two_games()
    {
        var results = new List<TournamentMatchResult>
        {
            Result(Alice, score: 990_000, gameIndex: 0),
            Result(Bob, score: 900_000, gameIndex: 0),
        };

        TieBreaker.Decide(Match(bestOf: 3), results, Rules).IsDecided.Should().BeFalse(
            "one game of three settles nothing");

        results.Add(Result(Alice, score: 995_000, gameIndex: 1));
        results.Add(Result(Bob, score: 900_000, gameIndex: 1));

        MatchOutcome outcome = TieBreaker.Decide(Match(bestOf: 3), results, Rules);
        outcome.WinnerPlayerId.Should().Be(Alice);
        outcome.WinnerGames.Should().Be(2);
    }

    [Fact]
    public void A_series_can_be_come_back_from()
    {
        var results = new List<TournamentMatchResult>
        {
            Result(Alice, score: 990_000, gameIndex: 0),
            Result(Bob, score: 900_000, gameIndex: 0),
            Result(Alice, score: 900_000, gameIndex: 1),
            Result(Bob, score: 990_000, gameIndex: 1),
            Result(Alice, score: 900_000, gameIndex: 2),
            Result(Bob, score: 995_000, gameIndex: 2),
        };

        MatchOutcome outcome = TieBreaker.Decide(Match(bestOf: 3), results, Rules);

        outcome.WinnerPlayerId.Should().Be(Bob);
        outcome.WinnerGames.Should().Be(2);
        outcome.LoserGames.Should().Be(1);
    }

    /// <summary>
    /// The hole an organiser exists to close: an unconfirmed result is a claim, and a claim
    /// must not settle a match.
    /// </summary>
    [Fact]
    public void An_unconfirmed_result_decides_nothing()
    {
        MatchOutcome outcome = TieBreaker.Decide(
            Match(),
            new[]
            {
                Result(Alice, score: 999_999, confirmed: false),
                Result(Bob, score: 100_000, confirmed: false),
            },
            Rules);

        outcome.IsDecided.Should().BeFalse();
        outcome.Reason.Should().Contain("confirmed");
    }

    [Fact]
    public void A_game_only_one_player_has_submitted_is_not_a_game_anybody_won()
    {
        MatchOutcome outcome = TieBreaker.Decide(
            Match(),
            new[] { Result(Alice, score: 990_000) },
            Rules);

        outcome.IsDecided.Should().BeFalse();
    }

    [Fact]
    public void A_bye_is_won_without_anybody_playing()
    {
        TournamentMatch bye = Match() with { Player2Id = null };

        MatchOutcome outcome = TieBreaker.Decide(bye, new[] { Result(Alice) }, Rules);

        outcome.WinnerPlayerId.Should().Be(Alice);
    }

    [Fact]
    public void A_match_with_nothing_in_it_says_why_it_is_undecided()
    {
        MatchOutcome outcome = TieBreaker.Decide(Match(), Array.Empty<TournamentMatchResult>(), Rules);

        outcome.IsDecided.Should().BeFalse();
        outcome.Reason.Should().NotBeEmpty();
    }

    [Fact]
    public void Results_from_another_match_are_ignored()
    {
        TournamentMatchResult elsewhere = Result(Alice, score: 999_999) with { MatchId = Guid.NewGuid() };

        TieBreaker.Decide(Match(), new[] { elsewhere }, Rules).IsDecided.Should().BeFalse();
    }

    // --- score attack ---

    [Fact]
    public void Standings_rank_players_by_their_best_confirmed_run()
    {
        Guid carol = Guid.NewGuid();

        var results = new[]
        {
            Result(Alice, score: 900_000),
            Result(Alice, score: 960_000),   // Alice's better run
            Result(Bob, score: 980_000),
            Result(carol, score: 700_000),
        };

        IReadOnlyList<StandingEntry> standings = TieBreaker.Standings(results, Rules);

        standings.Select(s => s.PlayerId).Should().Equal(Bob, Alice, carol);
        standings.Select(s => s.Rank).Should().Equal(1, 2, 3);
        standings[1].Best.Score.Should().Be(960_000, "a player is ranked on their best, not their last");
    }

    [Fact]
    public void Unconfirmed_runs_do_not_appear_in_the_standings()
    {
        var results = new[]
        {
            Result(Alice, score: 900_000),
            Result(Bob, score: 999_999, confirmed: false),
        };

        IReadOnlyList<StandingEntry> standings = TieBreaker.Standings(results, Rules);

        standings.Should().ContainSingle();
        standings[0].PlayerId.Should().Be(Alice);
    }

    /// <summary>
    /// The ranking and the head-to-head rules have to agree, or the same two plays produce
    /// different answers depending on which screen you are looking at.
    /// </summary>
    [Fact]
    public void Standings_break_ties_the_same_way_a_match_would()
    {
        var results = new[]
        {
            Result(Alice, score: 900_000, accuracy: 97.0),
            Result(Bob, score: 900_000, accuracy: 99.0),
        };

        IReadOnlyList<StandingEntry> standings = TieBreaker.Standings(results, Rules);

        standings[0].PlayerId.Should().Be(Bob);
        TieBreaker.WinnerOf(results[0], results[1], Rules.TieBreak).Should().Be(Bob);
    }

    [Fact]
    public void An_empty_field_ranks_nobody()
    {
        TieBreaker.Standings(Array.Empty<TournamentMatchResult>(), Rules).Should().BeEmpty();
    }
}

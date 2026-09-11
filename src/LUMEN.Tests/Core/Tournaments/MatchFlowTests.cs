using System;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Tournaments;
using Xunit;

namespace Lumen.Tests.Core.Tournaments;

public class MatchFlowTests
{
    private static TournamentMatch Match(MatchStatus status = MatchStatus.Pending) => new()
    {
        Id = Guid.NewGuid(),
        TournamentId = Guid.NewGuid(),
        RoundId = Guid.NewGuid(),
        Slot = 0,
        Player1Id = Guid.NewGuid(),
        Player2Id = Guid.NewGuid(),
        Status = status,
    };

    [Fact]
    public void A_match_walks_from_created_to_complete_one_step_at_a_time()
    {
        MatchStatus[] path =
        {
            MatchStatus.Pending,
            MatchStatus.Waiting,
            MatchStatus.SongSelected,
            MatchStatus.Ready,
            MatchStatus.Playing,
            MatchStatus.ResultPending,
            MatchStatus.Complete,
        };

        for (int i = 0; i < path.Length - 1; i++)
        {
            MatchFlow.CanTransition(path[i], path[i + 1]).Should().BeTrue(
                $"{path[i]} is meant to lead to {path[i + 1]}");
        }
    }

    /// <summary>
    /// The rule the state machine exists for. A match that arrives at Complete without
    /// passing through the states that record a song, a play and a result has nothing to
    /// show anybody who asks how it was decided.
    /// </summary>
    [Fact]
    public void A_match_cannot_jump_straight_to_a_winner()
    {
        MatchFlow.CanTransition(MatchStatus.Waiting, MatchStatus.Complete).Should().BeFalse();
        MatchFlow.CanTransition(MatchStatus.Pending, MatchStatus.Complete).Should().BeFalse();
        MatchFlow.CanTransition(MatchStatus.Pending, MatchStatus.Playing).Should().BeFalse();
        MatchFlow.CanTransition(MatchStatus.SongSelected, MatchStatus.Complete).Should().BeFalse();
    }

    [Fact]
    public void A_match_cannot_go_backwards_past_the_play()
    {
        MatchFlow.CanTransition(MatchStatus.Playing, MatchStatus.Ready).Should().BeFalse();
        MatchFlow.CanTransition(MatchStatus.ResultPending, MatchStatus.Playing).Should().BeFalse();
        MatchFlow.CanTransition(MatchStatus.Complete, MatchStatus.Playing).Should().BeFalse();
    }

    /// <summary>A pick can still be changed while nobody has committed to it.</summary>
    [Fact]
    public void A_song_choice_can_be_taken_back_before_the_players_are_ready()
    {
        MatchFlow.CanTransition(MatchStatus.SongSelected, MatchStatus.Waiting).Should().BeTrue();
        MatchFlow.CanTransition(MatchStatus.Ready, MatchStatus.SongSelected).Should().BeTrue();
    }

    /// <summary>
    /// A submitted result can be sent back — for the next game of a series, or because the
    /// organiser rejected it. Without this an organiser's only options are accept or void.
    /// </summary>
    [Fact]
    public void A_result_can_be_sent_back_for_another_game()
    {
        MatchFlow.CanTransition(MatchStatus.ResultPending, MatchStatus.Ready).Should().BeTrue();
    }

    [Fact]
    public void A_finished_match_stays_finished()
    {
        foreach (MatchStatus terminal in new[] { MatchStatus.Complete, MatchStatus.Void })
        {
            MatchFlow.IsTerminal(terminal).Should().BeTrue();
            MatchFlow.NextStates(terminal).Should().BeEmpty();

            foreach (MatchStatus any in Enum.GetValues<MatchStatus>())
            {
                MatchFlow.CanTransition(terminal, any).Should().BeFalse();
            }
        }
    }

    [Fact]
    public void A_match_can_be_voided_from_anywhere_it_is_still_live()
    {
        MatchStatus[] live =
        {
            MatchStatus.Pending, MatchStatus.Waiting, MatchStatus.SongSelected,
            MatchStatus.Ready, MatchStatus.Playing, MatchStatus.ResultPending,
        };

        foreach (MatchStatus status in live)
        {
            MatchFlow.CanTransition(status, MatchStatus.Void).Should().BeTrue(
                $"{status} must be abandonable — players do not turn up");
        }
    }

    [Fact]
    public void Staying_where_it_is_is_not_a_transition()
    {
        foreach (MatchStatus status in Enum.GetValues<MatchStatus>())
        {
            MatchFlow.CanTransition(status, status).Should().BeFalse();
        }
    }

    /// <summary>
    /// Throwing rather than returning a flag: an invalid transition is a bug in the caller,
    /// and swallowing it would leave the bracket quietly wrong.
    /// </summary>
    [Fact]
    public void An_impossible_move_throws_and_says_what_was_possible()
    {
        Action jump = () => MatchFlow.Transition(Match(MatchStatus.Waiting), MatchStatus.Complete);

        jump.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot go from Waiting to Complete*")
            .WithMessage("*SongSelected*");
    }

    [Fact]
    public void Finishing_a_match_that_is_already_over_says_so_plainly()
    {
        Action again = () => MatchFlow.Transition(Match(MatchStatus.Complete), MatchStatus.Void);

        again.Should().Throw<InvalidOperationException>().WithMessage("*it is finished*");
    }

    [Fact]
    public void Starting_play_stamps_when_it_started()
    {
        TournamentMatch ready = Match(MatchStatus.Ready);
        ready.StartedUtc.Should().BeNull();

        TournamentMatch playing = MatchFlow.Transition(ready, MatchStatus.Playing);

        playing.StartedUtc.Should().NotBeNull();
        playing.CompletedUtc.Should().BeNull();
    }

    [Fact]
    public void Finishing_stamps_when_it_finished()
    {
        TournamentMatch pending = Match(MatchStatus.ResultPending);

        TournamentMatch complete = MatchFlow.Transition(pending, MatchStatus.Complete);

        complete.CompletedUtc.Should().NotBeNull();
    }

    [Fact]
    public void A_restart_does_not_rewrite_when_the_match_first_started()
    {
        TournamentMatch playing = MatchFlow.Transition(Match(MatchStatus.Ready), MatchStatus.Playing);
        DateTime? first = playing.StartedUtc;

        TournamentMatch pending = MatchFlow.Transition(playing, MatchStatus.ResultPending);
        TournamentMatch back = MatchFlow.Transition(pending, MatchStatus.Ready);
        TournamentMatch again = MatchFlow.Transition(back, MatchStatus.Playing);

        again.StartedUtc.Should().Be(first);
    }

    // --- playability ---

    [Fact]
    public void A_match_with_no_song_on_it_is_not_playable()
    {
        TournamentMatch ready = Match(MatchStatus.Ready);

        MatchFlow.IsPlayable(ready).Should().BeFalse("there is no chart to play");
    }

    [Fact]
    public void A_ready_match_with_a_song_and_two_players_is_playable()
    {
        TournamentMatch ready = Match(MatchStatus.Ready) with
        {
            SelectedChartIds = new[] { Guid.NewGuid() },
        };

        MatchFlow.IsPlayable(ready).Should().BeTrue();
    }

    [Fact]
    public void A_match_nobody_is_ready_for_is_not_playable()
    {
        TournamentMatch waiting = Match(MatchStatus.Waiting) with
        {
            SelectedChartIds = new[] { Guid.NewGuid() },
        };

        MatchFlow.IsPlayable(waiting).Should().BeFalse();
    }

    // --- best of ---

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(5, 3)]
    [InlineData(7, 4)]
    public void Winning_a_series_takes_more_than_half_of_it(int bestOf, int needed)
    {
        MatchFlow.GamesToWin(bestOf).Should().Be(needed);
    }

    [Fact]
    public void A_nonsense_series_length_still_needs_one_win()
    {
        MatchFlow.GamesToWin(0).Should().Be(1);
        MatchFlow.GamesToWin(-3).Should().Be(1);
    }

    // --- the match record itself ---

    [Fact]
    public void A_match_knows_who_is_in_it_and_who_faces_whom()
    {
        TournamentMatch match = Match();
        Guid one = match.Player1Id!.Value;
        Guid two = match.Player2Id!.Value;

        match.Involves(one).Should().BeTrue();
        match.Involves(two).Should().BeTrue();
        match.Involves(Guid.NewGuid()).Should().BeFalse();

        match.Opponent(one).Should().Be(two);
        match.Opponent(two).Should().Be(one);
        match.Opponent(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void One_empty_seat_is_a_bye_and_two_filled_ones_are_not()
    {
        TournamentMatch both = Match();
        both.IsBye.Should().BeFalse();
        both.HasBothPlayers.Should().BeTrue();

        TournamentMatch bye = both with { Player2Id = null };
        bye.IsBye.Should().BeTrue();
        bye.HasBothPlayers.Should().BeFalse();

        TournamentMatch empty = both with { Player1Id = null, Player2Id = null };
        empty.IsBye.Should().BeFalse("an empty slot is not a bye, it is just not filled in yet");
    }
}

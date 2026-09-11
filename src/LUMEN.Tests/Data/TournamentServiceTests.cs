using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Tournaments;
using Lumen.Data;
using Lumen.Data.Repositories;
using Lumen.Data.Tournaments;
using Xunit;

namespace Lumen.Tests.Data;

/// <summary>
/// A tournament run from end to end, through the service and the real database.
///
/// The domain tests already pin the rules; this is about whether the pieces hold together
/// once a bracket is written down, results come back out of order, somebody is disqualified
/// and the database is closed and reopened.
/// </summary>
public class TournamentServiceTests : IDisposable
{
    private readonly string _dir;
    private Database _db;
    private TournamentService _service;

    private const string Chart = "chart-key-first-light";

    public TournamentServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-tour-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        (_db, _service) = Open();
    }

    private (Database, TournamentService) Open()
    {
        var db = new Database(Path.Combine(_dir, "database", GameIdentity.DatabaseFileName));
        db.Open();
        return (db, new TournamentService(new TournamentRepository(db)));
    }

    /// <summary>Closes and reopens, the way quitting and relaunching the game would.</summary>
    private void Restart()
    {
        _db.Dispose();
        (_db, _service) = Open();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // --- helpers ---

    private Tournament Create(
        TournamentFormat format = TournamentFormat.SingleElimination,
        TournamentRules? rules = null) =>
        _service.Create("Winter Open", format, rules ?? TournamentRules.Official, "LUMEN");

    private List<Guid> Enter(Tournament tournament, int count)
    {
        var ids = new List<Guid>();
        for (int i = 1; i <= count; i++)
        {
            var id = Guid.NewGuid();
            _service.AddParticipant(tournament.Id, id, $"Player {i}", seed: i);
            ids.Add(id);
        }

        return ids;
    }

    private void AddChart(Tournament tournament, string? chartKey = null) =>
        _service.AddSong(tournament.Id, new TournamentSong
        {
            TournamentId = tournament.Id,
            ChartKey = chartKey ?? Chart,
            Title = "First Light",
            DifficultyName = "NORMAL",
            Level = 3.5,
            ChartHash = "chart-hash",
            AudioHash = "audio-hash",
        });

    private TournamentMatchResult Play(
        TournamentMatch match, Guid player, long score, int gameIndex = 0) => new()
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            PlayerId = player,
            ChartKey = match.SelectedChartKeys.FirstOrDefault() ?? Chart,
            GameIndex = gameIndex,
            Score = score,
            Accuracy = score / 10_000.0,
            MaxCombo = 500,
            Perfect = 400,
            ChartHash = "chart-hash",
        };

    /// <summary>Plays one match through, with <paramref name="winner"/> scoring higher.</summary>
    private void Settle(TournamentMatch match, Guid winner)
    {
        _service.SelectSongs(match.Id, new[] { Chart });
        _service.MarkReady(match.Id);
        _service.StartMatch(match.Id);

        Guid loser = match.Opponent(winner)!.Value;

        TournamentMatchResult high = _service.SubmitResult(Play(match, winner, 990_000));
        TournamentMatchResult low = _service.SubmitResult(Play(match, loser, 900_000));

        _service.ConfirmResult(high.Id, match.Id);
        _service.ConfirmResult(low.Id, match.Id);
    }

    // --- creating ---

    [Fact]
    public void A_tournament_is_created_with_the_build_and_rules_it_will_run_under()
    {
        Tournament tournament = Create();

        tournament.Name.Should().Be("Winter Open");
        tournament.Status.Should().Be(TournamentStatus.Draft);
        tournament.GameVersion.Should().Be(GameIdentity.FullVersion);
        tournament.RuleHash.Should().Be(TournamentRules.Official.Hash());

        _service.Get(tournament.Id).Should().NotBeNull();
    }

    [Fact]
    public void A_tournament_with_no_name_is_refused()
    {
        Action create = () => _service.Create("  ", TournamentFormat.SingleElimination, TournamentRules.Official);

        create.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Fact]
    public void Rules_that_cannot_run_a_tournament_are_refused_at_the_door()
    {
        Action create = () => _service.Create(
            "Broken", TournamentFormat.SingleElimination, TournamentRules.Official with { BestOf = 2 });

        create.Should().Throw<ArgumentException>().WithMessage("*odd*");
    }

    // --- participants ---

    [Fact]
    public void Participants_are_entered_and_listed_in_seed_order()
    {
        Tournament tournament = Create();
        Enter(tournament, 4);

        _service.Participants(tournament.Id).Select(p => p.Seed).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void The_same_player_cannot_enter_twice()
    {
        Tournament tournament = Create();
        var id = Guid.NewGuid();
        _service.AddParticipant(tournament.Id, id, "Nagisa");

        Action again = () => _service.AddParticipant(tournament.Id, id, "Nagisa");

        again.Should().Throw<InvalidOperationException>().WithMessage("*already entered*");
    }

    [Fact]
    public void A_participant_can_be_removed_before_the_draw()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 4);

        _service.RemoveParticipant(tournament.Id, players[1]);

        _service.Participants(tournament.Id).Should().HaveCount(3);
    }

    /// <summary>
    /// The bracket is drawn from the entrants, so changing them afterwards would mean a
    /// bracket that no longer matches the tournament it belongs to.
    /// </summary>
    [Fact]
    public void The_field_cannot_change_once_the_bracket_is_drawn()
    {
        Tournament tournament = Create();
        Enter(tournament, 4);
        AddChart(tournament);
        _service.Start(tournament.Id);

        Action add = () => _service.AddParticipant(tournament.Id, Guid.NewGuid(), "Latecomer");

        add.Should().Throw<InvalidOperationException>().WithMessage("*already drawn*");
    }

    // --- starting ---

    [Fact]
    public void Starting_draws_a_bracket_and_locks_the_rules()
    {
        Tournament tournament = Create();
        Enter(tournament, 4);
        AddChart(tournament);

        Tournament started = _service.Start(tournament.Id);

        started.Status.Should().Be(TournamentStatus.Running);
        started.StartedUtc.Should().NotBeNull();
        started.RuleHash.Should().Be(TournamentRules.Official.Hash());

        _service.Rounds(tournament.Id).Should().HaveCount(2);
        _service.Matches(tournament.Id).Should().HaveCount(3);
    }

    [Fact]
    public void A_tournament_with_noCharts_cannot_start()
    {
        Tournament tournament = Create();
        Enter(tournament, 4);

        Action start = () => _service.Start(tournament.Id);

        start.Should().Throw<InvalidOperationException>().WithMessage("*at least one chart*");
    }

    [Fact]
    public void A_tournament_with_one_entrant_cannot_start()
    {
        Tournament tournament = Create();
        Enter(tournament, 1);
        AddChart(tournament);

        Action start = () => _service.Start(tournament.Id);

        start.Should().Throw<InvalidOperationException>().WithMessage("*at least 2*");
    }

    [Fact]
    public void A_tournament_cannot_be_started_twice()
    {
        Tournament tournament = Create();
        Enter(tournament, 4);
        AddChart(tournament);
        _service.Start(tournament.Id);

        Action again = () => _service.Start(tournament.Id);

        again.Should().Throw<InvalidOperationException>().WithMessage("*already been started*");
    }

    // --- the whole thing ---

    /// <summary>
    /// Four players, three matches, one champion — and the tournament notices it is over
    /// without being told.
    /// </summary>
    [Fact]
    public void A_four_player_tournament_runs_to_a_champion()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 4);
        AddChart(tournament);
        _service.Start(tournament.Id);

        IReadOnlyList<TournamentRound> rounds = _service.Rounds(tournament.Id);

        // Semi finals: seeds 1 and 2 go through.
        foreach (TournamentMatch semi in _service.Matches(tournament.Id)
                     .Where(m => m.RoundId == rounds[0].Id).ToList())
        {
            Guid favourite = semi.Player1Id!.Value;
            Settle(semi, favourite);
        }

        TournamentMatch final = _service.Matches(tournament.Id).Single(m => m.RoundId == rounds[1].Id);
        final.HasBothPlayers.Should().BeTrue("the semi final winners must have been seated");

        Settle(final, final.Player1Id!.Value);

        Tournament finished = _service.Get(tournament.Id)!;
        finished.Status.Should().Be(TournamentStatus.Complete);
        finished.WinnerPlayerId.Should().Be(players[0], "the top seed won every match");
        finished.FinishedUtc.Should().NotBeNull();
    }

    [Fact]
    public void A_match_result_is_not_counted_until_it_is_confirmed()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 2);
        AddChart(tournament);
        _service.Start(tournament.Id);

        TournamentMatch match = _service.Matches(tournament.Id).Single();
        _service.SelectSongs(match.Id, new[] { Chart });
        _service.MarkReady(match.Id);
        _service.StartMatch(match.Id);

        _service.SubmitResult(Play(match, players[0], 990_000));
        _service.SubmitResult(Play(match, players[1], 900_000));

        _service.Get(tournament.Id)!.Status.Should().Be(TournamentStatus.Running,
            "an unconfirmed result decides nothing");
        _service.Matches(tournament.Id).Single().WinnerPlayerId.Should().BeNull();
    }

    [Fact]
    public void A_match_cannot_be_started_before_aChart_is_chosen()
    {
        Tournament tournament = Create();
        Enter(tournament, 2);
        AddChart(tournament);
        _service.Start(tournament.Id);

        TournamentMatch match = _service.Matches(tournament.Id).Single();

        Action start = () => _service.StartMatch(match.Id);

        start.Should().Throw<InvalidOperationException>().WithMessage("*needs a chart*");
    }

    /// <summary>One attempt means one attempt, and the rules are what say so.</summary>
    [Fact]
    public void The_attempt_limit_is_enforced()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 2);
        AddChart(tournament);
        _service.Start(tournament.Id);

        TournamentMatch match = _service.Matches(tournament.Id).Single();
        _service.SelectSongs(match.Id, new[] { Chart });
        _service.MarkReady(match.Id);
        _service.StartMatch(match.Id);

        _service.SubmitResult(Play(match, players[0], 900_000));

        Action again = () => _service.SubmitResult(Play(match, players[0], 990_000));

        again.Should().Throw<InvalidOperationException>().WithMessage("*1 attempt*");
    }

    [Fact]
    public void A_more_forgiving_rule_set_allows_more_attempts()
    {
        Tournament tournament = Create(rules: TournamentRules.Casual);
        List<Guid> players = Enter(tournament, 2);
        AddChart(tournament);
        _service.Start(tournament.Id);

        TournamentMatch match = _service.Matches(tournament.Id).Single();
        _service.SelectSongs(match.Id, new[] { Chart });
        _service.MarkReady(match.Id);
        _service.StartMatch(match.Id);

        _service.SubmitResult(Play(match, players[0], 900_000));
        _service.SubmitResult(Play(match, players[0], 950_000));

        Action third = () => _service.SubmitResult(Play(match, players[0], 970_000));
        third.Should().NotThrow();
    }

    // --- byes ---

    /// <summary>
    /// A field that is not a power of two is normal, and the players with byes have to be
    /// moved up before anybody sits down to play.
    /// </summary>
    [Fact]
    public void Byes_are_resolved_when_the_bracket_is_drawn()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 3);
        AddChart(tournament);
        _service.Start(tournament.Id);

        IReadOnlyList<TournamentRound> rounds = _service.Rounds(tournament.Id);
        TournamentMatch final = _service.Matches(tournament.Id).Single(m => m.RoundId == rounds[1].Id);

        // The top seed had the bye, so they are already in the final.
        final.Player1Id.Should().Be(players[0]);
        final.Player2Id.Should().BeNull("the other semi final has not been played");
    }

    // --- disqualification ---

    [Fact]
    public void Disqualifying_a_player_hands_their_live_match_to_their_opponent()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 4);
        AddChart(tournament);
        _service.Start(tournament.Id);

        TournamentMatch semi = _service.Matches(tournament.Id).First(m => m.HasBothPlayers);
        Guid removed = semi.Player1Id!.Value;
        Guid survivor = semi.Player2Id!.Value;

        _service.Disqualify(tournament.Id, removed, "cheating");

        TournamentMatch after = _service.Matches(tournament.Id).Single(m => m.Id == semi.Id);
        after.Status.Should().Be(MatchStatus.Void);
        after.WinnerPlayerId.Should().Be(survivor);

        _service.Participants(tournament.Id)
            .Single(p => p.PlayerId == removed).Status
            .Should().Be(ParticipantStatus.Disqualified);
    }

    /// <summary>
    /// Their finished matches stand. Those games were played, and deleting them would
    /// rewrite history to tidy up the present.
    /// </summary>
    [Fact]
    public void Disqualification_leaves_matches_that_were_already_played_alone()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 4);
        AddChart(tournament);
        _service.Start(tournament.Id);

        IReadOnlyList<TournamentRound> rounds = _service.Rounds(tournament.Id);
        TournamentMatch semi = _service.Matches(tournament.Id).First(m => m.RoundId == rounds[0].Id);
        Guid winner = semi.Player1Id!.Value;

        Settle(semi, winner);

        _service.Disqualify(tournament.Id, winner);

        TournamentMatch after = _service.Matches(tournament.Id).Single(m => m.Id == semi.Id);
        after.Status.Should().Be(MatchStatus.Complete);
        after.WinnerPlayerId.Should().Be(winner, "that match was played and won");
    }

    // --- score attack ---

    [Fact]
    public void A_score_attack_ranks_everybody_who_played()
    {
        Tournament tournament = Create(TournamentFormat.ScoreAttack);
        List<Guid> players = Enter(tournament, 3);
        AddChart(tournament);
        _service.Start(tournament.Id);

        var scores = new long[] { 800_000, 990_000, 900_000 };

        foreach ((TournamentMatch match, int i) in _service.Matches(tournament.Id).Select((m, i) => (m, i)))
        {
            _service.SelectSongs(match.Id, new[] { Chart });
            _service.MarkReady(match.Id);
            _service.StartMatch(match.Id);

            TournamentMatchResult result = _service.SubmitResult(
                Play(match, match.Player1Id!.Value, scores[i]));
            _service.ConfirmResult(result.Id, match.Id);
        }

        IReadOnlyList<StandingEntry> standings = _service.FinishScoreAttack(tournament.Id);

        standings.Select(s => s.PlayerId).Should().Equal(players[1], players[2], players[0]);
        _service.Get(tournament.Id)!.WinnerPlayerId.Should().Be(players[1]);
    }

    // --- the audit log ---

    /// <summary>
    /// The difference between a result and a result somebody can check. Every action that
    /// changes the tournament has to leave a line behind.
    /// </summary>
    [Fact]
    public void Everything_that_happens_is_written_down()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 2);
        AddChart(tournament);
        _service.Start(tournament.Id);

        TournamentMatch match = _service.Matches(tournament.Id).Single();
        Settle(match, players[0]);

        List<string> types = _service.Events(tournament.Id).Select(e => e.Type).ToList();

        types.Should().Contain(TournamentEventTypes.Created);
        types.Should().Contain(TournamentEventTypes.ParticipantJoined);
        types.Should().Contain(TournamentEventTypes.SongAdded);
        types.Should().Contain(TournamentEventTypes.Started);
        types.Should().Contain(TournamentEventTypes.BracketGenerated);
        types.Should().Contain(TournamentEventTypes.SongSelected);
        types.Should().Contain(TournamentEventTypes.MatchStarted);
        types.Should().Contain(TournamentEventTypes.ResultSubmitted);
        types.Should().Contain(TournamentEventTypes.ResultConfirmed);
        types.Should().Contain(TournamentEventTypes.MatchCompleted);
        types.Should().Contain(TournamentEventTypes.Completed);
    }

    [Fact]
    public void The_log_is_in_the_order_things_happened()
    {
        Tournament tournament = Create();
        Enter(tournament, 2);
        AddChart(tournament);
        _service.Start(tournament.Id);

        IReadOnlyList<TournamentEvent> events = _service.Events(tournament.Id);

        events.Should().BeInAscendingOrder(e => e.Timestamp);
        events[0].Type.Should().Be(TournamentEventTypes.Created);
    }

    [Fact]
    public void A_disqualification_says_who_and_why()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 4);
        AddChart(tournament);
        _service.Start(tournament.Id);

        _service.Disqualify(tournament.Id, players[0], "used autoplay");

        TournamentEvent recorded = _service.Events(tournament.Id)
            .Last(e => e.Type == TournamentEventTypes.ParticipantDisqualified);

        recorded.ActorPlayerId.Should().Be(players[0]);
        recorded.Payload.Should().Contain("used autoplay");
    }

    // --- results carry their conditions ---

    /// <summary>
    /// A number in a table proves nothing on its own. Which build, which rules, which
    /// chart — those are what make a result arguable rather than merely asserted.
    /// </summary>
    [Fact]
    public void A_result_records_the_conditions_it_was_set_under()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 2);
        AddChart(tournament);
        _service.Start(tournament.Id);

        TournamentMatch match = _service.Matches(tournament.Id).Single();
        _service.SelectSongs(match.Id, new[] { Chart });
        _service.MarkReady(match.Id);
        _service.StartMatch(match.Id);

        TournamentMatchResult stored = _service.SubmitResult(Play(match, players[0], 990_000));

        stored.GameVersion.Should().Be(GameIdentity.FullVersion);
        stored.RuleHash.Should().Be(TournamentRules.Official.Hash());
        stored.ChartHash.Should().Be("chart-hash");
        stored.SubmittedUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
        stored.IsConfirmed.Should().BeFalse("submitting is not accepting");
    }

    // --- survival ---

    /// <summary>
    /// A tournament runs over an evening, and an evening includes somebody closing the
    /// game. Everything has to still be there afterwards.
    /// </summary>
    [Fact]
    public void A_tournament_in_progress_survives_the_game_being_closed()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 4);
        AddChart(tournament);
        _service.Start(tournament.Id);

        IReadOnlyList<TournamentRound> rounds = _service.Rounds(tournament.Id);
        TournamentMatch semi = _service.Matches(tournament.Id).First(m => m.RoundId == rounds[0].Id);
        Settle(semi, semi.Player1Id!.Value);

        Restart();

        Tournament reloaded = _service.Get(tournament.Id)!;
        reloaded.Status.Should().Be(TournamentStatus.Running);
        reloaded.Name.Should().Be("Winter Open");
        reloaded.Rules.MaxAttempts.Should().Be(1, "the rules came back too");

        _service.Participants(tournament.Id).Should().HaveCount(4);
        _service.Matches(tournament.Id).Should().HaveCount(3);
        _service.Matches(tournament.Id).Single(m => m.Id == semi.Id)
            .WinnerPlayerId.Should().Be(semi.Player1Id);

        _service.Events(tournament.Id).Should().NotBeEmpty();
    }

    [Fact]
    public void A_finished_tournament_can_be_read_back_whole()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 2);
        AddChart(tournament);
        _service.Start(tournament.Id);

        TournamentMatch match = _service.Matches(tournament.Id).Single();
        Settle(match, players[1]);

        Restart();

        Tournament finished = _service.Get(tournament.Id)!;
        finished.Status.Should().Be(TournamentStatus.Complete);
        finished.WinnerPlayerId.Should().Be(players[1]);
        _service.Results(tournament.Id).Should().HaveCount(2);
        _service.Results(tournament.Id).Should().OnlyContain(r => r.IsConfirmed);
    }

    // --- the player's record ---

    [Fact]
    public void A_players_tournament_record_is_kept_apart_from_their_rating()
    {
        Tournament tournament = Create();
        List<Guid> players = Enter(tournament, 2);
        AddChart(tournament);
        _service.Start(tournament.Id);

        TournamentMatch match = _service.Matches(tournament.Id).Single();
        Settle(match, players[0]);

        TournamentRecord winner = _service.RecordFor(players[0]);
        winner.MatchesPlayed.Should().Be(1);
        winner.Wins.Should().Be(1);
        winner.Losses.Should().Be(0);
        winner.Championships.Should().Be(1);
        winner.BestPlacement.Should().Be(1);
        winner.WinRate.Should().Be(1.0);

        TournamentRecord loser = _service.RecordFor(players[1]);
        loser.Wins.Should().Be(0);
        loser.Losses.Should().Be(1);
        loser.Championships.Should().Be(0);
    }

    [Fact]
    public void Somebody_who_has_never_entered_one_has_an_empty_record()
    {
        TournamentRecord none = _service.RecordFor(Guid.NewGuid());

        none.MatchesPlayed.Should().Be(0);
        none.WinRate.Should().Be(0);
        none.BestPlacement.Should().BeNull();
    }

    // --- listing ---

    [Fact]
    public void Tournaments_are_listed_newest_first()
    {
        _service.Create("First", TournamentFormat.SingleElimination, TournamentRules.Official);
        System.Threading.Thread.Sleep(10);
        _service.Create("Second", TournamentFormat.ScoreAttack, TournamentRules.Official);

        _service.All().Select(t => t.Name).Should().Equal("Second", "First");
    }

    [Fact]
    public void Cancelling_keeps_the_tournament_and_its_log()
    {
        Tournament tournament = Create();
        Enter(tournament, 2);

        _service.Cancel(tournament.Id, "not enough players turned up");

        Tournament cancelled = _service.Get(tournament.Id)!;
        cancelled.Status.Should().Be(TournamentStatus.Cancelled);
        _service.Events(tournament.Id).Should().Contain(e => e.Type == TournamentEventTypes.Cancelled);
    }
}

using System.Text.Json;
using Lumen.Core;
using Lumen.Core.Diagnostics;
using Lumen.Core.Tournaments;

namespace Lumen.Data.Tournaments;

/// <summary>
/// Running a tournament (spec: Tournament Dashboard / Match進行).
///
/// The one door between the screens and a tournament. Screens call this; they never write
/// to the repository themselves. That is not ceremony — every action here also writes an
/// event, applies the state machine, and advances the bracket, and a screen that reached
/// past it would produce a tournament whose log no longer explains it.
///
/// Nothing here touches the player's normal scores, Rating or PP. A tournament result is a
/// separate fact about a separate competition, recorded in its own tables, and that
/// separation is the whole reason the tournament tables exist rather than a flag on
/// <c>scores</c>.
/// </summary>
public sealed class TournamentService
{
    private readonly ITournamentRepository _repo;

    public TournamentService(ITournamentRepository repo) => _repo = repo;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // --- setting up ---

    public Tournament Create(
        string name,
        TournamentFormat format,
        TournamentRules rules,
        string organizer = "",
        string description = "")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A tournament needs a name.", nameof(name));
        }

        IReadOnlyList<string> problems = rules.Problems();
        if (problems.Count > 0)
        {
            throw new ArgumentException(
                "These rules cannot run a tournament: " + string.Join(" ", problems), nameof(rules));
        }

        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description.Trim(),
            Organizer = organizer.Trim(),
            Format = format,
            Rules = rules,
            Status = TournamentStatus.Draft,
            GameVersion = GameIdentity.FullVersion,
            RandomSeed = Random.Shared.Next(),
            CreatedUtc = DateTime.UtcNow,
        };

        Tournament created = _repo.Create(tournament);

        Record(created.Id, TournamentEventTypes.Created, null, new
        {
            name = created.Name,
            format = created.Format.ToString(),
            ruleHash = created.RuleHash,
            gameVersion = created.GameVersion,
        });

        Log.Info($"tournament created: {created.Name} ({created.Format}) {created.Id}");
        return created;
    }

    public void AddParticipant(Guid tournamentId, Guid playerId, string displayName, int seed = 0)
    {
        Tournament tournament = Require(tournamentId);
        RequireDraft(tournament, "add a participant");

        if (_repo.Participants(tournamentId).Any(p => p.PlayerId == playerId))
        {
            throw new InvalidOperationException($"{displayName} is already entered.");
        }

        _repo.AddParticipant(new TournamentParticipant
        {
            TournamentId = tournamentId,
            PlayerId = playerId,
            DisplayName = displayName,
            Seed = seed,
        });

        Record(tournamentId, TournamentEventTypes.ParticipantJoined, playerId,
            new { displayName, seed });
    }

    public void RemoveParticipant(Guid tournamentId, Guid playerId)
    {
        Tournament tournament = Require(tournamentId);
        RequireDraft(tournament, "remove a participant");

        _repo.RemoveParticipant(tournamentId, playerId);
        Record(tournamentId, TournamentEventTypes.ParticipantRemoved, playerId, new { });
    }

    /// <summary>
    /// Removes somebody from a running tournament.
    ///
    /// Their finished matches stand — those games were played and the log says so — but
    /// they take no further part, and any live match of theirs is handed to their opponent.
    /// Deleting them instead would rewrite history to tidy up the present.
    /// </summary>
    public void Disqualify(Guid tournamentId, Guid playerId, string reason = "")
    {
        Tournament tournament = Require(tournamentId);

        TournamentParticipant? participant = _repo.Participants(tournamentId)
            .FirstOrDefault(p => p.PlayerId == playerId);

        if (participant is null)
        {
            throw new InvalidOperationException("That player is not in this tournament.");
        }

        _repo.UpdateParticipant(participant with { Status = ParticipantStatus.Disqualified });

        foreach (TournamentMatch match in _repo.Matches(tournamentId)
                     .Where(m => m.Involves(playerId) && !MatchFlow.IsTerminal(m.Status)))
        {
            Guid? opponent = match.Opponent(playerId);

            if (opponent is null)
            {
                _repo.UpdateMatch(match with { Status = MatchStatus.Void, CompletedUtc = DateTime.UtcNow });
                continue;
            }

            _repo.UpdateMatch(match with
            {
                Status = MatchStatus.Void,
                WinnerPlayerId = opponent,
                CompletedUtc = DateTime.UtcNow,
            });

            AdvanceWinner(tournament, match.Id, opponent.Value);
        }

        Record(tournamentId, TournamentEventTypes.ParticipantDisqualified, playerId, new { reason });
        Log.Info($"tournament {tournamentId}: disqualified {participant.DisplayName}");
    }

    public void AddSong(Guid tournamentId, TournamentSong song)
    {
        Tournament tournament = Require(tournamentId);
        RequireDraft(tournament, "change the song pool");

        _repo.AddSong(song with { TournamentId = tournamentId });
        Record(tournamentId, TournamentEventTypes.SongAdded, null,
            new { chartId = song.ChartId, title = song.Title, chartHash = song.ChartHash });
    }

    public void RemoveSong(Guid tournamentId, Guid chartId)
    {
        Tournament tournament = Require(tournamentId);
        RequireDraft(tournament, "change the song pool");

        _repo.RemoveSong(tournamentId, chartId);
        Record(tournamentId, TournamentEventTypes.SongRemoved, null, new { chartId });
    }

    // --- starting ---

    /// <summary>
    /// Draws the bracket and locks the tournament.
    ///
    /// The rule hash is fixed here rather than at creation, because until this moment the
    /// rules could still be edited. After it they cannot, and every result recorded from
    /// now on can be checked against the rules that were actually in force.
    /// </summary>
    public Tournament Start(Guid tournamentId)
    {
        Tournament tournament = Require(tournamentId);

        if (tournament.Status != TournamentStatus.Draft)
        {
            throw new InvalidOperationException(
                $"This tournament has already been started — it is {tournament.Status}.");
        }

        IReadOnlyList<TournamentParticipant> participants = _repo.Participants(tournamentId);
        IReadOnlyList<TournamentSong> songs = _repo.Songs(tournamentId);

        if (songs.Count == 0)
        {
            throw new InvalidOperationException("Add at least one chart before starting.");
        }

        Bracket bracket = BracketBuilder.Build(
            tournamentId, tournament.Format, participants, tournament.Rules.BestOf);

        _repo.SaveBracket(tournamentId, bracket);

        Tournament started = tournament with
        {
            Status = TournamentStatus.Running,
            StartedUtc = DateTime.UtcNow,
            RuleHash = tournament.Rules.Hash(),
            GameVersion = GameIdentity.FullVersion,
        };

        _repo.Update(started);

        Record(tournamentId, TournamentEventTypes.Started, null, new
        {
            participants = participants.Count,
            songs = songs.Count,
            ruleHash = started.RuleHash,
        });

        Record(tournamentId, TournamentEventTypes.BracketGenerated, null, new
        {
            rounds = bracket.Rounds.Count,
            matches = bracket.Matches.Count,
        });

        // A bye is settled the moment the bracket is drawn, so the players who have one
        // move up before anybody sits down to play.
        foreach (TournamentMatch bye in bracket.Matches.Where(m => m.WinnerPlayerId is not null))
        {
            AdvanceWinner(started, bye.Id, bye.WinnerPlayerId!.Value);
        }

        Log.Info($"tournament started: {started.Name} — {participants.Count} players, {bracket.Matches.Count} matches");
        return started;
    }

    // --- running a match ---

    public void SelectSongs(Guid matchId, IReadOnlyList<Guid> chartIds, Guid? actor = null)
    {
        TournamentMatch match = RequireMatch(matchId);

        if (chartIds.Count == 0)
        {
            throw new ArgumentException("A match needs at least one chart.", nameof(chartIds));
        }

        TournamentMatch moved = MatchFlow.Transition(
            match with { SelectedChartIds = chartIds.ToArray() }, MatchStatus.SongSelected);

        _repo.UpdateMatch(moved);
        Record(match.TournamentId, TournamentEventTypes.SongSelected, actor,
            new { matchId, chartIds });
    }

    /// <summary>Both players have confirmed; the match may be played.</summary>
    public void MarkReady(Guid matchId)
    {
        TournamentMatch match = RequireMatch(matchId);
        _repo.UpdateMatch(MatchFlow.Transition(match, MatchStatus.Ready));
    }

    public void StartMatch(Guid matchId, Guid? actor = null)
    {
        TournamentMatch match = RequireMatch(matchId);

        if (!MatchFlow.IsPlayable(match))
        {
            throw new InvalidOperationException(
                "That match is not ready to be played — it needs a chart and both players.");
        }

        _repo.UpdateMatch(MatchFlow.Transition(match, MatchStatus.Playing));
        Record(match.TournamentId, TournamentEventTypes.MatchStarted, actor, new { matchId });
    }

    /// <summary>
    /// Records what somebody scored. Submitted, not accepted: the result is visible and
    /// decides nothing until an organiser confirms it.
    /// </summary>
    public TournamentMatchResult SubmitResult(TournamentMatchResult result)
    {
        TournamentMatch match = RequireMatch(result.MatchId);
        Tournament tournament = Require(match.TournamentId);

        if (match.Status is not (MatchStatus.Playing or MatchStatus.ResultPending))
        {
            throw new InvalidOperationException(
                $"A result cannot be submitted for a match that is {match.Status}.");
        }

        int already = _repo.ResultsForMatch(match.Id)
            .Count(r => r.PlayerId == result.PlayerId && r.GameIndex == result.GameIndex);

        if (already >= tournament.Rules.MaxAttempts)
        {
            throw new InvalidOperationException(
                $"The rules allow {tournament.Rules.MaxAttempts} attempt(s), and that is how many have been played.");
        }

        TournamentMatchResult stamped = result with
        {
            Id = result.Id == Guid.Empty ? Guid.NewGuid() : result.Id,
            RuleHash = tournament.RuleHash.Length > 0 ? tournament.RuleHash : tournament.Rules.Hash(),
            GameVersion = GameIdentity.FullVersion,
            SubmittedUtc = DateTime.UtcNow,
            ConfirmedUtc = null,
        };

        _repo.SubmitResult(stamped);

        if (match.Status == MatchStatus.Playing)
        {
            _repo.UpdateMatch(MatchFlow.Transition(match, MatchStatus.ResultPending));
        }

        Record(match.TournamentId, TournamentEventTypes.ResultSubmitted, result.PlayerId, new
        {
            matchId = match.Id,
            chartId = result.ChartId,
            score = result.Score,
            accuracy = result.Accuracy,
            chartHash = result.ChartHash,
        });

        return stamped;
    }

    /// <summary>
    /// Accepts a result, and settles the match if that was the last one it needed.
    /// </summary>
    public MatchOutcome ConfirmResult(Guid resultId, Guid matchId, Guid? actor = null)
    {
        TournamentMatch match = RequireMatch(matchId);
        Tournament tournament = Require(match.TournamentId);

        _repo.ConfirmResult(resultId, DateTime.UtcNow);
        Record(match.TournamentId, TournamentEventTypes.ResultConfirmed, actor,
            new { matchId, resultId });

        MatchOutcome outcome = TieBreaker.Decide(
            match, _repo.ResultsForMatch(matchId), tournament.Rules);

        if (!outcome.IsDecided)
        {
            return outcome;
        }

        _repo.UpdateMatch(MatchFlow.Transition(
            match with { WinnerPlayerId = outcome.WinnerPlayerId }, MatchStatus.Complete));

        Record(match.TournamentId, TournamentEventTypes.MatchCompleted, actor, new
        {
            matchId,
            winner = outcome.WinnerPlayerId,
            games = $"{outcome.WinnerGames}-{outcome.LoserGames}",
        });

        AdvanceWinner(tournament, matchId, outcome.WinnerPlayerId!.Value);
        return outcome;
    }

    // --- advancing ---

    /// <summary>
    /// Moves a winner into their next seat, drops the loser where the format says, and
    /// finishes the tournament if that match was the last one.
    /// </summary>
    private void AdvanceWinner(Tournament tournament, Guid matchId, Guid winnerPlayerId)
    {
        IReadOnlyList<TournamentRound> rounds = _repo.Rounds(tournament.Id);
        IReadOnlyList<TournamentMatch> matches = _repo.Matches(tournament.Id);

        TournamentMatch? completed = matches.FirstOrDefault(m => m.Id == matchId);
        if (completed is null)
        {
            return;
        }

        BracketSeat? seat = BracketProgression.NextSeatForWinner(rounds, matches, completed);

        if (seat is { } next)
        {
            TournamentMatch? target = matches.FirstOrDefault(m => m.Id == next.MatchId);
            if (target is not null)
            {
                _repo.UpdateMatch(BracketProgression.Seat(target, next, winnerPlayerId));
                Record(tournament.Id, TournamentEventTypes.PlayerAdvanced, winnerPlayerId,
                    new { from = matchId, to = next.MatchId });
            }
        }

        Guid? loser = completed.Opponent(winnerPlayerId);
        if (loser is not null)
        {
            BracketSeat? drop = BracketProgression.NextSeatForLoser(
                rounds, matches, completed, tournament.Format);

            if (drop is { } dropped)
            {
                TournamentMatch? target = _repo.GetMatch(dropped.MatchId);
                if (target is not null)
                {
                    _repo.UpdateMatch(BracketProgression.Seat(target, dropped, loser.Value));
                }
            }
        }

        CompleteIfFinished(tournament);
    }

    /// <summary>
    /// Finishes the tournament when the deciding match is over — and records a round as
    /// complete when its last match is, which is what a dashboard reads to say where the
    /// event has got to.
    /// </summary>
    private void CompleteIfFinished(Tournament tournament)
    {
        IReadOnlyList<TournamentRound> rounds = _repo.Rounds(tournament.Id);
        IReadOnlyList<TournamentMatch> matches = _repo.Matches(tournament.Id);

        foreach (TournamentRound round in rounds.Where(r => !r.IsComplete))
        {
            var inRound = matches.Where(m => m.RoundId == round.Id).ToList();

            // A round with seats still empty is not finished, it is waiting.
            bool finished = inRound.Count > 0
                && inRound.All(m => MatchFlow.IsTerminal(m.Status));

            if (finished)
            {
                Record(tournament.Id, TournamentEventTypes.RoundCompleted, null,
                    new { round = round.Name });
            }
        }

        TournamentMatch? deciding = BracketProgression.DecidingMatch(rounds, matches);

        if (deciding is null || deciding.WinnerPlayerId is null)
        {
            return;
        }

        Tournament finishedTournament = tournament with
        {
            Status = TournamentStatus.Complete,
            FinishedUtc = DateTime.UtcNow,
            WinnerPlayerId = deciding.WinnerPlayerId,
        };

        _repo.Update(finishedTournament);
        Record(tournament.Id, TournamentEventTypes.Completed, null,
            new { winner = deciding.WinnerPlayerId });

        Log.Info($"tournament complete: {tournament.Name}");
    }

    /// <summary>
    /// Finishes a score attack, which has no final to win: the ranking decides it.
    /// </summary>
    public IReadOnlyList<StandingEntry> FinishScoreAttack(Guid tournamentId)
    {
        Tournament tournament = Require(tournamentId);

        IReadOnlyList<StandingEntry> standings =
            TieBreaker.Standings(_repo.Results(tournamentId), tournament.Rules);

        _repo.Update(tournament with
        {
            Status = TournamentStatus.Complete,
            FinishedUtc = DateTime.UtcNow,
            WinnerPlayerId = standings.Count > 0 ? standings[0].PlayerId : null,
        });

        Record(tournamentId, TournamentEventTypes.Completed, null, new
        {
            winner = standings.Count > 0 ? standings[0].PlayerId : (Guid?)null,
            ranked = standings.Count,
        });

        return standings;
    }

    public void Cancel(Guid tournamentId, string reason = "")
    {
        Tournament tournament = Require(tournamentId);

        _repo.Update(tournament with
        {
            Status = TournamentStatus.Cancelled,
            FinishedUtc = DateTime.UtcNow,
        });

        Record(tournamentId, TournamentEventTypes.Cancelled, null, new { reason });
    }

    // --- reading ---

    public Tournament? Get(Guid tournamentId) => _repo.Get(tournamentId);

    public IReadOnlyList<Tournament> All() => _repo.All();

    public IReadOnlyList<TournamentParticipant> Participants(Guid id) => _repo.Participants(id);

    public IReadOnlyList<TournamentSong> Songs(Guid id) => _repo.Songs(id);

    public IReadOnlyList<TournamentRound> Rounds(Guid id) => _repo.Rounds(id);

    public IReadOnlyList<TournamentMatch> Matches(Guid id) => _repo.Matches(id);

    public IReadOnlyList<TournamentMatchResult> Results(Guid id) => _repo.Results(id);

    public IReadOnlyList<TournamentMatchResult> ResultsForMatch(Guid matchId) =>
        _repo.ResultsForMatch(matchId);

    public IReadOnlyList<TournamentEvent> Events(Guid id) => _repo.Events(id);

    public IReadOnlyList<StandingEntry> Standings(Guid tournamentId)
    {
        Tournament? tournament = _repo.Get(tournamentId);
        return tournament is null
            ? Array.Empty<StandingEntry>()
            : TieBreaker.Standings(_repo.Results(tournamentId), tournament.Rules);
    }

    /// <summary>
    /// The next match somebody could actually sit down and play, or null when the
    /// tournament is waiting on the organiser.
    /// </summary>
    public TournamentMatch? NextPlayableMatch(Guid tournamentId) =>
        _repo.Matches(tournamentId)
            .Where(m => !MatchFlow.IsTerminal(m.Status) && m.HasBothPlayers)
            .OrderBy(m => m.Slot)
            .FirstOrDefault();

    /// <summary>
    /// A player's record across every tournament, kept apart from their normal profile.
    /// </summary>
    public TournamentRecord RecordFor(Guid playerId)
    {
        int played = 0;
        int won = 0;
        int lost = 0;
        int championships = 0;
        int best = int.MaxValue;
        double pp = 0;

        foreach (Tournament tournament in _repo.All())
        {
            IReadOnlyList<TournamentParticipant> participants = _repo.Participants(tournament.Id);
            if (participants.All(p => p.PlayerId != playerId))
            {
                continue;
            }

            if (tournament.WinnerPlayerId == playerId)
            {
                championships++;
                best = 1;
            }

            foreach (TournamentMatch match in _repo.Matches(tournament.Id)
                         .Where(m => m.Involves(playerId) && m.Status == MatchStatus.Complete))
            {
                played++;
                if (match.WinnerPlayerId == playerId)
                {
                    won++;
                }
                else
                {
                    lost++;
                }
            }

            pp += _repo.Results(tournament.Id)
                .Where(r => r.PlayerId == playerId && r.IsConfirmed)
                .Sum(r => r.Pp);
        }

        return new TournamentRecord(played, won, lost, championships,
            best == int.MaxValue ? null : best, pp);
    }

    // --- plumbing ---

    private Tournament Require(Guid id) =>
        _repo.Get(id) ?? throw new InvalidOperationException("That tournament does not exist.");

    private TournamentMatch RequireMatch(Guid id) =>
        _repo.GetMatch(id) ?? throw new InvalidOperationException("That match does not exist.");

    private static void RequireDraft(Tournament tournament, string what)
    {
        if (tournament.Status != TournamentStatus.Draft)
        {
            throw new InvalidOperationException(
                $"You cannot {what} once a tournament has started — the bracket is already drawn.");
        }
    }

    private void Record(Guid tournamentId, string type, Guid? actor, object payload)
    {
        try
        {
            _repo.Record(new TournamentEvent
            {
                Id = Guid.NewGuid(),
                TournamentId = tournamentId,
                Type = type,
                ActorPlayerId = actor,
                Payload = JsonSerializer.Serialize(payload, Json),
                Timestamp = DateTime.UtcNow,
            });
        }
        catch (Exception ex)
        {
            // The log is evidence, not a feature, and failing to write a line about
            // something must not undo the something.
            Log.Warn($"tournament {tournamentId}: could not record {type}", ex);
        }
    }
}

/// <summary>
/// A player's tournament history, which is deliberately not part of their Rating.
/// </summary>
/// <param name="BestPlacement">1 for a win; null if they have never finished one.</param>
public readonly record struct TournamentRecord(
    int MatchesPlayed, int Wins, int Losses, int Championships, int? BestPlacement, double TournamentPp)
{
    public double WinRate => MatchesPlayed == 0 ? 0 : (double)Wins / MatchesPlayed;
}

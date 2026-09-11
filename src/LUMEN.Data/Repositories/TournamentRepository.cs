using System.Globalization;
using System.Text.Json;
using Lumen.Core.Tournaments;
using Microsoft.Data.Sqlite;

namespace Lumen.Data.Repositories;

/// <summary>
/// Tournaments kept in the local database (spec: Local Tournament).
///
/// The local half of <see cref="ITournamentRepository"/>. Everything a tournament knows
/// how to do — draw a bracket, break a tie, decide a match — lives in Core and does not
/// know this class exists, which is what makes a remote implementation a matter of writing
/// another one of these rather than of rewriting the rules.
/// </summary>
public sealed class TournamentRepository : ITournamentRepository
{
    private readonly Database _db;

    public TournamentRepository(Database db) => _db = db;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    // --- tournaments ---

    public Tournament Create(Tournament tournament)
    {
        Tournament stored = tournament with { RuleHash = tournament.Rules.Hash() };

        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO tournaments
                (tournament_id, name, description, organizer, format, status, rules_json,
                 rule_hash, game_version, random_seed, created_utc, started_utc, finished_utc,
                 winner_player_id)
            VALUES ($id, $name, $description, $organizer, $format, $status, $rules,
                    $ruleHash, $version, $seed, $created, $started, $finished, $winner);
            """;

        Bind(cmd, stored);
        cmd.ExecuteNonQuery();

        return stored;
    }

    public Tournament? Get(Guid tournamentId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT * FROM tournaments WHERE tournament_id = $id;";
        cmd.Parameters.AddWithValue("$id", Id(tournamentId));

        using SqliteDataReader reader = cmd.ExecuteReader();
        return reader.Read() ? ReadTournament(reader) : null;
    }

    public IReadOnlyList<Tournament> All()
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT * FROM tournaments ORDER BY created_utc DESC;";

        var list = new List<Tournament>();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(ReadTournament(reader));
        }

        return list;
    }

    public void Update(Tournament tournament)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            UPDATE tournaments SET
                name = $name, description = $description, organizer = $organizer,
                format = $format, status = $status, rules_json = $rules, rule_hash = $ruleHash,
                game_version = $version, random_seed = $seed, created_utc = $created,
                started_utc = $started, finished_utc = $finished, winner_player_id = $winner
            WHERE tournament_id = $id;
            """;

        Bind(cmd, tournament);
        cmd.ExecuteNonQuery();
    }

    public void Delete(Guid tournamentId)
    {
        // The children are all ON DELETE CASCADE, but foreign keys are only enforced when
        // the pragma is on, and this must not depend on that being true.
        _db.InTransaction(() =>
        {
            foreach (string table in new[]
            {
                "tournament_events", "tournament_match_results", "tournament_matches",
                "tournament_rounds", "tournament_songs", "tournament_participants", "tournaments",
            })
            {
                using SqliteCommand cmd = _db.CreateCommand();
                cmd.CommandText = $"DELETE FROM {table} WHERE tournament_id = $id;";
                cmd.Parameters.AddWithValue("$id", Id(tournamentId));
                cmd.ExecuteNonQuery();
            }
        });
    }

    private void Bind(SqliteCommand cmd, Tournament t)
    {
        cmd.Parameters.AddWithValue("$id", Id(t.Id));
        cmd.Parameters.AddWithValue("$name", t.Name);
        cmd.Parameters.AddWithValue("$description", t.Description);
        cmd.Parameters.AddWithValue("$organizer", t.Organizer);
        cmd.Parameters.AddWithValue("$format", (int)t.Format);
        cmd.Parameters.AddWithValue("$status", (int)t.Status);
        cmd.Parameters.AddWithValue("$rules", JsonSerializer.Serialize(t.Rules, Json));
        cmd.Parameters.AddWithValue("$ruleHash", t.RuleHash.Length > 0 ? t.RuleHash : t.Rules.Hash());
        cmd.Parameters.AddWithValue("$version", t.GameVersion);
        cmd.Parameters.AddWithValue("$seed", t.RandomSeed);
        cmd.Parameters.AddWithValue("$created", Utc(t.CreatedUtc));
        cmd.Parameters.AddWithValue("$started", (object?)Utc(t.StartedUtc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$finished", (object?)Utc(t.FinishedUtc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$winner", (object?)Id(t.WinnerPlayerId) ?? DBNull.Value);
    }

    private static Tournament ReadTournament(SqliteDataReader r) => new()
    {
        Id = Guid.Parse(r.GetString(r.GetOrdinal("tournament_id"))),
        Name = r.GetString(r.GetOrdinal("name")),
        Description = r.GetString(r.GetOrdinal("description")),
        Organizer = r.GetString(r.GetOrdinal("organizer")),
        Format = (TournamentFormat)r.GetInt32(r.GetOrdinal("format")),
        Status = (TournamentStatus)r.GetInt32(r.GetOrdinal("status")),
        Rules = JsonSerializer.Deserialize<TournamentRules>(
            r.GetString(r.GetOrdinal("rules_json")), Json) ?? TournamentRules.Official,
        RuleHash = r.GetString(r.GetOrdinal("rule_hash")),
        GameVersion = r.GetString(r.GetOrdinal("game_version")),
        RandomSeed = r.GetInt32(r.GetOrdinal("random_seed")),
        CreatedUtc = ParseUtc(r.GetString(r.GetOrdinal("created_utc"))),
        StartedUtc = ParseUtcOrNull(r, "started_utc"),
        FinishedUtc = ParseUtcOrNull(r, "finished_utc"),
        WinnerPlayerId = GuidOrNull(r, "winner_player_id"),
    };

    // --- participants ---

    public void AddParticipant(TournamentParticipant participant)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO tournament_participants
                (tournament_id, player_id, display_name, seed, status, joined_utc)
            VALUES ($tid, $pid, $name, $seed, $status, $joined)
            ON CONFLICT (tournament_id, player_id) DO UPDATE SET
                display_name = excluded.display_name,
                seed = excluded.seed,
                status = excluded.status;
            """;

        cmd.Parameters.AddWithValue("$tid", Id(participant.TournamentId));
        cmd.Parameters.AddWithValue("$pid", Id(participant.PlayerId));
        cmd.Parameters.AddWithValue("$name", participant.DisplayName);
        cmd.Parameters.AddWithValue("$seed", participant.Seed);
        cmd.Parameters.AddWithValue("$status", (int)participant.Status);
        cmd.Parameters.AddWithValue("$joined", Utc(participant.JoinedUtc));
        cmd.ExecuteNonQuery();
    }

    public void UpdateParticipant(TournamentParticipant participant) => AddParticipant(participant);

    public void RemoveParticipant(Guid tournamentId, Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            "DELETE FROM tournament_participants WHERE tournament_id = $tid AND player_id = $pid;";
        cmd.Parameters.AddWithValue("$tid", Id(tournamentId));
        cmd.Parameters.AddWithValue("$pid", Id(playerId));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<TournamentParticipant> Participants(Guid tournamentId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT * FROM tournament_participants
            WHERE tournament_id = $tid
            ORDER BY CASE WHEN seed = 0 THEN 1 ELSE 0 END, seed, joined_utc;
            """;
        cmd.Parameters.AddWithValue("$tid", Id(tournamentId));

        var list = new List<TournamentParticipant>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TournamentParticipant
            {
                TournamentId = tournamentId,
                PlayerId = Guid.Parse(r.GetString(r.GetOrdinal("player_id"))),
                DisplayName = r.GetString(r.GetOrdinal("display_name")),
                Seed = r.GetInt32(r.GetOrdinal("seed")),
                Status = (ParticipantStatus)r.GetInt32(r.GetOrdinal("status")),
                JoinedUtc = ParseUtc(r.GetString(r.GetOrdinal("joined_utc"))),
            });
        }

        return list;
    }

    // --- song pool ---

    public void AddSong(TournamentSong song)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO tournament_songs
                (tournament_id, chart_id, title, difficulty_name, level, chart_hash, audio_hash, category)
            VALUES ($tid, $cid, $title, $diff, $level, $chash, $ahash, $category)
            ON CONFLICT (tournament_id, chart_id) DO UPDATE SET
                title = excluded.title, difficulty_name = excluded.difficulty_name,
                level = excluded.level, chart_hash = excluded.chart_hash,
                audio_hash = excluded.audio_hash, category = excluded.category;
            """;

        cmd.Parameters.AddWithValue("$tid", Id(song.TournamentId));
        cmd.Parameters.AddWithValue("$cid", Id(song.ChartId));
        cmd.Parameters.AddWithValue("$title", song.Title);
        cmd.Parameters.AddWithValue("$diff", song.DifficultyName);
        cmd.Parameters.AddWithValue("$level", song.Level);
        cmd.Parameters.AddWithValue("$chash", song.ChartHash);
        cmd.Parameters.AddWithValue("$ahash", song.AudioHash);
        cmd.Parameters.AddWithValue("$category", song.Category);
        cmd.ExecuteNonQuery();
    }

    public void RemoveSong(Guid tournamentId, Guid chartId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM tournament_songs WHERE tournament_id = $tid AND chart_id = $cid;";
        cmd.Parameters.AddWithValue("$tid", Id(tournamentId));
        cmd.Parameters.AddWithValue("$cid", Id(chartId));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<TournamentSong> Songs(Guid tournamentId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT * FROM tournament_songs WHERE tournament_id = $tid ORDER BY title;";
        cmd.Parameters.AddWithValue("$tid", Id(tournamentId));

        var list = new List<TournamentSong>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TournamentSong
            {
                TournamentId = tournamentId,
                ChartId = Guid.Parse(r.GetString(r.GetOrdinal("chart_id"))),
                Title = r.GetString(r.GetOrdinal("title")),
                DifficultyName = r.GetString(r.GetOrdinal("difficulty_name")),
                Level = r.GetDouble(r.GetOrdinal("level")),
                ChartHash = r.GetString(r.GetOrdinal("chart_hash")),
                AudioHash = r.GetString(r.GetOrdinal("audio_hash")),
                Category = r.GetString(r.GetOrdinal("category")),
            });
        }

        return list;
    }

    // --- bracket ---

    /// <summary>
    /// One transaction for the whole bracket. A half-written bracket is a tournament
    /// nobody can run and nobody can repair, so it is all of it or none of it.
    /// </summary>
    public void SaveBracket(Guid tournamentId, Bracket bracket)
    {
        _db.InTransaction(() =>
        {
            using (SqliteCommand clearMatches = _db.CreateCommand())
            {
                clearMatches.CommandText = "DELETE FROM tournament_matches WHERE tournament_id = $tid;";
                clearMatches.Parameters.AddWithValue("$tid", Id(tournamentId));
                clearMatches.ExecuteNonQuery();
            }

            using (SqliteCommand clearRounds = _db.CreateCommand())
            {
                clearRounds.CommandText = "DELETE FROM tournament_rounds WHERE tournament_id = $tid;";
                clearRounds.Parameters.AddWithValue("$tid", Id(tournamentId));
                clearRounds.ExecuteNonQuery();
            }

            foreach (TournamentRound round in bracket.Rounds)
            {
                using SqliteCommand cmd = _db.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO tournament_rounds (round_id, tournament_id, round_index, name, side, is_complete)
                    VALUES ($id, $tid, $index, $name, $side, $done);
                    """;
                cmd.Parameters.AddWithValue("$id", Id(round.Id));
                cmd.Parameters.AddWithValue("$tid", Id(round.TournamentId));
                cmd.Parameters.AddWithValue("$index", round.Index);
                cmd.Parameters.AddWithValue("$name", round.Name);
                cmd.Parameters.AddWithValue("$side", (int)round.Side);
                cmd.Parameters.AddWithValue("$done", round.IsComplete ? 1 : 0);
                cmd.ExecuteNonQuery();
            }

            foreach (TournamentMatch match in bracket.Matches)
            {
                InsertMatch(match);
            }
        });
    }

    private void InsertMatch(TournamentMatch match)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO tournament_matches
                (match_id, tournament_id, round_id, slot, player1_id, player2_id, status,
                 winner_player_id, chart_ids, best_of, started_utc, completed_utc)
            VALUES ($id, $tid, $rid, $slot, $p1, $p2, $status, $winner, $charts, $bestOf, $started, $completed);
            """;

        BindMatch(cmd, match);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<TournamentRound> Rounds(Guid tournamentId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            "SELECT * FROM tournament_rounds WHERE tournament_id = $tid ORDER BY round_index;";
        cmd.Parameters.AddWithValue("$tid", Id(tournamentId));

        var list = new List<TournamentRound>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TournamentRound
            {
                Id = Guid.Parse(r.GetString(r.GetOrdinal("round_id"))),
                TournamentId = tournamentId,
                Index = r.GetInt32(r.GetOrdinal("round_index")),
                Name = r.GetString(r.GetOrdinal("name")),
                Side = (BracketSide)r.GetInt32(r.GetOrdinal("side")),
                IsComplete = r.GetInt32(r.GetOrdinal("is_complete")) != 0,
            });
        }

        return list;
    }

    public IReadOnlyList<TournamentMatch> Matches(Guid tournamentId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT m.* FROM tournament_matches m
            JOIN tournament_rounds r ON r.round_id = m.round_id
            WHERE m.tournament_id = $tid
            ORDER BY r.round_index, m.slot;
            """;
        cmd.Parameters.AddWithValue("$tid", Id(tournamentId));

        var list = new List<TournamentMatch>();
        using SqliteDataReader r2 = cmd.ExecuteReader();
        while (r2.Read())
        {
            list.Add(ReadMatch(r2));
        }

        return list;
    }

    public TournamentMatch? GetMatch(Guid matchId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT * FROM tournament_matches WHERE match_id = $id;";
        cmd.Parameters.AddWithValue("$id", Id(matchId));

        using SqliteDataReader r = cmd.ExecuteReader();
        return r.Read() ? ReadMatch(r) : null;
    }

    public void UpdateMatch(TournamentMatch match)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            UPDATE tournament_matches SET
                round_id = $rid, slot = $slot, player1_id = $p1, player2_id = $p2,
                status = $status, winner_player_id = $winner, chart_ids = $charts,
                best_of = $bestOf, started_utc = $started, completed_utc = $completed
            WHERE match_id = $id;
            """;

        BindMatch(cmd, match);
        cmd.ExecuteNonQuery();
    }

    private void BindMatch(SqliteCommand cmd, TournamentMatch m)
    {
        cmd.Parameters.AddWithValue("$id", Id(m.Id));
        cmd.Parameters.AddWithValue("$tid", Id(m.TournamentId));
        cmd.Parameters.AddWithValue("$rid", Id(m.RoundId));
        cmd.Parameters.AddWithValue("$slot", m.Slot);
        cmd.Parameters.AddWithValue("$p1", (object?)Id(m.Player1Id) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$p2", (object?)Id(m.Player2Id) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (int)m.Status);
        cmd.Parameters.AddWithValue("$winner", (object?)Id(m.WinnerPlayerId) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$charts", string.Join(",", m.SelectedChartIds.Select(Id)));
        cmd.Parameters.AddWithValue("$bestOf", m.BestOf);
        cmd.Parameters.AddWithValue("$started", (object?)Utc(m.StartedUtc) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$completed", (object?)Utc(m.CompletedUtc) ?? DBNull.Value);
    }

    private static TournamentMatch ReadMatch(SqliteDataReader r)
    {
        string charts = r.GetString(r.GetOrdinal("chart_ids"));

        return new TournamentMatch
        {
            Id = Guid.Parse(r.GetString(r.GetOrdinal("match_id"))),
            TournamentId = Guid.Parse(r.GetString(r.GetOrdinal("tournament_id"))),
            RoundId = Guid.Parse(r.GetString(r.GetOrdinal("round_id"))),
            Slot = r.GetInt32(r.GetOrdinal("slot")),
            Player1Id = GuidOrNull(r, "player1_id"),
            Player2Id = GuidOrNull(r, "player2_id"),
            Status = (MatchStatus)r.GetInt32(r.GetOrdinal("status")),
            WinnerPlayerId = GuidOrNull(r, "winner_player_id"),
            SelectedChartIds = charts.Length == 0
                ? Array.Empty<Guid>()
                : charts.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToArray(),
            BestOf = r.GetInt32(r.GetOrdinal("best_of")),
            StartedUtc = ParseUtcOrNull(r, "started_utc"),
            CompletedUtc = ParseUtcOrNull(r, "completed_utc"),
        };
    }

    // --- results ---

    public void SubmitResult(TournamentMatchResult result)
    {
        TournamentMatch? match = GetMatch(result.MatchId);
        if (match is null)
        {
            throw new InvalidOperationException("That match does not exist.");
        }

        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO tournament_match_results
                (result_id, match_id, tournament_id, player_id, chart_id, game_index, score,
                 accuracy, max_combo, perfect, great, good, bad, miss, pp, full_combo,
                 all_perfect, replay_id, chart_hash, game_version, rule_hash, submitted_utc, confirmed_utc)
            VALUES ($id, $mid, $tid, $pid, $cid, $game, $score, $accuracy, $combo, $perfect,
                    $great, $good, $bad, $miss, $pp, $fc, $ap, $replay, $chash, $version,
                    $rhash, $submitted, $confirmed);
            """;

        cmd.Parameters.AddWithValue("$id", Id(result.Id));
        cmd.Parameters.AddWithValue("$mid", Id(result.MatchId));
        cmd.Parameters.AddWithValue("$tid", Id(match.TournamentId));
        cmd.Parameters.AddWithValue("$pid", Id(result.PlayerId));
        cmd.Parameters.AddWithValue("$cid", Id(result.ChartId));
        cmd.Parameters.AddWithValue("$game", result.GameIndex);
        cmd.Parameters.AddWithValue("$score", result.Score);
        cmd.Parameters.AddWithValue("$accuracy", result.Accuracy);
        cmd.Parameters.AddWithValue("$combo", result.MaxCombo);
        cmd.Parameters.AddWithValue("$perfect", result.Perfect);
        cmd.Parameters.AddWithValue("$great", result.Great);
        cmd.Parameters.AddWithValue("$good", result.Good);
        cmd.Parameters.AddWithValue("$bad", result.Bad);
        cmd.Parameters.AddWithValue("$miss", result.Miss);
        cmd.Parameters.AddWithValue("$pp", result.Pp);
        cmd.Parameters.AddWithValue("$fc", result.FullCombo ? 1 : 0);
        cmd.Parameters.AddWithValue("$ap", result.AllPerfect ? 1 : 0);
        cmd.Parameters.AddWithValue("$replay", (object?)Id(result.ReplayId) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$chash", result.ChartHash);
        cmd.Parameters.AddWithValue("$version", result.GameVersion);
        cmd.Parameters.AddWithValue("$rhash", result.RuleHash);
        cmd.Parameters.AddWithValue("$submitted", Utc(result.SubmittedUtc));
        cmd.Parameters.AddWithValue("$confirmed", (object?)Utc(result.ConfirmedUtc) ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void ConfirmResult(Guid resultId, DateTime confirmedUtc)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            "UPDATE tournament_match_results SET confirmed_utc = $when WHERE result_id = $id;";
        cmd.Parameters.AddWithValue("$id", Id(resultId));
        cmd.Parameters.AddWithValue("$when", Utc(confirmedUtc)!);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<TournamentMatchResult> Results(Guid tournamentId) =>
        ReadResults("WHERE tournament_id = $key ORDER BY submitted_utc", Id(tournamentId));

    public IReadOnlyList<TournamentMatchResult> ResultsForMatch(Guid matchId) =>
        ReadResults("WHERE match_id = $key ORDER BY game_index, submitted_utc", Id(matchId));

    private IReadOnlyList<TournamentMatchResult> ReadResults(string where, string key)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT * FROM tournament_match_results {where};";
        cmd.Parameters.AddWithValue("$key", key);

        var list = new List<TournamentMatchResult>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TournamentMatchResult
            {
                Id = Guid.Parse(r.GetString(r.GetOrdinal("result_id"))),
                MatchId = Guid.Parse(r.GetString(r.GetOrdinal("match_id"))),
                PlayerId = Guid.Parse(r.GetString(r.GetOrdinal("player_id"))),
                ChartId = Guid.Parse(r.GetString(r.GetOrdinal("chart_id"))),
                GameIndex = r.GetInt32(r.GetOrdinal("game_index")),
                Score = r.GetInt64(r.GetOrdinal("score")),
                Accuracy = r.GetDouble(r.GetOrdinal("accuracy")),
                MaxCombo = r.GetInt32(r.GetOrdinal("max_combo")),
                Perfect = r.GetInt32(r.GetOrdinal("perfect")),
                Great = r.GetInt32(r.GetOrdinal("great")),
                Good = r.GetInt32(r.GetOrdinal("good")),
                Bad = r.GetInt32(r.GetOrdinal("bad")),
                Miss = r.GetInt32(r.GetOrdinal("miss")),
                Pp = r.GetDouble(r.GetOrdinal("pp")),
                FullCombo = r.GetInt32(r.GetOrdinal("full_combo")) != 0,
                AllPerfect = r.GetInt32(r.GetOrdinal("all_perfect")) != 0,
                ReplayId = GuidOrNull(r, "replay_id"),
                ChartHash = r.GetString(r.GetOrdinal("chart_hash")),
                GameVersion = r.GetString(r.GetOrdinal("game_version")),
                RuleHash = r.GetString(r.GetOrdinal("rule_hash")),
                SubmittedUtc = ParseUtc(r.GetString(r.GetOrdinal("submitted_utc"))),
                ConfirmedUtc = ParseUtcOrNull(r, "confirmed_utc"),
            });
        }

        return list;
    }

    // --- audit ---

    public void Record(TournamentEvent tournamentEvent)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO tournament_events (event_id, tournament_id, type, actor_player_id, payload, timestamp_utc)
            VALUES ($id, $tid, $type, $actor, $payload, $when);
            """;

        cmd.Parameters.AddWithValue("$id", Id(tournamentEvent.Id));
        cmd.Parameters.AddWithValue("$tid", Id(tournamentEvent.TournamentId));
        cmd.Parameters.AddWithValue("$type", tournamentEvent.Type);
        cmd.Parameters.AddWithValue("$actor", (object?)Id(tournamentEvent.ActorPlayerId) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$payload", tournamentEvent.Payload);
        cmd.Parameters.AddWithValue("$when", Utc(tournamentEvent.Timestamp));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<TournamentEvent> Events(Guid tournamentId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            "SELECT * FROM tournament_events WHERE tournament_id = $tid ORDER BY timestamp_utc, event_id;";
        cmd.Parameters.AddWithValue("$tid", Id(tournamentId));

        var list = new List<TournamentEvent>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new TournamentEvent
            {
                Id = Guid.Parse(r.GetString(r.GetOrdinal("event_id"))),
                TournamentId = tournamentId,
                Type = r.GetString(r.GetOrdinal("type")),
                ActorPlayerId = GuidOrNull(r, "actor_player_id"),
                Payload = r.GetString(r.GetOrdinal("payload")),
                Timestamp = ParseUtc(r.GetString(r.GetOrdinal("timestamp_utc"))),
            });
        }

        return list;
    }

    // --- conversions ---

    private static string Id(Guid id) => id.ToString("D");

    private static string? Id(Guid? id) => id?.ToString("D");

    private static string Utc(DateTime when) =>
        when.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string? Utc(DateTime? when) => when is null ? null : Utc(when.Value);

    private static DateTime ParseUtc(string text) =>
        DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ToUniversalTime();

    private static DateTime? ParseUtcOrNull(SqliteDataReader r, string column)
    {
        int index = r.GetOrdinal(column);
        return r.IsDBNull(index) ? null : ParseUtc(r.GetString(index));
    }

    private static Guid? GuidOrNull(SqliteDataReader r, string column)
    {
        int index = r.GetOrdinal(column);
        return r.IsDBNull(index) ? null : Guid.Parse(r.GetString(index));
    }
}

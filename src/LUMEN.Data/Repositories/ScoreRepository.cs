using Lumen.Core.Balance;
using Lumen.Core.Charts;
using Lumen.Core.Gameplay;
using Lumen.Core.Library;
using Lumen.Core.Profiles;
using Lumen.Core.Pp;
using Lumen.Core.Rating;
using Lumen.Core.Scores;
using Microsoft.Data.Sqlite;

namespace Lumen.Data.Repositories;

/// <inheritdoc />
public sealed class ScoreRepository : IScoreRepository
{
    private readonly Database _db;
    private readonly BalanceConfig _balance;

    public ScoreRepository(Database db, BalanceConfig balance)
    {
        _db = db;
        _balance = balance;
    }

    public ScoreSaveOutcome Save(Guid playerId, PlayResult result, Chart chart)
    {
        DifficultyAnalyzer.Result analysis = DifficultyAnalyzer.Analyze(chart);
        ChartAttributes attrs = analysis.Attributes;
        double level = analysis.EstimatedLevel;
        string chartKey = ChartKey.For(chart);

        RatingSnapshot before = GetLatestSnapshot(playerId);
        ChartBest? chartBest = GetChartBest(playerId, chartKey);

        PpBreakdown pp = PpAlgorithm.Compute(result, attrs, level, _balance.Pp);
        double perfRating = PerformanceRating.Compute(result, level, _balance.Rating);

        var scoreId = Guid.NewGuid();
        var performanceId = Guid.NewGuid();
        string nowUtc = DateTime.UtcNow.ToString("O");

        RatingSnapshot after = before;

        _db.InTransaction(() =>
        {
            InsertScore(scoreId, playerId, chartKey, chart.Meta, level, result, nowUtc);
            InsertPerformance(performanceId, scoreId, playerId, chartKey, pp, perfRating, result.Accuracy / 100.0, attrs, nowUtc);
            after = RecomputeSnapshot(playerId, nowUtc);
        });

        var saved = new SavedScore
        {
            ScoreId = scoreId,
            PlayerId = playerId,
            ChartKey = chartKey,
            Chart = chart.Meta with { DifficultyLevel = level },
            Score = result.Score,
            Accuracy = result.Accuracy,
            MaxCombo = result.MaxCombo,
            Perfect = result.Perfect,
            Great = result.Great,
            Good = result.Good,
            Bad = result.Bad,
            Miss = result.Miss,
            FullCombo = result.FullCombo,
            AllPerfect = result.AllPerfect,
            Grade = result.Grade,
            Pp = pp.FinalPp,
            PerformanceRating = perfRating,
            PlayedUtc = DateTime.Parse(nowUtc).ToUniversalTime(),
        };

        return new ScoreSaveOutcome
        {
            Score = saved,
            PpBreakdown = pp,
            Before = before,
            After = after,
            IsFirstPlayOnChart = chartBest is null,
            IsPersonalBest = chartBest is null || result.Score > chartBest.Score,
            IsPpRecord = pp.FinalPp > before.BestPp + 0.01,
            IsRatingRecord = after.Rating > before.Rating + 0.001,
        };
    }

    // --- writes ---

    private void InsertScore(Guid id, Guid playerId, string chartKey, ChartMeta meta,
                             double level, PlayResult r, string nowUtc)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO scores (score_id, player_id, chart_key, chart_title, chart_artist, chart_creator,
                                difficulty_name, difficulty_level, score, accuracy, max_combo,
                                perfect, great, good, bad, miss, full_combo, all_perfect, grade, played_utc)
            VALUES ($id, $pid, $ck, $title, $artist, $creator, $diff, $level, $score, $acc, $combo,
                    $p, $g, $gd, $b, $m, $fc, $ap, $grade, $utc);
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        cmd.Parameters.AddWithValue("$pid", playerId.ToString("D"));
        cmd.Parameters.AddWithValue("$ck", chartKey);
        cmd.Parameters.AddWithValue("$title", meta.Title);
        cmd.Parameters.AddWithValue("$artist", meta.Artist);
        cmd.Parameters.AddWithValue("$creator", meta.Creator);
        cmd.Parameters.AddWithValue("$diff", meta.DifficultyName);
        cmd.Parameters.AddWithValue("$level", level);
        cmd.Parameters.AddWithValue("$score", r.Score);
        cmd.Parameters.AddWithValue("$acc", r.Accuracy);
        cmd.Parameters.AddWithValue("$combo", r.MaxCombo);
        cmd.Parameters.AddWithValue("$p", r.Perfect);
        cmd.Parameters.AddWithValue("$g", r.Great);
        cmd.Parameters.AddWithValue("$gd", r.Good);
        cmd.Parameters.AddWithValue("$b", r.Bad);
        cmd.Parameters.AddWithValue("$m", r.Miss);
        cmd.Parameters.AddWithValue("$fc", r.FullCombo ? 1 : 0);
        cmd.Parameters.AddWithValue("$ap", r.AllPerfect ? 1 : 0);
        cmd.Parameters.AddWithValue("$grade", r.Grade);
        cmd.Parameters.AddWithValue("$utc", nowUtc);
        cmd.ExecuteNonQuery();
    }

    private void InsertPerformance(Guid id, Guid scoreId, Guid playerId, string chartKey,
                                   PpBreakdown pp, double perfRating, double accuracy01,
                                   ChartAttributes attrs, string nowUtc)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO performances (performance_id, score_id, player_id, chart_key, pp, performance_rating,
                                      base_pp, acc_mul, combo_mul, miss_mul, tech_mul, speed_mul, read_mul,
                                      accuracy, skill_attributes_json, computed_utc)
            VALUES ($id, $sid, $pid, $ck, $pp, $pr, $base, $acc, $combo, $miss, $tech, $speed, $read,
                    $accuracy, $attrs, $utc);
            """;
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        cmd.Parameters.AddWithValue("$sid", scoreId.ToString("D"));
        cmd.Parameters.AddWithValue("$pid", playerId.ToString("D"));
        cmd.Parameters.AddWithValue("$ck", chartKey);
        cmd.Parameters.AddWithValue("$pp", pp.FinalPp);
        cmd.Parameters.AddWithValue("$pr", perfRating);
        cmd.Parameters.AddWithValue("$base", pp.BasePp);
        cmd.Parameters.AddWithValue("$acc", pp.AccuracyMultiplier);
        cmd.Parameters.AddWithValue("$combo", pp.ComboMultiplier);
        cmd.Parameters.AddWithValue("$miss", pp.MissMultiplier);
        cmd.Parameters.AddWithValue("$tech", pp.TechnicalMultiplier);
        cmd.Parameters.AddWithValue("$speed", pp.SpeedMultiplier);
        cmd.Parameters.AddWithValue("$read", pp.ReadingMultiplier);
        cmd.Parameters.AddWithValue("$accuracy", accuracy01);
        cmd.Parameters.AddWithValue("$attrs", attrs.ToJson());
        cmd.Parameters.AddWithValue("$utc", nowUtc);
        cmd.ExecuteNonQuery();
    }

    private RatingSnapshot RecomputeSnapshot(Guid playerId, string nowUtc)
    {
        double rating = RatingEngine.Compute(BestPerChart(playerId, "performance_rating"), _balance.Rating);
        double totalPp = RatingEngine.ComputeTotalPp(BestPerChart(playerId, "pp"), _balance.Pp);
        double bestPp = ScalarDouble(
            "SELECT COALESCE(MAX(pp), 0) FROM performances WHERE player_id = $p", playerId);

        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO rating_snapshots (snapshot_id, player_id, rating, total_pp, best_pp, computed_utc)
            VALUES ($id, $p, $r, $t, $b, $utc);
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));
        cmd.Parameters.AddWithValue("$r", rating);
        cmd.Parameters.AddWithValue("$t", totalPp);
        cmd.Parameters.AddWithValue("$b", bestPp);
        cmd.Parameters.AddWithValue("$utc", nowUtc);
        cmd.ExecuteNonQuery();

        return new RatingSnapshot(rating, totalPp, bestPp, DateTime.Parse(nowUtc).ToUniversalTime());
    }

    // --- reads ---

    public ChartBest? GetChartBest(Guid playerId, string chartKey)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT s.score, s.accuracy, COALESCE(MAX(p.pp), 0), s.played_utc
            FROM scores s
            LEFT JOIN performances p ON p.score_id = s.score_id
            WHERE s.player_id = $p AND s.chart_key = $ck
            GROUP BY s.score_id
            ORDER BY s.score DESC
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));
        cmd.Parameters.AddWithValue("$ck", chartKey);

        using SqliteDataReader r = cmd.ExecuteReader();
        return r.Read()
            ? new ChartBest(r.GetInt64(0), r.GetDouble(1), r.GetDouble(2), ParseUtc(r.GetString(3)))
            : null;
    }

    public IReadOnlyDictionary<string, ChartStats> GetChartStats(Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        // Best score, best accuracy and best PP are maximised independently on purpose:
        // a player's highest-scoring run is not necessarily their most accurate one, and
        // Song Select reports all three (spec §35, §79).
        cmd.CommandText =
            """
            SELECT s.chart_key,
                   MAX(s.score)                AS best_score,
                   MAX(s.accuracy)             AS best_accuracy,
                   COALESCE(MAX(p.pp), 0)      AS best_pp,
                   COUNT(*)                    AS play_count
            FROM scores s
            LEFT JOIN performances p ON p.score_id = s.score_id
            WHERE s.player_id = $p
            GROUP BY s.chart_key;
            """;
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));

        var map = new Dictionary<string, ChartStats>(StringComparer.Ordinal);
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            map[r.GetString(0)] = new ChartStats(
                r.GetInt64(1), r.GetDouble(2), r.GetDouble(3), r.GetInt32(4));
        }

        return map;
    }

    public IReadOnlyList<ChartRankingEntry> GetChartRanking(string chartKey, int limit, Guid selfPlayerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        // One row per profile, at that profile's best score. A player who has played a
        // chart fifty times occupies one place on the board, not fifty.
        cmd.CommandText =
            """
            SELECT s.player_id, pr.display_name, s.score, s.accuracy,
                   COALESCE(MAX(p.pp), 0), s.max_combo, s.full_combo, s.played_utc
            FROM scores s
            JOIN profiles pr ON pr.player_id = s.player_id
            LEFT JOIN performances p ON p.score_id = s.score_id
            WHERE s.chart_key = $ck
              AND s.score = (SELECT MAX(q.score) FROM scores q
                             WHERE q.chart_key = s.chart_key AND q.player_id = s.player_id)
            GROUP BY s.player_id
            ORDER BY s.score DESC, s.accuracy DESC, s.played_utc ASC
            LIMIT $n;
            """;
        cmd.Parameters.AddWithValue("$ck", chartKey);
        cmd.Parameters.AddWithValue("$n", limit);

        var list = new List<ChartRankingEntry>();
        using SqliteDataReader r = cmd.ExecuteReader();
        int rank = 1;
        while (r.Read())
        {
            var playerId = Guid.Parse(r.GetString(0));
            list.Add(new ChartRankingEntry(
                rank++, playerId, r.GetString(1), r.GetInt64(2), r.GetDouble(3),
                r.GetDouble(4), r.GetInt32(5), r.GetInt64(6) == 1, ParseUtc(r.GetString(7)),
                playerId == selfPlayerId));
        }

        return list;
    }

    public int? GetChartRank(string chartKey, Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT (SELECT COUNT(*) FROM (
                        SELECT MAX(score) AS best FROM scores
                        WHERE chart_key = $ck GROUP BY player_id
                    ) WHERE best > COALESCE((SELECT MAX(score) FROM scores
                                             WHERE chart_key = $ck AND player_id = $p), -1)) + 1,
                   (SELECT COUNT(*) FROM scores WHERE chart_key = $ck AND player_id = $p);
            """;
        cmd.Parameters.AddWithValue("$ck", chartKey);
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));

        using SqliteDataReader r = cmd.ExecuteReader();
        if (!r.Read() || r.GetInt32(1) == 0)
        {
            return null;
        }

        return r.GetInt32(0);
    }

    public IReadOnlyList<BestPerformance> GetBestPerformances(Guid playerId, int limit = 50)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT p.chart_key, s.chart_title, s.chart_artist, s.chart_creator, s.difficulty_name,
                   s.difficulty_level, p.pp, p.performance_rating, s.accuracy, s.score, s.full_combo, s.played_utc
            FROM performances p
            JOIN scores s ON s.score_id = p.score_id
            WHERE p.player_id = $p
            ORDER BY p.pp DESC
            LIMIT $n;
            """;
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));
        cmd.Parameters.AddWithValue("$n", limit);

        var list = new List<BestPerformance>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new BestPerformance(
                r.GetString(0), Meta(r, 1), r.GetDouble(6), r.GetDouble(7),
                r.GetDouble(8), r.GetInt64(9), r.GetInt64(10) == 1, ParseUtc(r.GetString(11))));
        }

        return list;
    }

    public IReadOnlyList<PlayHistoryEntry> GetRecentPlays(Guid playerId, int limit = 25)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT s.chart_title, s.chart_artist, s.chart_creator, s.difficulty_name, s.difficulty_level,
                   s.accuracy, s.score, COALESCE(p.pp, 0), s.grade, s.played_utc
            FROM scores s
            LEFT JOIN performances p ON p.score_id = s.score_id
            WHERE s.player_id = $p
            ORDER BY s.played_utc DESC
            LIMIT $n;
            """;
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));
        cmd.Parameters.AddWithValue("$n", limit);

        var list = new List<PlayHistoryEntry>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new PlayHistoryEntry(
                Meta(r, 0), r.GetDouble(5), r.GetInt64(6), r.GetDouble(7), r.GetString(8), ParseUtc(r.GetString(9))));
        }

        return list;
    }

    public RatingSnapshot GetLatestSnapshot(Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT rating, total_pp, best_pp, computed_utc FROM rating_snapshots
            WHERE player_id = $p ORDER BY computed_utc DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));

        using SqliteDataReader r = cmd.ExecuteReader();
        return r.Read()
            ? new RatingSnapshot(r.GetDouble(0), r.GetDouble(1), r.GetDouble(2), ParseUtc(r.GetString(3)))
            : RatingSnapshot.Empty;
    }

    public PlayerStatistics GetStatistics(Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT COUNT(*), COALESCE(SUM(full_combo), 0), COALESCE(SUM(all_perfect), 0),
                   COALESCE(SUM(score), 0), COALESCE(AVG(accuracy), 0), COALESCE(MAX(accuracy), 0),
                   COALESCE(SUM(perfect), 0), COALESCE(SUM(great), 0), COALESCE(SUM(good), 0),
                   COALESCE(SUM(bad), 0), COALESCE(SUM(miss), 0), COUNT(DISTINCT chart_key),
                   MIN(played_utc), MAX(played_utc), COALESCE(MAX(max_combo), 0)
            FROM scores WHERE player_id = $p;
            """;
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));

        using SqliteDataReader r = cmd.ExecuteReader();
        if (!r.Read() || r.GetInt32(0) == 0)
        {
            return PlayerStatistics.Empty;
        }

        return new PlayerStatistics
        {
            PlayCount = r.GetInt32(0),
            FullCombos = r.GetInt32(1),
            AllPerfects = r.GetInt32(2),
            TotalScore = r.GetInt64(3),
            AverageAccuracy = r.GetDouble(4),
            BestAccuracy = r.GetDouble(5),
            TotalPerfect = r.GetInt32(6),
            TotalGreat = r.GetInt32(7),
            TotalGood = r.GetInt32(8),
            TotalBad = r.GetInt32(9),
            TotalMiss = r.GetInt32(10),
            DistinctCharts = r.GetInt32(11),
            FirstPlayUtc = r.IsDBNull(12) ? null : ParseUtc(r.GetString(12)),
            LastPlayUtc = r.IsDBNull(13) ? null : ParseUtc(r.GetString(13)),
            HighestCombo = r.GetInt32(14),
        };
    }

    public ProfileSummary GetSummary(Profile profile)
    {
        RatingSnapshot snap = GetLatestSnapshot(profile.PlayerId);
        PlayerStatistics stats = GetStatistics(profile.PlayerId);

        return new ProfileSummary
        {
            Profile = profile,
            Rating = snap.Rating,
            TotalPp = (long)Math.Round(snap.TotalPp),
            BestPp = snap.BestPp,
            Accuracy = stats.AverageAccuracy,
            PlayCount = stats.PlayCount,
            FullCombos = stats.FullCombos,
        };
    }

    public Lumen.Core.Rating.SkillAxes GetSkillProfile(Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT p.performance_rating, p.accuracy, p.skill_attributes_json
            FROM performances p
            WHERE p.player_id = $p
            ORDER BY p.pp DESC
            LIMIT 100;
            """;
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));

        var samples = new List<SkillProfile.Sample>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            samples.Add(new SkillProfile.Sample(
                r.GetDouble(0), r.GetDouble(1), ChartAttributes.FromJson(r.GetString(2))));
        }

        return SkillProfile.Compute(samples);
    }

    public int CountScores(Guid playerId) =>
        (int)ScalarDouble("SELECT COUNT(*) FROM scores WHERE player_id = $p", playerId);

    // --- helpers ---

    private IEnumerable<double> BestPerChart(Guid playerId, string column)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            $"SELECT MAX({column}) FROM performances WHERE player_id = $p GROUP BY chart_key;";
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));

        var list = new List<double>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(r.GetDouble(0));
        }

        return list;
    }

    private double ScalarDouble(string sql, Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));
        object? v = cmd.ExecuteScalar();
        return v is null or DBNull ? 0 : Convert.ToDouble(v);
    }

    private static ChartMeta Meta(SqliteDataReader r, int titleOrdinal) => new()
    {
        Title = r.GetString(titleOrdinal),
        Artist = r.GetString(titleOrdinal + 1),
        Creator = r.GetString(titleOrdinal + 2),
        DifficultyName = r.GetString(titleOrdinal + 3),
        DifficultyLevel = r.GetDouble(titleOrdinal + 4),
    };

    private static DateTime ParseUtc(string s) =>
        DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
}

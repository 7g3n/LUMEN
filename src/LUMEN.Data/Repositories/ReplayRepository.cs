using Lumen.Core;
using Lumen.Core.Diagnostics;
using Lumen.Core.Replays;
using Microsoft.Data.Sqlite;

namespace Lumen.Data.Repositories;

/// <summary>
/// Stores replays (spec §41).
///
/// The event stream goes to a file under <c>replays/</c> and only its metadata into
/// SQLite. A long play is tens of thousands of events; keeping those in the database
/// would make it slow to open and heavy to back up, and the list screen never needs them
/// — it needs a title, a score and a date, which is exactly what the row holds.
/// </summary>
public sealed class ReplayRepository
{
    private readonly Database _db;
    private readonly string _directory;

    public ReplayRepository(Database db, string replaysDirectory)
    {
        _db = db;
        _directory = replaysDirectory;
    }

    public string FileNameFor(Guid replayId) => $"{replayId:D}.lumenreplay";

    public string PathFor(Guid replayId) => Path.Combine(_directory, FileNameFor(replayId));

    /// <summary>
    /// Writes the stream, then the row. In that order deliberately: a file with no row is
    /// invisible and harmless, while a row with no file is a replay the player can see
    /// and cannot watch.
    /// </summary>
    public void Save(Replay replay, Guid? scoreId = null)
    {
        Directory.CreateDirectory(_directory);
        AtomicFile.WriteAllText(PathFor(replay.ReplayId), ReplayJson.Serialize(replay));

        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO replays (replay_id, player_id, score_id, chart_key, chart_id,
                                 title, artist, difficulty_name, difficulty_level,
                                 score, accuracy, max_combo, grade, full_combo,
                                 event_count, recorded_utc, file_name)
            VALUES ($id, $player, $score_id, $key, $chart_id, $title, $artist, $diff, $level,
                    $score, $accuracy, $combo, $grade, $fc, $events, $utc, $file);
            """;
        cmd.Parameters.AddWithValue("$id", replay.ReplayId.ToString("D"));
        cmd.Parameters.AddWithValue("$player", replay.PlayerId.ToString("D"));
        cmd.Parameters.AddWithValue("$score_id", (object?)scoreId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$key", replay.ChartKey);
        cmd.Parameters.AddWithValue("$chart_id", (object?)replay.ChartId?.ToString("D") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", replay.Chart.Title);
        cmd.Parameters.AddWithValue("$artist", replay.Chart.Artist);
        cmd.Parameters.AddWithValue("$diff", replay.Chart.DifficultyName);
        cmd.Parameters.AddWithValue("$level", replay.Chart.DifficultyLevel);
        cmd.Parameters.AddWithValue("$score", replay.Result.Score);
        cmd.Parameters.AddWithValue("$accuracy", replay.Result.Accuracy);
        cmd.Parameters.AddWithValue("$combo", replay.Result.MaxCombo);
        cmd.Parameters.AddWithValue("$grade", replay.Result.Grade);
        cmd.Parameters.AddWithValue("$fc", replay.Result.FullCombo ? 1 : 0);
        cmd.Parameters.AddWithValue("$events", replay.EventCount);
        cmd.Parameters.AddWithValue("$utc", replay.RecordedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$file", FileNameFor(replay.ReplayId));
        cmd.ExecuteNonQuery();
    }

    /// <summary>The full replay, or null when its file has gone missing.</summary>
    public Replay? Load(Guid replayId)
    {
        string path = PathFor(replayId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return ReplayJson.Deserialize(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Log.Warn($"replay {replayId:D} could not be read", ex);
            return null;
        }
    }

    public IReadOnlyList<ReplaySummary> ListForPlayer(Guid playerId, int limit = 50) =>
        Query("WHERE player_id = $p ORDER BY recorded_utc DESC LIMIT $n",
            cmd =>
            {
                cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));
                cmd.Parameters.AddWithValue("$n", limit);
            });

    /// <summary>The best-scoring replays on a chart, for watching how someone did it.</summary>
    public IReadOnlyList<ReplaySummary> ListForChart(string chartKey, int limit = 10) =>
        Query("WHERE chart_key = $k ORDER BY score DESC LIMIT $n",
            cmd =>
            {
                cmd.Parameters.AddWithValue("$k", chartKey);
                cmd.Parameters.AddWithValue("$n", limit);
            });

    public int Count(Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM replays WHERE player_id = $p;";
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public void Delete(Guid replayId)
    {
        using (SqliteCommand cmd = _db.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM replays WHERE replay_id = $id;";
            cmd.Parameters.AddWithValue("$id", replayId.ToString("D"));
            cmd.ExecuteNonQuery();
        }

        try
        {
            File.Delete(PathFor(replayId));
        }
        catch (Exception ex)
        {
            // The row is gone, which is what the player asked for; a locked file is not
            // worth failing the delete over.
            Log.Warn($"replay file for {replayId:D} could not be deleted", ex);
        }
    }

    /// <summary>
    /// Drops rows whose file has disappeared. Run on startup: a replay the player cannot
    /// watch should not sit in the list waiting to disappoint them.
    /// </summary>
    public int PruneMissing()
    {
        var orphans = new List<Guid>();

        using (SqliteCommand cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT replay_id FROM replays;";
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = Guid.Parse(r.GetString(0));
                if (!File.Exists(PathFor(id)))
                {
                    orphans.Add(id);
                }
            }
        }

        foreach (Guid id in orphans)
        {
            Delete(id);
        }

        return orphans.Count;
    }

    /// <summary>Keeps the newest <paramref name="keep"/> replays for a player.</summary>
    public int Prune(Guid playerId, int keep)
    {
        var doomed = new List<Guid>();

        using (SqliteCommand cmd = _db.CreateCommand())
        {
            cmd.CommandText =
                """
                SELECT replay_id FROM replays WHERE player_id = $p
                ORDER BY recorded_utc DESC LIMIT -1 OFFSET $keep;
                """;
            cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));
            cmd.Parameters.AddWithValue("$keep", keep);

            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                doomed.Add(Guid.Parse(r.GetString(0)));
            }
        }

        foreach (Guid id in doomed)
        {
            Delete(id);
        }

        return doomed.Count;
    }

    private IReadOnlyList<ReplaySummary> Query(string where, Action<SqliteCommand> bind)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            $"""
             SELECT replay_id, player_id, chart_key, title, artist, difficulty_name,
                    difficulty_level, score, accuracy, grade, full_combo, event_count,
                    recorded_utc,
                    (SELECT display_name FROM profiles WHERE profiles.player_id = replays.player_id)
             FROM replays
             {where};
             """;
        bind(cmd);

        var list = new List<ReplaySummary>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ReplaySummary(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                // The current display name, so a renamed player's replays follow them (§80).
                r.IsDBNull(13) ? "" : r.GetString(13),
                r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                r.GetDouble(6), r.GetInt64(7), r.GetDouble(8), r.GetString(9),
                r.GetInt64(10) == 1, r.GetInt32(11),
                DateTime.Parse(r.GetString(12), null,
                    System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime()));
        }

        return list;
    }
}

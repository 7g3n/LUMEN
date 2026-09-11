using Lumen.Core.Charts;
using Lumen.Core.Library;
using Microsoft.Data.Sqlite;

namespace Lumen.Data.Repositories;

/// <inheritdoc />
public sealed class LibraryRepository : ILibraryRepository
{
    private readonly Database _db;

    public LibraryRepository(Database db) => _db = db;

    public void Upsert(LibraryChart chart)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO charts (chart_key, title, artist, creator, difficulty_name, difficulty_level,
                                note_count, hold_count, lane_count, duration_ms, preview_ms,
                                audio_file, chart_path, audio_path, source, attributes_json,
                                added_utc, updated_utc)
            VALUES ($key, $title, $artist, $creator, $diff, $level, $notes, $holds, $lanes,
                    $duration, $preview, $audioFile, $chartPath, $audioPath, $source, $attrs,
                    $added, $updated)
            ON CONFLICT (chart_key) DO UPDATE SET
                title = excluded.title,
                artist = excluded.artist,
                creator = excluded.creator,
                difficulty_name = excluded.difficulty_name,
                difficulty_level = excluded.difficulty_level,
                note_count = excluded.note_count,
                hold_count = excluded.hold_count,
                lane_count = excluded.lane_count,
                duration_ms = excluded.duration_ms,
                preview_ms = excluded.preview_ms,
                audio_file = excluded.audio_file,
                chart_path = excluded.chart_path,
                audio_path = excluded.audio_path,
                source = excluded.source,
                attributes_json = excluded.attributes_json,
                updated_utc = excluded.updated_utc;
            """;

        cmd.Parameters.AddWithValue("$key", chart.ChartKey);
        cmd.Parameters.AddWithValue("$title", chart.Meta.Title);
        cmd.Parameters.AddWithValue("$artist", chart.Meta.Artist);
        cmd.Parameters.AddWithValue("$creator", chart.Meta.Creator);
        cmd.Parameters.AddWithValue("$diff", chart.Meta.DifficultyName);
        cmd.Parameters.AddWithValue("$level", chart.Level);
        cmd.Parameters.AddWithValue("$notes", chart.NoteCount);
        cmd.Parameters.AddWithValue("$holds", chart.HoldCount);
        cmd.Parameters.AddWithValue("$lanes", chart.LaneCount);
        cmd.Parameters.AddWithValue("$duration", chart.DurationMs);
        cmd.Parameters.AddWithValue("$preview", chart.Meta.PreviewMs);
        cmd.Parameters.AddWithValue("$audioFile", chart.Meta.AudioFile);
        cmd.Parameters.AddWithValue("$chartPath", chart.ChartPath);
        cmd.Parameters.AddWithValue("$audioPath", chart.AudioPath);
        cmd.Parameters.AddWithValue("$source", chart.Source.ToString());
        cmd.Parameters.AddWithValue("$attrs", chart.Attributes.ToJson());
        cmd.Parameters.AddWithValue("$added", Utc(chart.AddedUtc));
        cmd.Parameters.AddWithValue("$updated", Utc(chart.UpdatedUtc));
        cmd.ExecuteNonQuery();
    }

    public LibraryChart? Get(string chartKey)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = $"{SelectAll} WHERE chart_key = $key;";
        cmd.Parameters.AddWithValue("$key", chartKey);

        using SqliteDataReader r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public IReadOnlyList<LibraryChart> All()
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = $"{SelectAll} ORDER BY title, difficulty_level;";

        var list = new List<LibraryChart>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(Read(r));
        }

        return list;
    }

    public IReadOnlyDictionary<string, string> AllPaths()
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT chart_key, chart_path FROM charts;";

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            map[r.GetString(0)] = r.GetString(1);
        }

        return map;
    }

    public void Remove(string chartKey)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        // The chart row goes, but scores keyed on the same chart do not: a player who
        // deletes a chart file has not stopped having played it, and re-adding the file
        // brings the history straight back (spec §80's rule applied to charts).
        cmd.CommandText = "DELETE FROM charts WHERE chart_key = $key;";
        cmd.Parameters.AddWithValue("$key", chartKey);
        cmd.ExecuteNonQuery();
    }

    public int Count()
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM charts;";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    // --- favourites ---

    public bool IsFavorite(Guid playerId, string chartKey)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM favorites WHERE player_id = $p AND chart_key = $k;";
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));
        cmd.Parameters.AddWithValue("$k", chartKey);
        return cmd.ExecuteScalar() is not null;
    }

    public void SetFavorite(Guid playerId, string chartKey, bool favorite)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = favorite
            ? """
              INSERT INTO favorites (player_id, chart_key, created_utc)
              VALUES ($p, $k, $utc)
              ON CONFLICT (player_id, chart_key) DO NOTHING;
              """
            : "DELETE FROM favorites WHERE player_id = $p AND chart_key = $k;";

        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));
        cmd.Parameters.AddWithValue("$k", chartKey);
        if (favorite)
        {
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
        }

        cmd.ExecuteNonQuery();
    }

    public IReadOnlyCollection<string> Favorites(Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            "SELECT chart_key FROM favorites WHERE player_id = $p ORDER BY created_utc DESC;";
        cmd.Parameters.AddWithValue("$p", playerId.ToString("D"));

        var set = new HashSet<string>(StringComparer.Ordinal);
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            set.Add(r.GetString(0));
        }

        return set;
    }

    // --- mapping ---

    private const string SelectAll =
        """
        SELECT chart_key, title, artist, creator, difficulty_name, difficulty_level,
               note_count, hold_count, lane_count, duration_ms, preview_ms,
               audio_file, chart_path, audio_path, source, attributes_json,
               added_utc, updated_utc
        FROM charts
        """;

    private static LibraryChart Read(SqliteDataReader r) => new()
    {
        ChartKey = r.GetString(0),
        Meta = new ChartMeta
        {
            Title = r.GetString(1),
            Artist = r.GetString(2),
            Creator = r.GetString(3),
            DifficultyName = r.GetString(4),
            DifficultyLevel = r.GetDouble(5),
            AudioFile = r.GetString(11),
            PreviewMs = r.GetDouble(10),
        },
        NoteCount = r.GetInt32(6),
        HoldCount = r.GetInt32(7),
        LaneCount = r.GetInt32(8),
        DurationMs = r.GetDouble(9),
        Level = r.GetDouble(5),
        ChartPath = r.GetString(12),
        AudioPath = r.GetString(13),
        Source = Enum.TryParse(r.GetString(14), out ChartSource s) ? s : ChartSource.Local,
        Attributes = ChartAttributes.FromJson(r.GetString(15)),
        AddedUtc = ParseUtc(r.GetString(16)),
        UpdatedUtc = ParseUtc(r.GetString(17)),
    };

    private static string Utc(DateTime value) =>
        (value == default ? DateTime.UtcNow : value.ToUniversalTime()).ToString("O");

    private static DateTime ParseUtc(string s) =>
        DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
}

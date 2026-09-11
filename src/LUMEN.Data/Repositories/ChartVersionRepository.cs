using Lumen.Core.Charts;
using Microsoft.Data.Sqlite;

namespace Lumen.Data.Repositories;

/// <summary>One stored revision of a chart (spec §58).</summary>
public sealed record ChartVersion(
    Guid ChartId, int Version, DateTime SavedUtc, int NoteCount,
    double DifficultyLevel, string Title, string DifficultyName);

/// <summary>
/// Revision history for authored charts (spec §58).
///
/// The whole document is stored, not a diff: charts are tens of kilobytes of JSON, and a
/// history you can restore from without replaying a chain of patches is worth far more
/// than the space it costs. Keyed on the chart's stable id rather than its content key,
/// because the content key is exactly what changes when a chart is edited.
/// </summary>
public sealed class ChartVersionRepository
{
    /// <summary>Revisions kept per chart. Older ones are dropped as new ones arrive.</summary>
    public const int Retention = 30;

    private readonly Database _db;

    public ChartVersionRepository(Database db) => _db = db;

    /// <summary>
    /// Records a revision, unless it is identical to the newest one already stored -
    /// pressing save twice, or a Test Play that saves first, should not fill the history
    /// with copies of the same document.
    /// </summary>
    public int Record(Guid chartId, Chart chart, string document)
    {
        string? latest = LatestDocument(chartId);
        if (latest is not null && string.Equals(latest, document, StringComparison.Ordinal))
        {
            return LatestVersion(chartId);
        }

        int version = LatestVersion(chartId) + 1;

        using (SqliteCommand cmd = _db.CreateCommand())
        {
            cmd.CommandText =
                """
                INSERT INTO chart_versions (chart_id, version, saved_utc, note_count,
                                            difficulty_level, title, difficulty_name, document)
                VALUES ($id, $v, $utc, $notes, $level, $title, $diff, $doc);
                """;
            cmd.Parameters.AddWithValue("$id", chartId.ToString("D"));
            cmd.Parameters.AddWithValue("$v", version);
            cmd.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$notes", chart.Notes.Count);
            cmd.Parameters.AddWithValue("$level", chart.Meta.DifficultyLevel);
            cmd.Parameters.AddWithValue("$title", chart.Meta.Title);
            cmd.Parameters.AddWithValue("$diff", chart.Meta.DifficultyName);
            cmd.Parameters.AddWithValue("$doc", document);
            cmd.ExecuteNonQuery();
        }

        Prune(chartId, Retention);
        return version;
    }

    public IReadOnlyList<ChartVersion> List(Guid chartId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT chart_id, version, saved_utc, note_count, difficulty_level, title, difficulty_name
            FROM chart_versions WHERE chart_id = $id ORDER BY version DESC;
            """;
        cmd.Parameters.AddWithValue("$id", chartId.ToString("D"));

        var list = new List<ChartVersion>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ChartVersion(
                Guid.Parse(r.GetString(0)), r.GetInt32(1), ParseUtc(r.GetString(2)),
                r.GetInt32(3), r.GetDouble(4), r.GetString(5), r.GetString(6)));
        }

        return list;
    }

    /// <summary>The stored document for one revision, ready to hand back to the editor.</summary>
    public Chart? Get(Guid chartId, int version)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            "SELECT document FROM chart_versions WHERE chart_id = $id AND version = $v;";
        cmd.Parameters.AddWithValue("$id", chartId.ToString("D"));
        cmd.Parameters.AddWithValue("$v", version);

        if (cmd.ExecuteScalar() is not string document)
        {
            return null;
        }

        try
        {
            return ChartJson.Deserialize(document);
        }
        catch
        {
            // A revision that will not parse is a revision that cannot be restored; the
            // caller gets null rather than an exception from a history browser.
            return null;
        }
    }

    public int LatestVersion(Guid chartId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            "SELECT COALESCE(MAX(version), 0) FROM chart_versions WHERE chart_id = $id;";
        cmd.Parameters.AddWithValue("$id", chartId.ToString("D"));
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public int Count(Guid chartId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM chart_versions WHERE chart_id = $id;";
        cmd.Parameters.AddWithValue("$id", chartId.ToString("D"));
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private string? LatestDocument(Guid chartId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            SELECT document FROM chart_versions WHERE chart_id = $id
            ORDER BY version DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id", chartId.ToString("D"));
        return cmd.ExecuteScalar() as string;
    }

    private void Prune(Guid chartId, int keep)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            DELETE FROM chart_versions
            WHERE chart_id = $id AND version NOT IN (
                SELECT version FROM chart_versions WHERE chart_id = $id
                ORDER BY version DESC LIMIT $keep
            );
            """;
        cmd.Parameters.AddWithValue("$id", chartId.ToString("D"));
        cmd.Parameters.AddWithValue("$keep", keep);
        cmd.ExecuteNonQuery();
    }

    private static DateTime ParseUtc(string s) =>
        DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();
}

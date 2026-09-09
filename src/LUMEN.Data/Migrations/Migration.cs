using Microsoft.Data.Sqlite;

namespace Lumen.Data.Migrations;

/// <summary>
/// One forward-only schema step. <see cref="Version"/> values are contiguous starting
/// at 1 and map onto SQLite's <c>PRAGMA user_version</c>.
/// </summary>
public sealed class Migration
{
    public Migration(int version, string name, Action<SqliteConnection, SqliteTransaction> up)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Migration versions start at 1.");
        }

        Version = version;
        Name = name;
        Up = up;
    }

    public int Version { get; }

    public string Name { get; }

    public Action<SqliteConnection, SqliteTransaction> Up { get; }

    /// <summary>Convenience for migrations that are a single SQL script.</summary>
    public static Migration Sql(int version, string name, string sql) =>
        new(version, name, (conn, tx) =>
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        });
}

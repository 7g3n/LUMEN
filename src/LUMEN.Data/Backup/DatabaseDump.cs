using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Lumen.Data.Backup;

/// <summary>
/// A logical dump of the database: every user table, row by row, as JSON.
///
/// Logical rather than a copy of the file, so a backup taken on one build restores on a
/// later one whose schema has moved on. JSON rather than the `database.sql` the format
/// note originally sketched, because generating SQL means hand-escaping quotes, NULs and
/// blobs into string literals — a correctness risk with no upside, while JSON round-trips
/// values exactly and lets the importer skip a column that no longer exists.
///
/// Restore inserts into a database that migrations have already brought to the current
/// schema, so the dump carries data only.
/// </summary>
public static class DatabaseDump
{
    /// <summary>Tables that are rebuilt rather than restored.</summary>
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "sqlite_sequence",
    };

    /// <summary>
    /// Order matters: a row with a foreign key must land after the row it points at, or
    /// the insert is rejected. Anything not listed follows in whatever order it is found.
    /// </summary>
    private static readonly string[] InsertOrder =
    {
        "app_meta", "profiles", "settings", "charts", "chart_versions",
        "favorites", "scores", "performances", "rating_snapshots",
        "replays", "achievements",
    };

    public sealed record Result(int Tables, int Rows);

    public static JsonObject Export(Database db)
    {
        var tables = new JsonObject();

        foreach (string table in UserTables(db))
        {
            tables[table] = ExportTable(db, table);
        }

        return new JsonObject
        {
            ["schemaVersion"] = db.SchemaVersion,
            ["tables"] = tables,
        };
    }

    /// <summary>
    /// Merges a dump into the database inside one transaction.
    ///
    /// Rows are inserted with <c>INSERT OR REPLACE</c> on their primary keys, so importing
    /// the same backup twice is a no-op rather than a duplicate, and importing someone
    /// else's adds their profile next to yours instead of replacing it (spec §13).
    /// Columns the dump has and this build does not are dropped; columns this build has
    /// and the dump does not take their defaults.
    /// </summary>
    public static Result Import(Database db, JsonObject dump)
    {
        if (dump["tables"] is not JsonObject tables)
        {
            throw new InvalidDataException("This backup contains no table data.");
        }

        int tableCount = 0;
        int rowCount = 0;

        db.InTransaction(() =>
        {
            foreach (string table in OrderedTables(tables))
            {
                if (tables[table] is not JsonArray rows || !TableExists(db, table))
                {
                    continue;
                }

                HashSet<string> columns = ColumnsOf(db, table);
                int inserted = ImportTable(db, table, rows, columns);

                if (inserted > 0)
                {
                    tableCount++;
                    rowCount += inserted;
                }
            }
        });

        return new Result(tableCount, rowCount);
    }

    // --- internals ---

    public static IReadOnlyList<string> UserTables(Database db)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";

        var names = new List<string>();
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            string name = r.GetString(0);
            if (!Skipped.Contains(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static JsonArray ExportTable(Database db, string table)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Quote(table)};";

        var rows = new JsonArray();
        using SqliteDataReader r = cmd.ExecuteReader();

        while (r.Read())
        {
            var row = new JsonObject();
            for (int i = 0; i < r.FieldCount; i++)
            {
                row[r.GetName(i)] = ToNode(r, i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static JsonNode? ToNode(SqliteDataReader r, int i)
    {
        if (r.IsDBNull(i))
        {
            return null;
        }

        return r.GetFieldType(i) switch
        {
            var t when t == typeof(long) => JsonValue.Create(r.GetInt64(i)),
            var t when t == typeof(double) => JsonValue.Create(r.GetDouble(i)),
            var t when t == typeof(byte[]) => JsonValue.Create(Convert.ToBase64String((byte[])r.GetValue(i))),
            _ => JsonValue.Create(r.GetString(i)),
        };
    }

    private static int ImportTable(Database db, string table, JsonArray rows, HashSet<string> columns)
    {
        int inserted = 0;

        foreach (JsonNode? node in rows)
        {
            if (node is not JsonObject row)
            {
                continue;
            }

            var usable = row.Where(p => columns.Contains(p.Key)).ToArray();
            if (usable.Length == 0)
            {
                continue;
            }

            using SqliteCommand cmd = db.CreateCommand();
            cmd.CommandText =
                $"INSERT OR REPLACE INTO {Quote(table)} " +
                $"({string.Join(", ", usable.Select(p => Quote(p.Key)))}) " +
                $"VALUES ({string.Join(", ", usable.Select((_, i) => $"$p{i}"))});";

            for (int i = 0; i < usable.Length; i++)
            {
                cmd.Parameters.AddWithValue($"$p{i}", ValueOf(usable[i].Value) ?? DBNull.Value);
            }

            cmd.ExecuteNonQuery();
            inserted++;
        }

        return inserted;
    }

    private static object? ValueOf(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue(out long asLong))
        {
            return asLong;
        }

        if (value.TryGetValue(out double asDouble))
        {
            return asDouble;
        }

        if (value.TryGetValue(out bool asBool))
        {
            return asBool ? 1L : 0L;
        }

        return value.TryGetValue(out string? asString) ? asString : value.ToString();
    }

    private static IEnumerable<string> OrderedTables(JsonObject tables)
    {
        var present = tables.Select(p => p.Key).ToList();

        foreach (string name in InsertOrder)
        {
            if (present.Remove(name))
            {
                yield return name;
            }
        }

        foreach (string name in present)
        {
            yield return name;
        }
    }

    private static bool TableExists(Database db, string table)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $n;";
        cmd.Parameters.AddWithValue("$n", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static HashSet<string> ColumnsOf(Database db, string table)
    {
        using SqliteCommand cmd = db.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({Quote(table)});";

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            columns.Add(r.GetString(1));
        }

        return columns;
    }

    /// <summary>
    /// Identifiers come from <c>sqlite_master</c> and <c>PRAGMA table_info</c>, never from
    /// a backup file, but they are still quoted: the cost is nothing and it removes the
    /// question of whether a future table name could ever need it.
    /// </summary>
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public static string ToJson(JsonObject dump) =>
        dump.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    public static JsonObject FromJson(string json)
    {
        JsonNode? node = JsonNode.Parse(json)
                         ?? throw new InvalidDataException("This backup's data is empty.");

        return node as JsonObject
               ?? throw new InvalidDataException("This backup's data is not in the expected shape.");
    }
}

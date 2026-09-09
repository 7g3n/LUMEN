using Lumen.Core.Settings;
using Microsoft.Data.Sqlite;

namespace Lumen.Data.Repositories;

/// <inheritdoc />
public sealed class SettingsRepository : ISettingsRepository
{
    private readonly Database _db;

    public SettingsRepository(Database db) => _db = db;

    public string? Get(Guid playerId, string key)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE player_id = $id AND key = $k;";
        cmd.Parameters.AddWithValue("$id", playerId.ToString("D"));
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void Set(Guid playerId, string key, string value)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO settings (player_id, key, value) VALUES ($id, $k, $v)
            ON CONFLICT(player_id, key) DO UPDATE SET value = excluded.value;
            """;
        cmd.Parameters.AddWithValue("$id", playerId.ToString("D"));
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<string, string> GetAll(Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM settings WHERE player_id = $id;";
        cmd.Parameters.AddWithValue("$id", playerId.ToString("D"));

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            map[reader.GetString(0)] = reader.GetString(1);
        }

        return map;
    }

    public void Remove(Guid playerId, string key)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM settings WHERE player_id = $id AND key = $k;";
        cmd.Parameters.AddWithValue("$id", playerId.ToString("D"));
        cmd.Parameters.AddWithValue("$k", key);
        cmd.ExecuteNonQuery();
    }
}

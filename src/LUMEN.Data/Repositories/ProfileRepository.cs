using Lumen.Core.Profiles;
using Microsoft.Data.Sqlite;

namespace Lumen.Data.Repositories;

/// <inheritdoc />
public sealed class ProfileRepository : IProfileRepository
{
    private const string Columns =
        "player_id, display_name, avatar_path, created_utc, last_played_utc, total_play_time_ms";

    private readonly Database _db;

    public ProfileRepository(Database db) => _db = db;

    public Profile Create(string displayName)
    {
        PlayerNameResult validation = PlayerName.Validate(displayName);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"Invalid player name: {validation.Error}", nameof(displayName));
        }

        DateTime now = DateTime.UtcNow;
        var profile = new Profile
        {
            PlayerId = Guid.NewGuid(),
            DisplayName = validation.Normalized,
            CreatedUtc = now,
            LastPlayedUtc = now,
            TotalPlayTimeMs = 0,
        };

        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            $"""
             INSERT INTO profiles ({Columns})
             VALUES ($id, $name, $avatar, $created, $lastPlayed, 0);
             """;
        cmd.Parameters.AddWithValue("$id", profile.PlayerId.ToString("D"));
        cmd.Parameters.AddWithValue("$name", profile.DisplayName);
        cmd.Parameters.AddWithValue("$avatar", (object?)profile.AvatarPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$created", Iso(profile.CreatedUtc));
        cmd.Parameters.AddWithValue("$lastPlayed", Iso(profile.LastPlayedUtc));
        cmd.ExecuteNonQuery();

        return profile;
    }

    public Profile? Get(Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM profiles WHERE player_id = $id;";
        cmd.Parameters.AddWithValue("$id", playerId.ToString("D"));

        using SqliteDataReader reader = cmd.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public IReadOnlyList<Profile> GetAll()
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM profiles ORDER BY last_played_utc DESC;";

        var list = new List<Profile>();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(Read(reader));
        }

        return list;
    }

    public Profile? GetMostRecent()
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM profiles ORDER BY last_played_utc DESC LIMIT 1;";

        using SqliteDataReader reader = cmd.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public int Count()
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM profiles;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Rename(Guid playerId, string newDisplayName)
    {
        PlayerNameResult validation = PlayerName.Validate(newDisplayName);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"Invalid player name: {validation.Error}", nameof(newDisplayName));
        }

        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "UPDATE profiles SET display_name = $name WHERE player_id = $id;";
        cmd.Parameters.AddWithValue("$name", validation.Normalized);
        cmd.Parameters.AddWithValue("$id", playerId.ToString("D"));
        RequireOneRow(cmd.ExecuteNonQuery(), playerId);
    }

    public void UpdateLastPlayed(Guid playerId, DateTime utc)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "UPDATE profiles SET last_played_utc = $t WHERE player_id = $id;";
        cmd.Parameters.AddWithValue("$t", Iso(utc));
        cmd.Parameters.AddWithValue("$id", playerId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    public void AddPlayTime(Guid playerId, long milliseconds)
    {
        if (milliseconds <= 0)
        {
            return;
        }

        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            "UPDATE profiles SET total_play_time_ms = total_play_time_ms + $ms WHERE player_id = $id;";
        cmd.Parameters.AddWithValue("$ms", milliseconds);
        cmd.Parameters.AddWithValue("$id", playerId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    public void Delete(Guid playerId)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM profiles WHERE player_id = $id;";
        cmd.Parameters.AddWithValue("$id", playerId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    private static Profile Read(SqliteDataReader r) => new()
    {
        PlayerId = Guid.Parse(r.GetString(0)),
        DisplayName = r.GetString(1),
        AvatarPath = r.IsDBNull(2) ? null : r.GetString(2),
        CreatedUtc = ParseIso(r.GetString(3)),
        LastPlayedUtc = ParseIso(r.GetString(4)),
        TotalPlayTimeMs = r.GetInt64(5),
    };

    private static string Iso(DateTime utc) => utc.ToUniversalTime().ToString("O");

    private static DateTime ParseIso(string s) =>
        DateTime.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static void RequireOneRow(int affected, Guid playerId)
    {
        if (affected == 0)
        {
            throw new InvalidOperationException($"No profile with id {playerId}.");
        }
    }
}

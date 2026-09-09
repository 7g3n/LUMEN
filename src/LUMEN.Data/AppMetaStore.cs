using Lumen.Core;
using Microsoft.Data.Sqlite;

namespace Lumen.Data;

/// <summary>
/// Key/value access to the <c>app_meta</c> table: install id, first-run timestamp,
/// the game version that last opened the folder (used later to trigger backups on
/// upgrade, spec §12).
/// </summary>
public sealed class AppMetaStore
{
    public const string KeyInstallId = "install_id";
    public const string KeyCreatedUtc = "created_utc";
    public const string KeyLastVersion = "last_version";
    public const string KeyLastLaunchUtc = "last_launch_utc";
    public const string KeyActivePlayerId = "active_player_id";

    private readonly Database _db;

    public AppMetaStore(Database db) => _db = db;

    public string? Get(string key)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_meta WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void Set(string key, string value)
    {
        using SqliteCommand cmd = _db.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO app_meta (key, value) VALUES ($k, $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public string InstallId => Get(KeyInstallId) ?? "";

    public Guid? ActivePlayerId
    {
        get => Guid.TryParse(Get(KeyActivePlayerId), out Guid id) ? id : null;
        set
        {
            if (value is { } id)
            {
                Set(KeyActivePlayerId, id.ToString("D"));
            }
        }
    }

    /// <summary>
    /// Ensures the install-identity rows exist and records this launch. Returns the
    /// version string that previously opened this folder (null on first ever run).
    /// </summary>
    public string? RegisterLaunch()
    {
        string nowUtc = DateTime.UtcNow.ToString("O");
        string? previousVersion = null;

        _db.InTransaction(() =>
        {
            if (Get(KeyInstallId) is null)
            {
                Set(KeyInstallId, Guid.NewGuid().ToString("D"));
                Set(KeyCreatedUtc, nowUtc);
            }

            previousVersion = Get(KeyLastVersion);
            Set(KeyLastVersion, GameIdentity.Version);
            Set(KeyLastLaunchUtc, nowUtc);
        });

        return previousVersion;
    }
}

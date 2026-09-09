using Lumen.Core.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Lumen.Data.Migrations;

/// <summary>
/// Applies pending <see cref="Migration"/>s to a connection. Each migration runs in
/// its own transaction together with the <c>user_version</c> bump, so an interruption
/// never leaves the schema half-upgraded (spec §11, §74). Idempotent: running again
/// with nothing pending is a no-op.
/// </summary>
public static class MigrationRunner
{
    public static int Migrate(SqliteConnection connection, IReadOnlyList<Migration> migrations)
    {
        Validate(migrations);

        int current = GetUserVersion(connection);
        int target = migrations.Count == 0 ? current : migrations[^1].Version;

        if (current > target)
        {
            throw new InvalidOperationException(
                $"Database schema is v{current} but this build only knows up to v{target}. " +
                "The data folder was written by a newer version of the game.");
        }

        foreach (Migration migration in migrations.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            using SqliteTransaction tx = connection.BeginTransaction();
            try
            {
                migration.Up(connection, tx);
                SetUserVersion(connection, tx, migration.Version);
                tx.Commit();
                Log.Info($"migration v{migration.Version} applied: {migration.Name}");
            }
            catch (Exception ex)
            {
                tx.Rollback();
                Log.Error($"migration v{migration.Version} failed ({migration.Name}); rolled back", ex);
                throw;
            }
        }

        return GetUserVersion(connection);
    }

    private static void Validate(IReadOnlyList<Migration> migrations)
    {
        for (int i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Version != i + 1)
            {
                throw new InvalidOperationException(
                    $"Migrations must be contiguous starting at 1; entry {i} has version {migrations[i].Version}.");
            }
        }
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void SetUserVersion(SqliteConnection connection, SqliteTransaction tx, int version)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        // PRAGMA does not accept parameters; version is a validated int, not user input.
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }
}

using Lumen.Core.Diagnostics;
using Lumen.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace Lumen.Data;

/// <summary>
/// Owns the SQLite connection for a LUMEN data folder. Applies the pragmas that make
/// the database crash-resilient (WAL) and correct (foreign keys), then brings the
/// schema up to date via <see cref="MigrationRunner"/> (spec §10, §11).
/// </summary>
public sealed class Database : IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _connection;

    public Database(string databaseFilePath)
    {
        DatabaseFilePath = databaseFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databaseFilePath)!);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
        }.ToString();
    }

    public string DatabaseFilePath { get; }

    public int SchemaVersion { get; private set; }

    /// <summary>The transaction opened by the innermost <see cref="InTransaction"/>, if any.</summary>
    public SqliteTransaction? CurrentTransaction { get; private set; }

    public SqliteConnection Connection =>
        _connection ?? throw new InvalidOperationException("Database not opened. Call Open() first.");

    /// <summary>Opens the connection, applies pragmas, and runs pending migrations.</summary>
    public void Open()
    {
        if (_connection is not null)
        {
            return;
        }

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        ApplyConnectionPragmas(connection);

        SchemaVersion = MigrationRunner.Migrate(connection, SchemaMigrations.All);
        _connection = connection;

        Log.Info($"database ready: {Path.GetFileName(DatabaseFilePath)} schema v{SchemaVersion}");
    }

    /// <summary>
    /// Creates a command already enlisted in <see cref="CurrentTransaction"/> when one
    /// is open. Always prefer this over <c>Connection.CreateCommand()</c> in repositories.
    /// </summary>
    public SqliteCommand CreateCommand()
    {
        SqliteCommand cmd = Connection.CreateCommand();
        cmd.Transaction = CurrentTransaction;
        return cmd;
    }

    /// <summary>
    /// Runs an action inside a single transaction; rolls back on any exception (spec §11).
    /// Re-entrant: a nested call joins the outer transaction rather than starting a new one.
    /// </summary>
    public void InTransaction(Action work)
    {
        if (CurrentTransaction is not null)
        {
            work();
            return;
        }

        using SqliteTransaction tx = Connection.BeginTransaction();
        CurrentTransaction = tx;
        try
        {
            work();
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        finally
        {
            CurrentTransaction = null;
        }
    }

    internal static void ApplyConnectionPragmas(SqliteConnection connection)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA temp_store = MEMORY;
            """;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (_connection is not null)
        {
            using (SqliteCommand cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                try { cmd.ExecuteNonQuery(); } catch { /* best effort on shutdown */ }
            }

            _connection.Dispose();
            _connection = null;
        }

        SqliteConnection.ClearAllPools();
    }
}

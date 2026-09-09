using System;
using System.Collections.Generic;
using FluentAssertions;
using Lumen.Data;
using Lumen.Data.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Lumen.Tests.Data;

public class MigrationRunnerTests
{
    private static SqliteConnection OpenMemory()
    {
        // A private in-memory DB that stays alive for the life of the connection.
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        return conn;
    }

    private static int UserVersion(SqliteConnection conn)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static bool TableExists(SqliteConnection conn, string name)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$n;";
        cmd.Parameters.AddWithValue("$n", name);
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    [Fact]
    public void Applies_all_pending_migrations_and_reports_the_final_version()
    {
        using SqliteConnection conn = OpenMemory();

        int version = MigrationRunner.Migrate(conn, SchemaMigrations.All);

        version.Should().Be(SchemaMigrations.All.Count);
        UserVersion(conn).Should().Be(SchemaMigrations.All.Count);
        TableExists(conn, "app_meta").Should().BeTrue();
    }

    [Fact]
    public void Running_again_is_a_no_op()
    {
        using SqliteConnection conn = OpenMemory();
        MigrationRunner.Migrate(conn, SchemaMigrations.All);

        int again = MigrationRunner.Migrate(conn, SchemaMigrations.All);

        again.Should().Be(SchemaMigrations.All.Count);
    }

    [Fact]
    public void A_failing_migration_rolls_back_and_leaves_the_version_untouched()
    {
        using SqliteConnection conn = OpenMemory();
        var migrations = new List<Migration>
        {
            Migration.Sql(1, "ok", "CREATE TABLE a (id INTEGER);"),
            new(2, "boom", (c, tx) =>
            {
                using SqliteCommand cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "CREATE TABLE b (id INTEGER); INSERT INTO nonexistent VALUES (1);";
                cmd.ExecuteNonQuery();
            }),
        };

        Action act = () => MigrationRunner.Migrate(conn, migrations);

        act.Should().Throw<SqliteException>();
        UserVersion(conn).Should().Be(1);      // step 1 committed
        TableExists(conn, "b").Should().BeFalse(); // step 2 fully rolled back
    }

    [Fact]
    public void Rejects_a_database_newer_than_the_build()
    {
        using SqliteConnection conn = OpenMemory();
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version = 999;";
            cmd.ExecuteNonQuery();
        }

        Action act = () => MigrationRunner.Migrate(conn, SchemaMigrations.All);

        act.Should().Throw<InvalidOperationException>().WithMessage("*newer version*");
    }

    [Fact]
    public void Rejects_non_contiguous_migration_versions()
    {
        using SqliteConnection conn = OpenMemory();
        var migrations = new List<Migration>
        {
            Migration.Sql(1, "a", "CREATE TABLE a (id INTEGER);"),
            Migration.Sql(3, "c", "CREATE TABLE c (id INTEGER);"),
        };

        Action act = () => MigrationRunner.Migrate(conn, migrations);

        act.Should().Throw<InvalidOperationException>().WithMessage("*contiguous*");
    }
}

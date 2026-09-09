using System;
using System.IO;
using FluentAssertions;
using Lumen.Core;
using Lumen.Data;
using Xunit;

namespace Lumen.Tests.Data;

public class DatabaseTests : IDisposable
{
    private readonly string _dir;

    public DatabaseTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string DbPath => Path.Combine(_dir, "database", GameIdentity.DatabaseFileName);

    [Fact]
    public void Open_creates_the_file_and_migrates_to_the_latest_schema()
    {
        using var db = new Database(DbPath);
        db.Open();

        File.Exists(DbPath).Should().BeTrue();
        db.SchemaVersion.Should().Be(Lumen.Data.Migrations.SchemaMigrations.All.Count);
    }

    [Fact]
    public void Open_enables_wal_and_foreign_keys()
    {
        using var db = new Database(DbPath);
        db.Open();

        using var journal = db.CreateCommand();
        journal.CommandText = "PRAGMA journal_mode;";
        ((string)journal.ExecuteScalar()!).Should().BeEquivalentTo("wal");

        using var fk = db.CreateCommand();
        fk.CommandText = "PRAGMA foreign_keys;";
        Convert.ToInt32(fk.ExecuteScalar()).Should().Be(1);
    }

    [Fact]
    public void InTransaction_rolls_back_on_exception()
    {
        using var db = new Database(DbPath);
        db.Open();
        var meta = new AppMetaStore(db);

        try
        {
            db.InTransaction(() =>
            {
                meta.Set("temp", "value");
                throw new InvalidOperationException("abort");
            });
        }
        catch (InvalidOperationException)
        {
        }

        meta.Get("temp").Should().BeNull();
    }

    [Fact]
    public void RegisterLaunch_seeds_identity_on_first_run_then_reports_previous_version()
    {
        using (var db = new Database(DbPath))
        {
            db.Open();
            var meta = new AppMetaStore(db);

            meta.RegisterLaunch().Should().BeNull();          // first ever run
            meta.InstallId.Should().NotBeNullOrWhiteSpace();
            Guid.TryParse(meta.InstallId, out _).Should().BeTrue();
        }

        using (var db = new Database(DbPath))
        {
            db.Open();
            var meta = new AppMetaStore(db);

            meta.RegisterLaunch().Should().Be(GameIdentity.Version);
        }
    }

    [Fact]
    public void Data_survives_reopening()
    {
        using (var db = new Database(DbPath))
        {
            db.Open();
            new AppMetaStore(db).Set("k", "persisted");
        }

        using (var db = new Database(DbPath))
        {
            db.Open();
            new AppMetaStore(db).Get("k").Should().Be("persisted");
        }
    }
}

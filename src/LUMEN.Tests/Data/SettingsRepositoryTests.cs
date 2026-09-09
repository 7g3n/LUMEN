using System;
using System.IO;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Settings;
using Lumen.Data;
using Lumen.Data.Repositories;
using Xunit;

namespace Lumen.Tests.Data;

public class SettingsRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly SettingsRepository _settings;
    private readonly Guid _player;

    public SettingsRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "database", GameIdentity.DatabaseFileName));
        _db.Open();
        _settings = new SettingsRepository(_db);
        _player = new ProfileRepository(_db).Create("Tester").PlayerId;
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Set_then_Get_round_trips_and_upserts()
    {
        _settings.Set(_player, "audio.master", "70");
        _settings.Get(_player, "audio.master").Should().Be("70");

        _settings.Set(_player, "audio.master", "55");
        _settings.Get(_player, "audio.master").Should().Be("55");
    }

    [Fact]
    public void Missing_key_returns_null_and_typed_fallbacks_apply()
    {
        _settings.Get(_player, "nope").Should().BeNull();
        _settings.GetInt(_player, "nope", 42).Should().Be(42);
        _settings.GetBool(_player, "nope", true).Should().BeTrue();
        _settings.GetDouble(_player, "nope", 1.5).Should().Be(1.5);
    }

    [Fact]
    public void Typed_helpers_use_invariant_culture()
    {
        _settings.SetDouble(_player, "timing.offset", -12.5);
        _settings.Get(_player, "timing.offset").Should().Be("-12.5");
        _settings.GetDouble(_player, "timing.offset", 0).Should().Be(-12.5);
    }

    [Fact]
    public void GetAll_returns_only_this_players_settings()
    {
        Guid other = new ProfileRepository(_db).Create("Other").PlayerId;
        _settings.Set(_player, "a", "1");
        _settings.Set(_player, "b", "2");
        _settings.Set(other, "a", "99");

        _settings.GetAll(_player).Should().HaveCount(2)
            .And.Contain(new KeyValuePair<string, string>("a", "1"));
    }

    [Fact]
    public void Settings_persist_across_a_reopen()
    {
        _settings.Set(_player, "k", "v");
        _db.Dispose();

        using var db2 = new Database(Path.Combine(_dir, "database", GameIdentity.DatabaseFileName));
        db2.Open();
        new SettingsRepository(db2).Get(_player, "k").Should().Be("v");
    }

    [Fact]
    public void Deleting_a_profile_cascades_its_settings()
    {
        _settings.Set(_player, "k", "v");

        new ProfileRepository(_db).Delete(_player);

        _settings.GetAll(_player).Should().BeEmpty();
    }

    [Fact]
    public void Remove_deletes_one_key()
    {
        _settings.Set(_player, "k", "v");
        _settings.Remove(_player, "k");
        _settings.Get(_player, "k").Should().BeNull();
    }
}

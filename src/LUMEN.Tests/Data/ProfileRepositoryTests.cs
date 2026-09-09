using System;
using System.IO;
using System.Threading;
using FluentAssertions;
using Lumen.Core;
using Lumen.Data;
using Lumen.Data.Repositories;
using Xunit;

namespace Lumen.Tests.Data;

public class ProfileRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly ProfileRepository _repo;

    public ProfileRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-prof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "database", GameIdentity.DatabaseFileName));
        _db.Open();
        _repo = new ProfileRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Create_generates_a_uuid_and_normalizes_the_name()
    {
        var profile = _repo.Create("  Nagisa  ");

        profile.PlayerId.Should().NotBe(Guid.Empty);
        profile.DisplayName.Should().Be("Nagisa");
        _repo.Count().Should().Be(1);
        _repo.Get(profile.PlayerId)!.DisplayName.Should().Be("Nagisa");
    }

    [Fact]
    public void Create_rejects_an_invalid_name()
    {
        Action act = () => _repo.Create("   ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_changes_the_name_but_not_the_id()
    {
        var profile = _repo.Create("7g3");
        Guid originalId = profile.PlayerId;

        _repo.Rename(originalId, "Nagisa");

        var reloaded = _repo.Get(originalId)!;
        reloaded.PlayerId.Should().Be(originalId);
        reloaded.DisplayName.Should().Be("Nagisa");
        reloaded.CreatedUtc.Should().BeCloseTo(profile.CreatedUtc, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Two_profiles_can_share_a_display_name_with_distinct_ids()
    {
        var a = _repo.Create("Guest");
        var b = _repo.Create("Guest");

        a.PlayerId.Should().NotBe(b.PlayerId);
        _repo.Count().Should().Be(2);
    }

    [Fact]
    public void GetMostRecent_follows_last_played()
    {
        var a = _repo.Create("A");
        Thread.Sleep(5);
        var b = _repo.Create("B");

        _repo.GetMostRecent()!.PlayerId.Should().Be(b.PlayerId);

        _repo.UpdateLastPlayed(a.PlayerId, DateTime.UtcNow.AddSeconds(1));
        _repo.GetMostRecent()!.PlayerId.Should().Be(a.PlayerId);
    }

    [Fact]
    public void AddPlayTime_accumulates()
    {
        var p = _repo.Create("A");

        _repo.AddPlayTime(p.PlayerId, 1000);
        _repo.AddPlayTime(p.PlayerId, 500);
        _repo.AddPlayTime(p.PlayerId, -50); // ignored

        _repo.Get(p.PlayerId)!.TotalPlayTimeMs.Should().Be(1500);
    }

    [Fact]
    public void Rename_of_a_missing_profile_throws()
    {
        Action act = () => _repo.Rename(Guid.NewGuid(), "X");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Delete_removes_the_profile()
    {
        var p = _repo.Create("A");
        _repo.Delete(p.PlayerId);
        _repo.Get(p.PlayerId).Should().BeNull();
        _repo.Count().Should().Be(0);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Charts;
using Lumen.Core.Library;
using Lumen.Data;
using Lumen.Data.Repositories;
using Xunit;

namespace Lumen.Tests.Data;

public class LibraryRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly LibraryRepository _library;
    private readonly Guid _player;

    public LibraryRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "database", GameIdentity.DatabaseFileName));
        _db.Open();
        _library = new LibraryRepository(_db);
        _player = new ProfileRepository(_db).Create("Tester").PlayerId;
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static LibraryChart Entry(string key = "abc123", string title = "First Light",
                                      string difficulty = "MASTER", double level = 14.7)
    {
        return new LibraryChart
        {
            ChartKey = key,
            Meta = new ChartMeta
            {
                Title = title,
                Artist = "LUMEN",
                Creator = "7g3",
                DifficultyName = difficulty,
                DifficultyLevel = level,
                AudioFile = "first-light.wav",
                PreviewMs = 8000,
            },
            ChartPath = $@"C:\charts\{key}.lumenchart",
            AudioPath = @"C:\songs\first-light.wav",
            NoteCount = 842,
            HoldCount = 31,
            LaneCount = 4,
            DurationMs = 123_456,
            Level = level,
            Attributes = new ChartAttributes { Speed = 0.8, Technical = 0.4 },
            Source = ChartSource.Local,
            AddedUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            UpdatedUtc = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc),
        };
    }

    [Fact]
    public void An_upserted_chart_round_trips()
    {
        LibraryChart entry = Entry();
        _library.Upsert(entry);

        LibraryChart? read = _library.Get(entry.ChartKey);

        read.Should().NotBeNull();
        read!.Meta.Title.Should().Be("First Light");
        read.Meta.DifficultyName.Should().Be("MASTER");
        read.Level.Should().Be(14.7);
        read.NoteCount.Should().Be(842);
        read.HoldCount.Should().Be(31);
        read.LaneCount.Should().Be(4);
        read.DurationMs.Should().Be(123_456);
        read.ChartPath.Should().Be(entry.ChartPath);
        read.AudioPath.Should().Be(entry.AudioPath);
        read.Source.Should().Be(ChartSource.Local);
        read.Attributes.Speed.Should().BeApproximately(0.8, 1e-9);
        read.AddedUtc.Should().Be(entry.AddedUtc);
    }

    [Fact]
    public void Upserting_the_same_key_updates_rather_than_duplicating()
    {
        _library.Upsert(Entry(level: 14.7));
        _library.Upsert(Entry(level: 15.2) with { NoteCount = 900 });

        _library.Count().Should().Be(1);
        LibraryChart read = _library.Get("abc123")!;
        read.Level.Should().Be(15.2);
        read.NoteCount.Should().Be(900);
    }

    [Fact]
    public void Upserting_keeps_the_original_added_date_out_of_the_update_list()
    {
        // "added" is set once by the scanner; a re-scan must not make every chart look new.
        _library.Upsert(Entry());
        DateTime original = _library.Get("abc123")!.AddedUtc;

        _library.Upsert(Entry() with { AddedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow });

        _library.Get("abc123")!.AddedUtc.Should().Be(original);
    }

    [Fact]
    public void All_returns_every_chart()
    {
        _library.Upsert(Entry("a", "Alpha"));
        _library.Upsert(Entry("b", "Beta"));

        _library.All().Select(c => c.Meta.Title).Should().BeEquivalentTo("Alpha", "Beta");
    }

    [Fact]
    public void A_missing_chart_reads_back_as_null()
    {
        _library.Get("nothing-here").Should().BeNull();
    }

    [Fact]
    public void Remove_drops_only_the_named_chart()
    {
        _library.Upsert(Entry("a", "Alpha"));
        _library.Upsert(Entry("b", "Beta"));

        _library.Remove("a");

        _library.Count().Should().Be(1);
        _library.Get("b").Should().NotBeNull();
    }

    [Fact]
    public void AllPaths_maps_every_key_to_its_file()
    {
        _library.Upsert(Entry("a"));
        _library.Upsert(Entry("b"));

        IReadOnlyDictionary<string, string> paths = _library.AllPaths();

        paths.Should().HaveCount(2);
        paths["a"].Should().Be(@"C:\charts\a.lumenchart");
    }

    // --- favourites ---

    [Fact]
    public void A_chart_is_not_favourited_by_default()
    {
        _library.Upsert(Entry());
        _library.IsFavorite(_player, "abc123").Should().BeFalse();
    }

    [Fact]
    public void Favouriting_persists_and_can_be_undone()
    {
        _library.Upsert(Entry());

        _library.SetFavorite(_player, "abc123", true);
        _library.IsFavorite(_player, "abc123").Should().BeTrue();
        _library.Favorites(_player).Should().Contain("abc123");

        _library.SetFavorite(_player, "abc123", false);
        _library.IsFavorite(_player, "abc123").Should().BeFalse();
        _library.Favorites(_player).Should().BeEmpty();
    }

    [Fact]
    public void Favouriting_twice_is_not_an_error()
    {
        _library.Upsert(Entry());

        _library.SetFavorite(_player, "abc123", true);
        _library.SetFavorite(_player, "abc123", true);

        _library.Favorites(_player).Should().ContainSingle();
    }

    [Fact]
    public void Un_favouriting_something_that_was_never_favourited_is_not_an_error()
    {
        _library.Upsert(Entry());
        _library.SetFavorite(_player, "abc123", false);
        _library.Favorites(_player).Should().BeEmpty();
    }

    [Fact]
    public void Favourites_belong_to_one_profile_not_to_the_installation()
    {
        Guid other = new ProfileRepository(_db).Create("Someone Else").PlayerId;
        _library.Upsert(Entry());

        _library.SetFavorite(_player, "abc123", true);

        _library.IsFavorite(_player, "abc123").Should().BeTrue();
        _library.IsFavorite(other, "abc123").Should().BeFalse();
    }

    [Fact]
    public void Deleting_a_profile_takes_its_favourites_with_it()
    {
        _library.Upsert(Entry());
        _library.SetFavorite(_player, "abc123", true);

        new ProfileRepository(_db).Delete(_player);

        _library.Favorites(_player).Should().BeEmpty();
    }
}

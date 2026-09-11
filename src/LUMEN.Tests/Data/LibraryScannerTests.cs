using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Charts;
using Lumen.Core.Library;
using Lumen.Data;
using Lumen.Data.Library;
using Lumen.Data.Repositories;
using Xunit;

namespace Lumen.Tests.Data;

public class LibraryScannerTests : IDisposable
{
    private readonly string _dir;
    private readonly Database _db;
    private readonly LumenPaths _paths;
    private readonly LibraryRepository _library;
    private readonly LibraryScanner _scanner;

    public LibraryScannerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        _paths = LumenPaths.Resolve(_dir);
        // Resolve() picks %LOCALAPPDATA% unless a portable sentinel sits next to the exe;
        // the sentinel keeps this test entirely inside its temp folder.
        File.WriteAllText(Path.Combine(_dir, LumenPaths.PortableSentinelFileName), "");
        _paths = LumenPaths.Resolve(_dir);
        _paths.EnsureCreated();

        _db = new Database(_paths.DatabaseFile);
        _db.Open();
        _library = new LibraryRepository(_db);
        _scanner = new LibraryScanner(_library, _paths);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static Chart Chart(string title, string difficulty, double level, int notes = 64)
    {
        var list = new List<Note>();
        for (int i = 0; i < notes; i++)
        {
            list.Add(Note.Tap(500 + i * 250, i % 4));
        }

        return new Chart
        {
            LaneCount = 4,
            BpmPoints = new[] { new BpmPoint(0, 128) },
            Notes = list.ToArray(),
            Meta = new ChartMeta
            {
                Title = title,
                Artist = "LUMEN",
                Creator = "7g3",
                DifficultyName = difficulty,
                DifficultyLevel = level,
                AudioFile = "song.wav",
            },
        }.Normalized();
    }

    private string WriteChart(Chart chart, string fileName, bool imported = false)
    {
        string dir = imported ? _paths.ChartsImported : _paths.ChartsLocal;
        string path = Path.Combine(dir, fileName + "." + GameIdentity.ChartExtension);
        File.WriteAllText(path, ChartJson.Serialize(chart));
        return path;
    }

    private void WriteAudio(string name = "song.wav")
    {
        File.WriteAllBytes(Path.Combine(_paths.Songs, name), new byte[64]);
    }

    [Fact]
    public void A_chart_on_disk_is_indexed()
    {
        WriteChart(Chart("First Light", "MASTER", 14.7), "first-light-master");

        LibraryScanner.Result result = _scanner.Scan();

        result.Added.Should().Be(1);
        _library.Count().Should().Be(1);

        LibraryChart entry = _library.All().Single();
        entry.Meta.Title.Should().Be("First Light");
        entry.Meta.DifficultyName.Should().Be("MASTER");
        entry.Level.Should().Be(14.7);
        entry.NoteCount.Should().Be(64);
    }

    [Fact]
    public void Scanning_twice_updates_rather_than_duplicating()
    {
        WriteChart(Chart("First Light", "MASTER", 14.7), "first-light-master");

        _scanner.Scan();
        LibraryScanner.Result second = _scanner.Scan();

        second.Added.Should().Be(0);
        second.Updated.Should().Be(1);
        _library.Count().Should().Be(1);
    }

    [Fact]
    public void Both_chart_folders_are_scanned_and_their_source_recorded()
    {
        WriteChart(Chart("Mine", "NORMAL", 8), "mine");
        WriteChart(Chart("Theirs", "NORMAL", 8), "theirs", imported: true);

        _scanner.Scan();

        _library.All().Should().HaveCount(2);
        _library.All().Single(c => c.Meta.Title == "Mine").Source.Should().Be(ChartSource.Local);
        _library.All().Single(c => c.Meta.Title == "Theirs").Source.Should().Be(ChartSource.Imported);
    }

    [Fact]
    public void A_chart_whose_file_has_gone_is_dropped_from_the_index()
    {
        string path = WriteChart(Chart("First Light", "MASTER", 14.7), "first-light-master");
        _scanner.Scan();
        _library.Count().Should().Be(1);

        File.Delete(path);
        LibraryScanner.Result result = _scanner.Scan();

        result.Removed.Should().Be(1);
        _library.Count().Should().Be(0);
    }

    [Fact]
    public void Editing_a_chart_replaces_its_row_instead_of_adding_a_second_one()
    {
        // The chart key folds in the note count, so a saved edit arrives under a new key.
        // The row under the old key has to go, or every save would leave a difficulty in
        // the library that no longer exists in the file.
        string path = WriteChart(Chart("First Light", "MASTER", 14.7, notes: 64), "first-light");
        _scanner.Scan();
        _library.Count().Should().Be(1);

        File.WriteAllText(path, ChartJson.Serialize(Chart("First Light", "MASTER", 14.7, notes: 80)));
        LibraryScanner.Result result = _scanner.Scan();

        _library.Count().Should().Be(1);
        _library.All().Single().NoteCount.Should().Be(80);
        result.Removed.Should().Be(1);
    }

    [Fact]
    public void A_chart_whose_file_was_not_visited_this_pass_is_left_alone()
    {
        // Only the folders the scanner walks are reconciled; a row pointing somewhere
        // else (an old location, a drive that is offline) must not be silently dropped
        // while its file is still there.
        WriteChart(Chart("Kept", "NORMAL", 8), "kept");
        _scanner.Scan();

        string elsewhere = Path.Combine(_dir, "outside." + GameIdentity.ChartExtension);
        File.WriteAllText(elsewhere, ChartJson.Serialize(Chart("Outside", "NORMAL", 8)));
        _library.Upsert(_library.All().Single() with
        {
            ChartKey = "outside-key",
            ChartPath = elsewhere,
        });

        _scanner.Scan();

        _library.Get("outside-key").Should().NotBeNull();
    }

    [Fact]
    public void An_unreadable_chart_is_skipped_without_failing_the_scan()
    {
        WriteChart(Chart("Good", "NORMAL", 8), "good");
        File.WriteAllText(
            Path.Combine(_paths.ChartsLocal, "broken." + GameIdentity.ChartExtension),
            "{ this is not a chart");

        LibraryScanner.Result result = _scanner.Scan();

        result.Failed.Should().Be(1);
        result.Added.Should().Be(1);
        _library.All().Should().ContainSingle().Which.Meta.Title.Should().Be("Good");
    }

    [Fact]
    public void A_chart_with_no_notes_is_skipped()
    {
        WriteChart(Chart("Empty", "NORMAL", 8, notes: 0), "empty");

        _scanner.Scan().Added.Should().Be(0);
        _library.Count().Should().Be(0);
    }

    [Fact]
    public void The_analysed_level_fills_in_for_a_chart_whose_author_set_none()
    {
        // DifficultyLevel defaults to 1.0, which means "not set" as far as the scanner
        // is concerned; a chart like that must still sort somewhere sensible.
        WriteChart(Chart("Unlevelled", "NORMAL", 1.0), "unlevelled");

        _scanner.Scan();

        _library.All().Single().Level.Should().BeGreaterThan(1.0);
    }

    [Fact]
    public void The_authors_level_is_kept_when_they_set_one()
    {
        WriteChart(Chart("Levelled", "MASTER", 13.4), "levelled");

        _scanner.Scan();

        _library.All().Single().Level.Should().Be(13.4);
    }

    [Fact]
    public void Audio_next_to_the_chart_wins_over_the_songs_folder()
    {
        WriteAudio();
        string chartPath = WriteChart(Chart("First Light", "MASTER", 14.7), "first-light-master");
        string beside = Path.Combine(Path.GetDirectoryName(chartPath)!, "song.wav");
        File.WriteAllBytes(beside, new byte[32]);

        _scanner.Scan();

        _library.All().Single().AudioPath.Should().Be(beside);
    }

    [Fact]
    public void Audio_falls_back_to_the_songs_folder()
    {
        WriteAudio();
        WriteChart(Chart("First Light", "MASTER", 14.7), "first-light-master");

        _scanner.Scan();

        _library.All().Single().AudioPath
            .Should().Be(Path.Combine(_paths.Songs, "song.wav"));
    }

    [Fact]
    public void Two_difficulties_of_one_song_are_indexed_separately()
    {
        WriteChart(Chart("First Light", "EASY", 3, notes: 32), "first-light-easy");
        WriteChart(Chart("First Light", "MASTER", 14.7, notes: 128), "first-light-master");

        _scanner.Scan();

        _library.All().Should().HaveCount(2);
        LibraryQuery.Group(_library.All()).Should().ContainSingle()
            .Which.Charts.Should().HaveCount(2);
    }

    [Fact]
    public void Scanning_an_empty_library_reports_no_changes()
    {
        LibraryScanner.Result result = _scanner.Scan();

        result.Added.Should().Be(0);
        result.Removed.Should().Be(0);
        result.ChangedAnything.Should().BeFalse();
    }
}

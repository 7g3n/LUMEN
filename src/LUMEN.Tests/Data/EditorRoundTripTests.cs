using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Charts;
using Lumen.Core.Editing;
using Lumen.Core.Library;
using Lumen.Data;
using Lumen.Data.Library;
using Lumen.Data.Repositories;
using Xunit;

namespace Lumen.Tests.Data;

/// <summary>
/// The Phase 6 exit path, minus the pixels: notes are placed through commands, the chart
/// is written the way the editor writes it, and Song Select then finds a playable chart.
/// The editor screen itself needs a graphics device, so the chain is exercised through
/// the same Core and Data pieces the screen drives.
/// </summary>
public class EditorRoundTripTests : IDisposable
{
    private readonly string _dir;
    private readonly LumenPaths _paths;
    private readonly Database _db;
    private readonly LibraryRepository _library;
    private readonly LibraryScanner _scanner;

    public EditorRoundTripTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
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

    private static Chart Blank() => new()
    {
        LaneCount = 4,
        BpmPoints = new[] { new BpmPoint(0, 160) },
        Notes = Array.Empty<Note>(),
        Meta = new ChartMeta
        {
            Title = "Made In The Editor",
            Artist = "7g3",
            Creator = "7g3",
            DifficultyName = "MASTER",
            DifficultyLevel = 12.5,
            AudioFile = "song.wav",
        },
    };

    /// <summary>Builds a chart the way the editor does: one command per gesture.</summary>
    private static (Chart Chart, CommandStack Stack) Author()
    {
        var stack = new CommandStack();
        Chart chart = Blank();
        var tempo = new TempoMap(chart.BpmPoints);

        for (int beat = 0; beat < 16; beat++)
        {
            double time = BeatGrid.Snap(beat * 375, tempo, division: 4);
            chart = stack.Apply(chart, new AddNotes(Note.Tap(time, beat % 4)));
        }

        chart = stack.Apply(chart, new AddNotes(Note.Hold(6000, 0, 7000)));
        return (chart, stack);
    }

    private string Save(Chart chart, string fileName)
    {
        string path = Path.Combine(_paths.ChartsLocal, fileName + "." + GameIdentity.ChartExtension);
        AtomicFile.WriteAllText(path, ChartJson.Serialize(chart));
        return path;
    }

    [Fact]
    public void A_chart_authored_through_commands_saves_and_reloads_identically()
    {
        (Chart chart, _) = Author();
        string path = Save(chart, "made-in-the-editor");

        Chart reloaded = ChartJson.Deserialize(File.ReadAllText(path));

        reloaded.Notes.Should().Equal(chart.Notes);
        reloaded.Meta.Should().Be(chart.Meta);
        reloaded.BpmPoints.Should().Equal(chart.BpmPoints);
        reloaded.LaneCount.Should().Be(chart.LaneCount);
    }

    [Fact]
    public void The_saved_chart_turns_up_in_the_library_ready_to_play()
    {
        (Chart chart, _) = Author();
        Save(chart, "made-in-the-editor");
        File.WriteAllBytes(Path.Combine(_paths.Songs, "song.wav"), new byte[64]);

        _scanner.Scan();

        LibraryChart entry = _library.All().Should().ContainSingle().Subject;
        entry.Meta.Title.Should().Be("Made In The Editor");
        entry.Meta.DifficultyName.Should().Be("MASTER");
        entry.Level.Should().Be(12.5);
        entry.NoteCount.Should().Be(chart.Notes.Count);
        entry.HoldCount.Should().Be(1);

        // "Playable" means Song Select can hand both paths to the gameplay screen.
        File.Exists(entry.ChartPath).Should().BeTrue();
        File.Exists(entry.AudioPath).Should().BeTrue();
    }

    [Fact]
    public void Saving_again_after_more_editing_updates_the_same_library_row()
    {
        (Chart chart, CommandStack stack) = Author();
        string path = Save(chart, "made-in-the-editor");
        _scanner.Scan();
        int before = _library.All().Single().NoteCount;

        chart = stack.Apply(chart, new AddNotes(Note.Tap(9000, 2)));
        AtomicFile.WriteAllText(path, ChartJson.Serialize(chart));
        _scanner.Scan();

        // The chart key folds in the note count, so editing gives the file a new key. The
        // scan has to retire the row under the old key, or the library would grow a
        // phantom difficulty every time the author saved.
        _library.All().Should().HaveCount(1);
        _library.All().Single().NoteCount.Should().Be(before + 1);
    }

    [Fact]
    public void Undoing_every_edit_saves_an_empty_chart_that_the_scanner_declines()
    {
        (Chart chart, CommandStack stack) = Author();

        while (stack.CanUndo)
        {
            chart = stack.Undo(chart);
        }

        Save(chart, "emptied");
        _scanner.Scan();

        chart.Notes.Should().BeEmpty();
        _library.Count().Should().Be(0);
    }

    [Fact]
    public void A_half_written_file_never_replaces_a_good_one()
    {
        // AtomicFile is what stands between a crash mid-save and a lost chart (spec §74).
        (Chart chart, _) = Author();
        string path = Save(chart, "made-in-the-editor");
        string good = File.ReadAllText(path);

        try
        {
            AtomicFile.WriteAllText(path, null!);
        }
        catch
        {
            // The point is what the file says afterwards, not which exception came out.
        }

        File.ReadAllText(path).Should().Be(good);
    }

    [Fact]
    public void Pasted_phrases_survive_the_round_trip()
    {
        var stack = new CommandStack();
        var clipboard = new NoteClipboard();
        Chart chart = Blank();

        chart = stack.Apply(chart, new AddNotes(new[]
        {
            Note.Tap(0, 0), Note.Tap(250, 1), Note.Hold(500, 2, 1000),
        }));

        clipboard.Copy(chart.Notes);
        chart = stack.Apply(chart, new AddNotes(clipboard.Paste(4000, chart.LaneCount)));

        string path = Save(chart, "pasted");
        Chart reloaded = ChartJson.Deserialize(File.ReadAllText(path));

        reloaded.Notes.Should().HaveCount(6);
        reloaded.Notes.Count(n => n.IsHold).Should().Be(2);
        reloaded.Notes.Where(n => n.IsHold).Select(n => n.DurationMs)
            .Should().AllBeEquivalentTo(500.0);
    }
}

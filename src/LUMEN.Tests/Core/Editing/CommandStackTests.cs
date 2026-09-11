using System;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Charts;
using Lumen.Core.Editing;
using Xunit;

namespace Lumen.Tests.Core.Editing;

/// <summary>
/// Undo has to be total (spec §87): every mutation the editor can make must come back.
/// These tests drive the commands through the stack the way the editor does.
/// </summary>
public class CommandStackTests
{
    private static Chart Chart(params Note[] notes) => new()
    {
        LaneCount = 4,
        BpmPoints = new[] { new BpmPoint(0, 120) },
        Notes = ChartEdits.Sorted(notes),
        Meta = new ChartMeta { Title = "Test", Artist = "LUMEN", DifficultyName = "NORMAL" },
    };

    private static Chart Empty() => Chart();

    [Fact]
    public void A_fresh_stack_can_neither_undo_nor_redo()
    {
        var stack = new CommandStack();
        stack.CanUndo.Should().BeFalse();
        stack.CanRedo.Should().BeFalse();
        stack.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Undoing_a_placement_removes_the_note()
    {
        var stack = new CommandStack();
        Chart chart = stack.Apply(Empty(), new AddNotes(Note.Tap(1000, 1)));
        chart.Notes.Should().ContainSingle();

        chart = stack.Undo(chart);

        chart.Notes.Should().BeEmpty();
    }

    [Fact]
    public void Redoing_puts_it_back()
    {
        var stack = new CommandStack();
        Chart chart = stack.Apply(Empty(), new AddNotes(Note.Tap(1000, 1)));
        chart = stack.Undo(chart);
        chart = stack.Redo(chart);

        chart.Notes.Should().ContainSingle();
        chart.Notes[0].TimeMs.Should().Be(1000);
    }

    [Fact]
    public void Undo_and_redo_round_trip_an_arbitrary_sequence_of_edits()
    {
        var stack = new CommandStack();
        Chart start = Chart(Note.Tap(0, 0), Note.Hold(500, 1, 1500));
        Chart chart = start;

        chart = stack.Apply(chart, new AddNotes(Note.Tap(2000, 2)));
        chart = stack.Apply(chart, new RemoveNotes(new[] { Note.Tap(0, 0) }));
        chart = stack.Apply(chart, new ReplaceNotes(
            new[] { Note.Hold(500, 1, 1500) }, new[] { Note.Hold(750, 3, 2000) }));
        chart = stack.Apply(chart, new SetBpmPoints(new[] { new BpmPoint(0, 180) }));
        chart = stack.Apply(chart, new SetChartOffset(-42));
        chart = stack.Apply(chart, new SetMeta(chart.Meta with { Title = "Renamed" }));

        Chart edited = chart;

        for (int i = 0; i < 6; i++)
        {
            chart = stack.Undo(chart);
        }

        SameDocument(chart, start);
        stack.CanUndo.Should().BeFalse();

        for (int i = 0; i < 6; i++)
        {
            chart = stack.Redo(chart);
        }

        SameDocument(chart, edited);
    }

    /// <summary>
    /// Compares what the document actually says. <see cref="Chart"/> is a record, but its
    /// note and tempo lists are interfaces, so <c>==</c> compares those by reference -
    /// two charts built from the same edits are never the same instance.
    /// </summary>
    private static void SameDocument(Chart actual, Chart expected)
    {
        actual.Notes.Should().Equal(expected.Notes);
        actual.BpmPoints.Should().Equal(expected.BpmPoints);
        actual.ChartOffsetMs.Should().Be(expected.ChartOffsetMs);
        actual.Meta.Should().Be(expected.Meta);
        actual.LaneCount.Should().Be(expected.LaneCount);
    }

    [Fact]
    public void A_new_edit_discards_the_redo_branch()
    {
        var stack = new CommandStack();
        Chart chart = stack.Apply(Empty(), new AddNotes(Note.Tap(1000, 1)));
        chart = stack.Undo(chart);

        chart = stack.Apply(chart, new AddNotes(Note.Tap(2000, 2)));

        stack.CanRedo.Should().BeFalse();
        chart = stack.Redo(chart);
        chart.Notes.Should().ContainSingle().Which.TimeMs.Should().Be(2000);
    }

    [Fact]
    public void Undoing_with_nothing_to_undo_leaves_the_chart_alone()
    {
        var stack = new CommandStack();
        Chart chart = Chart(Note.Tap(0, 0));

        stack.Undo(chart).Should().Be(chart);
        stack.Redo(chart).Should().Be(chart);
    }

    [Fact]
    public void The_stack_reports_what_the_next_undo_would_do()
    {
        var stack = new CommandStack();
        Chart chart = stack.Apply(Empty(), new AddNotes(new[] { Note.Tap(0, 0), Note.Tap(500, 1) }));

        stack.UndoLabel.Should().Be("Remove notes");

        stack.Undo(chart);
        stack.RedoLabel.Should().Be("Place 2 notes");
    }

    [Fact]
    public void History_is_bounded_and_drops_the_oldest_step_first()
    {
        var stack = new CommandStack(limit: 3);
        Chart chart = Empty();

        for (int i = 0; i < 10; i++)
        {
            chart = stack.Apply(chart, new AddNotes(Note.Tap(i * 100, i % 4)));
        }

        stack.Depth.Should().Be(3);

        while (stack.CanUndo)
        {
            chart = stack.Undo(chart);
        }

        // The three most recent placements come back out; the earlier seven are history.
        chart.Notes.Should().HaveCount(7);
    }

    [Fact]
    public void A_composite_edit_undoes_as_one_step()
    {
        var stack = new CommandStack();
        Chart chart = Chart(Note.Tap(0, 0));

        chart = stack.Apply(chart, new CompositeCommand(new IEditCommand[]
        {
            new RemoveNotes(new[] { Note.Tap(0, 0) }),
            new AddNotes(new[] { Note.Tap(100, 1), Note.Tap(200, 2) }),
        }, "Replace phrase"));

        chart.Notes.Should().HaveCount(2);
        stack.Depth.Should().Be(1);

        chart = stack.Undo(chart);

        chart.Notes.Should().ContainSingle().Which.TimeMs.Should().Be(0);
    }

    // --- dirty tracking ---

    [Fact]
    public void An_edit_marks_the_document_dirty_and_saving_marks_it_clean()
    {
        var stack = new CommandStack();
        Chart chart = stack.Apply(Empty(), new AddNotes(Note.Tap(0, 0)));
        stack.IsDirty.Should().BeTrue();

        stack.MarkClean();
        stack.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Undoing_back_to_the_saved_state_is_not_dirty()
    {
        var stack = new CommandStack();
        Chart chart = Empty();
        chart = stack.Apply(chart, new AddNotes(Note.Tap(0, 0)));
        stack.MarkClean();

        chart = stack.Apply(chart, new AddNotes(Note.Tap(500, 1)));
        stack.IsDirty.Should().BeTrue();

        stack.Undo(chart);
        stack.IsDirty.Should().BeFalse();
    }

    // --- individual commands ---

    [Fact]
    public void Placing_a_note_where_one_already_sits_changes_nothing()
    {
        var stack = new CommandStack();
        Chart chart = Chart(Note.Tap(1000, 1));

        chart = stack.Apply(chart, new AddNotes(Note.Tap(1000, 1)));

        chart.Notes.Should().ContainSingle();
    }

    [Fact]
    public void A_collision_is_not_resurrected_by_undo()
    {
        // The inverse must remove only what was really added, or undo would delete the
        // note that was already there.
        var stack = new CommandStack();
        Chart chart = Chart(Note.Tap(1000, 1));

        chart = stack.Apply(chart, new AddNotes(Note.Tap(1000, 1)));
        chart = stack.Undo(chart);

        chart.Notes.Should().ContainSingle();
    }

    [Fact]
    public void Removing_a_note_that_is_not_there_is_harmless()
    {
        var stack = new CommandStack();
        Chart chart = Chart(Note.Tap(0, 0));

        chart = stack.Apply(chart, new RemoveNotes(new[] { Note.Tap(9999, 3) }));

        chart.Notes.Should().ContainSingle();
        stack.Undo(chart).Notes.Should().ContainSingle();
    }

    [Fact]
    public void Notes_stay_sorted_after_every_edit()
    {
        var stack = new CommandStack();
        Chart chart = Empty();

        chart = stack.Apply(chart, new AddNotes(new[]
        {
            Note.Tap(900, 3), Note.Tap(100, 0), Note.Tap(500, 2),
        }));

        chart.Notes.Select(n => n.TimeMs).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Moving_a_note_keeps_the_note_count()
    {
        var stack = new CommandStack();
        Chart chart = Chart(Note.Tap(1000, 1));

        chart = stack.Apply(chart, new ReplaceNotes(
            new[] { Note.Tap(1000, 1) }, new[] { Note.Tap(1250, 2) }));

        chart.Notes.Should().ContainSingle();
        chart.Notes[0].Lane.Should().Be(2);
        chart.Notes[0].TimeMs.Should().Be(1250);
    }

    [Fact]
    public void Resizing_a_hold_is_the_same_command_as_moving_one()
    {
        var stack = new CommandStack();
        Chart chart = Chart(Note.Hold(1000, 0, 1500));

        chart = stack.Apply(chart, new ReplaceNotes(
            new[] { Note.Hold(1000, 0, 1500) },
            new[] { Note.Hold(1000, 0, 2500) },
            "Resize hold"));

        chart.Notes[0].DurationMs.Should().Be(1500);
        stack.UndoLabel.Should().Be("Resize hold");
    }
}

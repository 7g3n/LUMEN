using Lumen.Core.Charts;

namespace Lumen.Core.Editing;

/// <summary>
/// One reversible change to a chart (spec §87).
///
/// Applying a command returns both the new chart and the command that puts it back. That
/// single rule is what makes undo total rather than best-effort: there is no way to write
/// a mutation that does not produce its own inverse, so a feature added in a later phase
/// cannot quietly become un-undoable.
/// </summary>
public interface IEditCommand
{
    /// <summary>Shown in the editor's undo hint, e.g. "Place 4 notes".</summary>
    string Label { get; }

    EditResult Apply(Chart chart);
}

/// <summary>The chart after a command, and the command that reverses it.</summary>
public sealed record EditResult(Chart Chart, IEditCommand Undo);

/// <summary>
/// Chart helpers shared by the commands. Notes are kept sorted by time then lane so that
/// the document is always in the shape the gameplay engine and the renderer expect, and
/// so two charts with the same notes compare equal however they were built.
/// </summary>
public static class ChartEdits
{
    public static Chart WithNotes(Chart chart, IEnumerable<Note> notes) =>
        chart with { Notes = Sorted(notes) };

    public static Note[] Sorted(IEnumerable<Note> notes) =>
        notes.OrderBy(n => n.TimeMs).ThenBy(n => n.Lane).ToArray();

    /// <summary>
    /// True when the chart already holds a note that would collide with this one: the
    /// same kind in the same lane at the same moment. Notes carry no id, so identical
    /// values are the same note - placing one twice has to be a no-op rather than
    /// producing a duplicate that selection and undo could not tell apart.
    /// </summary>
    public static bool Collides(Chart chart, Note note) =>
        chart.Notes.Any(n => n.Lane == note.Lane && Math.Abs(n.TimeMs - note.TimeMs) < 0.5);
}

/// <summary>Places notes (spec §87).</summary>
public sealed class AddNotes : IEditCommand
{
    private readonly IReadOnlyList<Note> _notes;

    public AddNotes(IEnumerable<Note> notes) => _notes = notes.ToArray();

    public AddNotes(Note note) : this(new[] { note }) { }

    public string Label => _notes.Count == 1 ? "Place note" : $"Place {_notes.Count} notes";

    public EditResult Apply(Chart chart)
    {
        // Anything that would land on an existing note is dropped, so the inverse only
        // removes what was actually added.
        var added = _notes.Where(n => !ChartEdits.Collides(chart, n)).ToArray();
        Chart next = ChartEdits.WithNotes(chart, chart.Notes.Concat(added));
        return new EditResult(next, new RemoveNotes(added, "Remove notes"));
    }
}

/// <summary>Deletes notes (spec §87).</summary>
public sealed class RemoveNotes : IEditCommand
{
    private readonly IReadOnlyList<Note> _notes;

    public RemoveNotes(IEnumerable<Note> notes, string? label = null)
    {
        _notes = notes.ToArray();
        Label = label ?? (_notes.Count == 1 ? "Delete note" : $"Delete {_notes.Count} notes");
    }

    public string Label { get; }

    public EditResult Apply(Chart chart)
    {
        var removing = new HashSet<Note>(_notes);
        var removed = chart.Notes.Where(removing.Contains).ToArray();
        Chart next = ChartEdits.WithNotes(chart, chart.Notes.Where(n => !removing.Contains(n)));
        return new EditResult(next, new AddNotes(removed));
    }
}

/// <summary>
/// Swaps one set of notes for another. Moving, resizing a hold and changing a note's kind
/// are all the same operation on an immutable note list, so they share one command.
/// </summary>
public sealed class ReplaceNotes : IEditCommand
{
    private readonly IReadOnlyList<Note> _from;
    private readonly IReadOnlyList<Note> _to;

    public ReplaceNotes(IEnumerable<Note> from, IEnumerable<Note> to, string label = "Move notes")
    {
        _from = from.ToArray();
        _to = to.ToArray();
        Label = label;
    }

    public string Label { get; }

    public EditResult Apply(Chart chart)
    {
        var removing = new HashSet<Note>(_from);
        IEnumerable<Note> kept = chart.Notes.Where(n => !removing.Contains(n));
        Chart next = ChartEdits.WithNotes(chart, kept.Concat(_to));
        return new EditResult(next, new ReplaceNotes(_to, _from, Label));
    }
}

/// <summary>Changes the tempo map (spec §46).</summary>
public sealed class SetBpmPoints : IEditCommand
{
    private readonly IReadOnlyList<BpmPoint> _points;

    public SetBpmPoints(IEnumerable<BpmPoint> points, string label = "Change BPM")
    {
        _points = points.OrderBy(p => p.AtMs).ToArray();
        Label = label;
    }

    public string Label { get; }

    public EditResult Apply(Chart chart)
    {
        IReadOnlyList<BpmPoint> previous = chart.BpmPoints;
        return new EditResult(chart with { BpmPoints = _points }, new SetBpmPoints(previous, Label));
    }
}

/// <summary>Shifts the note grid against the audio (spec §47).</summary>
public sealed class SetChartOffset : IEditCommand
{
    private readonly double _offsetMs;

    public SetChartOffset(double offsetMs) => _offsetMs = offsetMs;

    public string Label => "Change offset";

    public EditResult Apply(Chart chart) =>
        new(chart with { ChartOffsetMs = _offsetMs }, new SetChartOffset(chart.ChartOffsetMs));
}

/// <summary>Edits the chart's metadata (spec §60, §91).</summary>
public sealed class SetMeta : IEditCommand
{
    private readonly ChartMeta _meta;

    public SetMeta(ChartMeta meta, string label = "Edit details")
    {
        _meta = meta;
        Label = label;
    }

    public string Label { get; }

    public EditResult Apply(Chart chart) =>
        new(chart with { Meta = _meta }, new SetMeta(chart.Meta, Label));
}

/// <summary>Several commands applied - and undone - as one step.</summary>
public sealed class CompositeCommand : IEditCommand
{
    private readonly IReadOnlyList<IEditCommand> _commands;

    public CompositeCommand(IEnumerable<IEditCommand> commands, string label)
    {
        _commands = commands.ToArray();
        Label = label;
    }

    public string Label { get; }

    public EditResult Apply(Chart chart)
    {
        Chart current = chart;
        var undos = new List<IEditCommand>();

        foreach (IEditCommand command in _commands)
        {
            EditResult result = command.Apply(current);
            current = result.Chart;
            // Undone back to front, so the stack is built in reverse.
            undos.Insert(0, result.Undo);
        }

        return new EditResult(current, new CompositeCommand(undos, Label));
    }
}

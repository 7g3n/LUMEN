using Lumen.Core.Charts;

namespace Lumen.Core.Editing;

/// <summary>
/// Undo/redo history for one editing session (spec §87).
///
/// Redo is discarded the moment a new command is applied. The alternative is a branching
/// history, and no editor's interface can show that honestly - a player who undoes three
/// steps, places a note, and then presses redo expects nothing to happen, not to be taken
/// down a branch they have already abandoned.
/// </summary>
public sealed class CommandStack
{
    private readonly List<IEditCommand> _undo = new();
    private readonly List<IEditCommand> _redo = new();
    private readonly int _limit;

    public CommandStack(int limit = 300) => _limit = Math.Max(1, limit);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    public string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    public int Depth => _undo.Count;

    /// <summary>
    /// True when the document differs from the last point it was marked clean at. Used
    /// for the unsaved marker and the "discard changes?" prompt.
    /// </summary>
    public bool IsDirty => _undo.Count != _cleanDepth;

    private int _cleanDepth;

    public Chart Apply(Chart chart, IEditCommand command)
    {
        EditResult result = command.Apply(chart);
        _undo.Add(result.Undo);

        if (_undo.Count > _limit)
        {
            _undo.RemoveAt(0);
            // The dropped step can never be undone again, so the clean mark moves with it
            // or it would point at history that no longer exists.
            _cleanDepth = Math.Max(0, _cleanDepth - 1);
        }

        _redo.Clear();
        return result.Chart;
    }

    public Chart Undo(Chart chart)
    {
        if (_undo.Count == 0)
        {
            return chart;
        }

        IEditCommand command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);

        EditResult result = command.Apply(chart);
        _redo.Add(result.Undo);
        return result.Chart;
    }

    public Chart Redo(Chart chart)
    {
        if (_redo.Count == 0)
        {
            return chart;
        }

        IEditCommand command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);

        EditResult result = command.Apply(chart);
        _undo.Add(result.Undo);
        return result.Chart;
    }

    /// <summary>Marks the current state as saved.</summary>
    public void MarkClean() => _cleanDepth = _undo.Count;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _cleanDepth = 0;
    }
}

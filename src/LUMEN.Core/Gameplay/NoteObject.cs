using Lumen.Core.Charts;

namespace Lumen.Core.Gameplay;

public enum NoteStatus
{
    /// <summary>Not yet hit or missed.</summary>
    Pending,

    /// <summary>Hold head hit; currently being held.</summary>
    Holding,

    /// <summary>Fully resolved (hit or missed).</summary>
    Done,
}

/// <summary>Runtime state wrapping an immutable <see cref="Note"/> during a play.</summary>
public sealed class NoteObject
{
    public NoteObject(Note note) => Note = note;

    public Note Note { get; }

    public NoteStatus Status { get; internal set; } = NoteStatus.Pending;

    public Judgement? HeadJudgement { get; internal set; }

    public Judgement? TailJudgement { get; internal set; }

    /// <summary>Signed timing error of the head hit in ms (negative = early). Null if missed/pending.</summary>
    public double? HeadErrorMs { get; internal set; }

    public int Lane => Note.Lane;

    public bool IsHold => Note.IsHold;
}

/// <summary>Raised whenever a note's head or tail is judged, for popups and effects.</summary>
public readonly record struct JudgementEvent(
    NoteObject Note, Judgement Judgement, bool IsTail, double SongTimeMs, double ErrorMs);

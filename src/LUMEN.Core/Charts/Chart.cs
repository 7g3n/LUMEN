namespace Lumen.Core.Charts;

/// <summary>A single tempo marker (spec §46). <see cref="AtMs"/> is time from audio start.</summary>
public sealed record BpmPoint(double AtMs, double Bpm);

/// <summary>Descriptive fields for a chart. The full package format lands in Phase 7.</summary>
public sealed record ChartMeta
{
    public string Title { get; init; } = "Untitled";
    public string Artist { get; init; } = "Unknown";
    public string Creator { get; init; } = "";
    public string DifficultyName { get; init; } = "NORMAL";
    public double DifficultyLevel { get; init; } = 1.0;
    public string AudioFile { get; init; } = "";
    public double PreviewMs { get; init; }
}

/// <summary>
/// A playable chart: metadata, a tempo map, an offset, and an ordered note list.
/// Immutable once built; <see cref="Normalized"/> guarantees notes are time-sorted.
/// </summary>
public sealed record Chart
{
    public int FormatVersion { get; init; } = GameIdentity.ChartFormatVersion;

    public ChartMeta Meta { get; init; } = new();

    /// <summary>Shifts the whole note grid relative to the audio (spec §47).</summary>
    public double ChartOffsetMs { get; init; }

    public IReadOnlyList<BpmPoint> BpmPoints { get; init; } = new[] { new BpmPoint(0, 120) };

    public IReadOnlyList<Note> Notes { get; init; } = Array.Empty<Note>();

    public int LaneCount { get; init; } = 4;

    public double FirstNoteMs => Notes.Count > 0 ? Notes[0].TimeMs : 0;

    public double LastNoteMs => Notes.Count > 0 ? Notes.Max(n => Math.Max(n.TimeMs, n.EndTimeMs)) : 0;

    public int TapCount => Notes.Count(n => n.Kind == NoteKind.Tap);

    public int HoldCount => Notes.Count(n => n.Kind == NoteKind.Hold);

    /// <summary>Returns a copy with notes sorted by time then lane, and offset folded in.</summary>
    public Chart Normalized()
    {
        var notes = Notes
            .Select(n => n with
            {
                TimeMs = n.TimeMs + ChartOffsetMs,
                EndTimeMs = n.IsHold ? n.EndTimeMs + ChartOffsetMs : n.EndTimeMs,
            })
            .OrderBy(n => n.TimeMs)
            .ThenBy(n => n.Lane)
            .ToArray();

        var bpm = BpmPoints.OrderBy(b => b.AtMs).ToArray();
        if (bpm.Length == 0)
        {
            bpm = new[] { new BpmPoint(0, 120) };
        }

        return this with { Notes = notes, BpmPoints = bpm, ChartOffsetMs = 0 };
    }
}

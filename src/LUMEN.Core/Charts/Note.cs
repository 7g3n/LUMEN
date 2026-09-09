namespace Lumen.Core.Charts;

public enum NoteKind
{
    Tap,
    Hold,

    // Reserved for later phases (spec §18). The engine dispatches on NoteKind so these
    // slot in without changing the judging pipeline.
    Slide,
    Burst,
    Flick,
    Chain,
    Special,
}

/// <summary>
/// One immutable note in a chart. Times are milliseconds from the start of the audio.
/// A <see cref="NoteKind.Hold"/> uses <see cref="EndTimeMs"/>; other kinds ignore it.
/// </summary>
public sealed record Note
{
    public required NoteKind Kind { get; init; }

    public required double TimeMs { get; init; }

    public required int Lane { get; init; }

    public double EndTimeMs { get; init; }

    public double DurationMs => Math.Max(0, EndTimeMs - TimeMs);

    public bool IsHold => Kind == NoteKind.Hold;

    public static Note Tap(double timeMs, int lane) =>
        new() { Kind = NoteKind.Tap, TimeMs = timeMs, Lane = lane };

    public static Note Hold(double timeMs, int lane, double endTimeMs) =>
        new() { Kind = NoteKind.Hold, TimeMs = timeMs, Lane = lane, EndTimeMs = endTimeMs };
}

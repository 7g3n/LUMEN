using Lumen.Core.Charts;

namespace Lumen.Core.Editing;

public enum GridLineKind
{
    Bar,
    Beat,
    Subdivision,
}

public readonly record struct GridLine(double TimeMs, double Beat, GridLineKind Kind);

/// <summary>
/// The editor's beat grid (spec §45).
///
/// Everything is derived from the chart's own tempo map rather than from a fixed
/// interval, so a tempo change mid-song moves the grid with it and notes placed after the
/// change still land on the beat. Snapping works in beats and converts back to time,
/// which is what keeps it correct across a tempo change instead of only near one.
/// </summary>
public static class BeatGrid
{
    /// <summary>
    /// Divisions the editor offers, including the triplet family the spec asks for.
    /// A division of 3 means "three notes per beat", i.e. 1/3 in the editor's language.
    /// </summary>
    public static readonly IReadOnlyList<int> Divisions = new[] { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };

    public static bool IsTriplet(int division) => division % 3 == 0;

    public static string Label(int division) => $"1/{division}";

    /// <summary>Nearest grid time to <paramref name="timeMs"/>, never before zero.</summary>
    public static double Snap(double timeMs, TempoMap tempo, int division, double offsetMs = 0)
    {
        if (division <= 0)
        {
            return Math.Max(0, timeMs);
        }

        double beat = tempo.BeatAt(timeMs - offsetMs);
        double snappedBeat = Math.Round(beat * division, MidpointRounding.AwayFromZero) / division;
        return Math.Max(0, tempo.TimeMsAtBeat(snappedBeat) + offsetMs);
    }

    /// <summary>One grid step forward (<c>direction</c> 1) or back (-1) from a time.</summary>
    public static double Step(double timeMs, TempoMap tempo, int division, int direction, double offsetMs = 0)
    {
        if (division <= 0 || direction == 0)
        {
            return Math.Max(0, timeMs);
        }

        double beat = tempo.BeatAt(timeMs - offsetMs);
        double stepped = Math.Round(beat * division, MidpointRounding.AwayFromZero) + Math.Sign(direction);
        return Math.Max(0, tempo.TimeMsAtBeat(stepped / division) + offsetMs);
    }

    /// <summary>
    /// Grid lines between two times, for the timeline renderer. Bounded so a pathological
    /// zoom level cannot ask for a million lines.
    /// </summary>
    public static IReadOnlyList<GridLine> Lines(
        double fromMs, double toMs, TempoMap tempo, int division,
        int beatsPerBar = 4, double offsetMs = 0, int maxLines = 4000)
    {
        var lines = new List<GridLine>();
        if (toMs <= fromMs || division <= 0 || beatsPerBar <= 0)
        {
            return lines;
        }

        double startBeat = tempo.BeatAt(Math.Max(0, fromMs) - offsetMs);
        double endBeat = tempo.BeatAt(toMs - offsetMs);

        long step = 0;
        long first = (long)Math.Floor(startBeat * division);
        long last = (long)Math.Ceiling(endBeat * division);

        for (long i = first; i <= last && step < maxLines; i++, step++)
        {
            double beat = (double)i / division;
            if (beat < 0)
            {
                continue;
            }

            double time = tempo.TimeMsAtBeat(beat) + offsetMs;
            if (time < fromMs || time > toMs)
            {
                continue;
            }

            lines.Add(new GridLine(time, beat, KindOf(i, division, beatsPerBar)));
        }

        return lines;
    }

    private static GridLineKind KindOf(long index, int division, int beatsPerBar)
    {
        if (index % division != 0)
        {
            return GridLineKind.Subdivision;
        }

        long beatIndex = index / division;
        return beatIndex % beatsPerBar == 0 ? GridLineKind.Bar : GridLineKind.Beat;
    }

    /// <summary>Bar and beat numbers (both 1-based) for the editor's position readout.</summary>
    public static (int Bar, int Beat) BarBeatAt(
        double timeMs, TempoMap tempo, int beatsPerBar = 4, double offsetMs = 0)
    {
        if (beatsPerBar <= 0)
        {
            beatsPerBar = 4;
        }

        double beat = Math.Max(0, tempo.BeatAt(timeMs - offsetMs));
        int whole = (int)Math.Floor(beat + 1e-6);
        return (whole / beatsPerBar + 1, whole % beatsPerBar + 1);
    }
}

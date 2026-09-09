namespace Lumen.Core.Charts;

/// <summary>
/// Converts between time and musical beats across tempo changes (spec §46). Built once
/// from a chart's <see cref="BpmPoint"/>s; pure and allocation-free to query, so the
/// conductor and the editor can call it every frame.
/// </summary>
public sealed class TempoMap
{
    private readonly double[] _atMs;
    private readonly double[] _bpm;
    private readonly double[] _beatAtPoint; // cumulative beats reached by each point

    public TempoMap(IReadOnlyList<BpmPoint> points)
    {
        if (points.Count == 0)
        {
            points = new[] { new BpmPoint(0, 120) };
        }

        var ordered = points.OrderBy(p => p.AtMs).ToArray();
        if (ordered[0].AtMs > 0)
        {
            ordered = new[] { ordered[0] with { AtMs = 0 } }.Concat(ordered).ToArray();
        }

        _atMs = new double[ordered.Length];
        _bpm = new double[ordered.Length];
        _beatAtPoint = new double[ordered.Length];

        for (int i = 0; i < ordered.Length; i++)
        {
            _atMs[i] = ordered[i].AtMs;
            _bpm[i] = ordered[i].Bpm <= 0 ? 120 : ordered[i].Bpm;

            if (i > 0)
            {
                double spanMs = _atMs[i] - _atMs[i - 1];
                _beatAtPoint[i] = _beatAtPoint[i - 1] + spanMs / MsPerBeat(_bpm[i - 1]);
            }
        }
    }

    public double BpmAt(double timeMs) => _bpm[SegmentIndex(timeMs)];

    public double BeatAt(double timeMs)
    {
        int i = SegmentIndex(timeMs);
        return _beatAtPoint[i] + (timeMs - _atMs[i]) / MsPerBeat(_bpm[i]);
    }

    public double TimeMsAtBeat(double beat)
    {
        int i = 0;
        while (i + 1 < _beatAtPoint.Length && _beatAtPoint[i + 1] <= beat)
        {
            i++;
        }

        return _atMs[i] + (beat - _beatAtPoint[i]) * MsPerBeat(_bpm[i]);
    }

    public static double MsPerBeat(double bpm) => 60_000.0 / bpm;

    private int SegmentIndex(double timeMs)
    {
        int i = 0;
        while (i + 1 < _atMs.Length && _atMs[i + 1] <= timeMs)
        {
            i++;
        }

        return i;
    }
}

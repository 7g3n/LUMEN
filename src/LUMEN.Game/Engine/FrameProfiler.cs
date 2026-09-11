namespace Lumen.Game.Engine;

/// <summary>
/// Frame-time bookkeeping (spec §68).
///
/// A rhythm game is judged on its worst frames, not its average one: a play that holds
/// 240fps and drops two frames in the chorus feels worse than one that sits at 120
/// throughout. So this keeps the numbers that describe the tail — the worst frame, the
/// 99th percentile, and how many frames went over budget — rather than a smoothed
/// average that hides exactly the events that matter.
///
/// It also splits each frame into the part the game is responsible for and the part it
/// merely waited through. Both are real to the player, but only one of them can be fixed
/// here, and a single number that mixes them cannot tell you which you are looking at.
/// The measurement that led to this class is the case in point: a reproducible 60 ms
/// stutter turned out to be entirely inside the driver's present call, with the game's
/// own work for that frame at a tenth of a millisecond.
///
/// Samples are kept in a fixed ring, so profiling a three-minute song allocates nothing
/// after the first frame.
/// </summary>
public sealed class FrameProfiler
{
    /// <summary>
    /// Enough for about four and a half minutes at 240fps before the oldest is overwritten
    /// — longer than the songs this is meant to measure, so a percentile describes the
    /// whole play rather than the end of it. Half a megabyte, allocated once.
    /// </summary>
    public const int Capacity = 65_536;

    /// <summary>How many of the worst frames are kept with their attribution.</summary>
    public const int StutterCapacity = 8;

    private readonly double[] _samples = new double[Capacity];
    private int _count;
    private int _next;

    private readonly Stutter[] _stutters = new Stutter[StutterCapacity];
    private int _stutterCount;

    private readonly int[] _collectionsAtReset = new int[3];
    private long _allocatedAtReset;

    public FrameProfiler(double budgetMs = 0) => BudgetMs = budgetMs;

    /// <summary>What one frame cost, and where it went.</summary>
    /// <param name="Sample">Which frame of the span this was.</param>
    /// <param name="TotalMs">Wall time for the whole frame.</param>
    /// <param name="WorkMs">Update, draw, and the UI on top of them.</param>
    /// <param name="PresentMs">The driver's present call.</param>
    /// <param name="WaitMs">The frame limiter's deliberate wait.</param>
    public readonly record struct Stutter(
        int Sample, double TotalMs, double WorkMs, double PresentMs, double WaitMs)
    {
        /// <summary>
        /// What the frame is blamed on. A frame that spent its time inside the driver is
        /// not the game's to answer for, and saying so is far more useful than a bare
        /// millisecond count when somebody sends in a log.
        /// </summary>
        public string Cause =>
            WorkMs >= PresentMs && WorkMs >= WaitMs ? "game"
            : PresentMs >= WaitMs ? "present"
            : "wait";

        public override string ToString() =>
            $"#{Sample} {TotalMs:0.0}ms ({Cause}: work {WorkMs:0.0} present {PresentMs:0.0} wait {WaitMs:0.0})";
    }

    /// <summary>
    /// The frame time a frame must beat. Set from the refresh rate the game is pacing to,
    /// with a little headroom — a frame that lands a hair late is not a stutter.
    /// </summary>
    public double BudgetMs { get; private set; }

    public int SampleCount { get; private set; }

    /// <summary>Frames whose wall time went over budget, whatever the reason.</summary>
    public int OverBudget { get; private set; }

    /// <summary>
    /// Frames whose own work went over budget. This is the one the game controls, and so
    /// the one a change here can be held to.
    /// </summary>
    public int WorkOverBudget { get; private set; }

    public double WorstMs { get; private set; }

    /// <summary>
    /// Which frame the worst one was. A spike at sample 0 is the play starting up; a spike
    /// in the middle is the kind worth chasing, and the two need telling apart.
    /// </summary>
    public int WorstAtSample { get; private set; }

    public double WorstWorkMs { get; private set; }

    public double TotalMs { get; private set; }

    public double TotalWorkMs { get; private set; }

    /// <summary>
    /// Garbage collections during the span, by generation. Kept because "did a collection
    /// happen during the song" is a yes/no question a rhythm game should be able to answer,
    /// and because an allocation that only bites every few minutes is otherwise invisible.
    /// </summary>
    public int[] Collections { get; } = new int[3];

    /// <summary>
    /// Bytes allocated on the game thread during the span. The number that decides whether
    /// a collection happens mid-song at all: the pause cannot be made short enough to hide,
    /// so the allocation has to not happen.
    /// </summary>
    public long AllocatedBytes { get; private set; }

    public double AllocatedBytesPerFrame =>
        SampleCount == 0 ? 0 : (double)AllocatedBytes / SampleCount;

    public double AverageMs => SampleCount == 0 ? 0 : TotalMs / SampleCount;

    public double AverageWorkMs => SampleCount == 0 ? 0 : TotalWorkMs / SampleCount;

    public double OverBudgetPercent => SampleCount == 0 ? 0 : OverBudget * 100.0 / SampleCount;

    /// <summary>The worst frames of the span, worst first.</summary>
    public IReadOnlyList<Stutter> Stutters => _stutters.AsSpan(0, _stutterCount).ToArray();

    public static double BudgetForFps(double fps, double headroom = 1.35) =>
        fps <= 0 ? 0 : 1000.0 / fps * headroom;

    public void SetBudget(double budgetMs) => BudgetMs = budgetMs;

    public void Reset()
    {
        _count = 0;
        _next = 0;
        _stutterCount = 0;
        SampleCount = 0;
        OverBudget = 0;
        WorkOverBudget = 0;
        WorstMs = 0;
        WorstAtSample = 0;
        WorstWorkMs = 0;
        TotalMs = 0;
        TotalWorkMs = 0;

        for (int generation = 0; generation < _collectionsAtReset.Length; generation++)
        {
            _collectionsAtReset[generation] = GC.CollectionCount(generation);
            Collections[generation] = 0;
        }

        _allocatedAtReset = GC.GetAllocatedBytesForCurrentThread();
        AllocatedBytes = 0;
    }

    /// <summary>
    /// One frame. <paramref name="totalMs"/> is the whole frame; the other three are the
    /// parts of it that can be named, and they are expected very nearly to add up.
    /// </summary>
    public void Record(double totalMs, double workMs, double presentMs, double waitMs)
    {
        if (double.IsNaN(totalMs) || totalMs < 0)
        {
            return;
        }

        _samples[_next] = totalMs;
        _next = (_next + 1) % Capacity;
        if (_count < Capacity)
        {
            _count++;
        }

        int sample = SampleCount;
        SampleCount++;
        TotalMs += totalMs;
        TotalWorkMs += workMs;

        if (totalMs > WorstMs)
        {
            WorstMs = totalMs;
            WorstAtSample = sample;
        }

        if (workMs > WorstWorkMs)
        {
            WorstWorkMs = workMs;
        }

        if (BudgetMs <= 0)
        {
            return;
        }

        if (totalMs > BudgetMs)
        {
            OverBudget++;
            Keep(new Stutter(sample, totalMs, workMs, presentMs, waitMs));
        }

        if (workMs > BudgetMs)
        {
            WorkOverBudget++;
        }
    }

    /// <summary>Inserts into the worst-frames list, keeping it sorted worst-first and bounded.</summary>
    private void Keep(Stutter stutter)
    {
        if (_stutterCount == StutterCapacity && stutter.TotalMs <= _stutters[StutterCapacity - 1].TotalMs)
        {
            return;
        }

        int at = Math.Min(_stutterCount, StutterCapacity - 1);
        while (at > 0 && _stutters[at - 1].TotalMs < stutter.TotalMs)
        {
            _stutters[at] = _stutters[at - 1];
            at--;
        }

        _stutters[at] = stutter;
        _stutterCount = Math.Min(_stutterCount + 1, StutterCapacity);
    }

    /// <summary>
    /// The frame time at a percentile, e.g. 99 for "all but the worst one percent".
    /// Sorts a copy of the ring, so this is for reporting rather than per frame.
    /// </summary>
    public double PercentileMs(double percentile)
    {
        if (_count == 0)
        {
            return 0;
        }

        var sorted = new double[_count];
        Array.Copy(_samples, sorted, _count);
        Array.Sort(sorted);

        int index = (int)Math.Round((percentile / 100.0) * (sorted.Length - 1));
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    /// <summary>Samples the collection and allocation counters. Called when a span is reported.</summary>
    public void CaptureCounters()
    {
        for (int generation = 0; generation < Collections.Length; generation++)
        {
            Collections[generation] = GC.CollectionCount(generation) - _collectionsAtReset[generation];
        }

        AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - _allocatedAtReset;
    }

    /// <summary>One line for the log, at the end of a play.</summary>
    public string Summary()
    {
        CaptureCounters();

        return $"{SampleCount} frames, avg {AverageMs:0.00}ms, p99 {PercentileMs(99):0.00}ms, " +
               $"worst {WorstMs:0.00}ms @#{WorstAtSample}; " +
               $"work avg {AverageWorkMs:0.00}ms worst {WorstWorkMs:0.00}ms; " +
               $"over budget ({BudgetMs:0.00}ms) {OverBudget} ({OverBudgetPercent:0.00}%), " +
               $"the game's own {WorkOverBudget}; " +
               $"gc {Collections[0]}/{Collections[1]}/{Collections[2]}, " +
               $"alloc {AllocatedBytesPerFrame:0}B/frame";
    }

    /// <summary>The worst frames, attributed. "none" when nothing went over budget.</summary>
    public string StutterSummary() =>
        _stutterCount == 0 ? "none" : string.Join("  ", Stutters);
}

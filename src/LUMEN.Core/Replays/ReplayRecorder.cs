using Lumen.Core.Charts;
using Lumen.Core.Gameplay;

namespace Lumen.Core.Replays;

/// <summary>
/// Collects the lane events of a play as it happens (spec §41).
///
/// Recording is append-only and costs one list add per input, so it runs during every
/// play rather than behind a switch — a replay the player had to ask for in advance is
/// never there for the run that turned out to matter.
/// </summary>
public sealed class ReplayRecorder
{
    private readonly List<LaneEvent> _events = new();

    public int Count => _events.Count;

    public IReadOnlyList<LaneEvent> Events => _events;

    public void Record(IReadOnlyList<LaneEvent> events)
    {
        for (int i = 0; i < events.Count; i++)
        {
            _events.Add(events[i]);
        }
    }

    public void Clear() => _events.Clear();

    public Replay Build(
        Guid playerId, string playerName, Chart chart, ScoreState score,
        double inputOffsetMs = 0, double audioOffsetMs = 0)
    {
        return new Replay
        {
            ReplayId = Guid.NewGuid(),
            PlayerId = playerId,
            PlayerName = playerName,
            ChartKey = ChartKey.For(chart),
            ChartId = chart.Id,
            Chart = chart.Meta,
            RecordedUtc = DateTime.UtcNow,
            InputOffsetMs = inputOffsetMs,
            AudioOffsetMs = audioOffsetMs,
            // Ordered defensively: the engine requires it, and a provider that ever
            // delivered out of order would otherwise produce a replay that plays back
            // differently from the run it recorded.
            Events = _events.OrderBy(e => e.TimeMs).ToArray(),
            Result = ReplayResult.From(score),
        };
    }
}

/// <summary>
/// Feeds a recorded stream back to a session (spec §41).
///
/// Drains by song time exactly as a live input source does, so playback goes through the
/// same code path as a real play - the session cannot tell the difference, which is what
/// makes "the replay is what happened" true rather than hopeful.
/// </summary>
public sealed class ReplayPlayer
{
    private readonly IReadOnlyList<LaneEvent> _events;
    private int _cursor;

    public ReplayPlayer(Replay replay) : this(replay.Events) { }

    public ReplayPlayer(IReadOnlyList<LaneEvent> events) => _events = events;

    public bool Finished => _cursor >= _events.Count;

    public int Remaining => _events.Count - _cursor;

    /// <summary>Every event up to <paramref name="songTimeMs"/>, in order.</summary>
    public IReadOnlyList<LaneEvent> Drain(double songTimeMs)
    {
        if (Finished || _events[_cursor].TimeMs > songTimeMs)
        {
            return Array.Empty<LaneEvent>();
        }

        var slice = new List<LaneEvent>();
        while (_cursor < _events.Count && _events[_cursor].TimeMs <= songTimeMs)
        {
            slice.Add(_events[_cursor++]);
        }

        return slice;
    }

    public void Reset() => _cursor = 0;
}

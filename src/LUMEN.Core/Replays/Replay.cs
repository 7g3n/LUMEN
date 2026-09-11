using Lumen.Core.Charts;
using Lumen.Core.Gameplay;

namespace Lumen.Core.Replays;

/// <summary>
/// A recorded play (spec §41).
///
/// Only the input is stored, not the judgements. <see cref="GameplaySession"/> is a pure
/// function of (chart, ordered lane events), so the input *is* the play — keeping the
/// judgements as well would mean carrying a second copy of something derivable, and the
/// two could then disagree. What is stored instead is the result the recording produced,
/// so playback can be checked against it and a replay list can show a score without
/// re-simulating anything.
/// </summary>
public sealed record Replay
{
    public const int FormatVersion = 1;

    public required Guid ReplayId { get; init; }

    public required Guid PlayerId { get; init; }

    /// <summary>Name at the time of recording, so an old replay still reads sensibly.</summary>
    public required string PlayerName { get; init; }

    /// <summary>Identifies which chart was played, the same way scores do.</summary>
    public required string ChartKey { get; init; }

    /// <summary>The chart's stable id when it had one; null for charts never edited here.</summary>
    public Guid? ChartId { get; init; }

    public required ChartMeta Chart { get; init; }

    public required DateTime RecordedUtc { get; init; }

    /// <summary>
    /// The offsets in force when this was recorded, for information. Event times already
    /// have the input offset folded in, so playback must not apply it again.
    /// </summary>
    public double InputOffsetMs { get; init; }

    public double AudioOffsetMs { get; init; }

    public required IReadOnlyList<LaneEvent> Events { get; init; }

    /// <summary>What the recording scored. Playback is expected to reproduce it exactly.</summary>
    public required ReplayResult Result { get; init; }

    public int EventCount => Events.Count;

    /// <summary>Length of the recorded play, from its last input.</summary>
    public double DurationMs => Events.Count == 0 ? 0 : Events[^1].TimeMs;

    /// <summary>
    /// Records compare members with <c>==</c>, which for the event list would be a
    /// reference check. Two replays read from the same file are the same replay.
    /// </summary>
    public bool Equals(Replay? other) =>
        other is not null
        && ReplayId == other.ReplayId
        && PlayerId == other.PlayerId
        && PlayerName == other.PlayerName
        && ChartKey == other.ChartKey
        && ChartId == other.ChartId
        && Chart == other.Chart
        && RecordedUtc == other.RecordedUtc
        && InputOffsetMs.Equals(other.InputOffsetMs)
        && AudioOffsetMs.Equals(other.AudioOffsetMs)
        && Result == other.Result
        && Events.SequenceEqual(other.Events);

    public override int GetHashCode() => HashCode.Combine(ReplayId, PlayerId, ChartKey, RecordedUtc);
}

/// <summary>The headline numbers a recording produced (spec §33).</summary>
public sealed record ReplayResult(
    long Score, double Accuracy, int MaxCombo,
    int Perfect, int Great, int Good, int Bad, int Miss,
    bool FullCombo, bool AllPerfect, string Grade)
{
    public static ReplayResult From(PlayResult result) => new(
        result.Score, result.Accuracy, result.MaxCombo,
        result.Perfect, result.Great, result.Good, result.Bad, result.Miss,
        result.FullCombo, result.AllPerfect, result.Grade);

    public static ReplayResult From(ScoreState score) => new(
        score.Score, score.Accuracy, score.MaxCombo,
        score.Perfect, score.Great, score.Good, score.Bad, score.Miss,
        score.FullCombo, score.AllPerfect,
        Grades.For(score.Accuracy, score.FullCombo, score.AllPerfect));
}

/// <summary>A replay as the list shows it, without loading the event stream.</summary>
public sealed record ReplaySummary(
    Guid ReplayId, Guid PlayerId, string PlayerName, string ChartKey,
    string Title, string Artist, string DifficultyName, double DifficultyLevel,
    long Score, double Accuracy, string Grade, bool FullCombo,
    int EventCount, DateTime RecordedUtc);

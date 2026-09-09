using Lumen.Core.Charts;
using Lumen.Core.Gameplay;
using Lumen.Core.Pp;

namespace Lumen.Core.Scores;

/// <summary>A stored score row (spec §33).</summary>
public sealed record SavedScore
{
    public required Guid ScoreId { get; init; }
    public required Guid PlayerId { get; init; }
    public required string ChartKey { get; init; }
    public required ChartMeta Chart { get; init; }
    public required long Score { get; init; }
    public required double Accuracy { get; init; }
    public required int MaxCombo { get; init; }
    public required int Perfect { get; init; }
    public required int Great { get; init; }
    public required int Good { get; init; }
    public required int Bad { get; init; }
    public required int Miss { get; init; }
    public required bool FullCombo { get; init; }
    public required bool AllPerfect { get; init; }
    public required string Grade { get; init; }
    public required double Pp { get; init; }
    public required double PerformanceRating { get; init; }
    public required DateTime PlayedUtc { get; init; }
}

/// <summary>One of a player's top plays by PP (spec §34).</summary>
public sealed record BestPerformance(
    string ChartKey, ChartMeta Chart, double Pp, double PerformanceRating,
    double Accuracy, long Score, bool FullCombo, DateTime PlayedUtc);

/// <summary>Recent play list item (spec §63).</summary>
public sealed record PlayHistoryEntry(
    ChartMeta Chart, double Accuracy, long Score, double Pp, string Grade, DateTime PlayedUtc);

/// <summary>The player's best on a specific chart, for personal-best detection (spec §37).</summary>
public sealed record ChartBest(long Score, double Accuracy, double Pp, DateTime PlayedUtc);

public sealed record RatingSnapshot(double Rating, double TotalPp, double BestPp, DateTime ComputedUtc)
{
    public static readonly RatingSnapshot Empty = new(0, 0, 0, DateTime.MinValue);
}

/// <summary>
/// Everything the result screen needs after a save (spec §37, §38): the stored score,
/// the PP breakdown, and how the player's headline numbers changed.
/// </summary>
public sealed record ScoreSaveOutcome
{
    public required SavedScore Score { get; init; }
    public required PpBreakdown PpBreakdown { get; init; }
    public required RatingSnapshot Before { get; init; }
    public required RatingSnapshot After { get; init; }

    public bool IsPersonalBest { get; init; }
    public bool IsPpRecord { get; init; }
    public bool IsRatingRecord { get; init; }
    public bool IsFirstPlayOnChart { get; init; }

    public double RatingDelta => After.Rating - Before.Rating;
    public double TotalPpDelta => After.TotalPp - Before.TotalPp;
}

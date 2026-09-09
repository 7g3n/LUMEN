namespace Lumen.Core.Profiles;

/// <summary>Aggregate play statistics for a profile (spec §7).</summary>
public sealed record PlayerStatistics
{
    public int PlayCount { get; init; }
    public int FullCombos { get; init; }
    public int AllPerfects { get; init; }
    public long TotalScore { get; init; }
    public double AverageAccuracy { get; init; }
    public double BestAccuracy { get; init; }
    public int TotalPerfect { get; init; }
    public int TotalGreat { get; init; }
    public int TotalGood { get; init; }
    public int TotalBad { get; init; }
    public int TotalMiss { get; init; }
    public int DistinctCharts { get; init; }
    public DateTime? FirstPlayUtc { get; init; }
    public DateTime? LastPlayUtc { get; init; }

    public int TotalNotesHit => TotalPerfect + TotalGreat + TotalGood + TotalBad;

    public static readonly PlayerStatistics Empty = new();
}

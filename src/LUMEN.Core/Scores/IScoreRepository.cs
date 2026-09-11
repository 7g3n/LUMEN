using Lumen.Core.Charts;
using Lumen.Core.Gameplay;
using Lumen.Core.Library;
using Lumen.Core.Profiles;
using Lumen.Core.Rating;

namespace Lumen.Core.Scores;

/// <summary>
/// Stores plays and derives Rating / PP / statistics from them (spec §10–11, §28–34).
/// <see cref="Save"/> is a single transaction: insert score, compute the performance,
/// recompute the rating snapshot, all-or-nothing.
/// </summary>
public interface IScoreRepository
{
    ScoreSaveOutcome Save(Guid playerId, PlayResult result, Chart chart);

    ChartBest? GetChartBest(Guid playerId, string chartKey);

    /// <summary>
    /// Best score, accuracy and PP per chart for one player, keyed by chart key. Song
    /// Select needs all of them for the whole library at once, so this is one query
    /// rather than one per row.
    /// </summary>
    IReadOnlyDictionary<string, ChartStats> GetChartStats(Guid playerId);

    /// <summary>A chart's local leaderboard, best first (spec §39).</summary>
    IReadOnlyList<ChartRankingEntry> GetChartRanking(string chartKey, int limit, Guid selfPlayerId);

    /// <summary>
    /// Where a player sits on a chart's board, even when outside the visible top N.
    /// Null when they have never played it.
    /// </summary>
    int? GetChartRank(string chartKey, Guid playerId);

    IReadOnlyList<BestPerformance> GetBestPerformances(Guid playerId, int limit = 50);

    IReadOnlyList<PlayHistoryEntry> GetRecentPlays(Guid playerId, int limit = 25);

    RatingSnapshot GetLatestSnapshot(Guid playerId);

    PlayerStatistics GetStatistics(Guid playerId);

    ProfileSummary GetSummary(Profile profile);

    SkillAxes GetSkillProfile(Guid playerId);

    int CountScores(Guid playerId);
}

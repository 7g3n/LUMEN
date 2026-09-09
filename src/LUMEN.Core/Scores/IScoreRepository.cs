using Lumen.Core.Charts;
using Lumen.Core.Gameplay;
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

    IReadOnlyList<BestPerformance> GetBestPerformances(Guid playerId, int limit = 50);

    IReadOnlyList<PlayHistoryEntry> GetRecentPlays(Guid playerId, int limit = 25);

    RatingSnapshot GetLatestSnapshot(Guid playerId);

    PlayerStatistics GetStatistics(Guid playerId);

    ProfileSummary GetSummary(Profile profile);

    SkillAxes GetSkillProfile(Guid playerId);

    int CountScores(Guid playerId);
}

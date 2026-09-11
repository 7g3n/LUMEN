using Lumen.Core.Achievements;
using Lumen.Core.Library;
using Lumen.Core.Profiles;
using Lumen.Core.Scores;
using Lumen.Data.Repositories;

namespace Lumen.Data.Achievements;

/// <summary>
/// Gathers the numbers achievements are measured against and evaluates them (spec §64).
///
/// The snapshot is assembled at the moment of asking, from the same repositories the
/// profile screen reads. Nothing about achievements is cached: the whole design is that
/// they are a view over the player's statistics, so there is no separate copy that could
/// disagree with the scores behind it.
/// </summary>
public sealed class AchievementService
{
    private readonly IScoreRepository _scores;
    private readonly IProfileRepository _profiles;
    private readonly ILibraryRepository _library;
    private readonly AchievementRepository _achievements;

    public AchievementService(
        IScoreRepository scores, IProfileRepository profiles,
        ILibraryRepository library, AchievementRepository achievements)
    {
        _scores = scores;
        _profiles = profiles;
        _library = library;
        _achievements = achievements;
    }

    public AchievementStats StatsFor(Guid playerId)
    {
        PlayerStatistics stats = _scores.GetStatistics(playerId);
        RatingSnapshot snapshot = _scores.GetLatestSnapshot(playerId);
        Profile? profile = _profiles.Get(playerId);

        return new AchievementStats
        {
            PlayCount = stats.PlayCount,
            FullCombos = stats.FullCombos,
            AllPerfects = stats.AllPerfects,
            BestAccuracy = stats.BestAccuracy,
            HighestCombo = stats.HighestCombo,
            DistinctCharts = stats.DistinctCharts,
            ChartsCreated = ChartsCreatedBy(profile?.DisplayName),
            TotalPp = snapshot.TotalPp,
            BestPp = snapshot.BestPp,
            Rating = snapshot.Rating,
            TotalPlayTimeMs = profile?.TotalPlayTimeMs ?? 0,
        };
    }

    /// <summary>Evaluates and records, returning whatever unlocked.</summary>
    public IReadOnlyList<AchievementDefinition> Evaluate(Guid playerId) =>
        _achievements.Evaluate(playerId, StatsFor(playerId));

    public IReadOnlyList<AchievementState> All(Guid playerId) =>
        _achievements.All(playerId, StatsFor(playerId));

    public int UnlockedCount(Guid playerId) => _achievements.UnlockedCount(playerId);

    public int Total => AchievementEngine.All.Count;

    /// <summary>
    /// Charts this player authored, matched on the creator name they write into charts.
    /// A rename therefore changes the count - the alternative would be stamping a profile
    /// id into shared chart files, which would follow a chart to whoever received it.
    /// </summary>
    private int ChartsCreatedBy(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return 0;
        }

        return _library.All().Count(c =>
            c.Source == ChartSource.Local
            && string.Equals(c.Meta.Creator.Trim(), displayName.Trim(),
                StringComparison.OrdinalIgnoreCase));
    }
}

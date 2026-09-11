namespace Lumen.Core.Achievements;

/// <summary>
/// The snapshot every achievement is measured against.
///
/// Achievements are expressed as "this statistic has reached this number" rather than as
/// events fired at the moment something happens. That means an achievement added in a
/// later release unlocks retroactively for a player who already earned it, instead of
/// being permanently unreachable because the moment has passed — and it means the engine
/// needs no history, just the numbers as they stand.
/// </summary>
public sealed record AchievementStats
{
    public int PlayCount { get; init; }
    public int FullCombos { get; init; }
    public int AllPerfects { get; init; }
    public double BestAccuracy { get; init; }
    public int HighestCombo { get; init; }
    public int DistinctCharts { get; init; }
    public int ChartsCreated { get; init; }
    public double TotalPp { get; init; }
    public double BestPp { get; init; }
    public double Rating { get; init; }
    public long TotalPlayTimeMs { get; init; }

    public static readonly AchievementStats Empty = new();
}

/// <summary>One achievement (spec §64).</summary>
public sealed record AchievementDefinition(
    string Id, string Name, string Description, double Target,
    Func<AchievementStats, double> Measure)
{
    public double Progress(AchievementStats stats) => Math.Max(0, Measure(stats));

    public bool IsEarned(AchievementStats stats) => Progress(stats) >= Target;

    /// <summary>0..1, for a progress bar on a locked achievement.</summary>
    public double Fraction(AchievementStats stats) =>
        Target <= 0 ? 1 : Math.Clamp(Progress(stats) / Target, 0, 1);
}

/// <summary>An achievement as the profile screen shows it.</summary>
public sealed record AchievementState(
    AchievementDefinition Definition, DateTime? UnlockedUtc, double Progress)
{
    public bool Unlocked => UnlockedUtc is not null;

    public string Id => Definition.Id;
}

/// <summary>
/// The local achievement set and the rule for unlocking them (spec §64).
///
/// Local by design: these exist to give a solo player something to aim at on a machine
/// with no network, so nothing here needs a server to agree with.
/// </summary>
public static class AchievementEngine
{
    public static readonly IReadOnlyList<AchievementDefinition> All = new[]
    {
        new AchievementDefinition("first-play", "FIRST PLAY",
            "Finish your first chart.", 1, s => s.PlayCount),
        new AchievementDefinition("plays-100", "100 PLAYS",
            "Finish 100 charts.", 100, s => s.PlayCount),
        new AchievementDefinition("plays-1000", "1,000 PLAYS",
            "Finish 1,000 charts.", 1000, s => s.PlayCount),

        new AchievementDefinition("first-full-combo", "FIRST FULL COMBO",
            "Clear a chart without breaking your combo.", 1, s => s.FullCombos),
        new AchievementDefinition("full-combos-100", "100 FULL COMBOS",
            "Full combo 100 charts.", 100, s => s.FullCombos),
        new AchievementDefinition("first-all-perfect", "ALL PERFECT",
            "Hit every note perfectly.", 1, s => s.AllPerfects),

        // Accuracy is a percentage, so the target is the percentage itself.
        new AchievementDefinition("accuracy-99", "99% ACCURACY",
            "Finish a chart at 99% or better.", 99, s => s.BestAccuracy),
        new AchievementDefinition("accuracy-100", "100% ACCURACY",
            "Finish a chart without a single miss of a point.", 100, s => s.BestAccuracy),

        new AchievementDefinition("combo-500", "500 COMBO",
            "Reach a 500 note combo.", 500, s => s.HighestCombo),
        new AchievementDefinition("combo-1000", "1,000 COMBO",
            "Reach a 1,000 note combo.", 1000, s => s.HighestCombo),

        new AchievementDefinition("pp-1000", "1,000 PP",
            "Reach 1,000 total pp.", 1000, s => s.TotalPp),
        new AchievementDefinition("pp-10000", "10,000 PP",
            "Reach 10,000 total pp.", 10_000, s => s.TotalPp),

        new AchievementDefinition("rating-10", "RATING 10",
            "Reach a rating of 10.", 10, s => s.Rating),
        new AchievementDefinition("rating-15", "RATING 15",
            "Reach a rating of 15.", 15, s => s.Rating),

        new AchievementDefinition("explorer-25", "EXPLORER",
            "Play 25 different charts.", 25, s => s.DistinctCharts),
        new AchievementDefinition("first-chart", "FIRST CHART",
            "Create a chart of your own.", 1, s => s.ChartsCreated),
        new AchievementDefinition("charts-10", "CHART ARTIST",
            "Create 10 charts.", 10, s => s.ChartsCreated),
        new AchievementDefinition("playtime-10h", "10 HOURS",
            "Spend ten hours playing.", 10 * 60 * 60 * 1000d, s => s.TotalPlayTimeMs),
    };

    public static AchievementDefinition? Find(string id) =>
        All.FirstOrDefault(a => a.Id == id);

    /// <summary>
    /// Everything earned by these statistics that is not already unlocked. Evaluating the
    /// whole set each time costs a handful of comparisons and removes any chance of an
    /// achievement being missed because the code that should have fired it was not called.
    /// </summary>
    public static IReadOnlyList<AchievementDefinition> NewlyEarned(
        AchievementStats stats, IReadOnlyCollection<string> alreadyUnlocked)
    {
        var unlocked = alreadyUnlocked as ISet<string>
                       ?? new HashSet<string>(alreadyUnlocked, StringComparer.Ordinal);

        return All.Where(a => !unlocked.Contains(a.Id) && a.IsEarned(stats)).ToArray();
    }
}

using Lumen.Core.Balance;
using Lumen.Core.Gameplay;

namespace Lumen.Core.Rating;

/// <summary>
/// Per-play rating that feeds the <see cref="RatingEngine"/> pool (spec §29). Anchored
/// to the chart's difficulty, nudged down for lost accuracy and misses, up a touch for a
/// full combo. Distinct from PP.
/// </summary>
public static class PerformanceRating
{
    public static double Compute(double difficultyLevel, double accuracy01,
                                 int missCount, bool fullCombo, RatingConfig c)
    {
        double acc = Math.Clamp(accuracy01, 0, 1);
        double rating = difficultyLevel
                        + c.AccuracySlope * (acc - 1.0)      // 0 at 100%, negative below
                        - c.MissWeight * Math.Max(0, missCount)
                        + (fullCombo ? c.FullComboBonus : 0);

        return Math.Max(0, rating);
    }

    public static double Compute(PlayResult result, double difficultyLevel, RatingConfig c)
        => Compute(difficultyLevel, result.Accuracy / 100.0, result.Miss, result.FullCombo, c);
}

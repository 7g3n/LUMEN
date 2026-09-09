using Lumen.Core.Balance;

namespace Lumen.Core.Rating;

/// <summary>
/// The overall player Rating and its inputs (spec §29). Rating is a weighted average of
/// the player's best per-play performance ratings — a stable estimate of how strong they
/// reliably are, distinct from PP (which rates one play) and from Score.
/// </summary>
public static class RatingEngine
{
    /// <summary>
    /// Weighted mean of the top <c>PoolSize</c> values, sorted descending, with
    /// weightᵢ = DecayBase^i. Returns 0 for an empty input.
    /// </summary>
    public static double Compute(IEnumerable<double> performanceRatings, RatingConfig config)
        => WeightedTopMean(performanceRatings, config.PoolSize, config.DecayBase);

    /// <summary>Total PP: same shape as Rating but over best-per-chart PP values.</summary>
    public static double ComputeTotalPp(IEnumerable<double> bestPpPerChart, PpConfig config)
    {
        var sorted = bestPpPerChart.Where(v => v > 0).OrderByDescending(v => v)
            .Take(config.TotalPpPoolSize).ToArray();

        double total = 0;
        double w = 1.0;
        foreach (double pp in sorted)
        {
            total += pp * w;
            w *= config.TotalPpDecayBase;
        }

        return total;
    }

    public static double WeightedTopMean(IEnumerable<double> values, int poolSize, double decayBase)
    {
        var sorted = values.OrderByDescending(v => v).Take(Math.Max(1, poolSize)).ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        double weightedSum = 0;
        double weightTotal = 0;
        double w = 1.0;
        foreach (double v in sorted)
        {
            weightedSum += v * w;
            weightTotal += w;
            w *= decayBase;
        }

        return weightedSum / weightTotal;
    }
}

using Lumen.Core.Charts;

namespace Lumen.Core.Rating;

/// <summary>Per-axis ability estimate (spec §40). Same ~0–20 scale as Rating.</summary>
public sealed record SkillAxes(
    double Speed, double Technical, double Reading, double Stamina, double Accuracy)
{
    public static readonly SkillAxes Zero = new(0, 0, 0, 0, 0);
}

/// <summary>
/// Estimates the five skill axes from a player's best performances (spec §40). Each
/// axis is a decay-weighted mean of <c>performanceRating × normalisedAttribute</c> over
/// the performances that most exercise that axis. Rule-based; a learned model is a later
/// option.
/// </summary>
public static class SkillProfile
{
    public sealed record Sample(double PerformanceRating, double Accuracy01, ChartAttributes Attributes);

    public static SkillAxes Compute(IReadOnlyList<Sample> samples, double decayBase = 0.92)
    {
        if (samples.Count == 0)
        {
            return SkillAxes.Zero;
        }

        return new SkillAxes(
            Speed: Axis(samples, decayBase, s => s.Attributes.Speed, s => 1.0),
            Technical: Axis(samples, decayBase, s => s.Attributes.Technical, s => 1.0),
            Reading: Axis(samples, decayBase, s => (s.Attributes.Reading + s.Attributes.Reaction) / 2, s => 1.0),
            Stamina: Axis(samples, decayBase, s => s.Attributes.Stamina, s => 1.0),
            Accuracy: AccuracyAxis(samples, decayBase));
    }

    private static double Axis(IReadOnlyList<Sample> samples, double decayBase,
                               Func<Sample, double> attribute, Func<Sample, double> extraWeight)
    {
        // Rank samples by how much this axis contributed, then decay-weight.
        var ranked = samples
            .OrderByDescending(s => s.PerformanceRating * Math.Max(0.01, attribute(s)))
            .ToArray();

        double sum = 0;
        double weightTotal = 0;
        double w = 1.0;
        foreach (Sample s in ranked)
        {
            double attr = attribute(s);
            double contribution = s.PerformanceRating * ClampScale(attr) * extraWeight(s);
            sum += contribution * w;
            weightTotal += w;
            w *= decayBase;
        }

        return weightTotal > 0 ? Math.Round(sum / weightTotal, 2) : 0;
    }

    private static double AccuracyAxis(IReadOnlyList<Sample> samples, double decayBase)
    {
        var ranked = samples.OrderByDescending(s => s.Accuracy01 * s.PerformanceRating).ToArray();
        double sum = 0, weightTotal = 0, w = 1.0;
        foreach (Sample s in ranked)
        {
            // Reward high accuracy on harder charts.
            double value = s.PerformanceRating * Math.Pow(Math.Clamp(s.Accuracy01, 0, 1), 3);
            sum += value * w;
            weightTotal += w;
            w *= decayBase;
        }

        return weightTotal > 0 ? Math.Round(sum / weightTotal, 2) : 0;
    }

    /// <summary>Attribute (0–20) contributes as a factor around 1.0 (0.4 … 1.6).</summary>
    private static double ClampScale(double attr) => Math.Clamp(0.4 + attr / 20.0 * 1.2, 0.4, 1.6);
}

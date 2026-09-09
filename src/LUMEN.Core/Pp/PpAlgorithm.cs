using Lumen.Core.Balance;
using Lumen.Core.Charts;
using Lumen.Core.Gameplay;

namespace Lumen.Core.Pp;

/// <summary>Every factor that went into a PP value, for display and debugging (spec §31).</summary>
public sealed record PpBreakdown(
    double BasePp,
    double AccuracyMultiplier,
    double ComboMultiplier,
    double MissMultiplier,
    double TechnicalMultiplier,
    double SpeedMultiplier,
    double ReadingMultiplier,
    double FinalPp)
{
    public static readonly PpBreakdown Zero = new(0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Performance Points for a single play (spec §30–31). "How impressive was this one" —
/// a separate system from Score and from the overall Rating. Every step is a pure
/// function and independently unit-tested.
/// </summary>
public static class PpAlgorithm
{
    /// <summary>Base value from the chart's difficulty alone.</summary>
    public static double CalculateBasePp(double difficultyLevel, PpConfig c)
    {
        double d = Math.Max(0, difficultyLevel);
        return c.BaseScale * Math.Pow(d, c.BaseExponent);
    }

    /// <summary>Steep multiplier: near-100% accuracy is worth far more than "good enough".</summary>
    public static double CalculateAccuracyMultiplier(double accuracy01, PpConfig c)
    {
        double a = Math.Clamp(accuracy01, 0, 1);
        if (a <= c.AccuracyFloor)
        {
            return 0;
        }

        double scaled = (a - c.AccuracyFloor) / (1 - c.AccuracyFloor);
        return Math.Pow(scaled, c.AccuracyExponent);
    }

    public static double CalculateComboMultiplier(int maxCombo, int totalNotes, PpConfig c)
    {
        if (totalNotes <= 0)
        {
            return c.ComboFloor;
        }

        double ratio = Math.Clamp((double)maxCombo / totalNotes, 0, 1);
        return c.ComboFloor + (1 - c.ComboFloor) * Math.Pow(ratio, c.ComboExponent);
    }

    /// <summary>Multiplicative decay per miss.</summary>
    public static double CalculateMissPenalty(int missCount, PpConfig c)
        => Math.Pow(c.MissBase, Math.Max(0, missCount));

    public static double CalculateTechnicalMultiplier(double technicalAttr, PpConfig c)
        => 1 + c.TechnicalWeight * Normalize(technicalAttr, c);

    public static double CalculateSpeedMultiplier(double speedAttr, PpConfig c)
        => 1 + c.SpeedWeight * Normalize(speedAttr, c);

    public static double CalculateReadingMultiplier(double readingAttr, PpConfig c)
        => 1 + c.ReadingWeight * Normalize(readingAttr, c);

    public static double CalculateFinalPp(
        double basePp, double accMul, double comboMul, double missMul,
        double techMul, double speedMul, double readMul)
        => Math.Max(0, basePp * accMul * comboMul * missMul * techMul * speedMul * readMul);

    /// <summary>Runs the whole pipeline for a finished play.</summary>
    public static PpBreakdown Compute(PlayResult result, ChartAttributes attributes,
                                      double difficultyLevel, PpConfig c)
    {
        double basePp = CalculateBasePp(difficultyLevel, c);
        double acc = CalculateAccuracyMultiplier(result.Accuracy / 100.0, c);
        double combo = CalculateComboMultiplier(result.MaxCombo, result.TotalNotes, c);
        double miss = CalculateMissPenalty(result.Miss, c);
        double tech = CalculateTechnicalMultiplier(attributes.Technical, c);
        double speed = CalculateSpeedMultiplier(attributes.Speed, c);
        double read = CalculateReadingMultiplier(attributes.Reading, c);
        double final = CalculateFinalPp(basePp, acc, combo, miss, tech, speed, read);

        return new PpBreakdown(basePp, acc, combo, miss, tech, speed, read, final);
    }

    /// <summary>Maps an attribute value onto roughly [-1, 1] around the reference level.</summary>
    private static double Normalize(double attr, PpConfig c)
        => Math.Clamp((attr - c.AttributeReference) / c.AttributeSpread, -1, 1);
}

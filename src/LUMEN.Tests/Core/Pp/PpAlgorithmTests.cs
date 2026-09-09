using System;
using FluentAssertions;
using Lumen.Core.Balance;
using Lumen.Core.Pp;
using Xunit;

namespace Lumen.Tests.Core.Pp;

/// <summary>
/// Each PP step is a pure function (spec §31). These lock the default numbers so any
/// change to the formula or the coefficients is a deliberate, visible edit.
/// </summary>
public class PpAlgorithmTests
{
    private static readonly PpConfig C = new();

    // --- CalculateBasePp ---

    [Fact]
    public void BasePp_follows_scale_times_difficulty_to_the_exponent()
    {
        // locked to BaseScale = 0.28, BaseExponent = 2.4
        PpAlgorithm.CalculateBasePp(0, C).Should().Be(0);
        PpAlgorithm.CalculateBasePp(1, C).Should().BeApproximately(0.28, 1e-9);
        PpAlgorithm.CalculateBasePp(10, C).Should().BeApproximately(0.28 * Math.Pow(10, 2.4), 1e-12);
        PpAlgorithm.CalculateBasePp(10, C).Should().BeApproximately(70.33, 0.01); // magnitude sanity
    }

    [Fact]
    public void BasePp_is_strictly_increasing_in_difficulty()
    {
        double prev = -1;
        for (double d = 0; d <= 20; d += 0.5)
        {
            double v = PpAlgorithm.CalculateBasePp(d, C);
            v.Should().BeGreaterThan(prev);
            prev = v;
        }
    }

    [Fact]
    public void BasePp_clamps_negative_difficulty_to_zero()
    {
        PpAlgorithm.CalculateBasePp(-5, C).Should().Be(0);
    }

    // --- CalculateAccuracyMultiplier ---

    [Fact]
    public void AccuracyMultiplier_locked_values()
    {
        // locked to AccuracyFloor = 0.60, AccuracyExponent = 3.2
        PpAlgorithm.CalculateAccuracyMultiplier(1.00, C).Should().BeApproximately(1.0, 1e-12);
        PpAlgorithm.CalculateAccuracyMultiplier(0.60, C).Should().Be(0.0);
        PpAlgorithm.CalculateAccuracyMultiplier(0.50, C).Should().Be(0.0);
        PpAlgorithm.CalculateAccuracyMultiplier(0.80, C)
            .Should().BeApproximately(Math.Pow((0.80 - 0.60) / 0.40, 3.2), 1e-12);
        PpAlgorithm.CalculateAccuracyMultiplier(0.95, C)
            .Should().BeApproximately(Math.Pow((0.95 - 0.60) / 0.40, 3.2), 1e-12);
        // magnitude sanity so a silent formula swap is still caught
        PpAlgorithm.CalculateAccuracyMultiplier(0.95, C).Should().BeApproximately(0.652, 0.005);
    }

    [Fact]
    public void AccuracyMultiplier_is_steep_near_the_top()
    {
        double at100 = PpAlgorithm.CalculateAccuracyMultiplier(1.00, C);
        double at99 = PpAlgorithm.CalculateAccuracyMultiplier(0.99, C);
        double at98 = PpAlgorithm.CalculateAccuracyMultiplier(0.98, C);

        (at100 - at99).Should().BeGreaterThan(at99 - at98 - 1e-9); // convex: bigger drop nearer the top
        at99.Should().BeLessThan(at100);
    }

    // --- CalculateComboMultiplier ---

    [Theory]
    [InlineData(100, 100, 1.0)]
    [InlineData(50, 100, 0.75)]
    [InlineData(0, 100, 0.5)]
    [InlineData(10, 0, 0.5)]
    public void ComboMultiplier_locked_values(int maxCombo, int notes, double expected)
    {
        PpAlgorithm.CalculateComboMultiplier(maxCombo, notes, C).Should().BeApproximately(expected, 1e-9);
    }

    // --- CalculateMissPenalty ---

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1, 0.96)]
    [InlineData(5, 0.8153726976)]
    [InlineData(10, 0.6648326359)]
    public void MissPenalty_is_multiplicative_decay(int misses, double expected)
    {
        PpAlgorithm.CalculateMissPenalty(misses, C).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void MissPenalty_never_increases_with_more_misses()
    {
        for (int m = 1; m < 30; m++)
        {
            PpAlgorithm.CalculateMissPenalty(m, C)
                .Should().BeLessThan(PpAlgorithm.CalculateMissPenalty(m - 1, C));
        }
    }

    // --- skill multipliers ---

    [Theory]
    [InlineData(10.0, 1.00)]  // reference -> neutral
    [InlineData(18.0, 1.10)]  // +1 sigma
    [InlineData(2.0, 0.90)]   // -1 sigma
    [InlineData(26.0, 1.10)]  // clamped at +1
    public void TechnicalMultiplier_locked_values(double attr, double expected)
    {
        PpAlgorithm.CalculateTechnicalMultiplier(attr, C).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void SpeedAndReading_multipliers_use_their_own_weights()
    {
        PpAlgorithm.CalculateSpeedMultiplier(18, C).Should().BeApproximately(1.10, 1e-9);
        PpAlgorithm.CalculateReadingMultiplier(18, C).Should().BeApproximately(1.08, 1e-9);
    }

    // --- CalculateFinalPp ---

    [Fact]
    public void FinalPp_is_the_product_and_never_negative()
    {
        double f = PpAlgorithm.CalculateFinalPp(70.333, 0.652, 0.75, 0.96, 1.05, 1.10, 1.08);
        f.Should().BeApproximately(70.333 * 0.652 * 0.75 * 0.96 * 1.05 * 1.10 * 1.08, 1e-6);

        PpAlgorithm.CalculateFinalPp(-1, 1, 1, 1, 1, 1, 1).Should().Be(0);
    }

    [Fact]
    public void Compute_wires_the_whole_pipeline()
    {
        var result = MakeResult(accuracy: 99.0, maxCombo: 500, totalNotes: 500, miss: 0);
        var attrs = new Lumen.Core.Charts.ChartAttributes { Technical = 14, Speed = 12, Reading = 10 };

        PpBreakdown pp = PpAlgorithm.Compute(result, attrs, difficultyLevel: 12, C);

        pp.BasePp.Should().BeApproximately(PpAlgorithm.CalculateBasePp(12, C), 1e-9);
        pp.AccuracyMultiplier.Should().BeApproximately(PpAlgorithm.CalculateAccuracyMultiplier(0.99, C), 1e-9);
        pp.ComboMultiplier.Should().BeApproximately(1.0, 1e-9);
        pp.MissMultiplier.Should().Be(1.0);
        pp.FinalPp.Should().BeApproximately(
            pp.BasePp * pp.AccuracyMultiplier * pp.ComboMultiplier * pp.MissMultiplier *
            pp.TechnicalMultiplier * pp.SpeedMultiplier * pp.ReadingMultiplier, 1e-6);
    }

    [Fact]
    public void More_misses_and_less_accuracy_always_reduce_final_pp()
    {
        var attrs = Lumen.Core.Charts.ChartAttributes.Zero;
        double clean = PpAlgorithm.Compute(MakeResult(100, 400, 400, 0), attrs, 10, C).FinalPp;
        double oneMiss = PpAlgorithm.Compute(MakeResult(98, 380, 400, 1), attrs, 10, C).FinalPp;
        double messy = PpAlgorithm.Compute(MakeResult(90, 300, 400, 8), attrs, 10, C).FinalPp;

        clean.Should().BeGreaterThan(oneMiss);
        oneMiss.Should().BeGreaterThan(messy);
    }

    private static Lumen.Core.Gameplay.PlayResult MakeResult(double accuracy, int maxCombo, int totalNotes, int miss) => new()
    {
        Chart = new Lumen.Core.Charts.ChartMeta(),
        Score = 0,
        Accuracy = accuracy,
        MaxCombo = maxCombo,
        Perfect = totalNotes - miss,
        Great = 0, Good = 0, Bad = 0, Miss = miss,
        FullCombo = miss == 0,
        AllPerfect = miss == 0 && Math.Abs(accuracy - 100) < 1e-9,
        PlayedUtc = DateTime.UtcNow,
    };
}

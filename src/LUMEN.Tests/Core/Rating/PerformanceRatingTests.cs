using FluentAssertions;
using Lumen.Core.Balance;
using Lumen.Core.Rating;
using Xunit;

namespace Lumen.Tests.Core.Rating;

public class PerformanceRatingTests
{
    private static readonly RatingConfig C = new(); // slope 18, missWeight 0.45, fcBonus 0.15

    [Fact]
    public void Perfect_full_combo_sits_at_difficulty_plus_the_fc_bonus()
    {
        PerformanceRating.Compute(14.0, accuracy01: 1.0, missCount: 0, fullCombo: true, C)
            .Should().BeApproximately(14.0 + 0.15, 1e-9);
    }

    [Fact]
    public void Lost_accuracy_pulls_it_below_difficulty()
    {
        // 14 + 18*(0.98 - 1) - 0 + 0  = 14 - 0.36
        PerformanceRating.Compute(14.0, 0.98, 0, false, C)
            .Should().BeApproximately(14.0 - 0.36, 1e-9);
    }

    [Fact]
    public void Each_miss_costs_the_miss_weight()
    {
        double zero = PerformanceRating.Compute(14.0, 1.0, 0, false, C);
        double three = PerformanceRating.Compute(14.0, 1.0, 3, false, C);
        (zero - three).Should().BeApproximately(3 * 0.45, 1e-9);
    }

    [Fact]
    public void Never_goes_negative()
    {
        PerformanceRating.Compute(1.0, 0.10, 40, false, C).Should().Be(0);
    }

    [Fact]
    public void Higher_difficulty_beats_lower_at_equal_play_quality()
    {
        double easy = PerformanceRating.Compute(8.0, 0.97, 1, false, C);
        double hard = PerformanceRating.Compute(15.0, 0.97, 1, false, C);
        hard.Should().BeGreaterThan(easy);
    }
}

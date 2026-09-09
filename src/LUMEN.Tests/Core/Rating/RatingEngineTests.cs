using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Balance;
using Lumen.Core.Rating;
using Xunit;

namespace Lumen.Tests.Core.Rating;

public class RatingEngineTests
{
    private static readonly RatingConfig C = new(); // pool 50, decay 0.95

    [Fact]
    public void Empty_pool_is_zero()
    {
        RatingEngine.Compute(System.Array.Empty<double>(), C).Should().Be(0);
    }

    [Fact]
    public void Single_value_returns_itself()
    {
        RatingEngine.Compute(new[] { 14.2 }, C).Should().BeApproximately(14.2, 1e-9);
    }

    [Fact]
    public void Uniform_pool_returns_that_value()
    {
        RatingEngine.Compute(Enumerable.Repeat(13.0, 40), C).Should().BeApproximately(13.0, 1e-9);
    }

    [Fact]
    public void Best_values_dominate_via_the_decay_weighting()
    {
        // one strong play, many weak ones -> rating sits well above the mean
        var pool = new List<double> { 16.0 };
        pool.AddRange(Enumerable.Repeat(8.0, 49));

        double rating = RatingEngine.Compute(pool, C);
        double plainMean = pool.Average();

        rating.Should().BeGreaterThan(plainMean);
        rating.Should().BeLessThan(16.0);
    }

    [Fact]
    public void Only_the_top_pool_size_values_count()
    {
        var big = Enumerable.Repeat(15.0, 50).Concat(Enumerable.Repeat(2.0, 200));
        RatingEngine.Compute(big, C).Should().BeApproximately(15.0, 1e-9);
    }

    [Fact]
    public void Weighted_mean_matches_the_closed_form_for_two_values()
    {
        // weights 1, 0.95  ->  (a*1 + b*0.95) / 1.95
        double r = RatingEngine.WeightedTopMean(new[] { 10.0, 8.0 }, poolSize: 50, decayBase: 0.95);
        r.Should().BeApproximately((10.0 * 1 + 8.0 * 0.95) / 1.95, 1e-9);
    }

    [Fact]
    public void Adding_a_better_performance_never_lowers_the_rating()
    {
        var pool = new List<double> { 12, 11.5, 11, 10.5, 10 };
        double before = RatingEngine.Compute(pool, C);

        pool.Add(13.0);
        double after = RatingEngine.Compute(pool, C);

        after.Should().BeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void TotalPp_is_a_decayed_sum_not_a_mean()
    {
        double total = RatingEngine.ComputeTotalPp(new[] { 100.0, 100.0 }, new PpConfig());
        total.Should().BeApproximately(100.0 + 100.0 * 0.95, 1e-9);
    }

    [Fact]
    public void TotalPp_ignores_zero_and_negative_entries()
    {
        RatingEngine.ComputeTotalPp(new[] { 50.0, 0.0, -3.0 }, new PpConfig())
            .Should().BeApproximately(50.0, 1e-9);
    }
}

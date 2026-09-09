using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Balance;
using Lumen.Core.Gameplay;
using Xunit;

namespace Lumen.Tests.Core.Gameplay;

public class JudgementRuleTests
{
    private static readonly JudgementWindows W = new(); // 25 / 50 / 90 / 140

    [Theory]
    [InlineData(0, Judgement.Perfect)]
    [InlineData(25, Judgement.Perfect)]
    [InlineData(-25, Judgement.Perfect)]
    [InlineData(25.1, Judgement.Great)]
    [InlineData(50, Judgement.Great)]
    [InlineData(-50, Judgement.Great)]
    [InlineData(50.1, Judgement.Good)]
    [InlineData(90, Judgement.Good)]
    [InlineData(90.1, Judgement.Bad)]
    [InlineData(140, Judgement.Bad)]
    [InlineData(-140, Judgement.Bad)]
    [InlineData(140.1, Judgement.Miss)]
    [InlineData(500, Judgement.Miss)]
    public void ForTap_maps_error_to_tier(double errorMs, Judgement expected)
    {
        JudgementRule.ForTap(errorMs, W).Should().Be(expected);
    }

    [Theory]
    [InlineData(139.9, true)]
    [InlineData(140, true)]
    [InlineData(140.1, false)]
    public void InHitWindow_uses_the_widest_window(double errorMs, bool inside)
    {
        JudgementRule.InHitWindow(errorMs, W).Should().Be(inside);
    }

    [Theory]
    [InlineData(20, Judgement.Perfect)]
    [InlineData(45, Judgement.Great)]
    [InlineData(110, Judgement.Good)]   // within HoldTailMs = 120
    [InlineData(130, Judgement.Miss)]
    public void ForHoldTail_uses_a_wider_window(double errorMs, Judgement expected)
    {
        JudgementRule.ForHoldTail(errorMs, W).Should().Be(expected);
    }
}

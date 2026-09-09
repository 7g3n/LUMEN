using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Balance;
using Lumen.Core.Gameplay;
using Xunit;

namespace Lumen.Tests.Core.Gameplay;

public class ScoreStateTests
{
    private static readonly BalanceConfig Cfg = BalanceConfig.Default;

    [Fact]
    public void All_perfect_full_combo_reaches_max_score()
    {
        var s = new ScoreState(totalUnits: 50, Cfg);
        for (int i = 0; i < 50; i++)
        {
            s.Register(Judgement.Perfect);
        }

        s.Score.Should().Be(Cfg.Score.MaxScore);
        s.Accuracy.Should().BeApproximately(100, 1e-9);
        s.FullCombo.Should().BeTrue();
        s.AllPerfect.Should().BeTrue();
    }

    [Fact]
    public void All_great_full_combo_earns_full_combo_portion_but_reduced_accuracy_portion()
    {
        var s = new ScoreState(totalUnits: 50, Cfg);
        for (int i = 0; i < 50; i++)
        {
            s.Register(Judgement.Great);
        }

        // accuracy portion 0.85 * 0.90 + combo portion 0.15 * 1.0 = 0.915
        s.Score.Should().Be((long)System.Math.Round(Cfg.Score.MaxScore * 0.915));
        s.Accuracy.Should().BeApproximately(90, 1e-9);
        s.FullCombo.Should().BeTrue();
    }

    [Fact]
    public void Miss_and_bad_break_combo_and_clear_full_combo()
    {
        var s = new ScoreState(totalUnits: 4, Cfg);
        s.Register(Judgement.Perfect);
        s.Register(Judgement.Good);  // <= threshold (Good) -> combo continues
        s.Register(Judgement.Bad);   // breaks
        s.Register(Judgement.Perfect);

        s.MaxCombo.Should().Be(2);
        s.Combo.Should().Be(1);
        s.FullCombo.Should().BeFalse();
    }

    [Fact]
    public void Accuracy_is_over_units_judged_so_far()
    {
        var s = new ScoreState(totalUnits: 10, Cfg);
        s.Register(Judgement.Perfect);
        s.Register(Judgement.Miss);

        s.Accuracy.Should().BeApproximately(50, 1e-9); // (1.0 + 0) / 2
    }

    [Fact]
    public void Good_keeps_the_combo_by_default_threshold()
    {
        var s = new ScoreState(totalUnits: 3, Cfg);
        s.Register(Judgement.Perfect);
        s.Register(Judgement.Good);
        s.Register(Judgement.Great);

        s.Combo.Should().Be(3);
    }
}

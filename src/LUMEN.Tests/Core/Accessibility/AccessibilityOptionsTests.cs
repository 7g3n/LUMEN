using System;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Accessibility;
using Lumen.Core;
using Xunit;

namespace Lumen.Tests.Core.Accessibility;

public class AccessibilityOptionsTests
{
    [Fact]
    public void The_default_is_the_game_as_designed()
    {
        AccessibilityOptions d = AccessibilityOptions.Default;

        d.ReducedMotion.Should().BeFalse();
        d.HighContrast.Should().BeFalse();
        d.ShapeCues.Should().BeFalse();
        d.ShowJudgement.Should().BeTrue();
        d.ShowCombo.Should().BeTrue();
        d.EffectIntensity.Should().Be(1.0);
    }

    [Fact]
    public void Without_reduced_motion_an_animation_runs_its_course()
    {
        AccessibilityOptions d = AccessibilityOptions.Default;

        d.Progress(0, 0.5).Should().Be(0);
        d.Progress(0.25, 0.5).Should().BeApproximately(0.5, 0.0001);
        d.Progress(0.5, 0.5).Should().Be(1);
        d.MotionScale.Should().Be(1);
    }

    /// <summary>
    /// The point of the switch: the end state arrives immediately rather than being
    /// animated towards. Nothing is skipped — the information is there from frame one.
    /// </summary>
    [Fact]
    public void With_reduced_motion_an_animation_is_finished_from_the_first_frame()
    {
        var reduced = new AccessibilityOptions { ReducedMotion = true };

        reduced.Progress(0, 0.5).Should().Be(1);
        reduced.Progress(0.001, 10).Should().Be(1);
        reduced.MotionScale.Should().Be(0);
    }

    [Fact]
    public void An_animation_with_no_duration_is_already_over()
    {
        AccessibilityOptions.Default.Progress(0, 0).Should().Be(1);
        AccessibilityOptions.Default.Progress(5, -1).Should().Be(1);
    }

    [Fact]
    public void Progress_never_leaves_its_bounds()
    {
        AccessibilityOptions d = AccessibilityOptions.Default;

        d.Progress(-5, 1).Should().Be(0);
        d.Progress(500, 1).Should().Be(1);
    }

    [Fact]
    public void Effect_strength_follows_the_slider_and_is_silenced_by_reduced_motion()
    {
        new AccessibilityOptions { EffectIntensity = 0.4 }.EffectStrength
            .Should().BeApproximately(0.4, 0.0001);

        new AccessibilityOptions { EffectIntensity = 5 }.EffectStrength.Should().Be(1);
        new AccessibilityOptions { EffectIntensity = -2 }.EffectStrength.Should().Be(0);

        new AccessibilityOptions { EffectIntensity = 1, ReducedMotion = true }
            .EffectStrength.Should().Be(0);
    }
}

public class JudgementShapeTests
{
    /// <summary>
    /// The rule §67 exists to enforce: a grade must be distinguishable without telling
    /// the colours apart, so every judgement needs its own silhouette.
    /// </summary>
    [Fact]
    public void Every_judgement_has_a_shape_of_its_own()
    {
        var judgements = (Judgement[])Enum.GetValues(typeof(Judgement));

        string[] shapes = judgements.Select(JudgementShapes.For).ToArray();

        shapes.Should().OnlyContain(s => s.Length > 0);
        shapes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Labels_carry_the_shape_only_when_cues_are_on()
    {
        JudgementShapes.Label(Judgement.Perfect, shapeCues: false).Should().Be("PERFECT");
        JudgementShapes.Label(Judgement.Perfect, shapeCues: true)
            .Should().Be(JudgementShapes.For(Judgement.Perfect) + " PERFECT");
    }

    [Fact]
    public void Every_judgement_has_a_readable_label_either_way()
    {
        foreach (Judgement judgement in (Judgement[])Enum.GetValues(typeof(Judgement)))
        {
            JudgementShapes.Label(judgement, false).Should().NotBeEmpty();
            JudgementShapes.Label(judgement, true).Should().NotBeEmpty();
        }
    }

    /// <summary>
    /// These are asked for while drawing, so the same request has to hand back the same
    /// string rather than composing a new one each time.
    /// </summary>
    [Fact]
    public void Asking_twice_does_not_build_a_second_string()
    {
        ReferenceEquals(
            JudgementShapes.Label(Judgement.Great, true),
            JudgementShapes.Label(Judgement.Great, true)).Should().BeTrue();

        ReferenceEquals(
            JudgementShapes.Label(Judgement.Great, false),
            JudgementShapes.Label(Judgement.Great, false)).Should().BeTrue();
    }

    [Fact]
    public void A_judgement_outside_the_enum_is_blank_rather_than_a_crash()
    {
        JudgementShapes.Label((Judgement)999, true).Should().BeEmpty();
        JudgementShapes.Label((Judgement)(-1), false).Should().BeEmpty();
    }
}

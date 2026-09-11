using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Achievements;
using Xunit;

namespace Lumen.Tests.Core.Achievements;

public class AchievementEngineTests
{
    private static readonly string[] None = Array.Empty<string>();

    private static string[] Earned(AchievementStats stats, params string[] already) =>
        AchievementEngine.NewlyEarned(stats, already).Select(a => a.Id).ToArray();

    [Fact]
    public void A_fresh_profile_has_earned_nothing()
    {
        Earned(AchievementStats.Empty).Should().BeEmpty();
    }

    [Fact]
    public void The_spec_set_is_present()
    {
        string[] ids = AchievementEngine.All.Select(a => a.Id).ToArray();

        ids.Should().Contain(new[]
        {
            "first-play", "plays-100", "plays-1000",
            "first-full-combo", "full-combos-100",
            "accuracy-100", "pp-10000", "rating-15",
        });
    }

    [Fact]
    public void Every_achievement_has_a_name_a_description_and_a_positive_target()
    {
        foreach (AchievementDefinition a in AchievementEngine.All)
        {
            a.Name.Should().NotBeNullOrWhiteSpace();
            a.Description.Should().NotBeNullOrWhiteSpace();
            a.Target.Should().BeGreaterThan(0, a.Id);
        }
    }

    [Fact]
    public void Ids_are_unique()
    {
        AchievementEngine.All.Select(a => a.Id).Should().OnlyHaveUniqueItems();
    }

    // --- thresholds ---

    [Fact]
    public void The_first_play_unlocks_on_the_first_play()
    {
        Earned(AchievementStats.Empty with { PlayCount = 1 }).Should().Contain("first-play");
        Earned(AchievementStats.Empty with { PlayCount = 0 }).Should().NotContain("first-play");
    }

    [Theory]
    [InlineData(99, false)]
    [InlineData(100, true)]
    [InlineData(101, true)]
    public void A_count_achievement_unlocks_at_its_target_not_before(int plays, bool unlocked)
    {
        Earned(AchievementStats.Empty with { PlayCount = plays })
            .Contains("plays-100").Should().Be(unlocked);
    }

    [Theory]
    [InlineData(98.9, false)]
    [InlineData(99.0, true)]
    [InlineData(100.0, true)]
    public void Accuracy_achievements_read_the_percentage(double accuracy, bool unlocked)
    {
        Earned(AchievementStats.Empty with { BestAccuracy = accuracy })
            .Contains("accuracy-99").Should().Be(unlocked);
    }

    [Fact]
    public void A_perfect_run_unlocks_both_accuracy_tiers()
    {
        string[] earned = Earned(AchievementStats.Empty with { BestAccuracy = 100 });
        earned.Should().Contain("accuracy-99").And.Contain("accuracy-100");
    }

    [Theory]
    [InlineData(14.99, false)]
    [InlineData(15.0, true)]
    public void Rating_milestones_unlock_at_the_number(double rating, bool unlocked)
    {
        Earned(AchievementStats.Empty with { Rating = rating })
            .Contains("rating-15").Should().Be(unlocked);
    }

    [Fact]
    public void Total_pp_milestones_unlock_at_the_number()
    {
        Earned(AchievementStats.Empty with { TotalPp = 9_999 }).Should().NotContain("pp-10000");
        Earned(AchievementStats.Empty with { TotalPp = 10_000 }).Should().Contain("pp-10000");
    }

    [Fact]
    public void Play_time_is_measured_in_milliseconds()
    {
        Earned(AchievementStats.Empty with { TotalPlayTimeMs = 9 * 60 * 60 * 1000 })
            .Should().NotContain("playtime-10h");
        Earned(AchievementStats.Empty with { TotalPlayTimeMs = 10 * 60 * 60 * 1000 })
            .Should().Contain("playtime-10h");
    }

    // --- not unlocking twice ---

    [Fact]
    public void Something_already_unlocked_is_not_reported_again()
    {
        var stats = AchievementStats.Empty with { PlayCount = 500 };

        Earned(stats).Should().Contain("first-play");
        Earned(stats, "first-play", "plays-100").Should()
            .NotContain("first-play").And.NotContain("plays-100");
    }

    [Fact]
    public void Evaluating_twice_with_nothing_new_earns_nothing()
    {
        var stats = AchievementStats.Empty with { PlayCount = 3, FullCombos = 1 };
        string[] first = Earned(stats);

        Earned(stats, first).Should().BeEmpty();
    }

    [Fact]
    public void An_achievement_added_later_unlocks_retroactively()
    {
        // The whole reason achievements are measured against statistics rather than fired
        // from events: a player who already qualified should not be locked out because the
        // moment passed before the achievement existed.
        var veteran = AchievementStats.Empty with { PlayCount = 900, FullCombos = 150 };

        Earned(veteran, "first-play").Should().Contain("full-combos-100");
    }

    // --- progress ---

    [Fact]
    public void Progress_is_reported_as_a_fraction_of_the_target()
    {
        AchievementDefinition plays100 = AchievementEngine.Find("plays-100")!;

        plays100.Fraction(AchievementStats.Empty with { PlayCount = 25 }).Should().Be(0.25);
        plays100.Fraction(AchievementStats.Empty with { PlayCount = 0 }).Should().Be(0);
    }

    [Fact]
    public void Progress_never_exceeds_one()
    {
        AchievementDefinition firstPlay = AchievementEngine.Find("first-play")!;

        firstPlay.Fraction(AchievementStats.Empty with { PlayCount = 900 }).Should().Be(1);
    }

    [Fact]
    public void An_unknown_id_is_simply_not_found()
    {
        AchievementEngine.Find("no-such-achievement").Should().BeNull();
    }
}

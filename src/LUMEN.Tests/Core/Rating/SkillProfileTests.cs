using System.Collections.Generic;
using FluentAssertions;
using Lumen.Core.Charts;
using Lumen.Core.Rating;
using Xunit;

namespace Lumen.Tests.Core.Rating;

public class SkillProfileTests
{
    [Fact]
    public void No_samples_gives_zero_axes()
    {
        SkillProfile.Compute(System.Array.Empty<SkillProfile.Sample>()).Should().Be(SkillAxes.Zero);
    }

    [Fact]
    public void A_speed_heavy_history_produces_a_higher_speed_axis_than_a_reading_axis()
    {
        var samples = new List<SkillProfile.Sample>
        {
            new(14.0, 0.98, new ChartAttributes { Speed = 18, Technical = 6, Reading = 5, Stamina = 8, Reaction = 6 }),
            new(13.5, 0.97, new ChartAttributes { Speed = 17, Technical = 7, Reading = 6, Stamina = 9, Reaction = 6 }),
            new(13.0, 0.99, new ChartAttributes { Speed = 16, Technical = 5, Reading = 4, Stamina = 7, Reaction = 5 }),
        };

        SkillAxes axes = SkillProfile.Compute(samples);

        axes.Speed.Should().BeGreaterThan(axes.Reading);
        axes.Speed.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Accuracy_axis_rewards_clean_play_on_hard_charts()
    {
        var clean = new List<SkillProfile.Sample>
        {
            new(15.0, 0.999, new ChartAttributes { Speed = 12, Technical = 12, Reading = 12, Stamina = 12 }),
        };
        var sloppy = new List<SkillProfile.Sample>
        {
            new(15.0, 0.90, new ChartAttributes { Speed = 12, Technical = 12, Reading = 12, Stamina = 12 }),
        };

        SkillProfile.Compute(clean).Accuracy.Should().BeGreaterThan(SkillProfile.Compute(sloppy).Accuracy);
    }
}

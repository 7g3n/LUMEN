using FluentAssertions;
using Lumen.Core.Library;
using Xunit;

namespace Lumen.Tests.Core.Library;

public class DifficultyBandTests
{
    [Theory]
    [InlineData(0.0, DifficultyBand.Introductory)]
    [InlineData(3.9, DifficultyBand.Introductory)]
    [InlineData(4.0, DifficultyBand.Basic)]
    [InlineData(6.9, DifficultyBand.Basic)]
    [InlineData(7.0, DifficultyBand.Advanced)]
    [InlineData(9.9, DifficultyBand.Advanced)]
    [InlineData(10.0, DifficultyBand.Expert)]
    [InlineData(12.9, DifficultyBand.Expert)]
    [InlineData(13.0, DifficultyBand.Master)]
    [InlineData(14.9, DifficultyBand.Master)]
    [InlineData(15.0, DifficultyBand.Apex)]
    [InlineData(17.5, DifficultyBand.Apex)]
    public void Bands_split_at_their_documented_boundaries(double level, DifficultyBand expected)
    {
        DifficultyBands.For(level).Should().Be(expected);
    }

    [Fact]
    public void A_negative_level_still_lands_in_a_band()
    {
        DifficultyBands.For(-1).Should().Be(DifficultyBand.Introductory);
    }

    [Fact]
    public void Bands_never_go_backwards_as_the_level_rises()
    {
        var previous = DifficultyBand.Introductory;
        for (double level = 0; level <= 20; level += 0.1)
        {
            DifficultyBand band = DifficultyBands.For(level);
            ((int)band).Should().BeGreaterThanOrEqualTo((int)previous, $"level {level:0.0}");
            previous = band;
        }
    }

    [Theory]
    [InlineData(14.7, "14.7")]
    [InlineData(15.0, "15.0")]
    [InlineData(3.25, "3.3")]
    public void Precise_keeps_one_decimal(double level, string expected)
    {
        DifficultyBands.Precise(level).Should().Be(expected);
    }

    [Theory]
    [InlineData(14.0, "14")]
    [InlineData(14.4, "14")]
    [InlineData(14.5, "14+")]
    [InlineData(14.7, "14+")]
    [InlineData(15.0, "15")]
    public void Short_marks_the_upper_half_of_a_level_with_a_plus(double level, string expected)
    {
        DifficultyBands.Short(level).Should().Be(expected);
    }

    [Fact]
    public void Every_band_has_a_name()
    {
        foreach (DifficultyBand band in Enum.GetValues<DifficultyBand>())
        {
            DifficultyBands.Name(band).Should().NotBeNullOrWhiteSpace();
            DifficultyBands.Name(band).Should().NotBe("UNKNOWN");
        }
    }
}

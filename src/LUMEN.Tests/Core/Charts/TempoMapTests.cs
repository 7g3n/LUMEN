using FluentAssertions;
using Lumen.Core.Charts;
using Xunit;

namespace Lumen.Tests.Core.Charts;

public class TempoMapTests
{
    [Fact]
    public void Constant_tempo_beat_conversion()
    {
        var map = new TempoMap(new[] { new BpmPoint(0, 120) }); // 500 ms/beat

        map.BeatAt(0).Should().Be(0);
        map.BeatAt(500).Should().BeApproximately(1, 1e-9);
        map.BeatAt(2000).Should().BeApproximately(4, 1e-9);
        map.TimeMsAtBeat(4).Should().BeApproximately(2000, 1e-6);
        map.BpmAt(1234).Should().Be(120);
    }

    [Fact]
    public void Tempo_change_is_continuous_in_beats()
    {
        // 120 BPM for 2 s (4 beats), then 240 BPM.
        var map = new TempoMap(new[] { new BpmPoint(0, 120), new BpmPoint(2000, 240) });

        map.BeatAt(2000).Should().BeApproximately(4, 1e-9);
        map.BeatAt(2250).Should().BeApproximately(5, 1e-9);  // 250 ms at 240 BPM = 1 beat
        map.BpmAt(3000).Should().Be(240);
        map.TimeMsAtBeat(6).Should().BeApproximately(2500, 1e-6);
    }

    [Fact]
    public void First_point_is_snapped_to_zero()
    {
        var map = new TempoMap(new[] { new BpmPoint(1000, 100) });
        map.BeatAt(0).Should().Be(0);
        map.BpmAt(0).Should().Be(100);
    }

    [Fact]
    public void Empty_points_default_to_120()
    {
        var map = new TempoMap(System.Array.Empty<BpmPoint>());
        map.BpmAt(0).Should().Be(120);
    }
}

using System;
using System.Linq;
using FluentAssertions;
using Lumen.Audio;
using Xunit;

namespace Lumen.Tests.Core.Editing;

public class WaveformPeaksTests
{
    private const int Rate = 44100;

    private static float[] Stereo(Func<int, float> sample, int frames)
    {
        var data = new float[frames * 2];
        for (int f = 0; f < frames; f++)
        {
            float v = sample(f);
            data[f * 2] = v;
            data[f * 2 + 1] = v;
        }

        return data;
    }

    [Fact]
    public void An_empty_clip_produces_empty_peaks()
    {
        WaveformPeaks peaks = WaveformPeaks.From(Array.Empty<float>(), 2, Rate);

        peaks.BucketCount.Should().Be(0);
        peaks.Range(0, 1000).Should().Be((0f, 0f));
    }

    [Fact]
    public void The_duration_matches_the_sample_count()
    {
        WaveformPeaks peaks = WaveformPeaks.From(Stereo(_ => 0f, Rate * 2), 2, Rate);

        peaks.DurationMs.Should().BeApproximately(2000, 1);
    }

    [Fact]
    public void Silence_reduces_to_a_flat_line()
    {
        WaveformPeaks peaks = WaveformPeaks.From(Stereo(_ => 0f, Rate), 2, Rate);

        (float min, float max) = peaks.Range(0, 1000);
        min.Should().Be(0);
        max.Should().Be(0);
    }

    [Fact]
    public void A_full_scale_tone_reaches_both_rails()
    {
        WaveformPeaks peaks = WaveformPeaks.From(
            Stereo(f => MathF.Sin(f * 0.05f), Rate), 2, Rate);

        (float min, float max) = peaks.Range(0, 1000);
        max.Should().BeGreaterThan(0.9f);
        min.Should().BeLessThan(-0.9f);
    }

    [Fact]
    public void A_transient_survives_the_reduction()
    {
        // One loud frame in an otherwise silent second: an averaging reduction would
        // lose it, and it is exactly what an author lines a note up against.
        float[] data = Stereo(f => f == Rate / 2 ? 1f : 0f, Rate);

        WaveformPeaks peaks = WaveformPeaks.From(data, 2, Rate);

        peaks.Range(0, 1000).Max.Should().BeGreaterThan(0.9f);
    }

    [Fact]
    public void Peaks_are_localised_in_time()
    {
        // Loud in the first half-second, silent afterwards.
        float[] data = Stereo(f => f < Rate / 2 ? 0.8f : 0f, Rate);

        WaveformPeaks peaks = WaveformPeaks.From(data, 2, Rate);

        peaks.Range(0, 400).Max.Should().BeGreaterThan(0.7f);
        peaks.Range(600, 1000).Max.Should().BeLessThan(0.1f);
    }

    [Fact]
    public void A_backwards_range_is_empty_rather_than_throwing()
    {
        WaveformPeaks peaks = WaveformPeaks.From(Stereo(_ => 0.5f, Rate), 2, Rate);

        peaks.Range(800, 200).Should().Be((0f, 0f));
    }

    [Fact]
    public void A_range_past_the_end_is_clamped()
    {
        WaveformPeaks peaks = WaveformPeaks.From(Stereo(_ => 0.5f, Rate), 2, Rate);

        Action read = () => peaks.Range(0, 999_999);

        read.Should().NotThrow();
    }

    [Fact]
    public void Mono_input_is_accepted()
    {
        var mono = new float[Rate];
        for (int i = 0; i < mono.Length; i++)
        {
            mono[i] = 0.6f;
        }

        WaveformPeaks peaks = WaveformPeaks.From(mono, channels: 1, Rate);

        peaks.Range(0, 1000).Max.Should().BeApproximately(0.6f, 0.01f);
    }
}

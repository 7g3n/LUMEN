using System;
using System.Linq;
using FluentAssertions;
using Lumen.Audio;
using Lumen.Game.Screens;
using Xunit;

namespace Lumen.Tests.Game;

public class CalibrationTests
{
    private const double BeatMs = 600; // 100 BPM

    [Fact]
    public void A_tap_on_the_beat_has_no_error()
    {
        CalibrationScreen.ErrorForTap(0, BeatMs).Should().Be(0);
        CalibrationScreen.ErrorForTap(BeatMs * 7, BeatMs).Should().Be(0);
    }

    [Fact]
    public void A_late_tap_reads_positive_and_an_early_one_negative()
    {
        CalibrationScreen.ErrorForTap(BeatMs * 3 + 25, BeatMs).Should().BeApproximately(25, 0.0001);
        CalibrationScreen.ErrorForTap(BeatMs * 3 - 25, BeatMs).Should().BeApproximately(-25, 0.0001);
    }

    /// <summary>
    /// A tap is measured against the beat it is nearest, not the one that has just passed,
    /// so someone who anticipates the click is 20 ms early rather than 580 ms late.
    /// </summary>
    [Fact]
    public void A_tap_is_measured_against_the_nearest_beat()
    {
        CalibrationScreen.ErrorForTap(BeatMs - 20, BeatMs).Should().BeApproximately(-20, 0.0001);
        CalibrationScreen.ErrorForTap(BeatMs * 5 - 1, BeatMs).Should().BeApproximately(-1, 0.0001);
    }

    [Fact]
    public void The_error_never_exceeds_half_a_beat()
    {
        var random = new Random(1234);

        for (int i = 0; i < 500; i++)
        {
            double tap = random.NextDouble() * BeatMs * 24;
            Math.Abs(CalibrationScreen.ErrorForTap(tap, BeatMs))
                .Should().BeLessThanOrEqualTo(BeatMs / 2 + 0.0001);
        }
    }

    [Fact]
    public void The_median_of_an_odd_count_is_the_middle_value()
    {
        CalibrationScreen.Median(new double[] { 30, 10, 20 }).Should().Be(20);
    }

    [Fact]
    public void The_median_of_an_even_count_splits_the_middle_pair()
    {
        CalibrationScreen.Median(new double[] { 10, 20, 30, 40 }).Should().Be(25);
    }

    /// <summary>
    /// Why the median and not the mean: one badly fumbled tap in an otherwise steady run
    /// must not move the offset the player ends up playing with.
    /// </summary>
    [Fact]
    public void One_fumbled_tap_does_not_move_the_answer()
    {
        double[] steady = { 18, 20, 21, 19, 22, 20 };
        double[] withFumble = { 18, 20, 21, 19, 22, 20, 240 };

        double before = CalibrationScreen.Median(steady);
        double after = CalibrationScreen.Median(withFumble);

        Math.Abs(after - before).Should().BeLessThan(2);
        steady.Append(240).Average().Should().BeGreaterThan(before + 20); // the mean would not survive it
    }
}

public class MetronomeTests
{
    [Fact]
    public void A_click_track_is_long_enough_for_the_beats_it_promises()
    {
        AudioClip clip = Metronome.Build(bpm: 100, beats: 24);

        clip.DurationSeconds.Should().BeGreaterThanOrEqualTo(24 * 0.6);
        clip.DurationSeconds.Should().BeLessThan(24 * 0.6 + 1.0);
    }

    /// <summary>
    /// The calibration screen measures taps against the start of each click, so the loudest
    /// sample of a click has to sit on the beat rather than somewhere after it.
    /// </summary>
    [Fact]
    public void Each_click_peaks_on_its_beat()
    {
        const double Bpm = 100;
        AudioClip clip = Metronome.Build(Bpm, beats: 8);

        double beatSeconds = 60.0 / Bpm;

        for (int beat = 0; beat < 8; beat++)
        {
            int onBeat = (int)(beat * beatSeconds * AudioClip.SampleRate);

            float peakNear = PeakBetween(clip, onBeat, onBeat + AudioClip.SampleRate / 200);
            float peakBefore = PeakBetween(clip, onBeat - AudioClip.SampleRate / 50, onBeat - 8);

            peakNear.Should().BeGreaterThan(0.05f);
            peakNear.Should().BeGreaterThan(peakBefore);
        }
    }

    [Fact]
    public void The_bar_starts_louder_than_the_beats_inside_it()
    {
        AudioClip clip = Metronome.Build(bpm: 100, beats: 8);
        int beat = (int)(0.6 * AudioClip.SampleRate);

        float accent = PeakBetween(clip, 0, beat / 4);
        float plain = PeakBetween(clip, beat, beat + beat / 4);

        accent.Should().BeGreaterThan(plain);
    }

    [Fact]
    public void A_nonsense_tempo_falls_back_rather_than_producing_nothing()
    {
        Metronome.Build(bpm: 0, beats: 4).DurationSeconds.Should().BeGreaterThan(0);
        Metronome.Build(bpm: -50, beats: 4).DurationSeconds.Should().BeGreaterThan(0);
        Metronome.Build(bpm: 100, beats: 0).DurationSeconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_click_never_clips()
    {
        AudioClip clip = Metronome.Build(bpm: 100, beats: 16);

        foreach (float sample in clip.Samples)
        {
            Math.Abs(sample).Should().BeLessThanOrEqualTo(1f);
        }
    }

    private static float PeakBetween(AudioClip clip, int fromFrame, int toFrame)
    {
        float peak = 0;
        int from = Math.Max(0, fromFrame) * AudioClip.Channels;
        int to = Math.Min(clip.Samples.Length, Math.Max(0, toFrame) * AudioClip.Channels);

        for (int i = from; i < to; i++)
        {
            peak = Math.Max(peak, Math.Abs(clip.Samples[i]));
        }

        return peak;
    }
}

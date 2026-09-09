using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Charts;
using Xunit;

namespace Lumen.Tests.Core.Charts;

public class DifficultyAnalyzerTests
{
    private static Chart ChartAt(double bpm, int beats, System.Func<int, int> lane, double perBeat = 1)
    {
        double msPerBeat = 60_000.0 / bpm;
        var notes = new List<Note>();
        for (int b = 0; b < beats; b++)
        {
            for (int k = 0; k < perBeat; k++)
            {
                double t = b * msPerBeat + k * msPerBeat / perBeat;
                notes.Add(Note.Tap(t, lane(b * (int)perBeat + k)));
            }
        }

        return new Chart
        {
            LaneCount = 4,
            BpmPoints = new[] { new BpmPoint(0, bpm) },
            Notes = notes.ToArray(),
            Meta = new ChartMeta { DifficultyLevel = 0 }, // let the analyzer speak
        }.Normalized();
    }

    [Fact]
    public void Denser_charts_score_higher_speed()
    {
        var sparse = DifficultyAnalyzer.Analyze(ChartAt(120, 64, b => b % 4, perBeat: 1));
        var dense = DifficultyAnalyzer.Analyze(ChartAt(120, 64, b => b % 4, perBeat: 4));

        dense.Attributes.Speed.Should().BeGreaterThan(sparse.Attributes.Speed);
        dense.EstimatedLevel.Should().BeGreaterThan(sparse.EstimatedLevel);
    }

    [Fact]
    public void Jack_patterns_score_higher_technical_than_alternating()
    {
        var alternating = DifficultyAnalyzer.Analyze(ChartAt(160, 64, b => b % 4));
        var jacks = DifficultyAnalyzer.Analyze(ChartAt(160, 64, _ => 1));

        jacks.Attributes.Technical.Should().BeGreaterThan(alternating.Attributes.Technical);
    }

    [Fact]
    public void Attributes_stay_within_the_scale()
    {
        var r = DifficultyAnalyzer.Analyze(ChartAt(220, 200, b => b % 4, perBeat: 4));
        foreach (double v in new[]
                 {
                     r.Attributes.Speed, r.Attributes.Technical, r.Attributes.Reading,
                     r.Attributes.Stamina, r.Attributes.Reaction, r.Attributes.PatternComplexity,
                     r.EstimatedLevel,
                 })
        {
            v.Should().BeInRange(0, 20);
        }
    }

    [Fact]
    public void A_tiny_chart_does_not_throw()
    {
        var chart = new Chart { Notes = new[] { Note.Tap(0, 0) }, BpmPoints = new[] { new BpmPoint(0, 120) } };
        DifficultyAnalyzer.Analyze(chart).EstimatedLevel.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Authored_level_is_blended_not_ignored()
    {
        Chart chart = ChartAt(120, 32, b => b % 4) with { Meta = new ChartMeta { DifficultyLevel = 18 } };
        var r = DifficultyAnalyzer.Analyze(chart);

        // pure analysis of this easy chart is low; blending with 18 pulls it up
        r.EstimatedLevel.Should().BeGreaterThan(4);
        r.EstimatedLevel.Should().BeLessThan(18);
    }
}

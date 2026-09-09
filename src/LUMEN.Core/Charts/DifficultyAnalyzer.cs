namespace Lumen.Core.Charts;

/// <summary>
/// A light rule-based read of a chart's difficulty attributes (spec §53). It looks at
/// note density, spacing regularity, lane movement and jacks. The full multi-pass
/// analysis (and a refined estimated level) is Phase 7; this exists so PP and the skill
/// profile have real inputs in Phase 4.
/// </summary>
public static class DifficultyAnalyzer
{
    public sealed record Result(double EstimatedLevel, ChartAttributes Attributes);

    public static Result Analyze(Chart chart)
    {
        Chart c = chart.Normalized();
        var notes = c.Notes;
        if (notes.Count < 2)
        {
            return new Result(Math.Max(1, c.Meta.DifficultyLevel), ChartAttributes.Zero);
        }

        double spanSeconds = Math.Max(1.0, (c.LastNoteMs - c.FirstNoteMs) / 1000.0);
        double nps = notes.Count / spanSeconds;

        // Gaps between consecutive notes (ms).
        var gaps = new List<double>(notes.Count);
        int jacks = 0;
        int laneChanges = 0;
        for (int i = 1; i < notes.Count; i++)
        {
            gaps.Add(Math.Max(1, notes[i].TimeMs - notes[i - 1].TimeMs));
            if (notes[i].Lane == notes[i - 1].Lane)
            {
                jacks++;
            }
            else
            {
                laneChanges++;
            }
        }

        double meanGap = gaps.Average();
        double gapStdDev = Math.Sqrt(gaps.Select(g => (g - meanGap) * (g - meanGap)).Average());
        double gapVariability = Math.Clamp(gapStdDev / meanGap, 0, 1.5); // 0 = metronomic

        double burstiness = gaps.Count(g => g < meanGap * 0.55) / (double)gaps.Count;
        double jackRatio = jacks / (double)(jacks + laneChanges);
        double holdRatio = c.HoldCount / (double)notes.Count;

        // Map onto the ~0-20 scale. Coefficients are deliberately conservative.
        double speed = Clamp20(nps * 2.4);
        double stamina = Clamp20(nps * 1.8 * Math.Min(1.0, spanSeconds / 90.0) + holdRatio * 3);
        double technical = Clamp20(6 + gapVariability * 6 + jackRatio * 4);
        double reading = Clamp20(4 + burstiness * 10 + gapVariability * 3);
        double reaction = Clamp20(3 + burstiness * 8 + speed * 0.3);
        double pattern = Clamp20(5 + jackRatio * 6 + laneMovementEntropy(notes) * 6);

        double estimated = Clamp20(
            0.45 * speed + 0.20 * technical + 0.15 * reading + 0.12 * stamina + 0.08 * pattern);

        // Blend with the author's stated level so a deliberate label isn't overridden wholesale.
        double authored = c.Meta.DifficultyLevel;
        double level = authored > 0 ? 0.5 * authored + 0.5 * estimated : estimated;

        return new Result(Math.Round(level, 2), new ChartAttributes
        {
            Speed = Math.Round(speed, 2),
            Technical = Math.Round(technical, 2),
            Reading = Math.Round(reading, 2),
            Stamina = Math.Round(stamina, 2),
            Reaction = Math.Round(reaction, 2),
            PatternComplexity = Math.Round(pattern, 2),
        });
    }

    private static double Clamp20(double v) => Math.Clamp(v, 0, 20);

    private static double laneMovementEntropy(IReadOnlyList<Note> notes)
    {
        var transitions = new Dictionary<(int, int), int>();
        for (int i = 1; i < notes.Count; i++)
        {
            var key = (notes[i - 1].Lane, notes[i].Lane);
            transitions[key] = transitions.GetValueOrDefault(key) + 1;
        }

        int total = notes.Count - 1;
        double entropy = 0;
        foreach (int count in transitions.Values)
        {
            double p = count / (double)total;
            entropy -= p * Math.Log2(p);
        }

        // Normalise against a 4-lane max entropy (~4 bits for 16 transition types).
        return Math.Clamp(entropy / 4.0, 0, 1);
    }
}

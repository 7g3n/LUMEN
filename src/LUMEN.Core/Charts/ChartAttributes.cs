using System.Text.Json;

namespace Lumen.Core.Charts;

/// <summary>
/// A chart's skill fingerprint on the same ~0–20 scale as difficulty. Consumed by the
/// PP algorithm (technical / speed / reading) and the skill profile (spec §31, §40, §53).
/// A light rule-based estimate for now; the full analysis lands in Phase 7.
/// </summary>
public sealed record ChartAttributes
{
    public double Speed { get; init; }
    public double Technical { get; init; }
    public double Reading { get; init; }
    public double Stamina { get; init; }
    public double Reaction { get; init; }
    public double PatternComplexity { get; init; }

    public static readonly ChartAttributes Zero = new();

    public string ToJson() => JsonSerializer.Serialize(this);

    public static ChartAttributes FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? Zero
            : JsonSerializer.Deserialize<ChartAttributes>(json) ?? Zero;
}

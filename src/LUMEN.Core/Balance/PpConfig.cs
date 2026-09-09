namespace Lumen.Core.Balance;

/// <summary>
/// Coefficients for the PP pipeline (spec §31). Every value is here so the algorithm
/// can be re-tuned from <c>balance.json</c> without a rebuild, and so the unit tests
/// lock a known set of numbers.
/// </summary>
public sealed record PpConfig
{
    // Base curve: BasePp = Scale * difficulty^Exponent
    public double BaseScale { get; init; } = 0.28;
    public double BaseExponent { get; init; } = 2.4;

    // Accuracy multiplier: ((acc - Floor) / (1 - Floor))^Exponent, clamped to [0, 1].
    public double AccuracyFloor { get; init; } = 0.60;
    public double AccuracyExponent { get; init; } = 3.2;

    // Combo multiplier: Floor + (1 - Floor) * (maxCombo / notes)^Exponent
    public double ComboFloor { get; init; } = 0.50;
    public double ComboExponent { get; init; } = 1.0;

    // Miss penalty: MissBase^missCount
    public double MissBase { get; init; } = 0.96;

    // Skill multipliers: 1 + Weight * norm(attribute)
    public double TechnicalWeight { get; init; } = 0.10;
    public double SpeedWeight { get; init; } = 0.10;
    public double ReadingWeight { get; init; } = 0.08;

    /// <summary>Attribute value that maps to norm = 0 (an "average" chart).</summary>
    public double AttributeReference { get; init; } = 10.0;

    /// <summary>Attribute distance from the reference that maps to norm = ±1.</summary>
    public double AttributeSpread { get; init; } = 8.0;

    // Total PP: weighted sum of best-per-chart PP, own decay.
    public int TotalPpPoolSize { get; init; } = 100;
    public double TotalPpDecayBase { get; init; } = 0.95;
}

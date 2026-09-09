namespace Lumen.Core.Balance;

/// <summary>
/// Coefficients for the overall Rating and per-play performance rating (spec §29).
/// Rating is a separate system from PP: it estimates how strong a player reliably is,
/// from their best performance ratings.
/// </summary>
public sealed record RatingConfig
{
    /// <summary>How many best performance ratings feed the pool.</summary>
    public int PoolSize { get; init; } = 50;

    /// <summary>weightᵢ = DecayBase^i over the sorted-desc pool.</summary>
    public double DecayBase { get; init; } = 0.95;

    // --- per-play performance rating ---
    // perfRating = difficulty + AccuracySlope * (accuracy - 1)   // 0 at 100%, negative below
    //            - MissWeight * missCount
    //            + (fullCombo ? FullComboBonus : 0)
    // clamped to >= 0
    public double AccuracySlope { get; init; } = 18.0;
    public double MissWeight { get; init; } = 0.45;
    public double FullComboBonus { get; init; } = 0.15;
}

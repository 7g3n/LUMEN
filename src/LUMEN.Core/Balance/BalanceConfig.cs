using System.Text.Json.Serialization;

namespace Lumen.Core.Balance;

/// <summary>
/// Every gameplay-tuning number in one place (spec §22–25). Defaults are the values
/// from the design plan; they can be overridden per install by a
/// <c>settings/balance.json</c> without a rebuild. Score, PP and Rating stay separate
/// systems — this file only covers judgement, accuracy, combo and score.
/// </summary>
public sealed record BalanceConfig
{
    public JudgementWindows Windows { get; init; } = new();

    public AccuracyWeights Accuracy { get; init; } = new();

    public ScoreConfig Score { get; init; } = new();

    /// <summary>Judgements at or better than this keep the combo alive; worse ones break it.</summary>
    public Judgement ComboBreakThreshold { get; init; } = Judgement.Good;

    public static BalanceConfig Default { get; } = new();
}

/// <summary>Half-widths in milliseconds around the exact note time (spec §22).</summary>
public sealed record JudgementWindows
{
    public double PerfectMs { get; init; } = 25;
    public double GreatMs { get; init; } = 50;
    public double GoodMs { get; init; } = 90;
    public double BadMs { get; init; } = 140;

    /// <summary>Widest window that registers as a hit at all.</summary>
    [JsonIgnore]
    public double HitMs => BadMs;

    /// <summary>Separate, wider window for the release of a hold note.</summary>
    public double HoldTailMs { get; init; } = 120;

    /// <summary>Grace period for releasing a hold slightly early without breaking it.</summary>
    public double HoldReleaseGraceMs { get; init; } = 60;
}

/// <summary>Fraction of a note's value earned by each judgement (spec §23).</summary>
public sealed record AccuracyWeights
{
    public double Perfect { get; init; } = 1.00;
    public double Great { get; init; } = 0.90;
    public double Good { get; init; } = 0.60;
    public double Bad { get; init; } = 0.20;
    public double Miss { get; init; } = 0.00;

    public double Of(Judgement judgement) => judgement switch
    {
        Judgement.Perfect => Perfect,
        Judgement.Great => Great,
        Judgement.Good => Good,
        Judgement.Bad => Bad,
        _ => Miss,
    };
}

/// <summary>Score model constants (spec §25). Score is deliberately independent of PP/Rating.</summary>
public sealed record ScoreConfig
{
    /// <summary>Displayed score is scaled so a theoretical-max play reaches this.</summary>
    public int MaxScore { get; init; } = 1_000_000;

    /// <summary>Share of the score that comes from raw note accuracy vs. combo.</summary>
    public double AccuracyPortion { get; init; } = 0.85;

    [JsonIgnore]
    public double ComboPortion => 1.0 - AccuracyPortion;

    /// <summary>Combo count at which the combo bonus is fully earned.</summary>
    public int ComboBonusFullAt { get; init; } = 400;
}

using Lumen.Core.Balance;

namespace Lumen.Core.Gameplay;

/// <summary>
/// Running score, accuracy and combo for a play (spec §23–25). Score, PP and Rating
/// are separate systems — this is only Score/Accuracy/Combo. PP and performance rating
/// are computed later, in Phase 4, from the final <see cref="PlayResult"/>.
/// </summary>
public sealed class ScoreState
{
    private readonly BalanceConfig _balance;
    private readonly int[] _counts = new int[5];

    private readonly double _maxComboBonus; // Σ over a never-broken combo (theoretical max)

    private double _accuracyPoints;   // Σ weight(judgement)
    private double _comboBonusPoints; // Σ clamped(combo / fullAt)

    public ScoreState(int totalUnits, BalanceConfig balance)
    {
        TotalUnits = Math.Max(1, totalUnits);
        _balance = balance;

        double fullAt = balance.Score.ComboBonusFullAt;
        for (int i = 1; i <= TotalUnits; i++)
        {
            _maxComboBonus += Math.Min(1.0, i / fullAt);
        }
    }

    /// <summary>Total judgeable units in the chart: one per tap, two per hold (head + tail).</summary>
    public int TotalUnits { get; }

    public int JudgedUnits { get; private set; }

    public int Combo { get; private set; }

    public int MaxCombo { get; private set; }

    public int Count(Judgement j) => _counts[(int)j];

    public int Perfect => _counts[(int)Judgement.Perfect];
    public int Great => _counts[(int)Judgement.Great];
    public int Good => _counts[(int)Judgement.Good];
    public int Bad => _counts[(int)Judgement.Bad];
    public int Miss => _counts[(int)Judgement.Miss];

    /// <summary>Percentage over units judged so far (0–100). 100 before the first judgement.</summary>
    public double Accuracy => JudgedUnits == 0 ? 100.0 : _accuracyPoints / JudgedUnits * 100.0;

    /// <summary>Displayed score, scaled so a theoretical-max play reaches <c>MaxScore</c>.</summary>
    public long Score
    {
        get
        {
            ScoreConfig c = _balance.Score;
            double normalized =
                c.AccuracyPortion * (_accuracyPoints / TotalUnits) +
                c.ComboPortion * (_comboBonusPoints / _maxComboBonus);
            return (long)Math.Round(c.MaxScore * normalized);
        }
    }

    public bool FullCombo => JudgedUnits > 0 && Miss == 0 && Bad == 0;

    public bool AllPerfect => JudgedUnits > 0 && JudgedUnits == Perfect;

    public void Register(Judgement judgement)
    {
        _counts[(int)judgement]++;
        JudgedUnits++;
        _accuracyPoints += _balance.Accuracy.Of(judgement);

        if (judgement <= _balance.ComboBreakThreshold)
        {
            Combo++;
            MaxCombo = Math.Max(MaxCombo, Combo);
        }
        else
        {
            Combo = 0;
        }

        double fraction = Math.Min(1.0, (double)Combo / _balance.Score.ComboBonusFullAt);
        _comboBonusPoints += fraction;
    }
}

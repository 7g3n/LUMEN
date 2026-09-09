namespace Lumen.Core;

/// <summary>Hit-quality tiers (spec §22). Ordered best → worst; lower value is better.</summary>
public enum Judgement
{
    Perfect = 0,
    Great = 1,
    Good = 2,
    Bad = 3,
    Miss = 4,
}

public static class JudgementExtensions
{
    public static bool IsHit(this Judgement j) => j != Judgement.Miss;

    public static string Label(this Judgement j) => j switch
    {
        Judgement.Perfect => "PERFECT",
        Judgement.Great => "GREAT",
        Judgement.Good => "GOOD",
        Judgement.Bad => "BAD",
        _ => "MISS",
    };
}

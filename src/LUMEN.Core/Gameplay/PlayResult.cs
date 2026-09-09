using Lumen.Core.Charts;

namespace Lumen.Core.Gameplay;

/// <summary>
/// Immutable summary of a finished play. Score/accuracy/combo only — PP and performance
/// rating are computed from this in Phase 4 and stored alongside it.
/// </summary>
public sealed record PlayResult
{
    public required ChartMeta Chart { get; init; }
    public required long Score { get; init; }
    public required double Accuracy { get; init; }
    public required int MaxCombo { get; init; }
    public required int Perfect { get; init; }
    public required int Great { get; init; }
    public required int Good { get; init; }
    public required int Bad { get; init; }
    public required int Miss { get; init; }
    public required bool FullCombo { get; init; }
    public required bool AllPerfect { get; init; }
    public required DateTime PlayedUtc { get; init; }

    public int TotalNotes => Perfect + Great + Good + Bad + Miss;

    public string Grade => Grades.For(Accuracy, FullCombo, AllPerfect);

    public static PlayResult From(GameplaySession session) => new()
    {
        Chart = session.Chart.Meta,
        Score = session.Score.Score,
        Accuracy = session.Score.Accuracy,
        MaxCombo = session.Score.MaxCombo,
        Perfect = session.Score.Perfect,
        Great = session.Score.Great,
        Good = session.Score.Good,
        Bad = session.Score.Bad,
        Miss = session.Score.Miss,
        FullCombo = session.Score.FullCombo,
        AllPerfect = session.Score.AllPerfect,
        PlayedUtc = DateTime.UtcNow,
    };
}

/// <summary>Letter grades from accuracy (spec §36 shows "A+"). Thresholds are LUMEN's own.</summary>
public static class Grades
{
    public static string For(double accuracy, bool fullCombo, bool allPerfect)
    {
        if (allPerfect) return "PERFECT";
        return accuracy switch
        {
            >= 99.0 => fullCombo ? "S+" : "S",
            >= 96.0 => "A+",
            >= 92.0 => "A",
            >= 87.0 => "B",
            >= 80.0 => "C",
            _ => "D",
        };
    }
}

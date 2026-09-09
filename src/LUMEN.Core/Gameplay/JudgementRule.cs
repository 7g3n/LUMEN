using Lumen.Core.Balance;

namespace Lumen.Core.Gameplay;

/// <summary>Maps a timing error (ms) onto a <see cref="Judgement"/> tier (spec §22).</summary>
public static class JudgementRule
{
    /// <summary>Tap / hold-head judgement from the absolute timing error.</summary>
    public static Judgement ForTap(double errorMs, JudgementWindows w)
    {
        double e = Math.Abs(errorMs);
        if (e <= w.PerfectMs) return Judgement.Perfect;
        if (e <= w.GreatMs) return Judgement.Great;
        if (e <= w.GoodMs) return Judgement.Good;
        if (e <= w.BadMs) return Judgement.Bad;
        return Judgement.Miss;
    }

    /// <summary>Hold-tail judgement — a single wider window (spec §17).</summary>
    public static Judgement ForHoldTail(double errorMs, JudgementWindows w)
    {
        double e = Math.Abs(errorMs);
        if (e <= w.PerfectMs) return Judgement.Perfect;
        if (e <= w.GreatMs) return Judgement.Great;
        if (e <= w.HoldTailMs) return Judgement.Good;
        return Judgement.Miss;
    }

    /// <summary>True if a press this far from the note should count as hitting it at all.</summary>
    public static bool InHitWindow(double errorMs, JudgementWindows w) => Math.Abs(errorMs) <= w.HitMs;
}

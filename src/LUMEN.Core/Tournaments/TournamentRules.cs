using System.Security.Cryptography;
using System.Text;

namespace Lumen.Core.Tournaments;

/// <summary>
/// The rules a tournament runs under (spec: Tournament Rule Set).
///
/// Two things make this more than a settings bag.
///
/// The first is that these are *enforced*, not suggested. A tournament that forbids
/// retries has to actually refuse one, and the settings a player could normally change —
/// their input offset, their note speed — have to stop being theirs to change for the
/// duration. A rule nobody applies is a rule nobody keeps.
///
/// The second is <see cref="Hash"/>. A result recorded against a rule set is only
/// meaningful if you can still tell, afterwards, what those rules were. Storing the hash
/// with every result means a tournament whose rules were edited halfway through cannot
/// quietly pass its earlier matches off as having been played under the new ones.
/// </summary>
public sealed record TournamentRules
{
    /// <summary>Whether score is recorded at all. Off for a purely accuracy-based event.</summary>
    public bool ScoreEnabled { get; init; } = true;

    /// <summary>Whether PP is computed for tournament plays. Never feeds the player's normal total.</summary>
    public bool PpEnabled { get; init; } = true;

    /// <summary>
    /// Whether tournament plays move the player's normal Rating.
    ///
    /// Off by default, and that default is the important one: a tournament is a separate
    /// competition, and a player who enters one should not find their everyday rating
    /// moved by a chart an organiser chose for them under rules they did not pick.
    /// </summary>
    public bool AffectsNormalRating { get; init; }

    /// <summary>How many times a player may play a given match chart. 1 for a real event.</summary>
    public int MaxAttempts { get; init; } = 1;

    public bool RetryAllowed { get; init; }

    public bool PauseAllowed { get; init; }

    /// <summary>Speed, mirror, random and the rest, as one switch.</summary>
    public bool ModifiersAllowed { get; init; }

    public bool MirrorAllowed { get; init; }

    public bool RandomAllowed { get; init; }

    /// <summary>Autoplay. Off in every real event; a switch because test events exist.</summary>
    public bool AutoplayAllowed { get; init; }

    /// <summary>
    /// Note speed every player uses, or null to let them keep their own.
    ///
    /// Note speed is a reading preference rather than an advantage, so the honest default
    /// is to leave it alone; an organiser who wants everyone on identical screens can
    /// still say so.
    /// </summary>
    public double? FixedNoteSpeed { get; init; }

    /// <summary>
    /// Input offset forced on every player, or null to let them keep their calibration.
    ///
    /// Null is the right default and it is worth saying why: a player's input offset
    /// corrects for their hardware, not their skill. Forcing it to zero does not level the
    /// field, it tilts it towards whoever happens to own a low-latency monitor.
    /// </summary>
    public double? FixedInputOffsetMs { get; init; }

    /// <summary>Audio offset forced on every player, on the same reasoning.</summary>
    public double? FixedAudioOffsetMs { get; init; }

    /// <summary>Matches are best of this many charts. Odd numbers only.</summary>
    public int BestOf { get; init; } = 1;

    /// <summary>How the chart for each match is chosen.</summary>
    public SongPickMode PickMode { get; init; } = SongPickMode.Random;

    /// <summary>What a tie is broken on, in order.</summary>
    public IReadOnlyList<TieBreakCriterion> TieBreak { get; init; } = DefaultTieBreak;

    public static readonly IReadOnlyList<TieBreakCriterion> DefaultTieBreak = new[]
    {
        TieBreakCriterion.Score,
        TieBreakCriterion.Accuracy,
        TieBreakCriterion.MissCount,
        TieBreakCriterion.MaxCombo,
        TieBreakCriterion.PerfectCount,
        TieBreakCriterion.SubmissionTime,
    };

    /// <summary>What a serious event looks like: one attempt, nothing to lean on.</summary>
    public static readonly TournamentRules Official = new();

    /// <summary>
    /// A relaxed set for a tournament among friends, where getting through the evening
    /// matters more than the strictness of it.
    /// </summary>
    public static readonly TournamentRules Casual = new()
    {
        MaxAttempts = 3,
        RetryAllowed = true,
        PauseAllowed = true,
        ModifiersAllowed = true,
        MirrorAllowed = true,
    };

    /// <summary>
    /// A stable fingerprint of these rules.
    ///
    /// Written out field by field rather than serialised, so that adding a field is a
    /// deliberate act: a hash that silently changes shape would invalidate every result
    /// recorded before it, and one that silently ignores a new field would let two
    /// different rule sets claim to be the same.
    /// </summary>
    public string Hash()
    {
        var text = new StringBuilder()
            .Append("v1|")
            .Append(ScoreEnabled).Append('|')
            .Append(PpEnabled).Append('|')
            .Append(AffectsNormalRating).Append('|')
            .Append(MaxAttempts).Append('|')
            .Append(RetryAllowed).Append('|')
            .Append(PauseAllowed).Append('|')
            .Append(ModifiersAllowed).Append('|')
            .Append(MirrorAllowed).Append('|')
            .Append(RandomAllowed).Append('|')
            .Append(AutoplayAllowed).Append('|')
            .Append(FixedNoteSpeed?.ToString("R") ?? "-").Append('|')
            .Append(FixedInputOffsetMs?.ToString("R") ?? "-").Append('|')
            .Append(FixedAudioOffsetMs?.ToString("R") ?? "-").Append('|')
            .Append(BestOf).Append('|')
            .Append((int)PickMode).Append('|')
            .Append(string.Join(",", TieBreak.Select(t => (int)t)))
            .ToString();

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant()[..16];
    }

    /// <summary>
    /// Problems that would stop this rule set from running a tournament, in plain words.
    /// Empty means it is usable.
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        if (MaxAttempts < 1)
        {
            problems.Add("A player has to be allowed at least one attempt.");
        }

        if (BestOf < 1)
        {
            problems.Add("A match has to be at least one chart long.");
        }
        else if (BestOf % 2 == 0)
        {
            problems.Add("Best-of has to be an odd number, or a match can end level.");
        }

        if (TieBreak.Count == 0)
        {
            problems.Add("There has to be at least one tie-break, or a draw cannot be resolved.");
        }

        if (TieBreak.Distinct().Count() != TieBreak.Count)
        {
            problems.Add("The same tie-break is listed twice.");
        }

        if (!ScoreEnabled && TieBreak.FirstOrDefault() == TieBreakCriterion.Score)
        {
            problems.Add("Score cannot break ties in a tournament that does not record it.");
        }

        if (FixedNoteSpeed is { } speed && (speed < 1 || speed > 20))
        {
            problems.Add("A fixed note speed has to be between 1 and 20.");
        }

        return problems;
    }

    public bool IsUsable => Problems().Count == 0;
}

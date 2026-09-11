namespace Lumen.Core.Tournaments;

/// <summary>
/// How a tournament decides who wins (spec: Tournament Mode).
/// </summary>
public enum TournamentFormat
{
    /// <summary>Lose once and you are out.</summary>
    SingleElimination = 0,

    /// <summary>A losers' bracket, so one bad match is not the end of a run.</summary>
    DoubleElimination = 1,

    /// <summary>Everybody plays the same charts; the ranking is the result.</summary>
    ScoreAttack = 2,
}

/// <summary>Where a tournament is in its life.</summary>
public enum TournamentStatus
{
    /// <summary>Being set up. Participants and songs can still change.</summary>
    Draft = 0,

    /// <summary>Running. The bracket is fixed.</summary>
    Running = 1,

    /// <summary>Finished, with a winner.</summary>
    Complete = 2,

    /// <summary>Abandoned. Kept rather than deleted, because the log of it is still true.</summary>
    Cancelled = 3,
}

/// <summary>
/// A match's progress. The order is the order it happens in, which is what
/// <see cref="MatchFlow"/> uses to refuse a jump.
/// </summary>
public enum MatchStatus
{
    /// <summary>Created, but at least one seat is still empty.</summary>
    Pending = 0,

    /// <summary>Both players known, no song picked.</summary>
    Waiting = 1,

    /// <summary>A song is chosen; the players have not confirmed.</summary>
    SongSelected = 2,

    /// <summary>Both ready. The next step is playing.</summary>
    Ready = 3,

    /// <summary>Being played.</summary>
    Playing = 4,

    /// <summary>Results are in and waiting for the organiser.</summary>
    ResultPending = 5,

    /// <summary>Confirmed, with a winner.</summary>
    Complete = 6,

    /// <summary>Nobody played it — a bye, or both players gone.</summary>
    Void = 7,
}

/// <summary>Which half of a double-elimination bracket a round belongs to.</summary>
public enum BracketSide
{
    /// <summary>The only bracket in a single-elimination tournament.</summary>
    Winners = 0,

    Losers = 1,

    /// <summary>Winners' champion against losers' champion.</summary>
    Grand = 2,
}

/// <summary>Whether a participant is still in it.</summary>
public enum ParticipantStatus
{
    Active = 0,
    Eliminated = 1,

    /// <summary>Removed by the organiser. Their played matches stand; their future ones do not.</summary>
    Disqualified = 2,

    /// <summary>Withdrew.</summary>
    Withdrawn = 3,
}

/// <summary>How the chart for a match is chosen.</summary>
public enum SongPickMode
{
    /// <summary>The organiser names it in advance.</summary>
    Fixed = 0,

    /// <summary>Drawn from the pool, reproducibly, from the tournament's seed.</summary>
    Random = 1,

    /// <summary>A player picks.</summary>
    PlayerPick = 2,

    /// <summary>The organiser picks at the time.</summary>
    OrganizerPick = 3,
}

/// <summary>
/// What a tie is broken on, in the order it is tried (spec: Tie Break).
/// </summary>
public enum TieBreakCriterion
{
    Score = 0,
    Accuracy = 1,

    /// <summary>Fewer misses wins.</summary>
    MissCount = 2,

    MaxCombo = 3,

    /// <summary>More PERFECTs wins.</summary>
    PerfectCount = 4,

    /// <summary>Whoever submitted first. A last resort that always decides.</summary>
    SubmissionTime = 5,
}

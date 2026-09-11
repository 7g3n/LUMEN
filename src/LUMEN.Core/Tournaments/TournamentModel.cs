namespace Lumen.Core.Tournaments;

/// <summary>
/// A tournament (spec: Tournament Information).
///
/// <see cref="GameVersion"/> is recorded because an event outlives the build it was run
/// on, and "which version was this played under" stops being answerable the moment the
/// game updates. Same reasoning as <see cref="RuleHash"/>: the facts that make a result
/// mean something have to travel with the result.
/// </summary>
public sealed record Tournament
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    /// <summary>Who is running it. Free text — an organiser need not be a player here.</summary>
    public string Organizer { get; init; } = "";

    public required TournamentFormat Format { get; init; }

    public TournamentStatus Status { get; init; } = TournamentStatus.Draft;

    public required TournamentRules Rules { get; init; }

    /// <summary>The rule set as it was when the tournament started, for later audit.</summary>
    public string RuleHash { get; init; } = "";

    /// <summary>The build that ran it.</summary>
    public string GameVersion { get; init; } = "";

    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    public DateTime? StartedUtc { get; init; }

    public DateTime? FinishedUtc { get; init; }

    /// <summary>
    /// Seed for every random choice the tournament makes, so a draw can be re-derived
    /// rather than taken on trust. An organiser accused of picking a favourable chart can
    /// point at the seed.
    /// </summary>
    public int RandomSeed { get; init; }

    /// <summary>The player who won, once there is one.</summary>
    public Guid? WinnerPlayerId { get; init; }
}

/// <summary>
/// Somebody entered in a tournament.
///
/// <see cref="DisplayName"/> is a snapshot taken when they entered, not a live read.
/// Players can rename themselves, and a bracket that silently relabels a completed match
/// is a bracket that no longer shows what happened.
/// </summary>
public sealed record TournamentParticipant
{
    public required Guid TournamentId { get; init; }

    public required Guid PlayerId { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>1 is the top seed. Seeds decide who meets whom in the first round.</summary>
    public int Seed { get; init; }

    public ParticipantStatus Status { get; init; } = ParticipantStatus.Active;

    public DateTime JoinedUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// A chart in the tournament's pool.
///
/// The hashes are what make "the chart that was played" a checkable claim. An organiser
/// who edits a chart mid-event produces a different hash, and the results recorded before
/// the edit stay attached to the version they were actually set on.
/// </summary>
public sealed record TournamentSong
{
    public required Guid TournamentId { get; init; }

    public required string ChartKey { get; init; }

    /// <summary>Title and difficulty as they were when the pool was built.</summary>
    public required string Title { get; init; }

    public string DifficultyName { get; init; } = "";

    public double Level { get; init; }

    /// <summary>The chart's identity hash at the time it entered the pool.</summary>
    public string ChartHash { get; init; } = "";

    /// <summary>The audio's content hash, so a swapped song is visible too.</summary>
    public string AudioHash { get; init; } = "";

    /// <summary>Free-text grouping — speed, technical, stamina — for a structured pool.</summary>
    public string Category { get; init; } = "";
}

/// <summary>One round of a bracket.</summary>
public sealed record TournamentRound
{
    public required Guid Id { get; init; }

    public required Guid TournamentId { get; init; }

    /// <summary>0-based, in playing order.</summary>
    public required int Index { get; init; }

    /// <summary>"Quarter Final", "Losers Round 2", "Grand Final".</summary>
    public required string Name { get; init; }

    public BracketSide Side { get; init; } = BracketSide.Winners;

    public bool IsComplete { get; init; }
}

/// <summary>
/// One match.
///
/// A seat may be empty — <c>null</c> — which is how byes and not-yet-decided slots are
/// represented. A bracket is built whole, before anybody has played, so most of it starts
/// out unpopulated and fills in as results come back.
/// </summary>
public sealed record TournamentMatch
{
    public required Guid Id { get; init; }

    public required Guid TournamentId { get; init; }

    public required Guid RoundId { get; init; }

    /// <summary>Position within the round, 0-based. Decides who feeds into whom.</summary>
    public required int Slot { get; init; }

    public Guid? Player1Id { get; init; }

    public Guid? Player2Id { get; init; }

    public MatchStatus Status { get; init; } = MatchStatus.Pending;

    public Guid? WinnerPlayerId { get; init; }

    /// <summary>Charts chosen for this match, in playing order.</summary>
    public IReadOnlyList<string> SelectedChartKeys { get; init; } = Array.Empty<string>();

    public int BestOf { get; init; } = 1;

    public DateTime? StartedUtc { get; init; }

    public DateTime? CompletedUtc { get; init; }

    /// <summary>Both seats filled, so the match can be played.</summary>
    public bool HasBothPlayers => Player1Id is not null && Player2Id is not null;

    /// <summary>
    /// Exactly one seat filled. That player advances without playing — which is what a bye
    /// is, and why a bracket with a number of entrants that is not a power of two still
    /// works.
    /// </summary>
    public bool IsBye => Player1Id is null ^ Player2Id is null;

    public bool Involves(Guid playerId) => Player1Id == playerId || Player2Id == playerId;

    /// <summary>The other player, given one of them.</summary>
    public Guid? Opponent(Guid playerId) =>
        Player1Id == playerId ? Player2Id
        : Player2Id == playerId ? Player1Id
        : null;
}

/// <summary>
/// What one player scored on one chart of one match.
///
/// Everything needed to check the result later travels with it: which chart, which build,
/// which rules, and the replay. A number in a table proves nothing on its own; a number
/// with the conditions it was set under can at least be argued about.
/// </summary>
public sealed record TournamentMatchResult
{
    public required Guid Id { get; init; }

    public required Guid MatchId { get; init; }

    public required Guid PlayerId { get; init; }

    public required string ChartKey { get; init; }

    /// <summary>Which chart of a best-of series this was, 0-based.</summary>
    public int GameIndex { get; init; }

    public long Score { get; init; }

    public double Accuracy { get; init; }

    public int MaxCombo { get; init; }

    public int Perfect { get; init; }

    public int Great { get; init; }

    public int Good { get; init; }

    public int Bad { get; init; }

    public int Miss { get; init; }

    /// <summary>PP this play would be worth. Never added to the player's normal total.</summary>
    public double Pp { get; init; }

    public bool FullCombo { get; init; }

    public bool AllPerfect { get; init; }

    /// <summary>The recording, when there is one — the evidence behind the number.</summary>
    public Guid? ReplayId { get; init; }

    public string ChartHash { get; init; } = "";

    public string GameVersion { get; init; } = "";

    public string RuleHash { get; init; } = "";

    public DateTime SubmittedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When the organiser accepted it. Unconfirmed results are visible but do not decide
    /// anything, which is what keeps a match from being settled by whoever submits first.
    /// </summary>
    public DateTime? ConfirmedUtc { get; init; }

    public bool IsConfirmed => ConfirmedUtc is not null;
}

/// <summary>
/// Something that happened, written down (spec: Tournament Event Log).
///
/// The log is append-only and covers every action that changes the tournament, so that
/// afterwards it is possible to say not just who won but how the event got there. It is
/// the difference between a result and a result somebody can check.
/// </summary>
public sealed record TournamentEvent
{
    public required Guid Id { get; init; }

    public required Guid TournamentId { get; init; }

    public required string Type { get; init; }

    /// <summary>Who did it: a player id, or empty for the organiser running the event.</summary>
    public Guid? ActorPlayerId { get; init; }

    /// <summary>Detail, as JSON. Shape depends on <see cref="Type"/>.</summary>
    public string Payload { get; init; } = "{}";

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>The event names the tournament writes. Constants, so a typo cannot invent one.</summary>
public static class TournamentEventTypes
{
    public const string Created = "tournament.created";
    public const string ParticipantJoined = "participant.joined";
    public const string ParticipantRemoved = "participant.removed";
    public const string ParticipantDisqualified = "participant.disqualified";
    public const string SongAdded = "song.added";
    public const string SongRemoved = "song.removed";
    public const string Started = "tournament.started";
    public const string BracketGenerated = "bracket.generated";
    public const string MatchCreated = "match.created";
    public const string SongSelected = "match.song_selected";
    public const string MatchStarted = "match.started";
    public const string ResultSubmitted = "result.submitted";
    public const string ResultConfirmed = "result.confirmed";
    public const string MatchCompleted = "match.completed";
    public const string RoundCompleted = "round.completed";
    public const string PlayerAdvanced = "player.advanced";
    public const string Completed = "tournament.completed";
    public const string Cancelled = "tournament.cancelled";
}

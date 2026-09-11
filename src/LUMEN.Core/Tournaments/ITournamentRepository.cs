namespace Lumen.Core.Tournaments;

/// <summary>
/// Everything a tournament needs from storage (spec: 将来のOnline Tournamentを考慮).
///
/// An interface rather than a class for one concrete reason: today the only implementation
/// keeps tournaments in the local SQLite database, and the point of running them locally
/// first is that nothing about the rules, the bracket or the results depends on that. When
/// a remote implementation arrives it slots in here, and the screens, the bracket builder
/// and the tie-break rules do not learn about it.
///
/// Everything is synchronous. The local store is a file on the same disk, and an async
/// surface adopted now purely in case of a server later would cost every caller a state
/// machine it does not need. A remote implementation can block on its own transport, or
/// this can grow an async twin at the point one exists.
/// </summary>
public interface ITournamentRepository
{
    // --- tournaments ---

    Tournament Create(Tournament tournament);

    Tournament? Get(Guid tournamentId);

    /// <summary>Most recently created first.</summary>
    IReadOnlyList<Tournament> All();

    void Update(Tournament tournament);

    // --- participants ---

    void AddParticipant(TournamentParticipant participant);

    void RemoveParticipant(Guid tournamentId, Guid playerId);

    void UpdateParticipant(TournamentParticipant participant);

    IReadOnlyList<TournamentParticipant> Participants(Guid tournamentId);

    // --- song pool ---

    void AddSong(TournamentSong song);

    void RemoveSong(Guid tournamentId, Guid chartId);

    IReadOnlyList<TournamentSong> Songs(Guid tournamentId);

    // --- bracket ---

    /// <summary>
    /// Writes a whole bracket at once. All of it or none of it: a half-written bracket is
    /// a tournament nobody can run and nobody can repair.
    /// </summary>
    void SaveBracket(Guid tournamentId, Bracket bracket);

    IReadOnlyList<TournamentRound> Rounds(Guid tournamentId);

    IReadOnlyList<TournamentMatch> Matches(Guid tournamentId);

    TournamentMatch? GetMatch(Guid matchId);

    void UpdateMatch(TournamentMatch match);

    // --- results ---

    void SubmitResult(TournamentMatchResult result);

    void ConfirmResult(Guid resultId, DateTime confirmedUtc);

    IReadOnlyList<TournamentMatchResult> Results(Guid tournamentId);

    IReadOnlyList<TournamentMatchResult> ResultsForMatch(Guid matchId);

    // --- audit ---

    void Record(TournamentEvent tournamentEvent);

    /// <summary>Oldest first, which is the order it happened in.</summary>
    IReadOnlyList<TournamentEvent> Events(Guid tournamentId);

    /// <summary>
    /// Removes a tournament and everything under it. For a draft somebody abandoned —
    /// a tournament that has been run is cancelled rather than deleted, because the log of
    /// what happened stays true even when the event does not.
    /// </summary>
    void Delete(Guid tournamentId);
}

namespace Lumen.Core.Library;

/// <summary>
/// The chart library index (spec §59, §62, §93). Rows mirror the <c>.lumenchart</c> files
/// on disk; the files stay the source of truth, and a row whose file has gone is dropped
/// rather than shown and then failing to load.
/// </summary>
public interface ILibraryRepository
{
    void Upsert(LibraryChart chart);

    LibraryChart? Get(string chartKey);

    IReadOnlyList<LibraryChart> All();

    /// <summary>Chart key → file path, for the scanner's prune pass.</summary>
    IReadOnlyDictionary<string, string> AllPaths();

    void Remove(string chartKey);

    int Count();

    // --- favourites (spec §62) ---

    bool IsFavorite(Guid playerId, string chartKey);

    void SetFavorite(Guid playerId, string chartKey, bool favorite);

    IReadOnlyCollection<string> Favorites(Guid playerId);
}

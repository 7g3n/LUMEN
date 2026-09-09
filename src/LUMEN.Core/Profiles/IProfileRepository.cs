namespace Lumen.Core.Profiles;

/// <summary>
/// Persistence for <see cref="Profile"/>s. Implemented in LUMEN.Data; game code depends
/// only on this interface (spec §70). The data model supports many profiles even while
/// the UI exposes one (spec §8).
/// </summary>
public interface IProfileRepository
{
    /// <summary>
    /// Creates a profile with a fresh UUID. <paramref name="displayName"/> must already
    /// pass <see cref="PlayerName.Validate"/>; it is normalized before storage.
    /// </summary>
    Profile Create(string displayName);

    Profile? Get(Guid playerId);

    IReadOnlyList<Profile> GetAll();

    /// <summary>Most recently played profile, or null if none exist.</summary>
    Profile? GetMostRecent();

    int Count();

    /// <summary>Changes only the display name. The UUID and every score link are untouched (§6).</summary>
    void Rename(Guid playerId, string newDisplayName);

    void UpdateLastPlayed(Guid playerId, DateTime utc);

    void AddPlayTime(Guid playerId, long milliseconds);

    void Delete(Guid playerId);
}

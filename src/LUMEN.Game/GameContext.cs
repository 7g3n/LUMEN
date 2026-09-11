using Lumen.Core.Balance;
using Lumen.Core.Diagnostics;
using Lumen.Core.Profiles;
using Lumen.Core.Scores;
using Lumen.Core.Settings;
using Lumen.Data;
using Lumen.Data.Library;
using Lumen.Game.Config;

namespace Lumen.Game;

/// <summary>
/// Services shared across screens. Constructed once in <see cref="LumenGame"/> and passed
/// down; screens never reach for globals.
/// </summary>
public sealed class GameContext
{
    public required LumenPaths Paths { get; init; }

    public required Database Database { get; init; }

    public required IProfileRepository Profiles { get; init; }

    public required ISettingsRepository Settings { get; init; }

    public required IScoreRepository Scores { get; init; }

    public required LibraryService Library { get; init; }

    public required AppMetaStore AppMeta { get; init; }

    public required DisplayConfig Display { get; init; }

    public required BalanceConfig Balance { get; init; }

    public required Session Session { get; init; }

    /// <summary>Requests a clean shutdown of the game (wired to <c>Game.Exit</c>).</summary>
    public required Action RequestExit { get; init; }
}

/// <summary>The currently selected profile. One at a time in the UI; many in the data (§8).</summary>
public sealed class Session
{
    private readonly Database _db;
    private readonly IProfileRepository _profiles;
    private readonly AppMetaStore _appMeta;

    public Session(Database db, IProfileRepository profiles, AppMetaStore appMeta)
    {
        _db = db;
        _profiles = profiles;
        _appMeta = appMeta;
    }

    public Profile? ActiveProfile { get; private set; }

    public bool HasProfile => ActiveProfile is not null;

    public Guid ActivePlayerId =>
        ActiveProfile?.PlayerId ?? throw new InvalidOperationException("No active profile.");

    /// <summary>Loads the previously active profile (or the most recent) at startup.</summary>
    public void Restore()
    {
        Profile? profile = _appMeta.ActivePlayerId is { } id ? _profiles.Get(id) : null;
        profile ??= _profiles.GetMostRecent();
        if (profile is not null)
        {
            SetActive(profile);
        }
    }

    public void SetActive(Profile profile)
    {
        ActiveProfile = profile;
        _db.InTransaction(() =>
        {
            _appMeta.ActivePlayerId = profile.PlayerId;
            _profiles.UpdateLastPlayed(profile.PlayerId, DateTime.UtcNow);
        });
        Log.Info($"active profile: {profile.DisplayName} ({profile.PlayerId})");
    }

    /// <summary>Re-reads the active profile from storage (after a rename, say).</summary>
    public void Refresh()
    {
        if (ActiveProfile is { } current)
        {
            ActiveProfile = _profiles.Get(current.PlayerId) ?? ActiveProfile;
        }
    }
}

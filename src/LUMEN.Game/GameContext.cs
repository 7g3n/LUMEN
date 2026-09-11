using Lumen.Core.Balance;
using Lumen.Core.Diagnostics;
using Lumen.Core.Profiles;
using Lumen.Core.Scores;
using Lumen.Core.Settings;
using Lumen.Data;
using Lumen.Data.Achievements;
using Lumen.Data.Backup;
using Lumen.Data.Library;
using Lumen.Data.Packages;
using Lumen.Data.Repositories;
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

    public required PackageService Packages { get; init; }

    public required ChartVersionRepository ChartVersions { get; init; }

    public required ReplayRepository Replays { get; init; }

    public required AchievementService Achievements { get; init; }

    public required BackupService Backups { get; init; }

    /// <summary>
    /// Running tournaments (spec: Tournament Mode). Screens go through this and never
    /// write to the tournament tables themselves: every action here also writes an audit
    /// event and applies the match state machine, and a screen that reached past it would
    /// produce a tournament whose log no longer explains it.
    /// </summary>
    public required Lumen.Data.Tournaments.TournamentService Tournaments { get; init; }

    /// <summary>
    /// The tournament store underneath that service. Screens need it for the one thing the
    /// service deliberately does not offer — deleting a draft nobody ever ran.
    /// </summary>
    public required Lumen.Core.Tournaments.ITournamentRepository TournamentStore { get; init; }

    /// <summary>
    /// Frame-time bookkeeping (§68). Screens reset it at the point a measurement should
    /// begin — gameplay does so when the countdown ends, so the numbers describe the song
    /// rather than the loading that preceded it.
    /// </summary>
    public required Engine.FrameProfiler Frames { get; init; }

    /// <summary>
    /// The accessibility switches in force. Re-read through <see cref="RefreshAccessibility"/>
    /// whenever they change or the profile does, so every screen can simply consult this.
    /// </summary>
    public Lumen.Core.Accessibility.AccessibilityOptions Accessibility { get; private set; } =
        Lumen.Core.Accessibility.AccessibilityOptions.Default;

    public void RefreshAccessibility()
    {
        Accessibility = Session.HasProfile
            ? Config.AccessibilitySettings.Load(Settings, Session.ActivePlayerId)
            : Lumen.Core.Accessibility.AccessibilityOptions.Default;

        Config.AccessibilitySettings.Apply(Accessibility);
    }

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

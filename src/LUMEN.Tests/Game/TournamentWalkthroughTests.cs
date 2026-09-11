using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Lumen.Core.Balance;
using Lumen.Core.Charts;
using Lumen.Core.Tournaments;
using Lumen.Data;
using Lumen.Data.Achievements;
using Lumen.Data.Backup;
using Lumen.Data.Library;
using Lumen.Data.Packages;
using Lumen.Data.Repositories;
using Lumen.Game;
using Lumen.Game.Config;
using Lumen.Game.Screens;
using Lumen.Game.Screens.Tournaments;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Lumen.Tests.Game;

/// <summary>
/// A tournament set up and run through the real screens, pressing only keys those screens
/// name (spec: Tournament Mode UI).
///
/// The service tests already prove a tournament runs; this proves somebody can actually
/// get to one. Playing a match needs a graphics device and an audio device, so the
/// walkthrough stops at the point the match screen hands over to gameplay — which is
/// exactly where the covered paths resume.
/// </summary>
public class TournamentWalkthroughTests : IDisposable
{
    private readonly string _exeDir;
    private readonly LumenPaths _paths;
    private readonly Database _db;
    private readonly GameContext _context;
    private readonly ScreenManager _screens;

    private KeyboardState _previousKeyboard = new();

    public TournamentWalkthroughTests()
    {
        _exeDir = Path.Combine(Path.GetTempPath(), "lumen-tui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_exeDir);
        File.WriteAllText(Path.Combine(_exeDir, LumenPaths.PortableSentinelFileName), "");

        _paths = LumenPaths.Resolve(_exeDir);
        _paths.EnsureCreated();

        _db = new Database(_paths.DatabaseFile);
        _db.Open();

        var profiles = new ProfileRepository(_db);
        var settings = new SettingsRepository(_db);
        var appMeta = new AppMetaStore(_db);
        var session = new Session(_db, profiles, appMeta);
        var library = new LibraryService(new LibraryRepository(_db), _paths);
        var scores = new ScoreRepository(_db, BalanceConfig.Default);
        var tournamentStore = new TournamentRepository(_db);

        _context = new GameContext
        {
            Paths = _paths,
            Database = _db,
            Profiles = profiles,
            Settings = settings,
            Scores = scores,
            Library = library,
            Packages = new PackageService(_paths, library),
            ChartVersions = new ChartVersionRepository(_db),
            Replays = new ReplayRepository(_db, _paths.Replays),
            Achievements = new AchievementService(
                scores, profiles, library.Charts, new AchievementRepository(_db)),
            Backups = new BackupService(_db, _paths, appMeta, library),
            Tournaments = new Lumen.Data.Tournaments.TournamentService(tournamentStore),
            TournamentStore = tournamentStore,
            Frames = new Lumen.Game.Engine.FrameProfiler(),
            AppMeta = appMeta,
            Display = new DisplayConfig(),
            Balance = BalanceConfig.Default,
            Session = session,
            RequestExit = () => { },
        };

        // Four people who share a computer already have four profiles — which is exactly
        // what a local tournament runs on.
        foreach (string name in new[] { "Nagisa", "Rin", "Kaito", "Mei" })
        {
            profiles.Create(name);
        }

        session.SetActive(profiles.GetAll().First(p => p.DisplayName == "Nagisa"));

        // A chart to compete on.
        Lumen.Game.Content.TestContent.EnsureInstalled(_paths);
        library.Scan();

        _screens = new ScreenManager(_context);
    }

    public void Dispose()
    {
        _screens.Top?.OnExit();
        _db.Dispose();
        try { Directory.Delete(_exeDir, recursive: true); } catch { /* best effort */ }
    }

    // --- driving ---

    private void Frame(params Keys[] held)
    {
        var keyboard = new KeyboardState(held);
        _screens.Update(new InputFrame
        {
            Keyboard = keyboard,
            PreviousKeyboard = _previousKeyboard,
            Mouse = new MouseState(),
            PreviousMouse = new MouseState(),
            TypedText = "",
            DeltaSeconds = 1 / 60.0,
        });
        _previousKeyboard = keyboard;
    }

    private void Wait(double seconds)
    {
        for (double t = 0; t < seconds; t += 1 / 60.0)
        {
            Frame();
        }
    }

    private void Press(Keys key)
    {
        Frame(key);
        Frame();
        Wait(0.4);
    }

    /// <summary>Sets a tournament up through the screens and draws its bracket.</summary>
    private Tournament RunCreateScreen(int players = 4)
    {
        _screens.SetRoot(new MainMenuScreen());

        int steps = Array.IndexOf(MainMenuScreen.Actions, "TOURNAMENT");
        steps.Should().BeGreaterThan(0, "the hub must offer tournaments");

        for (int i = 0; i < steps; i++)
        {
            Press(Keys.Down);
        }

        Press(Keys.Enter);
        _screens.Top.Should().BeOfType<TournamentHubScreen>();

        Press(Keys.Enter); // CREATE TOURNAMENT
        _screens.Top.Should().BeOfType<CreateTournamentScreen>();

        // Name is already filled in; Tab down to the players.
        Press(Keys.Tab); // format
        Press(Keys.Tab); // rules
        Press(Keys.Tab); // players

        // The signed-in profile is already picked, so add the rest.
        for (int i = 1; i < players; i++)
        {
            Press(Keys.Right);
            Press(Keys.Space);
        }

        Press(Keys.Tab); // songs
        Press(Keys.Space);
        Press(Keys.Tab); // the button
        Press(Keys.Enter);

        _screens.Top.Should().BeOfType<TournamentDashboardScreen>(
            "drawing the bracket should land on the dashboard");

        return _context.Tournaments.All().Single();
    }

    // --- the walkthrough ---

    [Fact]
    public void A_tournament_can_be_set_up_and_drawn_entirely_from_the_menu()
    {
        Tournament tournament = RunCreateScreen();

        tournament.Status.Should().Be(TournamentStatus.Running);
        tournament.Format.Should().Be(TournamentFormat.SingleElimination);

        _context.Tournaments.Participants(tournament.Id).Should().HaveCount(4);
        _context.Tournaments.Songs(tournament.Id).Should().HaveCount(1);
        _context.Tournaments.Matches(tournament.Id).Should().HaveCount(3);
    }

    [Fact]
    public void The_organiser_is_entered_without_having_to_pick_themselves()
    {
        Tournament tournament = RunCreateScreen(players: 2);

        _context.Tournaments.Participants(tournament.Id)
            .Should().Contain(p => p.PlayerId == _context.Session.ActivePlayerId);
    }

    /// <summary>
    /// The chart in the pool is recorded with the hash it had at the time, which is what
    /// makes "the chart that was played" checkable afterwards.
    /// </summary>
    [Fact]
    public void The_song_pool_records_the_chart_as_it_was()
    {
        Tournament tournament = RunCreateScreen(players: 2);

        TournamentSong song = _context.Tournaments.Songs(tournament.Id).Single();

        song.ChartKey.Should().NotBeEmpty();
        song.ChartHash.Should().Be(song.ChartKey);
        song.Title.Should().NotBeEmpty();
    }

    [Fact]
    public void A_tournament_appears_in_the_hub_afterwards()
    {
        RunCreateScreen(players: 2);

        // Back out to the hub the way a player would.
        Press(Keys.Escape);

        _screens.Top.Should().BeOfType<TournamentHubScreen>();
        _context.Tournaments.All().Should().ContainSingle();
    }

    /// <summary>
    /// The dashboard into a match, a chart locked in, and both players marked ready — the
    /// whole of the organiser's job up to somebody sitting down to play.
    /// </summary>
    [Fact]
    public void A_match_can_be_opened_and_set_up_from_the_dashboard()
    {
        Tournament tournament = RunCreateScreen(players: 2);

        Press(Keys.Enter); // open the only playable match
        _screens.Top.Should().BeOfType<TournamentMatchScreen>();

        Press(Keys.Enter); // lock the chart in

        TournamentMatch match = _context.Tournaments.Matches(tournament.Id).Single();
        match.SelectedChartKeys.Should().ContainSingle();
        match.Status.Should().Be(MatchStatus.SongSelected);

        Press(Keys.Enter); // both ready

        _context.Tournaments.Matches(tournament.Id).Single()
            .Status.Should().Be(MatchStatus.Ready);
    }

    /// <summary>
    /// The promise the whole separation exists for, checked at the level somebody would
    /// actually worry about it: running a tournament leaves their normal record alone.
    /// </summary>
    [Fact]
    public void Setting_up_and_running_a_tournament_leaves_normal_scores_untouched()
    {
        Guid player = _context.Session.ActivePlayerId;

        Tournament tournament = RunCreateScreen(players: 2);

        Press(Keys.Enter);
        Press(Keys.Enter);
        Press(Keys.Enter);

        // A result arrives the way the match screen would record one.
        TournamentMatch match = _context.Tournaments.Matches(tournament.Id).Single();
        _context.Tournaments.StartMatch(match.Id);

        _context.Tournaments.SubmitResult(new TournamentMatchResult
        {
            Id = Guid.NewGuid(),
            MatchId = match.Id,
            PlayerId = player,
            ChartKey = match.SelectedChartKeys[0],
            Score = 990_000,
            Accuracy = 99.5,
        });

        _context.Scores.GetSummary(_context.Session.ActiveProfile!).PlayCount
            .Should().Be(0, "a tournament play is not a normal play");
        _context.Scores.GetSummary(_context.Session.ActiveProfile!).Rating
            .Should().Be(0, "and it must not have moved the rating");
    }

    [Fact]
    public void A_draft_nobody_ran_can_be_removed_from_the_hub()
    {
        Tournament tournament = _context.Tournaments.Create(
            "Abandoned", TournamentFormat.SingleElimination, TournamentRules.Official);

        _screens.SetRoot(new TournamentHubScreen());

        _context.Tournaments.All().Should().ContainSingle();

        Press(Keys.Delete);

        _context.Tournaments.All().Should().BeEmpty(
            "a draft nobody ever ran leaves nothing worth keeping");
    }

    /// <summary>
    /// A tournament that has been run is cancelled rather than deleted: the record of what
    /// happened stays true even when the event does not.
    /// </summary>
    [Fact]
    public void A_tournament_that_was_played_is_cancelled_rather_than_deleted()
    {
        Tournament tournament = RunCreateScreen(players: 2);

        Press(Keys.Escape);
        _screens.Top.Should().BeOfType<TournamentHubScreen>();

        Press(Keys.Delete);

        Tournament? after = _context.Tournaments.Get(tournament.Id);
        after.Should().NotBeNull();
        after!.Status.Should().Be(TournamentStatus.Cancelled);
        _context.Tournaments.Events(tournament.Id).Should().NotBeEmpty();
    }

    [Fact]
    public void The_rules_explainer_is_reachable_and_leaves_cleanly()
    {
        _screens.SetRoot(new TournamentHubScreen());

        // With nothing in the list the actions have focus already.
        Press(Keys.Down);   // HOW TOURNAMENTS WORK
        Press(Keys.Enter);

        _screens.Top.Should().BeOfType<TournamentRulesScreen>();

        Press(Keys.Escape);
        _screens.Top.Should().BeOfType<TournamentHubScreen>();
    }
}

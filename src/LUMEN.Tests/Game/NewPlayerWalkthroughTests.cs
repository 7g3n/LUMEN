using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Lumen.Core;
using Lumen.Core.Balance;
using Lumen.Data;
using Lumen.Data.Achievements;
using Lumen.Data.Backup;
using Lumen.Data.Library;
using Lumen.Data.Packages;
using Lumen.Data.Repositories;
using Lumen.Game;
using Lumen.Game.Config;
using Lumen.Game.Screens;
using Lumen.Game.Ui;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Lumen.Tests.Game;

/// <summary>
/// The walkthrough behind Phase 10's exit criterion: somebody who has never seen LUMEN
/// gets from a cold start to a score without being told anything the game did not tell
/// them (spec §81–83).
///
/// It is scripted through the real screens rather than asserted about in prose — every
/// key pressed here is a key the screen in front of the player names at the time. If a
/// step ever needs a key that is not on screen, this test has to press it, and that is
/// the point at which the criterion is broken.
///
/// Drawing needs a graphics device, so only <c>Update</c> runs. That is enough: the flow
/// between screens is decided there.
/// </summary>
public class NewPlayerWalkthroughTests : IDisposable
{
    private readonly string _exeDir;
    private readonly LumenPaths _paths;
    private readonly Database _db;
    private readonly GameContext _context;
    private readonly ScreenManager _screens;

    private KeyboardState _previousKeyboard = new();

    public NewPlayerWalkthroughTests()
    {
        _exeDir = Path.Combine(Path.GetTempPath(), "lumen-walk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_exeDir);

        // A portable root, so the walkthrough cannot touch the machine's real data.
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
        var replays = new ReplayRepository(_db, _paths.Replays);

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
            Replays = replays,
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
        var input = new InputFrame
        {
            Keyboard = keyboard,
            PreviousKeyboard = _previousKeyboard,
            Mouse = new MouseState(),
            PreviousMouse = new MouseState(),
            TypedText = "",
            DeltaSeconds = 1 / 60.0,
        };

        _previousKeyboard = keyboard;
        _screens.Update(input);
    }

    /// <summary>
    /// A press: one frame down, one frame up so <c>Pressed</c> sees an edge, then enough
    /// idle frames for any screen change it caused to finish fading in. A player pressing
    /// a key waits for the screen to arrive too.
    /// </summary>
    private void Press(Keys key)
    {
        Frame(key);
        Frame();
        Wait(0.5);
    }

    private void Type(string text)
    {
        foreach (char c in text)
        {
            var keyboard = new KeyboardState();
            var input = new InputFrame
            {
                Keyboard = keyboard,
                PreviousKeyboard = _previousKeyboard,
                Mouse = new MouseState(),
                PreviousMouse = new MouseState(),
                TypedText = c.ToString(),
                DeltaSeconds = 1 / 60.0,
            };

            _previousKeyboard = keyboard;
            _screens.Update(input);
        }
    }

    /// <summary>Lets time pass without any input, for steps that settle on their own.</summary>
    private void Wait(double seconds)
    {
        for (double t = 0; t < seconds; t += 1 / 60.0)
        {
            Frame();
        }
    }

    // --- the walkthrough ---

    [Fact]
    public void A_new_player_reaches_their_first_song_without_being_told_anything()
    {
        // Cold start: no profile, so the game opens on setup.
        _context.Session.HasProfile.Should().BeFalse();
        _screens.SetRoot(new SetupScreen());

        // 1. Type a name and press the key the screen names.
        Type("Nagisa");
        Press(Keys.Enter);

        _context.Session.HasProfile.Should().BeTrue();
        _context.Session.ActiveProfile!.DisplayName.Should().Be("Nagisa");
        _screens.Top.Should().BeOfType<WelcomeScreen>();

        // 2. The welcome screen offers to teach them, and Enter takes it.
        Press(Keys.Enter);
        _screens.Top.Should().BeOfType<TutorialScreen>();

        // 3. The tutorial asks for the four lane keys, which it shows on the keycaps.
        foreach (Keys lane in new[] { Keys.A, Keys.S, Keys.D, Keys.F })
        {
            Press(lane);
        }

        // 4. Three explanations, each advanced with the key its footer names.
        Press(Keys.Enter);
        Press(Keys.Enter);
        Press(Keys.Enter);

        // 5. And the tutorial hands them straight into a song rather than a menu.
        _screens.Top.Should().BeOfType<GameplayScreen>(
            "the first run has to end in playing, not in a list of options");
    }

    /// <summary>
    /// The song they are handed has to exist on a machine that has never run LUMEN
    /// before — which is the whole reason the practice track is generated rather than
    /// shipped as a file.
    /// </summary>
    [Fact]
    public void The_first_song_is_there_on_a_machine_that_has_never_run_the_game()
    {
        Directory.GetFiles(_paths.Songs).Should().BeEmpty();

        _screens.SetRoot(new SetupScreen());
        Type("Nagisa");
        Press(Keys.Enter);
        Press(Keys.Enter);

        foreach (Keys lane in new[] { Keys.A, Keys.S, Keys.D, Keys.F })
        {
            Press(lane);
        }

        Press(Keys.Enter);
        Press(Keys.Enter);
        Press(Keys.Enter);

        File.Exists(Path.Combine(_paths.Songs, Lumen.Game.Content.TestContent.AudioFileName))
            .Should().BeTrue();
        File.Exists(Path.Combine(_paths.ChartsLocal, Lumen.Game.Content.TestContent.ChartFileName))
            .Should().BeTrue();
    }

    /// <summary>
    /// Escape is the one key a player is assumed to know, and it must always mean "not
    /// this". Somebody who does not want a tutorial is not trapped in one.
    /// </summary>
    [Fact]
    public void A_player_who_does_not_want_the_tutorial_can_leave_it()
    {
        _screens.SetRoot(new SetupScreen());
        Type("Nagisa");
        Press(Keys.Enter);
        Press(Keys.Enter);

        _screens.Top.Should().BeOfType<TutorialScreen>();

        Press(Keys.Escape);

        _screens.Top.Should().BeOfType<GameplayScreen>(
            "skipping the explanation still means getting to play");
    }

    [Fact]
    public void A_player_who_wants_neither_can_go_straight_to_the_menu()
    {
        _screens.SetRoot(new SetupScreen());
        Type("Nagisa");
        Press(Keys.Enter);

        _screens.Top.Should().BeOfType<WelcomeScreen>();
        Press(Keys.Escape);

        _screens.Top.Should().BeOfType<MainMenuScreen>();
    }

    /// <summary>
    /// The tutorial is not a one-time gate: it stays reachable from the hub, and coming
    /// back from it returns the player where they were.
    /// </summary>
    [Fact]
    public void The_tutorial_stays_reachable_from_the_menu_afterwards()
    {
        _context.Session.SetActive(_context.Profiles.Create("Nagisa"));
        _screens.SetRoot(new MainMenuScreen());

        // Down to HOW TO PLAY, wherever the menu happens to put it.
        int steps = Array.IndexOf(MainMenuScreen.Actions, "HOW TO PLAY");
        steps.Should().BeGreaterThan(0, "the menu must still offer the tutorial");

        for (int i = 0; i < steps; i++)
        {
            Press(Keys.Down);
        }

        Press(Keys.Enter);
        _screens.Top.Should().BeOfType<TutorialScreen>();

        Press(Keys.Escape);
        _screens.Top.Should().BeOfType<MainMenuScreen>();
    }

    /// <summary>
    /// An invalid name has to be refused without stranding the player: they stay on the
    /// screen, with the profile not created, free to try again.
    /// </summary>
    [Fact]
    public void A_name_that_will_not_do_keeps_the_player_on_the_setup_screen()
    {
        _screens.SetRoot(new SetupScreen());

        Press(Keys.Enter); // nothing typed yet

        _context.Session.HasProfile.Should().BeFalse();
        _screens.Top.Should().BeOfType<SetupScreen>();

        Type("Nagisa");
        Press(Keys.Enter);

        _context.Session.HasProfile.Should().BeTrue();
    }

    /// <summary>
    /// Every profile is its own player (§8), and a walkthrough that creates one must not
    /// leave the game pointing at a nameless or shared identity.
    /// </summary>
    [Fact]
    public void The_profile_the_walkthrough_creates_is_a_real_one_with_its_own_id()
    {
        _screens.SetRoot(new SetupScreen());
        Type("Nagisa");
        Press(Keys.Enter);

        var created = _context.Session.ActiveProfile!;
        created.PlayerId.Should().NotBe(Guid.Empty);
        created.DisplayName.Should().Be("Nagisa");

        IReadOnlyList<Lumen.Core.Profiles.Profile> all = _context.Profiles.GetAll();
        all.Should().ContainSingle(p => p.PlayerId == created.PlayerId);
    }
}

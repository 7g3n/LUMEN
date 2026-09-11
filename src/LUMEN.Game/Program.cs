using System;
using Lumen.Core;
using Lumen.Core.Diagnostics;
using Lumen.Data;
using Lumen.Data.Logging;
using Lumen.Game.Config;
using Lumen.Game.Engine;

namespace Lumen.Game;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        LaunchOptions options = LaunchOptions.Parse(args);
        LumenPaths paths = LumenPaths.Resolve();
        paths.EnsureCreated();

        using var log = new FileLog(paths.Logs, minLevel: LogLevel.Debug);
        Log.Current = log;
        CrashGuard.Install(paths);
        FrameLimiter.RequestHighResolutionTimer();

        Log.Info($"=== {GameIdentity.Name} {GameIdentity.FullVersion} starting ===");
        Log.Info($"data root: {paths.Root}");
        Console.WriteLine($"{GameIdentity.Name} {GameIdentity.FullVersion}  |  {paths.Root}  |  log: {log.CurrentFilePath}");

        Database? db = null;
        try
        {
            db = new Database(paths.DatabaseFile);
            db.Open();

            string? previousVersion = new AppMetaStore(db).RegisterLaunch();
            if (previousVersion is not null && previousVersion != GameIdentity.Version)
            {
                Log.Info($"data folder last opened by v{previousVersion}");
            }

            DisplayConfig display = DisplayConfig.Load(paths.Settings);

            using var game = new LumenGame(paths, options, display, db);
            game.Run();

            Log.Info("clean exit");
            return 0;
        }
        catch (Exception ex)
        {
            // Startup / unrecoverable failure: log, crash report, dialog — never a silent die.
            CrashGuard.Handle(ex, terminating: true, showDialog: !options.Smoke && !options.CrashTest);
            return 1;
        }
        finally
        {
            db?.Dispose();

            // The logger is about to be disposed by the `using`. Anything that runs after
            // that — a process-exit handler, a finalizer — would be writing to a closed
            // file, so it is pointed at the no-op logger first. FileLog survives being
            // written to after disposal on its own account; this simply means the last
            // lines of a shutdown go somewhere that is honestly nowhere.
            Log.Current = NullLog.Instance;
        }
    }
}

/// <summary>Command-line switches. Real configuration lives in settings files.</summary>
/// <param name="AutoPlayChart">
/// A chart to autoplay instead of the bundled practice track. Lets a chart be verified
/// end to end without a person at the keyboard — the result and the frame-time figures
/// are printed and the game exits — which is as useful to somebody who has just written
/// a chart as it is to the release checks.
/// </param>
internal sealed record LaunchOptions(
    bool Smoke, bool CrashTest, string? CaptureDir, bool AutoPlay, string? AutoPlayChart)
{
    public static LaunchOptions Parse(string[] args)
    {
        bool Has(string name) => Array.Exists(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The argument after a switch, when it is not itself a switch.</summary>
        string? ValueAfter(string name)
        {
            int at = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[at + 1]
                : null;
        }

        return new LaunchOptions(
            Smoke: Has("--smoke"),
            CrashTest: Has("--crashtest"),
            CaptureDir: ValueAfter("--capture"),
            AutoPlay: Has("--autoplay"),
            AutoPlayChart: ValueAfter("--autoplay"));
    }
}

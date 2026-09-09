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

        Log.Info($"=== {GameIdentity.Name} {GameIdentity.Version} starting ===");
        Log.Info($"data root: {paths.Root}");
        Console.WriteLine($"{GameIdentity.Name} {GameIdentity.Version}  |  {paths.Root}  |  log: {log.CurrentFilePath}");

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
        }
    }
}

/// <summary>Command-line switches. Real configuration lives in settings files.</summary>
internal sealed record LaunchOptions(bool Smoke, bool CrashTest)
{
    public static LaunchOptions Parse(string[] args)
    {
        bool Has(string name) => Array.Exists(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return new LaunchOptions(Smoke: Has("--smoke"), CrashTest: Has("--crashtest"));
    }
}

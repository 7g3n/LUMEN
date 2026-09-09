using System;
using Lumen.Core;
using Lumen.Data;

namespace Lumen.Game;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = LaunchOptions.Parse(args);

        // Resolve (but do not yet fully use) the data layout so a broken environment
        // surfaces immediately rather than deep inside gameplay.
        LumenPaths paths = LumenPaths.Resolve();

        Console.WriteLine($"{GameIdentity.Name} {GameIdentity.Version}");
        Console.WriteLine($"data root: {paths.Root}");

        try
        {
            using var game = new LumenGame(paths, options);
            game.Run();
            return 0;
        }
        catch (Exception ex)
        {
            // Phase 1 replaces this with an in-app error screen + log file (spec §96).
            Console.Error.WriteLine($"FATAL: {ex}");
            return 1;
        }
    }
}

/// <summary>Command-line switches. Kept tiny; real config lives in settings later.</summary>
internal sealed record LaunchOptions(bool Smoke)
{
    public static LaunchOptions Parse(string[] args)
    {
        bool smoke = Array.Exists(args, a =>
            a.Equals("--smoke", StringComparison.OrdinalIgnoreCase));
        return new LaunchOptions(smoke);
    }
}

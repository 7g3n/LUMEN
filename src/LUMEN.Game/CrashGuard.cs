using System.Runtime.InteropServices;
using Lumen.Core;
using Lumen.Core.Diagnostics;
using Lumen.Data;

namespace Lumen.Game;

/// <summary>
/// Last line of defence (spec §96): an unhandled exception is logged, written to a
/// crash report next to the logs, and shown to the player in a plain dialog rather
/// than vanishing. Recoverable in-game errors (a bad chart, a missing audio file) are
/// handled by the screen layer instead and never reach here.
/// </summary>
public static class CrashGuard
{
    private static LumenPaths? _paths;

    public static void Install(LumenPaths paths)
    {
        _paths = paths;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception ?? new Exception("Non-CLS exception");
            Handle(ex, terminating: e.IsTerminating);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Logs, writes a crash report, and (optionally) shows the dialog. Returns the report path.</summary>
    public static string Handle(Exception ex, bool terminating, bool showDialog = true)
    {
        Log.Error($"FATAL{(terminating ? " (terminating)" : "")}", ex);

        string reportPath = WriteReport(ex);

        if (showDialog)
        {
            ShowDialog(
                $"{GameIdentity.Name} hit a problem",
                $"{GameIdentity.Name} had to stop.\n\n" +
                $"{ex.GetType().Name}: {ex.Message}\n\n" +
                $"A report was saved to:\n{reportPath}");
        }

        return reportPath;
    }

    private static string WriteReport(Exception ex)
    {
        try
        {
            string dir = _paths?.Logs ?? Path.GetTempPath();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

            string body =
                $"{GameIdentity.Name} {GameIdentity.Version}\n" +
                $"{DateTime.Now:O}\n" +
                $"OS: {Environment.OSVersion} / CLR: {Environment.Version}\n" +
                $"64-bit process: {Environment.Is64BitProcess}\n\n" +
                ex;

            File.WriteAllText(path, body);
            return path;
        }
        catch
        {
            return "(could not write crash report)";
        }
    }

    // --- Win32 MessageBox: avoids a WinForms dependency for one dialog ---

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MbIconError = 0x00000010;
    private const uint MbOk = 0x00000000;
    private const uint MbTopmost = 0x00040000;

    public static void ShowDialog(string caption, string text)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, text, caption, MbOk | MbIconError | MbTopmost);
        }
        catch
        {
            Console.Error.WriteLine($"{caption}\n{text}");
        }
    }
}

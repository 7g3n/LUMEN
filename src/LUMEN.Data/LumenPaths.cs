using Lumen.Core;

namespace Lumen.Data;

/// <summary>
/// Resolves and creates the on-disk layout for LUMEN user data (spec §9).
///
/// Two modes:
///  - Installed:  data lives under <c>%LOCALAPPDATA%\LUMEN\</c>.
///  - Portable:   data lives under <c>./data/</c> next to the executable, when a
///                <c>portable.txt</c> sentinel file sits beside the executable.
///
/// Game binaries never write outside this root; user data never lands in Program Files (§73).
/// Full implementation (atomic writes, backup rotation) arrives in Phase 1 / Phase 9;
/// this is the Phase 0 skeleton.
/// </summary>
public sealed class LumenPaths
{
    public const string PortableSentinelFileName = "portable.txt";

    public string Root { get; }

    public string Database => Path.Combine(Root, "database");
    public string ChartsLocal => Path.Combine(Root, "charts", "local");
    public string ChartsImported => Path.Combine(Root, "charts", "imported");
    public string Songs => Path.Combine(Root, "songs");
    public string Replays => Path.Combine(Root, "replays");
    public string Backups => Path.Combine(Root, "backups");
    public string Cache => Path.Combine(Root, "cache");
    public string Exports => Path.Combine(Root, "exports");
    public string Settings => Path.Combine(Root, "settings");
    public string Logs => Path.Combine(Root, "logs");

    public string DatabaseFile => Path.Combine(Database, GameIdentity.DatabaseFileName);

    private LumenPaths(string root) => Root = root;

    /// <summary>
    /// Chooses portable vs. installed based on the presence of a sentinel file next
    /// to the executable, then returns the resolved paths. Does not create anything.
    /// </summary>
    public static LumenPaths Resolve(string? executableDirectory = null)
    {
        string exeDir = executableDirectory
                        ?? AppContext.BaseDirectory;

        string portableRoot = Path.Combine(exeDir, "data");
        bool portable = File.Exists(Path.Combine(exeDir, PortableSentinelFileName));

        string root = portable
            ? portableRoot
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                GameIdentity.DataFolderName);

        return new LumenPaths(root);
    }

    /// <summary>Creates the full directory tree if it does not already exist.</summary>
    public void EnsureCreated()
    {
        foreach (string dir in new[]
                 {
                     Root, Database, ChartsLocal, ChartsImported, Songs,
                     Replays, Backups, Cache, Exports, Settings, Logs,
                 })
        {
            Directory.CreateDirectory(dir);
        }
    }
}

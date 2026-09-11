using System.Reflection;

namespace Lumen.Core;

/// <summary>
/// The single source of truth for the game's name and every string, path segment,
/// and file extension derived from it.
///
/// The working title is "LUMEN". Per the project spec, the name must be renamable
/// without touching game logic — so nothing anywhere else in the codebase should
/// hardcode the literal "LUMEN". Read it from here instead.
/// </summary>
public static class GameIdentity
{
    /// <summary>Display name, shown in the UI, window title, result screens, etc.</summary>
    public const string Name = "LUMEN";

    /// <summary>Short tagline shown on the setup / title screen.</summary>
    public const string Tagline = "A NEW RHYTHM EXPERIENCE";

    /// <summary>
    /// Filesystem-safe identifier used for the data folder name
    /// (<c>%LOCALAPPDATA%\{DataFolderName}\</c>) and similar. ASCII, no spaces.
    /// </summary>
    public const string DataFolderName = "LUMEN";

    /// <summary>Lowercase slug used for internal file names (e.g. <c>lumen.db</c>).</summary>
    public const string Slug = "lumen";

    /// <summary>
    /// Current application version, read from the assembly rather than written here.
    ///
    /// It used to be a constant "kept in sync with Directory.Build.props", which is a
    /// promise no codebase keeps: the two drift the first time somebody bumps one of them,
    /// and the version a player reports from the title bar stops matching the build it
    /// came from. The build number is set in exactly one place now — the
    /// <c>&lt;Version&gt;</c> in Directory.Build.props — and everything else asks.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// What produced this build, when the release script stamped one in: the source
    /// revision, appended by the SDK after a <c>+</c>. Empty for an ordinary local build.
    /// Shown in crash reports and the log header, where "which build was this?" is the
    /// first question worth answering.
    /// </summary>
    public static string BuildId { get; } = ReadBuildId();

    /// <summary>Version with the build stamp when there is one, e.g. <c>0.1.0+1a2b3c4</c>.</summary>
    public static string FullVersion => BuildId.Length == 0 ? Version : $"{Version}+{BuildId}";

    private static string InformationalVersion =>
        typeof(GameIdentity).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "";

    private static string ReadVersion()
    {
        string informational = InformationalVersion;

        // "0.1.0+1a2b3c4" -> "0.1.0". A build stamp is diagnostic detail, not part of the
        // version a player reads.
        int plus = informational.IndexOf('+');
        if (plus > 0)
        {
            return informational[..plus];
        }

        if (informational.Length > 0)
        {
            return informational;
        }

        // No attribute at all: a host that loaded the assembly in an unusual way. Fall
        // back to the assembly version rather than showing nothing.
        return typeof(GameIdentity).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>How much of a commit hash is worth showing. The conventional short form.</summary>
    private const int ShortRevisionLength = 7;

    private static string ReadBuildId()
    {
        string informational = InformationalVersion;
        int plus = informational.IndexOf('+');
        if (plus <= 0 || plus + 1 >= informational.Length)
        {
            return "";
        }

        string stamp = informational[(plus + 1)..];

        // The SDK stamps the full 40-character commit hash. Nobody reads forty characters
        // off a title bar, and the first seven identify a commit as well as the whole
        // thing does for the purpose this serves — telling one build from another.
        return IsCommitHash(stamp) ? stamp[..ShortRevisionLength] : stamp;
    }

    private static bool IsCommitHash(string value)
    {
        if (value.Length <= ShortRevisionLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            bool hex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Chart data format version. Independent of <see cref="Version"/> (spec §97).</summary>
    public const int ChartFormatVersion = 2;

    /// <summary>Backup / replay container format version.</summary>
    public const int BackupFormatVersion = 1;

    /// <summary>File extension (no dot) for a single chart.</summary>
    public const string ChartExtension = "lumenchart";

    /// <summary>File extension (no dot) for a song + chart(s) package.</summary>
    public const string PackageExtension = "lumen";

    /// <summary>File extension (no dot) for a full data backup / migration bundle.</summary>
    public const string BackupExtension = "lumenbackup";

    /// <summary>SQLite database file name.</summary>
    public static string DatabaseFileName => $"{Slug}.db";

    /// <summary>Window title, e.g. "LUMEN 0.1.0".</summary>
    public static string WindowTitle => $"{Name} {Version}";
}

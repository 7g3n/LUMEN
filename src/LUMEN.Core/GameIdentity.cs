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

    /// <summary>Current application version. Kept in sync with Directory.Build.props.</summary>
    public const string Version = "0.1.0";

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

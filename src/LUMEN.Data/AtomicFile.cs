using System.Text;

namespace Lumen.Data;

/// <summary>
/// Crash-safe file writes (spec §74): write to a sibling <c>*.tmp</c>, flush it to
/// disk, then atomically replace the target. A process kill at any point leaves the
/// previous good file intact — a half-written file never appears at the real path.
/// </summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string contents)
        => WriteAllBytes(path, Utf8NoBom.GetBytes(contents));

    public static void WriteAllBytes(string path, byte[] contents)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tmp = path + ".tmp";

        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(contents, 0, contents.Length);
            fs.Flush(flushToDisk: true);
        }

        // File.Move with overwrite is atomic on Windows (ReplaceFile / rename semantics).
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// Reads a file that may have a leftover <c>*.tmp</c> from an interrupted write.
    /// Returns the committed file if present; otherwise null. Never returns the tmp.
    /// </summary>
    public static string? ReadAllTextOrNull(string path)
        => File.Exists(path) ? File.ReadAllText(path, Utf8NoBom) : null;
}

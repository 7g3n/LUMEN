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

    /// <summary>
    /// Removes <c>*.tmp</c> files left behind by writes that were interrupted.
    ///
    /// A kill between the write and the rename leaves the real file untouched, which is
    /// the point — but it also leaves debris the player can see in their songs folder.
    /// Only files older than <paramref name="olderThan"/> are removed, so a write that is
    /// in flight right now is never touched.
    /// </summary>
    public static int SweepStaleTemporaries(string directory, TimeSpan? olderThan = null)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        DateTime cutoff = DateTime.UtcNow - (olderThan ?? TimeSpan.FromMinutes(5));
        int removed = 0;

        foreach (string file in Directory.EnumerateFiles(directory, "*.tmp"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) > cutoff)
                {
                    continue;
                }

                File.Delete(file);
                removed++;
            }
            catch (IOException)
            {
                // Still held by something; it will be swept next time.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }
}

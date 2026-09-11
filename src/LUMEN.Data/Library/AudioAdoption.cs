using System.Security.Cryptography;
using Lumen.Core.Diagnostics;

namespace Lumen.Data.Library;

/// <summary>
/// Brings an audio file the author dropped in from elsewhere into the songs folder
/// (spec §49, §73).
///
/// A chart records the *name* of its audio, not a path to it, and resolves that name
/// against the songs folder. That is what lets a chart be exported, sent to somebody else
/// and opened on their machine. So a file dropped from a desktop or a download folder
/// cannot simply be remembered where it lies: the next time the chart is opened, that path
/// may be gone, may belong to a different file, or may not exist on that computer at all.
/// It has to be taken in.
/// </summary>
public static class AudioAdoption
{
    /// <summary>What adopting a file did, and what the chart should now point at.</summary>
    /// <param name="FileName">The name to store in the chart.</param>
    /// <param name="Path">Where it now lives.</param>
    /// <param name="Copied">False when the file was already in the songs folder.</param>
    /// <param name="ReusedExisting">True when an identical file was already there.</param>
    public readonly record struct Result(string FileName, string Path, bool Copied, bool ReusedExisting);

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into <paramref name="songsDirectory"/> and
    /// returns the name the chart should record.
    ///
    /// A file already in the songs folder is left where it is. A name that is taken is
    /// compared by content first: dropping the same song twice should not leave two copies
    /// of it, but two genuinely different files called <c>song.wav</c> must not overwrite
    /// one another either — the second gets a suffix.
    /// </summary>
    public static Result Adopt(string sourcePath, string songsDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("No audio file given.", nameof(sourcePath));
        }

        var source = new FileInfo(sourcePath);
        if (!source.Exists)
        {
            throw new FileNotFoundException("That audio file no longer exists.", sourcePath);
        }

        Directory.CreateDirectory(songsDirectory);

        string fileName = System.IO.Path.GetFileName(sourcePath);

        // Already ours: nothing to copy, and no second copy to keep in step.
        if (IsInside(source.FullName, songsDirectory))
        {
            return new Result(fileName, source.FullName, Copied: false, ReusedExisting: false);
        }

        string sourceHash = HashOf(source.FullName);
        string target = System.IO.Path.Combine(songsDirectory, fileName);

        if (File.Exists(target))
        {
            if (HashOf(target) == sourceHash)
            {
                Log.Info($"audio already in the library: {fileName}");
                return new Result(fileName, target, Copied: false, ReusedExisting: true);
            }

            target = FreeName(songsDirectory, fileName);
            fileName = System.IO.Path.GetFileName(target);
        }

        CopyAtomically(source.FullName, target);
        Log.Info($"adopted audio into the library: {fileName}");

        return new Result(fileName, target, Copied: true, ReusedExisting: false);
    }

    /// <summary>
    /// Copies through a temporary file in the destination folder, then renames.
    ///
    /// The same rule the rest of the game writes files by (§74): a copy interrupted by a
    /// crash or a pulled cable leaves debris that the startup sweep clears, never a
    /// half-written song that the library would go on to treat as real.
    /// </summary>
    private static void CopyAtomically(string source, string target)
    {
        string temp = target + ".tmp";

        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary><c>song.wav</c> → <c>song (2).wav</c>, and so on.</summary>
    private static string FreeName(string directory, string fileName)
    {
        string stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        string extension = System.IO.Path.GetExtension(fileName);

        for (int n = 2; n < 10_000; n++)
        {
            string candidate = System.IO.Path.Combine(directory, $"{stem} ({n}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        // Vanishingly unlikely, and better than looping forever.
        return System.IO.Path.Combine(directory, $"{stem} ({Guid.NewGuid():N}){extension}");
    }

    private static bool IsInside(string path, string directory)
    {
        string full = System.IO.Path.GetFullPath(directory)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar);

        return System.IO.Path.GetFullPath(path)
            .StartsWith(full + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Streamed, because these files run to hundreds of megabytes.</summary>
    public static string HashOf(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}

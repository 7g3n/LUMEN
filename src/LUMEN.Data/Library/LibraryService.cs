using Lumen.Core.Library;

namespace Lumen.Data.Library;

/// <summary>
/// The library as the rest of the game sees it: the index to read from, and a way to
/// bring it back in line with the files on disk.
///
/// Bundling the two means a screen never has to know that the index is a cache of the
/// chart folders — it asks for a scan when it is about to show the library, and reads
/// rows the rest of the time.
/// </summary>
public sealed class LibraryService
{
    private readonly LibraryScanner _scanner;

    public LibraryService(ILibraryRepository charts, LumenPaths paths)
    {
        Charts = charts;
        _scanner = new LibraryScanner(charts, paths);
    }

    public ILibraryRepository Charts { get; }

    /// <summary>Reconciles the index with the chart folders. Safe to call often.</summary>
    public LibraryScanner.Result Scan() => _scanner.Scan();

    public bool IsEmpty => Charts.Count() == 0;
}

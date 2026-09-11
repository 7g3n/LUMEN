using System.Text;
using Lumen.Core.Diagnostics;

namespace Lumen.Data.Logging;

/// <summary>
/// Thread-safe day-rolling file logger for <c>logs/</c> (spec §95). Each line:
/// <c>2026-09-09 14:30:05.123 [INFO ] (t12) message</c>. Warn/Error also go to
/// stderr. Old logs beyond <see cref="_keepDays"/> are pruned on open, as are old
/// crash reports, which live in the same folder.
///
/// Two rules this logger holds itself to, because a log is a diagnostic and not a
/// feature: it never throws at its caller, and it never grows without bound. A game
/// that dies because it could not write a line about something going wrong has turned
/// a problem into a crash, and a warning logged once a frame would fill a disk in
/// minutes at the frame rates this game targets.
/// </summary>
public sealed class FileLog : ILog, IDisposable
{
    /// <summary>
    /// Ceiling for one day's log. Generous for any normal session — a long play session
    /// writes a few hundred kilobytes — and small enough that a runaway message cannot
    /// take the disk with it.
    /// </summary>
    public const long DefaultMaxBytes = 32L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly int _keepDays;
    private readonly bool _alsoConsole;
    private readonly long _maxBytes;

    private StreamWriter? _writer;
    private DateOnly _openDay;
    private long _bytesWritten;
    private bool _capped;
    private bool _disposed;

    public FileLog(string directory, LogLevel minLevel = LogLevel.Debug, int keepDays = 14,
                   bool alsoConsole = true, long maxBytes = DefaultMaxBytes)
    {
        _directory = directory;
        _minLevel = minLevel;
        _keepDays = keepDays;
        _alsoConsole = alsoConsole;
        _maxBytes = maxBytes > 0 ? maxBytes : DefaultMaxBytes;

        Directory.CreateDirectory(_directory);
        Prune();
        Roll(DateOnly.FromDateTime(DateTime.Now));
    }

    public string CurrentFilePath { get; private set; } = "";

    /// <summary>True once today's log has hit its ceiling and further lines are dropped.</summary>
    public bool IsCapped
    {
        get { lock (_gate) { return _capped; } }
    }

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        if (level < _minLevel)
        {
            return;
        }

        DateTime now = DateTime.Now;
        string line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{Tag(level)}] (t{Environment.CurrentManagedThreadId}) {message}";

        WriteToFile(now, line, exception);

        if (_alsoConsole && level >= LogLevel.Warn)
        {
            // Console failures are as survivable as file ones: a redirected or closed
            // stderr must not take the game down either.
            try
            {
                Console.Error.WriteLine(line);
                if (exception is not null)
                {
                    Console.Error.WriteLine(Indent(exception.ToString()));
                }
            }
            catch
            {
                // nothing further to do; the file copy is the one that matters
            }
        }
    }

    private void WriteToFile(DateTime now, string line, Exception? exception)
    {
        try
        {
            lock (_gate)
            {
                // Logging during shutdown is normal — disposal order is not something
                // every caller can be expected to reason about — so it is dropped rather
                // than faulted.
                if (_disposed)
                {
                    return;
                }

                DateOnly today = DateOnly.FromDateTime(now);
                if (today != _openDay)
                {
                    Roll(today);
                }

                if (_writer is null || _capped)
                {
                    return;
                }

                string text = exception is null ? line : line + Environment.NewLine + Indent(exception.ToString());

                _writer.WriteLine(text);
                _writer.Flush();

                _bytesWritten += text.Length + Environment.NewLine.Length;
                if (_bytesWritten >= _maxBytes)
                {
                    Cap();
                }
            }
        }
        catch
        {
            // A log line is a diagnostic. Losing one is a nuisance; throwing here would
            // turn whatever was being reported into a second, worse failure.
        }
    }

    /// <summary>
    /// Stops writing for the rest of the day, leaving one line that says so. Silently
    /// going quiet would make the log lie about what happened after this point.
    /// </summary>
    private void Cap()
    {
        _capped = true;

        try
        {
            _writer?.WriteLine(
                $"--- log capped at {_maxBytes / 1024 / 1024} MB; further lines this day are dropped ---");
            _writer?.Flush();
        }
        catch
        {
            // best effort
        }
    }

    private void Roll(DateOnly day)
    {
        try
        {
            _writer?.Flush();
            _writer?.Dispose();
        }
        catch
        {
            // best effort
        }

        _writer = null;
        _openDay = day;
        _capped = false;
        _bytesWritten = 0;
        CurrentFilePath = Path.Combine(_directory, $"lumen-{day:yyyyMMdd}.log");

        try
        {
            var file = new FileStream(
                CurrentFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

            // Appending to an existing day continues where it left off, so a session that
            // restarts cannot use up the ceiling twice.
            _bytesWritten = file.Length;
            _capped = _bytesWritten >= _maxBytes;

            _writer = new StreamWriter(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // No log file: the game carries on without one rather than refusing to start.
            _writer = null;
        }
    }

    /// <summary>
    /// Drops logs and crash reports past their keep window. Crash reports are pruned here
    /// too, because they land in this folder and nothing else was ever removing them —
    /// a game that crashes occasionally would otherwise accumulate them forever.
    /// </summary>
    private void Prune()
    {
        DateTime cutoff = DateTime.Now.Date.AddDays(-_keepDays);

        foreach (string pattern in new[] { "lumen-*.log", "crash-*.txt" })
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(_directory, pattern);
            }
            catch
            {
                continue;
            }

            foreach (string file in files)
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // best effort
                }
            }
        }
    }

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO ",
        LogLevel.Warn => "WARN ",
        LogLevel.Error => "ERROR",
        _ => "?????",
    };

    private static string Indent(string text)
        => "    " + text.Replace("\n", "\n    ");

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;

            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch
            {
                // best effort
            }

            _writer = null;
        }
    }
}

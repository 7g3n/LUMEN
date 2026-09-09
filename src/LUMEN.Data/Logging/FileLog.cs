using System.Text;
using Lumen.Core.Diagnostics;

namespace Lumen.Data.Logging;

/// <summary>
/// Thread-safe day-rolling file logger for <c>logs/</c> (spec §95). Each line:
/// <c>2026-09-09 14:30:05.123 [INFO ] (t12) message</c>. Warn/Error also go to
/// stderr. Old logs beyond <see cref="_keepDays"/> are pruned on open.
/// </summary>
public sealed class FileLog : ILog, IDisposable
{
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly int _keepDays;
    private readonly bool _alsoConsole;

    private StreamWriter? _writer;
    private DateOnly _openDay;

    public FileLog(string directory, LogLevel minLevel = LogLevel.Debug, int keepDays = 14, bool alsoConsole = true)
    {
        _directory = directory;
        _minLevel = minLevel;
        _keepDays = keepDays;
        _alsoConsole = alsoConsole;

        Directory.CreateDirectory(_directory);
        Prune();
        Roll(DateOnly.FromDateTime(DateTime.Now));
    }

    public string CurrentFilePath { get; private set; } = "";

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        if (level < _minLevel)
        {
            return;
        }

        DateTime now = DateTime.Now;
        string line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{Tag(level)}] (t{Environment.CurrentManagedThreadId}) {message}";

        lock (_gate)
        {
            DateOnly today = DateOnly.FromDateTime(now);
            if (today != _openDay)
            {
                Roll(today);
            }

            _writer!.WriteLine(line);
            if (exception is not null)
            {
                _writer.WriteLine(Indent(exception.ToString()));
            }

            _writer.Flush();
        }

        if (_alsoConsole && level >= LogLevel.Warn)
        {
            Console.Error.WriteLine(line);
            if (exception is not null)
            {
                Console.Error.WriteLine(Indent(exception.ToString()));
            }
        }
    }

    private void Roll(DateOnly day)
    {
        _writer?.Flush();
        _writer?.Dispose();

        _openDay = day;
        CurrentFilePath = Path.Combine(_directory, $"lumen-{day:yyyyMMdd}.log");
        _writer = new StreamWriter(
            new FileStream(CurrentFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private void Prune()
    {
        DateTime cutoff = DateTime.Now.Date.AddDays(-_keepDays);
        foreach (string file in Directory.EnumerateFiles(_directory, "lumen-*.log"))
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
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }
    }
}

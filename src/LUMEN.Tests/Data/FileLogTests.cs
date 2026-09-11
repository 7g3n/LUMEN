using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Lumen.Core.Diagnostics;
using Lumen.Data.Logging;
using Xunit;

namespace Lumen.Tests.Data;

public class FileLogTests : IDisposable
{
    private readonly string _dir;

    public FileLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lumen-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Reads the log while the logger still holds it open. The plain File.ReadAllText
    /// asks for exclusive-enough access to collide with the writer, which is a fact about
    /// the test rather than about the log — the log is deliberately shareable so it can be
    /// read during a hang.
    /// </summary>
    private static string ReadLog(string path)
    {
        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string[] ReadLogLines(string path) =>
        ReadLog(path).Split(
            new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void A_line_reaches_the_file_immediately()
    {
        using var log = new FileLog(_dir, alsoConsole: false);

        log.Write(LogLevel.Info, "hello");

        // Readable while still open: a log nobody can read during a hang is no use.
        ReadLog(log.CurrentFilePath).Should().Contain("hello");
    }

    [Fact]
    public void An_exception_is_written_under_its_message()
    {
        using var log = new FileLog(_dir, alsoConsole: false);

        log.Write(LogLevel.Error, "could not open the chart",
            new InvalidOperationException("bad json"));

        string text = ReadLog(log.CurrentFilePath);
        text.Should().Contain("could not open the chart");
        text.Should().Contain("bad json");
        text.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public void Lines_below_the_minimum_level_are_not_written()
    {
        using var log = new FileLog(_dir, minLevel: LogLevel.Warn, alsoConsole: false);

        log.Write(LogLevel.Debug, "chatter");
        log.Write(LogLevel.Info, "routine");
        log.Write(LogLevel.Warn, "worth knowing");

        string text = ReadLog(log.CurrentFilePath);
        text.Should().NotContain("chatter");
        text.Should().NotContain("routine");
        text.Should().Contain("worth knowing");
    }

    /// <summary>
    /// A warning logged once a frame would fill a disk in minutes at the frame rates this
    /// game targets, so the day's log has a ceiling — and says so rather than going
    /// quietly missing.
    /// </summary>
    [Fact]
    public void A_runaway_log_stops_at_its_ceiling_and_says_so()
    {
        using var log = new FileLog(_dir, alsoConsole: false, maxBytes: 8 * 1024);

        for (int i = 0; i < 5000; i++)
        {
            log.Write(LogLevel.Warn, $"something is wrong, frame {i}");
        }

        log.IsCapped.Should().BeTrue();

        var size = new FileInfo(log.CurrentFilePath).Length;
        size.Should().BeLessThan(32 * 1024);

        ReadLog(log.CurrentFilePath).Should().Contain("log capped");
    }

    [Fact]
    public void A_capped_log_does_not_keep_growing()
    {
        using var log = new FileLog(_dir, alsoConsole: false, maxBytes: 4 * 1024);

        for (int i = 0; i < 2000; i++)
        {
            log.Write(LogLevel.Warn, $"noise {i}");
        }

        long afterCap = new FileInfo(log.CurrentFilePath).Length;

        for (int i = 0; i < 2000; i++)
        {
            log.Write(LogLevel.Warn, $"more noise {i}");
        }

        new FileInfo(log.CurrentFilePath).Length.Should().Be(afterCap);
    }

    /// <summary>
    /// Disposal order is not something every caller can be expected to reason about, and
    /// a line logged on the way out must not become the reason the game fails to exit.
    /// </summary>
    [Fact]
    public void Logging_after_disposal_is_dropped_rather_than_thrown()
    {
        var log = new FileLog(_dir, alsoConsole: false);
        log.Dispose();

        Action write = () => log.Write(LogLevel.Error, "on the way out",
            new InvalidOperationException("late"));

        write.Should().NotThrow();
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        var log = new FileLog(_dir, alsoConsole: false);

        log.Dispose();
        Action again = () => log.Dispose();

        again.Should().NotThrow();
    }

    /// <summary>
    /// Crash reports land in the log folder and nothing else removes them, so the logger
    /// prunes them on the same schedule as the logs.
    /// </summary>
    [Fact]
    public void Old_crash_reports_are_pruned_alongside_old_logs()
    {
        string oldLog = Path.Combine(_dir, "lumen-20200101.log");
        string oldCrash = Path.Combine(_dir, "crash-20200101-000000.txt");
        string recentCrash = Path.Combine(_dir, "crash-recent.txt");

        File.WriteAllText(oldLog, "ancient");
        File.WriteAllText(oldCrash, "ancient");
        File.WriteAllText(recentCrash, "yesterday");

        File.SetLastWriteTime(oldLog, DateTime.Now.AddDays(-60));
        File.SetLastWriteTime(oldCrash, DateTime.Now.AddDays(-60));

        using var log = new FileLog(_dir, keepDays: 14, alsoConsole: false);

        File.Exists(oldLog).Should().BeFalse();
        File.Exists(oldCrash).Should().BeFalse();
        File.Exists(recentCrash).Should().BeTrue();
    }

    [Fact]
    public void Unrelated_files_in_the_log_folder_are_left_alone()
    {
        string other = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(other, "mine");
        File.SetLastWriteTime(other, DateTime.Now.AddDays(-900));

        using var log = new FileLog(_dir, keepDays: 14, alsoConsole: false);

        File.Exists(other).Should().BeTrue();
    }

    /// <summary>
    /// Reopening the same day appends rather than restarting, so a session that crashes
    /// and is relaunched cannot spend the day's allowance twice over.
    /// </summary>
    [Fact]
    public void Reopening_the_same_day_continues_the_same_file()
    {
        string path;

        using (var first = new FileLog(_dir, alsoConsole: false))
        {
            first.Write(LogLevel.Info, "first session");
            path = first.CurrentFilePath;
        }

        using (var second = new FileLog(_dir, alsoConsole: false))
        {
            second.Write(LogLevel.Info, "second session");
            second.CurrentFilePath.Should().Be(path);
        }

        string text = ReadLog(path);
        text.Should().Contain("first session");
        text.Should().Contain("second session");
    }

    [Fact]
    public async Task Writing_from_several_threads_does_not_interleave_or_throw()
    {
        using var log = new FileLog(_dir, alsoConsole: false);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                log.Write(LogLevel.Info, $"worker {worker} line {i}");
            }
        })));

        string[] lines = ReadLogLines(log.CurrentFilePath);
        lines.Should().HaveCount(8 * 200);
        lines.Should().OnlyContain(l => l.Contains("worker ") && l.Contains(" line "));
    }
}

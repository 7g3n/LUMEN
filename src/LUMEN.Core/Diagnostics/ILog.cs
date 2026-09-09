namespace Lumen.Core.Diagnostics;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
}

/// <summary>
/// Minimal logging surface. Implementations live outside Core (file logger in
/// LUMEN.Data). Keep messages free of personal data beyond the player's chosen
/// display name (spec §95).
/// </summary>
public interface ILog
{
    void Write(LogLevel level, string message, Exception? exception = null);
}

public static class LogExtensions
{
    public static void Debug(this ILog log, string message) => log.Write(LogLevel.Debug, message);
    public static void Info(this ILog log, string message) => log.Write(LogLevel.Info, message);
    public static void Warn(this ILog log, string message, Exception? ex = null) => log.Write(LogLevel.Warn, message, ex);
    public static void Error(this ILog log, string message, Exception? ex = null) => log.Write(LogLevel.Error, message, ex);
}

/// <summary>No-op logger; the default so code never has to null-check.</summary>
public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();
    public void Write(LogLevel level, string message, Exception? exception = null) { }
}

/// <summary>
/// Process-wide logger holder. Set once at startup; reads are cheap and lock-free.
/// </summary>
public static class Log
{
    public static ILog Current { get; set; } = NullLog.Instance;

    public static void Debug(string message) => Current.Write(LogLevel.Debug, message);
    public static void Info(string message) => Current.Write(LogLevel.Info, message);
    public static void Warn(string message, Exception? ex = null) => Current.Write(LogLevel.Warn, message, ex);
    public static void Error(string message, Exception? ex = null) => Current.Write(LogLevel.Error, message, ex);
}

using Microsoft.Extensions.Logging;

namespace ReportingTool.Platform.Interfaces.Logging;

/// <summary>
/// The service's logging surface: one singleton with a method per level, so call sites stay terse
/// and the verbosity can be changed at runtime (<see cref="SetMinimumLevel"/>) without a restart.
/// Message templates are structured (Serilog-style <c>{Placeholder}</c>), never interpolated
/// strings. Call sites depend on this interface, never on Serilog directly — Serilog is only the
/// sink pipeline behind the host logger factory.
/// </summary>
public interface ILoggerService
{
    /// <summary>The current minimum level; events below it are discarded.</summary>
    LogLevel MinimumLevel { get; }

    /// <summary>Changes the minimum level at runtime (no restart).</summary>
    void SetMinimumLevel(LogLevel level);

    /// <summary>True when <paramref name="level"/> would be logged — hot paths guard with this before logging.</summary>
    bool IsEnabled(LogLevel level);

    void Debug(string messageTemplate, params object?[] args);
    void Info(string messageTemplate, params object?[] args);
    void Warning(string messageTemplate, params object?[] args);
    void Warning(Exception exception, string messageTemplate, params object?[] args);
    void Error(string messageTemplate, params object?[] args);
    void Error(Exception? exception, string messageTemplate, params object?[] args);
    void Critical(Exception? exception, string messageTemplate, params object?[] args);

    /// <summary>Level-parameterized overload for callers that decide severity at runtime (the error boundaries).</summary>
    void Log(LogLevel level, Exception? exception, string messageTemplate, params object?[] args);
}

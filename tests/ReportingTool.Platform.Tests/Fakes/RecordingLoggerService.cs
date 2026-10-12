using Microsoft.Extensions.Logging;
using ReportingTool.Platform.Interfaces.Logging;

namespace ReportingTool.Platform.Tests.Fakes;

/// <summary>An <see cref="ILoggerService"/> that keeps every entry so tests can assert on level and text.</summary>
public sealed class RecordingLoggerService : ILoggerService
{
    public sealed record Entry(LogLevel Level, Exception? Exception, string Template, object?[] Args);

    public List<Entry> Entries { get; } = [];

    public LogLevel MinimumLevel { get; private set; } = LogLevel.Trace;

    public void SetMinimumLevel(LogLevel level) => MinimumLevel = level;

    public bool IsEnabled(LogLevel level) => level >= MinimumLevel;

    public void Debug(string messageTemplate, params object?[] args) => Log(LogLevel.Debug, null, messageTemplate, args);

    public void Info(string messageTemplate, params object?[] args) => Log(LogLevel.Information, null, messageTemplate, args);

    public void Warning(string messageTemplate, params object?[] args) => Log(LogLevel.Warning, null, messageTemplate, args);

    public void Warning(Exception exception, string messageTemplate, params object?[] args) =>
        Log(LogLevel.Warning, exception, messageTemplate, args);

    public void Error(string messageTemplate, params object?[] args) => Log(LogLevel.Error, null, messageTemplate, args);

    public void Error(Exception? exception, string messageTemplate, params object?[] args) =>
        Log(LogLevel.Error, exception, messageTemplate, args);

    public void Critical(Exception? exception, string messageTemplate, params object?[] args) =>
        Log(LogLevel.Critical, exception, messageTemplate, args);

    public void Log(LogLevel level, Exception? exception, string messageTemplate, params object?[] args) =>
        Entries.Add(new Entry(level, exception, messageTemplate, args));
}

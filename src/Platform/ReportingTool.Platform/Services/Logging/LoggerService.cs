using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Extensions.Logging;

namespace ReportingTool.Platform.Services.Logging;

/// <summary>
/// Singleton <see cref="ILoggerService"/> over the Serilog pipeline (configured in the "Serilog"
/// appsettings section). Writes through the host's logger factory — which <c>AddSerilog</c> backs
/// with Serilog directly — under the service's application name as the category, and owns the
/// <see cref="LoggingLevelSwitch"/> that <c>MinimumLevel.ControlledBy</c> hooks in
/// <see cref="PlatformHostingExtensions.AddPlatformLogging"/>, which is what makes
/// <see cref="SetMinimumLevel"/> take effect immediately at runtime.
/// </summary>
public sealed class LoggerService : ILoggerService
{
    private readonly ILogger _logger;
    private readonly LoggingLevelSwitch _levelSwitch;

    public LoggerService(ILoggerFactory loggerFactory, LoggingLevelSwitch levelSwitch, IHostEnvironment environment)
    {
        _logger = loggerFactory.CreateLogger(environment.ApplicationName);
        _levelSwitch = levelSwitch;
    }

    // Level mapping is Serilog's own LevelConvert — no hand-rolled map to drift when levels change.
    public LogLevel MinimumLevel => LevelConvert.ToExtensionsLevel(_levelSwitch.MinimumLevel);

    public void SetMinimumLevel(LogLevel level)
    {
        _levelSwitch.MinimumLevel = LevelConvert.ToSerilogLevel(level);
        _logger.LogInformation("Minimum log level set to {Level}.", level);
    }

    public bool IsEnabled(LogLevel level) => _logger.IsEnabled(level);

    public void Debug(string messageTemplate, params object?[] args) =>
        _logger.LogDebug(messageTemplate, args);

    public void Info(string messageTemplate, params object?[] args) =>
        _logger.LogInformation(messageTemplate, args);

    public void Warning(string messageTemplate, params object?[] args) =>
        _logger.LogWarning(messageTemplate, args);

    public void Warning(Exception exception, string messageTemplate, params object?[] args) =>
        _logger.LogWarning(exception, messageTemplate, args);

    public void Error(string messageTemplate, params object?[] args) =>
        _logger.LogError(messageTemplate, args);

    public void Error(Exception? exception, string messageTemplate, params object?[] args) =>
        _logger.LogError(exception, messageTemplate, args);

    public void Critical(Exception? exception, string messageTemplate, params object?[] args) =>
        _logger.LogCritical(exception, messageTemplate, args);

    public void Log(LogLevel level, Exception? exception, string messageTemplate, params object?[] args) =>
        _logger.Log(level, exception, messageTemplate, args);
}

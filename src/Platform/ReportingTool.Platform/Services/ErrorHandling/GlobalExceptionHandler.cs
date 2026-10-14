using Microsoft.Extensions.Logging;

namespace ReportingTool.Platform.Services.ErrorHandling;

/// <summary>
/// The one concrete non-HTTP error boundary (see <see cref="IGlobalExceptionHandler"/>). Together
/// with <see cref="ProblemDetailsExceptionHandler"/> this file holds every catch in the service;
/// the rest of the codebase throws and routes resumable work through Run/RunAsync.
/// </summary>
public sealed class GlobalExceptionHandler : IGlobalExceptionHandler
{
    private readonly ILoggerService _logger;

    public GlobalExceptionHandler(ILoggerService logger) => _logger = logger;

    public void RegisterProcessHooks()
    {
        // A truly unhandled exception on any thread: log it before the runtime terminates the process.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _logger.Critical(e.ExceptionObject as Exception,
                "Unhandled exception{Terminating}.", e.IsTerminating ? " — process is terminating" : "");

        // A faulted Task whose exception was never observed: log and mark observed so it never escalates.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger.Error(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        };

        _logger.Info("[BOOT] Process-level exception hooks registered (AppDomain + TaskScheduler).");
    }

    public void Run(Action operation, string context, LogLevel failureLevel = LogLevel.Error,
        Func<Exception, bool>? handles = null)
    {
        try
        {
            operation();
        }
        catch (Exception ex)
        {
            if (handles is not null && !handles(ex))
                throw; // not this call's failure to own — the enclosing boundary handles it
            Report(ex, context, failureLevel, CancellationToken.None);
        }
    }

    public T Run<T>(Func<T> operation, string context, T fallback, LogLevel failureLevel = LogLevel.Error,
        Func<Exception, bool>? handles = null)
    {
        try
        {
            return operation();
        }
        catch (Exception ex)
        {
            if (handles is not null && !handles(ex))
                throw;
            Report(ex, context, failureLevel, CancellationToken.None);
            return fallback;
        }
    }

    public async Task RunAsync(Func<Task> operation, string context, LogLevel failureLevel = LogLevel.Error,
        Func<Exception, bool>? handles = null, CancellationToken ct = default)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            if (handles is not null && !handles(ex))
                throw;
            Report(ex, context, failureLevel, ct);
        }
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> operation, string context, T fallback,
        LogLevel failureLevel = LogLevel.Error,
        Func<Exception, bool>? handles = null, CancellationToken ct = default)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex)
        {
            if (handles is not null && !handles(ex))
                throw;
            Report(ex, context, failureLevel, ct);
            return fallback;
        }
    }

    private void Report(Exception ex, string context, LogLevel failureLevel, CancellationToken ct)
    {
        switch (ex)
        {
            case OperationCanceledException when ct.IsCancellationRequested:
                // Genuine cancellation (shutdown, or the caller's work was aborted) — expected.
                // An OCE WITHOUT a cancelled token falls through to default: it is a timeout in
                // disguise — a real failure.
                _logger.Info("{Context}: cancelled.", context);
                break;

            default:
                _logger.Log(failureLevel, ex, "{Context} failed; the service continues.", context);
                break;
        }
    }
}

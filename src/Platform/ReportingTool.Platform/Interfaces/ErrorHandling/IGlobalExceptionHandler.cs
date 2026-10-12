using Microsoft.Extensions.Logging;

namespace ReportingTool.Platform.Interfaces.ErrorHandling;

/// <summary>
/// The service's single error boundary for non-HTTP work (background jobs, fire-and-forget
/// tasks, teardown). Code elsewhere never try/catches: risky work runs through
/// <see cref="Run"/>/<see cref="RunAsync"/> (which classifies the failure, logs it, and returns
/// the fallback so the service keeps running), and anything that escapes lands in the process
/// hooks (log-only: .NET cannot resume a thread whose exception reached
/// <c>AppDomain.UnhandledException</c>).
///
/// <para>HTTP requests have their own boundary, <see cref="ProblemDetailsExceptionHandler"/>,
/// plugged into ASP.NET Core's exception-handler middleware so endpoints also never catch.</para>
///
/// <para>Classification: <see cref="OperationCanceledException"/> is benign (Info) ONLY when the
/// caller's <c>ct</c> was actually cancelled — an OCE without a cancelled token is a timeout and
/// logs as a real failure; everything else logs at the caller's <c>failureLevel</c>.</para>
///
/// <para>The optional <c>handles</c> filter scopes a boundary call to the failures it owns: an
/// exception it does not handle is rethrown and bubbles to the ENCLOSING boundary call.
/// <c>null</c> means handle everything.</para>
/// </summary>
public interface IGlobalExceptionHandler
{
    /// <summary>
    /// Hooks <c>AppDomain.UnhandledException</c> (log before the runtime terminates the process)
    /// and <c>TaskScheduler.UnobservedTaskException</c> (log + mark observed so a faulted
    /// fire-and-forget task never escalates). Called once at startup.
    /// </summary>
    void RegisterProcessHooks();

    /// <summary>Runs the operation; on a handled failure logs (with <paramref name="context"/>) and continues.</summary>
    void Run(Action operation, string context, LogLevel failureLevel = LogLevel.Error,
        Func<Exception, bool>? handles = null);

    /// <summary>Runs the operation; on a handled failure logs and returns <paramref name="fallback"/>.</summary>
    T Run<T>(Func<T> operation, string context, T fallback, LogLevel failureLevel = LogLevel.Error,
        Func<Exception, bool>? handles = null);

    /// <summary>Awaits the operation; on a handled failure logs (with <paramref name="context"/>) and continues.</summary>
    Task RunAsync(Func<Task> operation, string context, LogLevel failureLevel = LogLevel.Error,
        Func<Exception, bool>? handles = null, CancellationToken ct = default);

    /// <summary>Awaits the operation; on a handled failure logs and returns <paramref name="fallback"/>.</summary>
    Task<T> RunAsync<T>(Func<Task<T>> operation, string context, T fallback,
        LogLevel failureLevel = LogLevel.Error,
        Func<Exception, bool>? handles = null, CancellationToken ct = default);
}

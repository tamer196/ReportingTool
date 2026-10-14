using Microsoft.Extensions.Logging;
using ReportingTool.Platform.Services.ErrorHandling;
using ReportingTool.Platform.Tests.Fakes;

namespace ReportingTool.Platform.Tests;

public sealed class GlobalExceptionHandlerTests
{
    private readonly RecordingLoggerService _logger = new();
    private readonly GlobalExceptionHandler _handler;

    public GlobalExceptionHandlerTests() => _handler = new GlobalExceptionHandler(_logger);

    [Fact]
    public void Run_logs_a_failure_with_its_context_and_continues()
    {
        _handler.Run(static () => throw new InvalidOperationException("boom"), "nightly cleanup");

        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Contains("nightly cleanup", entry.Args);
    }

    [Fact]
    public void Run_returns_the_fallback_when_the_operation_fails()
    {
        var result = _handler.Run<int>(static () => throw new InvalidOperationException(), "count rows", fallback: -1);

        Assert.Equal(-1, result);
    }

    [Fact]
    public void Run_rethrows_failures_it_does_not_own_to_the_enclosing_boundary()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _handler.Run(static () => throw new InvalidOperationException(), "read body",
                handles: static ex => ex is IOException));

        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task RunAsync_logs_cancellation_as_info_when_the_token_was_cancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await _handler.RunAsync(() => Task.FromCanceled(cts.Token), "shutdown wait", ct: cts.Token);

        Assert.Equal(LogLevel.Information, Assert.Single(_logger.Entries).Level);
    }

    [Fact]
    public async Task RunAsync_treats_cancellation_without_a_cancelled_token_as_a_failure()
    {
        // An OperationCanceledException nobody asked for is a timeout in disguise.
        await _handler.RunAsync(static () => throw new TaskCanceledException("timed out"), "send email",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(LogLevel.Error, Assert.Single(_logger.Entries).Level);
    }
}

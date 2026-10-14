using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ReportingTool.Platform.Services.ErrorHandling;

/// <summary>
/// The HTTP request boundary. ASP.NET Core's exception-handler middleware catches anything an
/// endpoint throws and hands it here, so endpoints and services never try/catch. Every failure
/// becomes an RFC 9457 problem-details response; the exception text and stack trace are logged,
/// never returned to the caller (they can contain table names or data values).
///
/// <para>Status-code contract: 400 for malformed requests (<see cref="BadHttpRequestException"/>
/// carries its own status), 499 with no body (logged at Info) when the client disconnected and
/// the request token was cancelled, 500 for everything else.</para>
/// </summary>
public sealed class ProblemDetailsExceptionHandler : IExceptionHandler
{
    /// <summary>Non-standard "client closed request" code (nginx convention); nobody reads the body.</summary>
    private const int ClientClosedRequest = 499;

    private readonly ILoggerService _logger;
    private readonly IProblemDetailsService _problemDetails;

    public ProblemDetailsExceptionHandler(ILoggerService logger, IProblemDetailsService problemDetails)
    {
        _logger = logger;
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var request = httpContext.Request;

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.Info("{Method} {Path}: client disconnected, request cancelled.", request.Method, request.Path);
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var status = exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError;

        _logger.Log(status >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Warning,
            exception, "{Method} {Path} failed with {StatusCode}.", request.Method, request.Path, status);

        httpContext.Response.StatusCode = status;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = status >= StatusCodes.Status500InternalServerError
                    ? "An unexpected error occurred."
                    : "The request could not be processed.",
            },
        });
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ReportingTool.Reports.Endpoints;

/// <summary>
/// The designer and viewer API under <c>/api/reports</c>. Every route needs a signed-in user,
/// and every call is scoped to that user: catalog, templates and runs never cross principals.
/// There is deliberately no route that accepts SQL, a connection string, a URL or raw HTML (G3).
/// </summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/reports").RequireAuthorization().WithTags("Reports");

        api.MapGet("/catalog/datasets", static async (ClaimsPrincipal user, IQueryService query, CancellationToken ct) =>
            TypedResults.Ok(await query.ListSchemasAsync(Subject(user), ct)));

        api.MapGet("/templates", static async (ClaimsPrincipal user, ITemplateService templates, CancellationToken ct) =>
            TypedResults.Ok(await templates.ListAsync(Subject(user), ct)));

        api.MapGet("/templates/{id:guid}", static async Task<Results<Ok<TemplateResponse>, NotFound>> (
            Guid id, ClaimsPrincipal user, ITemplateService templates, CancellationToken ct) =>
            await templates.FindAsync(Subject(user), id, ct) is { } t ? TypedResults.Ok(t) : TypedResults.NotFound());

        api.MapPut("/templates/{id:guid}", static async Task<Results<Ok<TemplateResponse>, ValidationProblem>> (
            Guid id, SaveTemplateRequest request, ClaimsPrincipal user, ITemplateService templates, CancellationToken ct) =>
        {
            var (saved, errors) = await templates.SaveAsync(Subject(user), id, request, ct);
            return saved is not null
                ? TypedResults.Ok(saved)
                : TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["definition"] = [.. errors] });
        });

        api.MapPost("/runs", static async Task<Results<Accepted<RunResponse>, NotFound>> (
            StartRunRequest request, ClaimsPrincipal user, IRunService runs, CancellationToken ct) =>
            await runs.StartAsync(Subject(user), request, ct) is { } run
                ? TypedResults.Accepted($"/api/reports/runs/{run.Id}", run)
                : TypedResults.NotFound());

        api.MapGet("/runs/{id:guid}", static async Task<Results<Ok<RunResponse>, NotFound>> (
            Guid id, ClaimsPrincipal user, IRunService runs, CancellationToken ct) =>
            await runs.FindAsync(Subject(user), id, ct) is { } run ? TypedResults.Ok(run) : TypedResults.NotFound());

        api.MapGet("/runs/{id:guid}/{format}", static async Task<Results<PhysicalFileHttpResult, NotFound>> (
            Guid id, string format, ClaimsPrincipal user, IRunService runs, CancellationToken ct) =>
        {
            var path = await runs.FindOutputAsync(Subject(user), id, format, ct);
            return path is null || !File.Exists(path)
                ? TypedResults.NotFound()
                : TypedResults.PhysicalFile(path, format == "pdf" ? "application/pdf" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"report-{id:N}.{format}");
        });

        return app;
    }

    /// <summary>The identity provider's user name; grants and ownership are keyed on it (PoC).</summary>
    private static string Subject(ClaimsPrincipal user) =>
        user.FindFirstValue("preferred_username") ?? throw new BadHttpRequestException("Token has no user name.", StatusCodes.Status401Unauthorized);
}

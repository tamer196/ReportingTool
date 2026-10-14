using System.Reflection;

namespace ReportingTool.Api.Endpoints;

/// <summary><c>GET /api/system/info</c> → 200 with <see cref="SystemInfoResponse"/>.</summary>
public static class SystemInfoEndpoints
{
    /// <summary>
    /// Read once: the informational version is fixed at build time. The image build passes the
    /// git commit as <c>SourceRevisionId</c>, so it ends in <c>+&lt;commit sha&gt;</c>.
    /// </summary>
    private static readonly string Version =
        typeof(SystemInfoEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public static IEndpointRouteBuilder MapSystemInfoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/system/info", static (IHostEnvironment environment, TimeProvider time) =>
                TypedResults.Ok(new SystemInfoResponse(
                    environment.ApplicationName, Version, environment.EnvironmentName, time.GetUtcNow().UtcDateTime)))
            .WithTags("System");

        return app;
    }
}

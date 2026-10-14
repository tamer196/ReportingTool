namespace ReportingTool.Api.Endpoints;

/// <summary>
/// <c>GET /api/time-zones</c> → 200 with every zone a user can choose (<see cref="ITimeZoneService.SelectableIds"/>),
/// <c>UTC</c> first. The browser fills the time zone picker from this list rather than from its
/// own database, so it never offers an ID the server would reject (#95 AC3).
/// </summary>
public static class TimeZoneEndpoints
{
    public static IEndpointRouteBuilder MapTimeZoneEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/time-zones", static (ITimeZoneService timeZones, TimeProvider time) =>
            {
                var now = time.GetUtcNow();
                return TypedResults.Ok(timeZones.SelectableIds
                    .Select(id => new TimeZoneResponse(id, (int)timeZones.GetUtcOffset(id, now).TotalMinutes))
                    .ToList());
            })
            .WithTags("TimeZones");

        return app;
    }
}

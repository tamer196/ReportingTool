namespace ReportingTool.Api.Endpoints;

/// <summary>
/// <c>/api/greetings</c>: the hello-world slice behind the Angular home page.
/// <list type="bullet">
/// <item><c>GET</c> → 200 with the newest greetings, newest first.</item>
/// <item><c>POST</c> → 201 with the saved greeting; 400 problem details when the message is
/// missing, blank or longer than <see cref="Greeting.MessageMaxLength"/>.</item>
/// </list>
/// </summary>
public static class GreetingEndpoints
{
    /// <summary>How many greetings the page shows. Page design, not deployment config.</summary>
    private const int LatestCount = 20;

    public static IEndpointRouteBuilder MapGreetingEndpoints(this IEndpointRouteBuilder app)
    {
        var greetings = app.MapGroup("/api/greetings").WithTags("Greetings");

        greetings.MapGet("/", static async (IGreetingService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListLatestAsync(LatestCount, ct)));

        greetings.MapPost("/", static async (CreateGreetingRequest request, IGreetingService service) =>
        {
            var created = await service.CreateAsync(request.Message);
            return TypedResults.Created($"/api/greetings/{created.Id}", created);
        });

        return app;
    }
}

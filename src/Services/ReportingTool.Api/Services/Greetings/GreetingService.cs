using Microsoft.EntityFrameworkCore;

namespace ReportingTool.Api.Services.Greetings;

/// <summary>EF Core implementation of <see cref="IGreetingService"/>.</summary>
public sealed class GreetingService : IGreetingService
{
    private readonly ReportingDbContext _db;
    private readonly TimeProvider _time;

    public GreetingService(ReportingDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<IReadOnlyList<GreetingResponse>> ListLatestAsync(int count, CancellationToken ct = default) =>
        await _db.Greetings
            .AsNoTracking()
            .OrderByDescending(static g => g.CreatedAtUtc)
            .ThenByDescending(static g => g.Id)
            .Take(count)
            .Select(static g => new GreetingResponse(g.Id, g.Message, g.CreatedAtUtc))
            .ToListAsync(ct);

    public async Task<GreetingResponse> CreateAsync(string message)
    {
        var greeting = new Greeting
        {
            Message = message.Trim(),
            CreatedAtUtc = _time.GetUtcNow().UtcDateTime,
        };
        _db.Greetings.Add(greeting);

        // Durable write: CancellationToken.None so a client that hangs up after sending a valid
        // greeting cannot lose it half-way. Database:CommandTimeoutSeconds bounds the wait.
        await _db.SaveChangesAsync(CancellationToken.None);

        return new GreetingResponse(greeting.Id, greeting.Message, greeting.CreatedAtUtc);
    }
}

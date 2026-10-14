using Microsoft.EntityFrameworkCore;

namespace ReportingTool.Api.Persistence;

/// <summary>
/// The API's SQL Server database. Schema changes are EF Core migrations under
/// <c>Persistence/Migrations</c>, applied at startup by the platform's migration hosted service
/// (see the Developer Setup guide for the <c>dotnet ef</c> commands).
/// </summary>
public sealed class ReportingDbContext : DbContext
{
    public ReportingDbContext(DbContextOptions<ReportingDbContext> options) : base(options)
    {
    }

    public DbSet<Greeting> Greetings => Set<Greeting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.StoreDateTimesAsUtc();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Greeting>(greeting =>
        {
            greeting.ToTable("Greetings");
            greeting.Property(static g => g.Message).HasMaxLength(Greeting.MessageMaxLength);
            greeting.HasIndex(static g => g.CreatedAtUtc);

            // A row is present from the first migration on, so a fresh environment shows data
            // as soon as the slice is deployed.
            greeting.HasData(new Greeting
            {
                Id = 1,
                Message = "Hello from SQL Server",
                CreatedAtUtc = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc),
            });
        });
    }
}

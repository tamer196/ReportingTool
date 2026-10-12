using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(ReportingTool.Api.Tests.ApiFactory))]

namespace ReportingTool.Api.Tests;

/// <summary>
/// Runs the real API in memory against a real SQL Server in a throwaway container, so the tests
/// cover the same path as the deployed slice: migrations, the UTC converter, validation and the
/// error boundary. Needs Docker (available on the GitHub-hosted Ubuntu runners). One instance is
/// shared by the whole test assembly.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Same SQL Server version as docker-compose.yml, so local and CI match.</summary>
    private const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";

    private readonly MsSqlContainer _sql = new MsSqlBuilder(SqlServerImage).Build();

    /// <summary>The API's clock. Tests move it forward instead of sleeping.</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero));

    public async ValueTask InitializeAsync()
    {
        await _sql.StartAsync();

        // Build the host once, before any test runs. Test classes run in parallel; if each
        // started the server lazily from CreateClient(), Program.cs would run twice at the same
        // time and Serilog's static bootstrap logger would be frozen twice.
        StartServer();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": that would load the developer's user-secrets into the test run.
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", _sql.GetConnectionString());
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Time));
    }
}

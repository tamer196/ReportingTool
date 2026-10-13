using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ReportingTool.Reports.HostedServices;

/// <summary>
/// Installs Hangfire's tables after EF Core has created the database. Hangfire's own
/// "prepare schema" runs when its storage is first built, which on a fresh environment is before
/// the database exists, and it does not try again. The installer takes an application lock, so
/// replicas starting together are safe.
/// </summary>
public sealed class HangfireSchemaInstaller : IHostedService
{
    public const string Schema = "hangfire";

    private readonly DatabaseOptions _database;
    private readonly ILoggerService _logger;

    public HangfireSchemaInstaller(IOptions<DatabaseOptions> database, ILoggerService logger)
    {
        _database = database.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync(ct);
        SqlServerObjectsInstaller.Install(connection, Schema, enableHeavyMigrations: false);
        _logger.Info("[BOOT] Hangfire schema {Schema} is ready.", Schema);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

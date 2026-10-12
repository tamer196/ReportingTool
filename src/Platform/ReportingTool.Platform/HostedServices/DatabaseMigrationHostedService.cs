using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ReportingTool.Platform.HostedServices;

/// <summary>
/// Applies pending EF Core migrations for <typeparamref name="TContext"/> before the service
/// accepts traffic, when <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/> is set.
/// <para>
/// A failure here is a startup failure on purpose (fail-fast): the exception escapes, the host
/// does not start, and the orchestrator restarts the pod until SQL Server is reachable. A service
/// running against an out-of-date schema would fail later in confusing ways. Several replicas
/// starting together are safe: EF Core takes a database lock while it migrates.
/// </para>
/// </summary>
public sealed class DatabaseMigrationHostedService<TContext> : IHostedService
    where TContext : DbContext
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DatabaseOptions _options;
    private readonly ILoggerService _logger;

    public DatabaseMigrationHostedService(IServiceScopeFactory scopes, IOptions<DatabaseOptions> options,
        ILoggerService logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_options.ApplyMigrationsOnStartup != true)
        {
            _logger.Info("[DB] Migrations on startup are off for {Context}; the deployment pipeline owns the schema.",
                typeof(TContext).Name);
            return;
        }

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0)
        {
            _logger.Info("[DB] {Context} schema is up to date.", typeof(TContext).Name);
            return;
        }

        _logger.Info("[DB] Applying {Count} migration(s) to {Context}: {Migrations}",
            pending.Count, typeof(TContext).Name, pending);
        await db.Database.MigrateAsync(ct);
        _logger.Info("[DB] {Context} schema migrated.", typeof(TContext).Name);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

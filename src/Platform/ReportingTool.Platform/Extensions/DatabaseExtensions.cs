using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ReportingTool.Platform.Extensions;

/// <summary>SQL Server wiring shared by every service that owns a database.</summary>
public static class DatabaseExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> against SQL Server from the validated
    /// <see cref="DatabaseOptions"/>, adds it to the <c>/health/ready</c> checks, and applies
    /// migrations at startup when configured (<see cref="DatabaseMigrationHostedService{TContext}"/>).
    /// <para>Transient-fault retries use EF Core's SQL Server execution strategy with its default
    /// bounds (6 retries, 30 s maximum delay). Code that opens its own transaction must run it
    /// through <c>Database.CreateExecutionStrategy()</c>.</para>
    /// </summary>
    public static WebApplicationBuilder AddSqlServerDatabase<TContext>(this WebApplicationBuilder builder)
        where TContext : DbContext
    {
        builder.Services.AddOptions<DatabaseOptions>()
            .Bind(builder.Configuration.GetSection(DatabaseOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddDbContext<TContext>(static (services, db) =>
        {
            var options = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            db.UseSqlServer(options.ConnectionString, sql =>
            {
                sql.EnableRetryOnFailure();
                sql.CommandTimeout(options.CommandTimeoutSeconds);
            });
        });

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<TContext>("database", tags: [PlatformHostingExtensions.ReadyTag]);

        builder.Services.AddHostedService<DatabaseMigrationHostedService<TContext>>();
        return builder;
    }
}

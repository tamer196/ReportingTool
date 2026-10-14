using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ReportingTool.Platform.Extensions;

/// <summary>
/// The composition every Reporting Tool service shares, split into named steps so each
/// service's Program.cs still reads in the fixed house order (secrets → logging → platform
/// services → options → domain → build → <see cref="UsePlatform"/>). This file and Program.cs
/// are the only places that touch Serilog types.
/// </summary>
public static class PlatformHostingExtensions
{
    /// <summary>Health-check tag for dependencies that must be up before the service takes traffic.</summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Adds the vault's key-per-file secrets (see <see cref="SecretsOptions"/>) as the
    /// highest-precedence configuration source. Runs first, before anything reads configuration,
    /// and validates its own section immediately because configuration sources are fixed before
    /// the options system exists.
    /// </summary>
    public static WebApplicationBuilder AddVaultSecrets(this WebApplicationBuilder builder)
    {
        var options = new SecretsOptions();
        builder.Configuration.GetSection(SecretsOptions.Section).Bind(options);

        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(options, new ValidationContext(options), errors, validateAllProperties: true))
            throw new OptionsValidationException(SecretsOptions.Section, typeof(SecretsOptions),
                errors.Select(static e => $"{SecretsOptions.Section}: {e.ErrorMessage}"));

        if (options.Required == true && !Directory.Exists(options.Directory))
            throw new InvalidOperationException(
                $"Secrets:Required is true but the secrets directory '{options.Directory}' does not exist. " +
                "The vault volume is not mounted; the service will not start without its credentials.");

        // Read once at startup: options snapshots are taken at boot, so a rotated secret is
        // picked up by a rolling restart rather than a file watcher.
        builder.Configuration.AddKeyPerFile(options.Directory, optional: options.Required != true, reloadOnChange: false);

        builder.Services.AddOptions<SecretsOptions>()
            .Bind(builder.Configuration.GetSection(SecretsOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return builder;
    }

    /// <summary>
    /// Serilog, fully configured from the "Serilog" appsettings section, plus the
    /// <see cref="ILoggerService"/> every class logs through. Structured events carry the
    /// service and environment names so logs from all services can share one store.
    /// </summary>
    public static WebApplicationBuilder AddPlatformLogging(this WebApplicationBuilder builder)
    {
        // The minimum level is owned by a LoggingLevelSwitch so LoggerService.SetMinimumLevel can
        // change it at runtime; it starts at the configured default. Both config shapes are
        // honored — the object form (MinimumLevel:Default) and the string shorthand
        // (MinimumLevel: "Debug") — because ControlledBy() overrides what ReadFrom.Configuration parsed.
        var configuredDefault = builder.Configuration["Serilog:MinimumLevel:Default"]
                                ?? builder.Configuration["Serilog:MinimumLevel"];
        var levelSwitch = new LoggingLevelSwitch(
            Enum.TryParse<LogEventLevel>(configuredDefault, ignoreCase: true, out var configuredLevel)
                ? configuredLevel
                : LogEventLevel.Information);
        builder.Services.AddSingleton(levelSwitch);

        builder.Services.AddSerilog((_, loggerConfiguration) =>
        {
            loggerConfiguration.ReadFrom.Configuration(builder.Configuration)
                .MinimumLevel.ControlledBy(levelSwitch)
                .Enrich.WithProperty("Service", builder.Environment.ApplicationName)
                .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName);

            // Fail-safe: never run sink-less. If the environment's config carries no
            // Serilog:WriteTo section, fall back to the console so a failure can never die silently.
            if (!builder.Configuration.GetSection("Serilog:WriteTo").Exists())
                loggerConfiguration.WriteTo.Console();
        });

        builder.Services.AddSingleton<ILoggerService, LoggerService>();
        return builder;
    }

    /// <summary>
    /// The error boundaries, problem-details responses, the UTC clock (BR-14), the time zone
    /// list (TD-12) and the health check registry that <see cref="DatabaseExtensions.AddSqlServerDatabase{TContext}"/> and
    /// later dependencies add their readiness checks to.
    /// </summary>
    public static WebApplicationBuilder AddPlatformServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IGlobalExceptionHandler, GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

        // BR-14: the only clock in the codebase (DateTime.Now/UtcNow are banned at compile time).
        // Tests replace it with a FakeTimeProvider.
        builder.Services.TryAddSingleton(TimeProvider.System);

        // TD-12: the one list of valid IANA time zones (#95), shared by every service.
        builder.Services.AddSingleton<ITimeZoneService, TimeZoneService>();

        builder.Services.AddHealthChecks();
        return builder;
    }

    /// <summary>
    /// Middleware and endpoints every service exposes: process hooks, request logging, the HTTP
    /// error boundary, and the two health endpoints the orchestrator probes —
    /// <c>/health/live</c> (process is up; no dependency checks, so a database outage never
    /// causes a restart loop) and <c>/health/ready</c> (dependencies tagged
    /// <see cref="ReadyTag"/> are reachable; failing it only takes the pod out of the load balancer).
    /// </summary>
    public static WebApplication UsePlatform(this WebApplication app)
    {
        app.Services.GetRequiredService<IGlobalExceptionHandler>().RegisterProcessHooks();

        // Request logging wraps the error boundary so it records the final status code.
        app.UseSerilogRequestLogging(static options =>
            options.GetLevel = static (context, _, exception) =>
                exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? LogEventLevel.Error
                    : context.Request.Path.StartsWithSegments("/health")
                        ? LogEventLevel.Verbose // probes hit every few seconds; keep them out of the logs
                        : LogEventLevel.Information);

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = static _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = static check => check.Tags.Contains(ReadyTag) });

        return app;
    }
}

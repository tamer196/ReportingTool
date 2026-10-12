using Serilog;

// Reporting Tool API: the first deployable .NET service. Its components share one process
// and one DI container:
//   HTTP endpoints: /api/greetings (the RT-106 hello-world slice), /api/system/info,
//                   /health/live, /health/ready
//   DatabaseMigrationHostedService: applies EF Core migrations before traffic (when configured)
//
// Two-stage Serilog initialization: a bootstrap logger catches host-build failures,
// then the fully configured logger (read from appsettings) replaces it.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting ReportingTool.Api host");

    var builder = WebApplication.CreateBuilder(args);

    // ----- Platform: vault secrets first (everything after may read them), then logging,
    // the error boundaries, the UTC clock and the health-check registry -----
    builder.AddVaultSecrets();
    builder.AddPlatformLogging();
    builder.AddPlatformServices();

    // ----- Options + data: bound from config, validated on start, NO code defaults -----
    builder.AddSqlServerDatabase<ReportingDbContext>();

    // ----- Domain services -----
    builder.Services.AddScoped<IGreetingService, GreetingService>();

    // ----- HTTP surface -----
    builder.Services.AddValidation();
    builder.Services.AddOpenApi();

    var app = builder.Build();

    app.UsePlatform();
    if (app.Environment.IsDevelopment())
        app.MapOpenApi();
    app.MapSystemInfoEndpoints();
    app.MapGreetingEndpoints();

    var log = app.Services.GetRequiredService<ILoggerService>();
    log.Info("[BOOT] ReportingTool.Api host built for {Environment}, starting (minimum log level {Level}).",
        app.Environment.EnvironmentName, log.MinimumLevel);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException is how `dotnet ef` and WebApplicationFactory stop the host right
    // after building it. It is not a failure.
    Log.Fatal(ex, "ReportingTool.Api terminated unexpectedly");
    Environment.ExitCode = 1; // so Compose, Kubernetes and CI see a failed start
}
finally
{
    Log.CloseAndFlush();
}

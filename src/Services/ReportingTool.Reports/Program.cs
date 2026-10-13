using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Serilog;

// RT-108 proof of concept: the Report service stand-in for the revised ADR-001 (option 1: our
// own designer and layout model, HTML printed by headless Chromium, ClosedXML for XLSX).
//   HTTP endpoints: /api/reports/... (catalog, templates, runs), /health/live, /health/ready
//   Hangfire server: queue "reports" renders runs in the background (G4)
//   Hosted services: EF migrations, Hangfire schema, then the synthetic data seed (D-21)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting ReportingTool.Reports host");

    var builder = WebApplication.CreateBuilder(args);

    builder.AddVaultSecrets();
    builder.AddPlatformLogging();
    builder.AddPlatformServices();

    builder.AddSqlServerDatabase<ReportsDbContext>();
    builder.Services.AddOptions<AuthOptions>().Bind(builder.Configuration.GetSection(AuthOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
    builder.Services.AddOptions<RenderingOptions>().Bind(builder.Configuration.GetSection(RenderingOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
    builder.Services.AddOptions<OutputStorageOptions>().Bind(builder.Configuration.GetSection(OutputStorageOptions.Section)).ValidateDataAnnotations().ValidateOnStart();

    // D-17: standard OIDC bearer tokens only, so Keycloak can be swapped for another provider.
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
    builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
        .Configure<IOptions<AuthOptions>>(static (jwt, auth) =>
        {
            jwt.MetadataAddress = auth.Value.MetadataAddress;
            jwt.RequireHttpsMetadata = auth.Value.RequireHttpsMetadata == true;
            jwt.MapInboundClaims = false;
            jwt.TokenValidationParameters.ValidIssuer = auth.Value.Issuer;
            jwt.TokenValidationParameters.ValidAudience = auth.Value.Audience;
            jwt.TokenValidationParameters.NameClaimType = "preferred_username";
        });
    builder.Services.AddAuthorization();

    builder.Services.AddHangfire(static (services, hangfire) => hangfire
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSqlServerStorage(
            () => new SqlConnection(services.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString),
            new SqlServerStorageOptions
            {
                SchemaName = HangfireSchemaInstaller.Schema,
                PrepareSchemaIfNecessary = false, // HangfireSchemaInstaller does it once the database exists
                TryAutoDetectSchemaDependentOptions = false,
            }));
    builder.Services.AddHostedService<HangfireSchemaInstaller>(); // after the EF migrations, before the server
    builder.Services.AddHangfireServer(static (services, server) =>
    {
        server.Queues = ["reports"];
        server.WorkerCount = services.GetRequiredService<IOptions<RenderingOptions>>().Value.MaxConcurrentRenders;
        server.ServerName = Environment.MachineName;
    });

    builder.Services.AddScoped<IQueryService, QueryService>();
    builder.Services.AddSingleton<ITemplateValidator, TemplateValidator>();
    builder.Services.AddScoped<ITemplateService, TemplateService>();
    builder.Services.AddScoped<IRunService, RunService>();
    builder.Services.AddScoped<IReportComposer, HtmlReportComposer>();
    builder.Services.AddScoped<IXlsxWriter, XlsxReportWriter>();
    builder.Services.AddSingleton<IPdfRenderer, ChromiumPdfRenderer>();
    builder.Services.AddSingleton<IPdfAssembler, QpdfAssembler>();
    builder.Services.AddScoped<RenderReportJob>();
    builder.Services.AddHostedService<SyntheticDataSeeder>();

    builder.Services.AddValidation();
    builder.Services.ConfigureHttpJsonOptions(static o => ReportJson.Configure(o.SerializerOptions));

    var app = builder.Build();

    app.UsePlatform();
    // G5 evidence: which replica answered. PoC only.
    app.Use(static (context, next) =>
    {
        context.Response.Headers["X-Served-By"] = Environment.MachineName;
        return next(context);
    });
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapReportEndpoints();

    var log = app.Services.GetRequiredService<ILoggerService>();
    log.Info("[BOOT] ReportingTool.Reports host built for {Environment}.", app.Environment.EnvironmentName);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "ReportingTool.Reports terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

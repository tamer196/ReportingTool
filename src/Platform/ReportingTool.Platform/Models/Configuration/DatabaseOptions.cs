using System.ComponentModel.DataAnnotations;

namespace ReportingTool.Platform.Models.Configuration;

/// <summary>
/// The service's SQL Server database (bound from the "Database" section). No code defaults —
/// values must come from config; a missing/empty section fails fast at startup.
/// </summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>
    /// SQL Server connection string. This is a SECRET: it comes from the vault mount (file
    /// <c>Database__ConnectionString</c>, see <see cref="SecretsOptions"/>) or from user-secrets
    /// on a developer machine. The committed appsettings files leave it empty.
    /// </summary>
    [Required] public string ConnectionString { get; set; } = null!;

    /// <summary>
    /// Seconds before a SQL command times out. This also bounds the durable writes that
    /// deliberately ignore the request's cancellation token.
    /// </summary>
    [Range(1, 600)] public int CommandTimeoutSeconds { get; set; }

    /// <summary>
    /// Whether the service applies pending EF Core migrations when it starts. True for local,
    /// dev and test, where the newest build owns the schema. Must be set consciously: an omitted
    /// value fails boot instead of silently meaning either answer.
    /// </summary>
    [Required] public bool? ApplyMigrationsOnStartup { get; set; }
}

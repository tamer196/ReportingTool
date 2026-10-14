using System.ComponentModel.DataAnnotations;

namespace ReportingTool.Platform.Models.Configuration;

/// <summary>
/// Where the service reads its secrets (bound from the "Secrets" section). No code defaults —
/// values must come from config; a missing/empty section fails fast at startup.
///
/// <para>Platform secrets are files, one per key, in a directory the platform mounts read-only
/// (Docker Compose secrets locally, ADR-002 §5.2). Any vault that can mount files works the same
/// way, so the service code does not change if the vault does. The file name is the
/// configuration key with <c>__</c> as the section separator, e.g.
/// <c>Database__ConnectionString</c>. Secrets are never environment variables or pipeline
/// variables (RT-106 AC 4). Data-source credentials, written at runtime, use the vault's API
/// instead (RT-125).</para>
/// </summary>
public sealed class SecretsOptions
{
    public const string Section = "Secrets";

    /// <summary>Absolute path of the mounted secrets directory (normally <c>/run/secrets</c>).</summary>
    [Required] public string Directory { get; set; } = null!;

    /// <summary>
    /// True in the shared Dev and Test environments, where a missing mount means the secrets were
    /// not provisioned and the service must not start without them. False on a developer machine,
    /// where <c>dotnet run</c> gets the same keys from user-secrets instead.
    /// </summary>
    [Required] public bool? Required { get; set; }
}

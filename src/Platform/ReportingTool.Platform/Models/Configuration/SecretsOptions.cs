using System.ComponentModel.DataAnnotations;

namespace ReportingTool.Platform.Models.Configuration;

/// <summary>
/// Where the service reads its secrets (bound from the "Secrets" section). No code defaults —
/// values must come from config; a missing/empty section fails fast at startup.
///
/// <para>Secrets are files, one per key, in a directory the platform mounts read-only: Docker
/// Compose secrets on a developer machine, the Kubernetes Secrets Store CSI driver in the cloud
/// (it has providers for both Azure Key Vault and AWS Secrets Manager, so the service code does
/// not change whichever vault ADR-002 picks). The file name is the configuration key with
/// <c>__</c> as the section separator, e.g. <c>Database__ConnectionString</c>. Secrets are never
/// environment variables or pipeline variables (RT-106 AC 4).</para>
/// </summary>
public sealed class SecretsOptions
{
    public const string Section = "Secrets";

    /// <summary>Absolute path of the mounted secrets directory (normally <c>/run/secrets</c>).</summary>
    [Required] public string Directory { get; set; } = null!;

    /// <summary>
    /// True in cloud environments, where a missing mount means the vault integration is broken
    /// and the service must not start without its credentials. False on a developer machine,
    /// where <c>dotnet run</c> gets the same keys from user-secrets instead.
    /// </summary>
    [Required] public bool? Required { get; set; }
}

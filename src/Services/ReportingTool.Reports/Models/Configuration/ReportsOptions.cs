using System.ComponentModel.DataAnnotations;

namespace ReportingTool.Reports.Models.Configuration;

/// <summary>
/// OIDC bearer validation (D-17: Keycloak locally, any OIDC provider later). The issuer is the
/// URL the browser sees; the metadata address is how this container reaches the provider.
/// </summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    [Required] public string Issuer { get; set; } = null!;
    [Required] public string MetadataAddress { get; set; } = null!;
    [Required] public string Audience { get; set; } = null!;
    [Required] public bool? RequireHttpsMetadata { get; set; }
}

/// <summary>Headless Chromium, qpdf and the limits of one render.</summary>
public sealed class RenderingOptions
{
    public const string Section = "Rendering";

    [Required] public string ChromiumPath { get; set; } = null!;
    [Required] public string QpdfPath { get; set; } = null!;
    [Required] public string WorkDirectory { get; set; } = null!;

    /// <summary>Rows per body chunk when a table breaks pages per group. Bounds browser memory.</summary>
    [Range(100, 100_000)] public int ChunkRowTarget { get; set; }

    /// <summary>PDF page cap (#87 AC2). The run stops with "Limit exceeded" above it.</summary>
    [Range(1, 100_000)] public int MaxPdfPages { get; set; }

    /// <summary>Concurrent render jobs per replica (Hangfire workers).</summary>
    [Range(1, 32)] public int MaxConcurrentRenders { get; set; }

    /// <summary>Restart the browser after this many chunk renders, so its memory cannot creep.</summary>
    [Range(1, 10_000)] public int BrowserRecycleAfterRenders { get; set; }

    /// <summary>Timeout for one chunk print, in seconds.</summary>
    [Range(5, 3600)] public int ChunkTimeoutSeconds { get; set; }
}

/// <summary>Where finished outputs go. File system here; S3-compatible storage behind the same seam later (D-15).</summary>
public sealed class OutputStorageOptions
{
    public const string Section = "OutputStorage";

    [Required] public string Directory { get; set; } = null!;
}

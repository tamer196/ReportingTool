namespace ReportingTool.Reports.Models.Runs;

public enum RunStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
}

/// <summary>One report run (TDD v2 ReportInstance, reduced). The principal is stored so the job re-checks access at run time.</summary>
public sealed class ReportRun
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public int TemplateVersion { get; set; }
    public string RequestedBy { get; set; } = null!;
    public string ParametersJson { get; set; } = null!;
    public string Formats { get; set; } = null!;
    public RunStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? Pages { get; set; }
    public long? Rows { get; set; }
    public string? PdfPath { get; set; }
    public string? XlsxPath { get; set; }

    /// <summary>Host that rendered it: evidence for the multi-replica gate (G5).</summary>
    public string? RenderedBy { get; set; }

    /// <summary>Renderer version (ADR-001 §9 rule 4) and timings and memory samples (G4 evidence).</summary>
    public string? MetricsJson { get; set; }
}

public sealed record StartRunRequest(Guid TemplateId, IReadOnlyDictionary<string, string?> Parameters, IReadOnlyList<string> Formats);

public sealed record RunResponse(
    Guid Id, Guid TemplateId, int TemplateVersion, string Status, string? Error,
    DateTime CreatedAtUtc, DateTime? StartedAtUtc, DateTime? CompletedAtUtc,
    int? Pages, long? Rows, bool HasPdf, bool HasXlsx, string? RenderedBy, string? MetricsJson);

/// <summary>Timings, sizes and memory of one run, stored as JSON on the run.</summary>
public sealed record RenderMetrics(
    string Engine,
    string Browser,
    int Chunks,
    int Pages,
    long Rows,
    double QueryAndComposeSeconds,
    double PdfRenderSeconds,
    double AssembleSeconds,
    double XlsxSeconds,
    double TotalSeconds,
    double PeakContainerRssMb,
    double PeakBrowserRssMb,
    double PeakManagedMb,
    long PdfBytes,
    long XlsxBytes,
    int BlockedRequests,
    IReadOnlyList<MemorySample> Samples);

public sealed record MemorySample(double Seconds, double ContainerRssMb, double BrowserRssMb);

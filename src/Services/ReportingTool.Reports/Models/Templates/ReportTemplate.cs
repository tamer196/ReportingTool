namespace ReportingTool.Reports.Models.Templates;

/// <summary>
/// A saved report template. The PoC keeps one row with a version counter; the product stores an
/// immutable TemplateVersion per save (TDD v2 §3.5).
/// </summary>
public sealed class ReportTemplate
{
    public Guid Id { get; set; }
    public string OwnerSubject { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DefinitionJson { get; set; } = null!;
    public int Version { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed record SaveTemplateRequest(string Name, Layout.ReportDefinition Definition);

public sealed record TemplateSummary(Guid Id, string Name, int Version, DateTime UpdatedAtUtc);

public sealed record TemplateResponse(Guid Id, string Name, int Version, DateTime UpdatedAtUtc, Layout.ReportDefinition Definition);

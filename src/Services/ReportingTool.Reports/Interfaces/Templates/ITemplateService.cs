namespace ReportingTool.Reports.Interfaces.Templates;

/// <summary>Saves and loads report templates. Every call is scoped to the calling author.</summary>
public interface ITemplateService
{
    Task<IReadOnlyList<TemplateSummary>> ListAsync(string subject, CancellationToken ct);

    /// <summary>The template, or null when it does not exist or belongs to someone else (404 either way).</summary>
    Task<TemplateResponse?> FindAsync(string subject, Guid id, CancellationToken ct);

    /// <summary>
    /// Validates the definition against the author's own schema, then creates or updates it.
    /// Returns the validation errors instead of saving when there are any.
    /// </summary>
    Task<(TemplateResponse? Saved, IReadOnlyList<string> Errors)> SaveAsync(
        string subject, Guid id, SaveTemplateRequest request, CancellationToken ct);
}

/// <summary>
/// Checks a definition against one author's view of the catalog (G3): every dataset must be
/// granted, every field visible or masked, and masked fields are never filtered, sorted or grouped.
/// </summary>
public interface ITemplateValidator
{
    IReadOnlyList<string> Validate(ReportDefinition definition, IReadOnlyList<DatasetSchema> schemas);
}

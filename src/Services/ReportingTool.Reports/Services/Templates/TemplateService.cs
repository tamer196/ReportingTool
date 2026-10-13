using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ReportingTool.Reports.Services.Templates;

/// <summary>EF Core template storage. Only the owner can read or change a template.</summary>
public sealed class TemplateService : ITemplateService
{
    private readonly ReportsDbContext _db;
    private readonly IQueryService _query;
    private readonly ITemplateValidator _validator;
    private readonly TimeProvider _time;

    public TemplateService(ReportsDbContext db, IQueryService query, ITemplateValidator validator, TimeProvider time)
    {
        _db = db;
        _query = query;
        _validator = validator;
        _time = time;
    }

    public async Task<IReadOnlyList<TemplateSummary>> ListAsync(string subject, CancellationToken ct) =>
        await _db.Templates.AsNoTracking()
            .Where(t => t.OwnerSubject == subject)
            .OrderBy(static t => t.Name)
            .Select(static t => new TemplateSummary(t.Id, t.Name, t.Version, t.UpdatedAtUtc))
            .ToListAsync(ct);

    public async Task<TemplateResponse?> FindAsync(string subject, Guid id, CancellationToken ct)
    {
        var template = await _db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.OwnerSubject == subject, ct);
        return template is null ? null : ToResponse(template);
    }

    public async Task<(TemplateResponse? Saved, IReadOnlyList<string> Errors)> SaveAsync(
        string subject, Guid id, SaveTemplateRequest request, CancellationToken ct)
    {
        var errors = _validator.Validate(request.Definition, await _query.ListSchemasAsync(subject, ct)).ToList();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
            errors.Add("A name of 1 to 200 characters is required.");

        var existing = await _db.Templates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (existing is not null && existing.OwnerSubject != subject)
            errors.Add("Template not found.");
        if (await _db.Templates.AnyAsync(t => t.OwnerSubject == subject && t.Name == request.Name && t.Id != id, ct))
            errors.Add("You already have a template with this name (BR-11).");
        if (errors.Count > 0)
            return (null, errors);

        var template = existing ?? _db.Templates.Add(new ReportTemplate { Id = id, OwnerSubject = subject }).Entity;
        template.Name = request.Name.Trim();
        template.DefinitionJson = JsonSerializer.Serialize(request.Definition, ReportJson.Options);
        template.Version++;
        template.UpdatedAtUtc = _time.GetUtcNow().UtcDateTime;

        await _db.SaveChangesAsync(CancellationToken.None);
        return (ToResponse(template), []);
    }

    private static TemplateResponse ToResponse(ReportTemplate t) =>
        new(t.Id, t.Name, t.Version, t.UpdatedAtUtc, JsonSerializer.Deserialize<ReportDefinition>(t.DefinitionJson, ReportJson.Options)!);
}

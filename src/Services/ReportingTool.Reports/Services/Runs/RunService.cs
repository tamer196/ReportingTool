using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ReportingTool.Reports.Services.Runs;

/// <summary>Creates run rows and hands them to Hangfire. Status and files are only ever served to the requester.</summary>
public sealed class RunService : IRunService
{
    private static readonly HashSet<string> KnownFormats = ["pdf", "xlsx"];

    private readonly ReportsDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly TimeProvider _time;

    public RunService(ReportsDbContext db, IBackgroundJobClient jobs, TimeProvider time)
    {
        _db = db;
        _jobs = jobs;
        _time = time;
    }

    public async Task<RunResponse?> StartAsync(string subject, StartRunRequest request, CancellationToken ct)
    {
        var template = await _db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == request.TemplateId && t.OwnerSubject == subject, ct);
        if (template is null)
            return null;

        var formats = request.Formats.Where(KnownFormats.Contains).Distinct().ToList();
        var run = new ReportRun
        {
            Id = Guid.CreateVersion7(_time.GetUtcNow()),
            TemplateId = template.Id,
            TemplateVersion = template.Version,
            RequestedBy = subject,
            ParametersJson = JsonSerializer.Serialize(request.Parameters),
            Formats = string.Join(',', formats.Count > 0 ? formats : ["pdf"]),
            Status = RunStatus.Queued,
            CreatedAtUtc = _time.GetUtcNow().UtcDateTime,
        };
        _db.Runs.Add(run);
        await _db.SaveChangesAsync(CancellationToken.None);

        _jobs.Enqueue<RenderReportJob>(job => job.ExecuteAsync(run.Id, CancellationToken.None));
        return ToResponse(run);
    }

    public async Task<RunResponse?> FindAsync(string subject, Guid runId, CancellationToken ct)
    {
        var run = await _db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId && r.RequestedBy == subject, ct);
        return run is null ? null : ToResponse(run);
    }

    public async Task<string?> FindOutputAsync(string subject, Guid runId, string format, CancellationToken ct)
    {
        var run = await _db.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId && r.RequestedBy == subject, ct);
        return format switch
        {
            "pdf" => run?.PdfPath,
            "xlsx" => run?.XlsxPath,
            _ => null,
        };
    }

    private static RunResponse ToResponse(ReportRun r) =>
        new(r.Id, r.TemplateId, r.TemplateVersion, r.Status.ToString(), r.Error, r.CreatedAtUtc, r.StartedAtUtc, r.CompletedAtUtc,
            r.Pages, r.Rows, r.PdfPath is not null, r.XlsxPath is not null, r.RenderedBy, r.MetricsJson);
}

using System.Diagnostics;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ReportingTool.Reports.Services.Runs;

/// <summary>
/// The background render (G4). It re-checks the requester's access at run time, renders the
/// body in chunks (memory stays flat however many pages there are), stops at the page cap, then
/// lays the header/footer overlay over the merged body. Failures are recorded on the run with a
/// reason the user can act on; unexpected errors go through the platform boundary.
/// </summary>
public sealed class RenderReportJob
{
    private const string Engine = "rt-html-chromium/0.1-poc";

    private readonly ReportsDbContext _db;
    private readonly IQueryService _query;
    private readonly ITemplateValidator _validator;
    private readonly IReportComposer _composer;
    private readonly IPdfRenderer _pdf;
    private readonly IPdfAssembler _assembler;
    private readonly IXlsxWriter _xlsx;
    private readonly IGlobalExceptionHandler _boundary;
    private readonly ILoggerService _logger;
    private readonly TimeProvider _time;
    private readonly RenderingOptions _rendering;
    private readonly OutputStorageOptions _storage;

    public RenderReportJob(ReportsDbContext db, IQueryService query, ITemplateValidator validator, IReportComposer composer,
        IPdfRenderer pdf, IPdfAssembler assembler, IXlsxWriter xlsx, IGlobalExceptionHandler boundary, ILoggerService logger,
        TimeProvider time, IOptions<RenderingOptions> rendering, IOptions<OutputStorageOptions> storage)
    {
        _db = db;
        _query = query;
        _validator = validator;
        _composer = composer;
        _pdf = pdf;
        _assembler = assembler;
        _xlsx = xlsx;
        _boundary = boundary;
        _logger = logger;
        _time = time;
        _rendering = rendering.Value;
        _storage = storage.Value;
    }

    [Queue("reports")]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(Guid runId, CancellationToken ct)
    {
        var run = await _db.Runs.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is not { Status: RunStatus.Queued })
            return; // already handled: a Hangfire redelivery must not render twice (BR-15)

        run.Status = RunStatus.Running;
        run.StartedAtUtc = _time.GetUtcNow().UtcDateTime;
        run.RenderedBy = Environment.MachineName;
        await _db.SaveChangesAsync(CancellationToken.None);

        var error = await _boundary.RunAsync(() => RenderAsync(run, ct), $"render run {runId}",
            fallback: "Unexpected error while rendering; see the service log.", LogLevel.Error, ct: ct);

        run.Status = error is null ? RunStatus.Succeeded : RunStatus.Failed;
        run.Error = error;
        run.CompletedAtUtc = _time.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(CancellationToken.None);
        _logger.Info("[RUN] {RunId} {Status} on {Host}: {Pages} pages, {Error}", runId, run.Status, run.RenderedBy, run.Pages, error);
    }

    /// <summary>Returns null on success, or the reason the run failed.</summary>
    private async Task<string?> RenderAsync(ReportRun run, CancellationToken ct)
    {
        var total = Stopwatch.StartNew();
        var template = await _db.Templates.AsNoTracking().FirstAsync(t => t.Id == run.TemplateId, ct);
        var definition = JsonSerializer.Deserialize<ReportDefinition>(template.DefinitionJson, ReportJson.Options)!;
        var parameters = JsonSerializer.Deserialize<Dictionary<string, string?>>(run.ParametersJson)!;

        // G3 at run time: grants may have changed since the template was saved.
        var schemas = await _query.ListSchemasAsync(run.RequestedBy, ct);
        var errors = _validator.Validate(definition, schemas);
        if (errors.Count > 0)
            return string.Join("; ", errors);
        var missing = definition.Parameters.Where(p => p.Required && string.IsNullOrWhiteSpace(parameters.GetValueOrDefault(p.Name))).ToList();
        if (missing.Count > 0)
            return "Missing required parameter(s): " + string.Join(", ", missing.Select(static p => p.Label));

        var context = new RenderContext(definition, template.Name, run.RequestedBy, parameters, _time.GetUtcNow(), schemas);
        var work = Directory.CreateDirectory(Path.Combine(_rendering.WorkDirectory, run.Id.ToString("N")));
        var output = Directory.CreateDirectory(Path.Combine(_storage.Directory, run.Id.ToString("N")));
        await using var sampler = new ProcessMemorySampler(TimeSpan.FromMilliseconds(250), _boundary);

        double composeSeconds = 0, pdfSeconds = 0, assembleSeconds = 0, xlsxSeconds = 0;
        int pages = 0, chunks = 0, blocked = 0;
        long rows = 0, pdfBytes = 0, xlsxBytes = 0;
        var browser = string.Empty;
        string? failure = null;

        if (run.Formats.Contains("pdf", StringComparison.Ordinal))
        {
            browser = await _pdf.GetVersionAsync(ct);
            var chunkFiles = new List<string>();
            var groups = new List<GroupSpan>();
            var step = Stopwatch.StartNew();

            await foreach (var chunk in _composer.ComposeBodyAsync(context, ct))
            {
                composeSeconds += step.Elapsed.TotalSeconds;
                var html = Path.Combine(work.FullName, $"body-{chunk.Index:D4}.html");
                var pdf = Path.Combine(work.FullName, $"body-{chunk.Index:D4}.pdf");
                await File.WriteAllTextAsync(html, chunk.Html, ct);

                step.Restart();
                blocked += (await _pdf.RenderAsync(html, pdf, chunk.HasCharts, ct)).BlockedRequests;
                var chunkPages = await _assembler.CountPagesAsync(pdf, ct);
                var headingPages = await _assembler.GetHeadingPagesAsync(pdf, ct);
                pdfSeconds += step.Elapsed.TotalSeconds;
                File.Delete(html);

                var groupPages = headingPages.Where((_, i) => i < chunk.HeadingIsGroup.Count && chunk.HeadingIsGroup[i]).ToList();
                for (var g = 0; g < chunk.GroupLabels.Count && g < groupPages.Count; g++)
                    groups.Add(new GroupSpan(chunk.GroupLabels[g], pages + groupPages[g], 0));

                pages += chunkPages;
                rows += chunk.Rows;
                chunks++;
                chunkFiles.Add(pdf);

                // #87 AC2: stop as soon as the cap is passed, rather than rendering the rest.
                if (pages > _rendering.MaxPdfPages)
                {
                    failure = $"Limit exceeded: the PDF would have more than {_rendering.MaxPdfPages:N0} pages. Use CSV/XLSX or a summary layout.";
                    break;
                }

                step.Restart();
            }

            if (failure is null)
            {
                step.Restart();
                var spans = groups.Select((g, i) => g with { PageCount = (i + 1 < groups.Count ? groups[i + 1].FirstPage : pages + 1) - g.FirstPage }).ToList();
                var overlayHtml = Path.Combine(work.FullName, "overlay.html");
                var overlayPdf = Path.Combine(work.FullName, "overlay.pdf");
                await File.WriteAllTextAsync(overlayHtml, _composer.ComposeOverlay(context, new OverlayPlan(pages, spans)), ct);
                blocked += (await _pdf.RenderAsync(overlayHtml, overlayPdf, waitForCharts: false, ct)).BlockedRequests;
                var overlayPages = await _assembler.CountPagesAsync(overlayPdf, ct);
                if (overlayPages != pages)
                    return $"Internal layout error: overlay has {overlayPages} pages for a body of {pages}.";

                var final = Path.Combine(output.FullName, "report.pdf");
                await _assembler.AssembleAsync(chunkFiles, overlayPdf, final, ct);
                assembleSeconds = step.Elapsed.TotalSeconds;
                run.PdfPath = final;
                pdfBytes = new FileInfo(final).Length;
            }
        }

        if (failure is null && run.Formats.Contains("xlsx", StringComparison.Ordinal))
        {
            var step = Stopwatch.StartNew();
            var final = Path.Combine(output.FullName, "report.xlsx");
            var xlsxRows = await _xlsx.WriteAsync(context, final, ct);
            rows = Math.Max(rows, xlsxRows);
            xlsxSeconds = step.Elapsed.TotalSeconds;
            run.XlsxPath = final;
            xlsxBytes = new FileInfo(final).Length;
        }

        work.Delete(recursive: true);
        var samples = sampler.Stop();
        run.Pages = pages;
        run.Rows = rows;
        run.MetricsJson = JsonSerializer.Serialize(new RenderMetrics(
            Engine, browser, chunks, pages, rows,
            Math.Round(composeSeconds, 2), Math.Round(pdfSeconds, 2), Math.Round(assembleSeconds, 2), Math.Round(xlsxSeconds, 2),
            Math.Round(total.Elapsed.TotalSeconds, 2),
            Math.Round(sampler.PeakContainerMb, 1), Math.Round(sampler.PeakBrowserMb, 1), Math.Round(sampler.PeakManagedMb, 1),
            pdfBytes, xlsxBytes, blocked, samples));
        return failure;
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ReportingTool.Reports.Services.Rendering;

/// <summary>
/// qpdf (Apache-2.0) does the PDF plumbing: page counts, the outline, merging the body chunks
/// and laying the header/footer overlay on every page. It streams, so a 2,000-page merge does
/// not load the document into this process.
/// </summary>
public sealed class QpdfAssembler : IPdfAssembler
{
    /// <summary>qpdf exits with 3 when it succeeded with warnings.</summary>
    private const int SucceededWithWarnings = 3;

    private readonly RenderingOptions _options;

    public QpdfAssembler(IOptions<RenderingOptions> options) => _options = options.Value;

    public async Task<int> CountPagesAsync(string pdfPath, CancellationToken ct) =>
        int.Parse((await RunAsync(["--show-npages", pdfPath], ct)).Trim(), CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<int>> GetHeadingPagesAsync(string pdfPath, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await RunAsync(["--json", "--json-key=outlines", pdfPath], ct));
        var pages = new List<int>();
        void Walk(JsonElement nodes)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                pages.Add(node.GetProperty("destpageposfrom1").GetInt32());
                Walk(node.GetProperty("kids"));
            }
        }

        Walk(json.RootElement.GetProperty("outlines"));
        return pages;
    }

    /// <summary>
    /// The first chunk is qpdf's primary input, so its document-level objects survive: the tag
    /// structure (tagged PDF for screen readers) and the outline (bookmarks). A single-chunk
    /// report stays fully tagged; in a multi-chunk report only the first chunk's pages keep tags.
    /// </summary>
    public Task AssembleAsync(IReadOnlyList<string> bodyChunks, string overlayPath, string outputPath, CancellationToken ct) =>
        bodyChunks.Count == 1
            ? RunAsync([bodyChunks[0], "--overlay", overlayPath, "--", outputPath], ct)
            : RunAsync([bodyChunks[0], "--pages", ".", .. bodyChunks.Skip(1), "--", "--overlay", overlayPath, "--", outputPath], ct);

    private async Task<string> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var start = new ProcessStartInfo(_options.QpdfPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode is not (0 or SucceededWithWarnings))
            throw new InvalidOperationException($"qpdf failed with exit code {process.ExitCode}: {await stderr}");
        return await stdout;
    }
}

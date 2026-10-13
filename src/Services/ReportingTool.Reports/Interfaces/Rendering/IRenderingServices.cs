namespace ReportingTool.Reports.Interfaces.Rendering;

/// <summary>
/// Turns a definition plus Query-service rows into print-ready HTML. The body comes in chunks
/// (bounded memory for 2,000-page outputs); page headers, footers, page numbers and the
/// watermark come from a separate overlay document that is laid over the merged body.
/// </summary>
public interface IReportComposer
{
    IAsyncEnumerable<BodyChunk> ComposeBodyAsync(RenderContext context, CancellationToken ct);

    string ComposeOverlay(RenderContext context, OverlayPlan plan);
}

/// <summary>Prints one HTML file to PDF in the headless browser, with every network request blocked.</summary>
public interface IPdfRenderer
{
    /// <summary>Browser product and version, recorded on each run.</summary>
    Task<string> GetVersionAsync(CancellationToken ct);

    Task<PdfRenderResult> RenderAsync(string htmlPath, string pdfPath, bool waitForCharts, CancellationToken ct);
}

/// <summary>Page counting, outline reading, merging and overlaying with qpdf (Apache-2.0).</summary>
public interface IPdfAssembler
{
    Task<int> CountPagesAsync(string pdfPath, CancellationToken ct);

    /// <summary>The page of each outline entry (one per heading), in document order.</summary>
    Task<IReadOnlyList<int>> GetHeadingPagesAsync(string pdfPath, CancellationToken ct);

    Task AssembleAsync(IReadOnlyList<string> bodyChunks, string overlayPath, string outputPath, CancellationToken ct);
}

/// <summary>Writes the report's tables to XLSX: one worksheet per table (#45 AC2), typed cells, RTL sheets.</summary>
public interface IXlsxWriter
{
    Task<long> WriteAsync(RenderContext context, string outputPath, CancellationToken ct);
}

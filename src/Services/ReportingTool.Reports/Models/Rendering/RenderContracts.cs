using System.Globalization;

namespace ReportingTool.Reports.Models.Rendering;

/// <summary>Everything one render needs. Data is never inside: the composer fetches it as <see cref="Subject"/>.</summary>
public sealed record RenderContext(
    ReportDefinition Definition,
    string ReportName,
    string Subject,
    IReadOnlyDictionary<string, string?> Parameters,
    DateTimeOffset RunAtUtc,
    IReadOnlyList<DatasetSchema> Schemas)
{
    public bool IsRtl => Definition.Layout.Direction == "rtl";

    public CultureInfo Culture => CultureInfo.GetCultureInfo(Definition.Layout.Culture);

    public DatasetSchema Schema(string datasetId) => Schemas.First(s => s.Id == datasetId);

    public ComponentBinding Binding(string componentId) => Definition.Bindings.First(b => b.ComponentId == componentId);
}

/// <summary>
/// One body chunk. <see cref="HeadingIsGroup"/> lists the chunk's headings in document order and
/// marks the group titles, so the outline Chromium writes tells us each group's first page.
/// </summary>
public sealed record BodyChunk(
    int Index,
    string Html,
    IReadOnlyList<bool> HeadingIsGroup,
    IReadOnlyList<string> GroupLabels,
    long Rows,
    bool HasCharts);

/// <summary>Final page count and where each group starts, for {Page}/{Total Pages} per group.</summary>
public sealed record OverlayPlan(int TotalPages, IReadOnlyList<GroupSpan> Groups);

public sealed record GroupSpan(string Label, int FirstPage, int PageCount);

public sealed record PdfRenderResult(int BlockedRequests);

namespace ReportingTool.Reports.Interfaces.Query;

/// <summary>
/// Stand-in for the platform's Query service (TDD v2 §2.4, §5.2): the <b>only</b> path from the
/// designer and the renderer to business data. It checks grants, never lists hidden fields,
/// rejects filters and sorts on masked fields, and masks values before they leave.
/// </summary>
public interface IQueryService
{
    /// <summary>The datasets <paramref name="subject"/> is granted, with hidden fields removed.</summary>
    Task<IReadOnlyList<DatasetSchema>> ListSchemasAsync(string subject, CancellationToken ct);

    /// <summary>
    /// Streams rows for a granted dataset, values in the order of <see cref="QueryRequest.Fields"/>.
    /// Throws <see cref="DataAccessDeniedException"/> when the request reaches outside the
    /// subject's schema: a defence in depth, because callers validate templates first.
    /// </summary>
    IAsyncEnumerable<object?[]> StreamRowsAsync(string subject, QueryRequest request, CancellationToken ct);
}

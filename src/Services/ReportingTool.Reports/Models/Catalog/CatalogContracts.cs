namespace ReportingTool.Reports.Models.Catalog;

/// <summary>A dataset as one author may see it: only granted datasets, never hidden fields, no source names.</summary>
public sealed record DatasetSchema(string Id, string Name, string NameAr, IReadOnlyList<FieldSchema> Fields);

/// <summary>A field as an author may see it. <paramref name="Masked"/> fields cannot be filtered or sorted on.</summary>
public sealed record FieldSchema(string Key, string Label, string LabelAr, string DataType, bool Masked);

/// <summary>A request to the Query service. Field names are catalog keys, never source columns.</summary>
public sealed record QueryRequest(
    string DatasetId,
    IReadOnlyList<string> Fields,
    IReadOnlyList<QueryFilter> Filters,
    IReadOnlyList<string> OrderBy);

public sealed record QueryFilter(string Field, string Op, object Value);

/// <summary>
/// Raised when a request asks for data its principal may not see. The message is safe to show
/// ("Field not available: X"); it never names a source table or column.
/// </summary>
public sealed class DataAccessDeniedException(string message) : Exception(message);

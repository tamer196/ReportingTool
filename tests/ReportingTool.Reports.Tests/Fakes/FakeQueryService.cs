using System.Runtime.CompilerServices;
using ReportingTool.Reports.Interfaces.Query;
using ReportingTool.Reports.Models.Catalog;

namespace ReportingTool.Reports.Tests.Fakes;

/// <summary>
/// An in-memory Query service. Rows are field-keyed so each request gets its values in the order
/// it asked for, the way the real service returns them. Requests are recorded, so tests can prove
/// the renderer reads data as the run's requester and nothing else.
/// </summary>
public sealed class FakeQueryService : IQueryService
{
    private readonly IReadOnlyList<DatasetSchema> _schemas;
    private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _rows;

    public FakeQueryService(IReadOnlyList<DatasetSchema> schemas, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        _schemas = schemas;
        _rows = rows;
    }

    public List<(string Subject, QueryRequest Request)> Requests { get; } = [];

    public Task<IReadOnlyList<DatasetSchema>> ListSchemasAsync(string subject, CancellationToken ct) => Task.FromResult(_schemas);

    public async IAsyncEnumerable<object?[]> StreamRowsAsync(string subject, QueryRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        Requests.Add((subject, request));
        foreach (var row in _rows)
        {
            await Task.Yield();
            yield return request.Fields.Select(f => row.GetValueOrDefault(f)).ToArray();
        }
    }
}

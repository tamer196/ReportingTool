using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ReportingTool.Reports.Services.Query;

/// <summary>
/// The PoC's Query service. SQL is assembled only from catalog rows (source table and column
/// names live in the database, never in a request), and every value is a parameter. Masking is
/// applied to the stream, so a masked value never reaches the renderer, the designer or a file.
/// </summary>
public sealed class QueryService : IQueryService
{
    private static readonly HashSet<string> SupportedOps = ["eq", "gte", "lte"];

    private readonly ReportsDbContext _db;

    public QueryService(ReportsDbContext db) => _db = db;

    public async Task<IReadOnlyList<DatasetSchema>> ListSchemasAsync(string subject, CancellationToken ct)
    {
        var datasets = await _db.Datasets
            .AsNoTracking()
            .Where(d => _db.DatasetGrants.Any(g => g.DatasetId == d.Id && g.Subject == subject))
            .Include(static d => d.Fields)
            .OrderBy(static d => d.Name)
            .ToListAsync(ct);

        return datasets.Select(static d => new DatasetSchema(d.Id, d.Name, d.NameAr,
                d.Fields
                    .Where(static f => f.Visibility != FieldVisibility.Hidden)
                    .OrderBy(static f => f.Ordinal)
                    .Select(static f => new FieldSchema(f.FieldKey, f.Label, f.LabelAr, f.DataType, f.Visibility == FieldVisibility.Masked))
                    .ToList()))
            .ToList();
    }

    public async IAsyncEnumerable<object?[]> StreamRowsAsync(string subject, QueryRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var granted = await _db.DatasetGrants.AnyAsync(g => g.DatasetId == request.DatasetId && g.Subject == subject, ct);
        var dataset = granted
            ? await _db.Datasets.AsNoTracking().Include(static d => d.Fields).FirstOrDefaultAsync(d => d.Id == request.DatasetId, ct)
            : null;
        if (dataset is null)
            throw new DataAccessDeniedException($"Dataset not available: {request.DatasetId}");

        var fields = dataset.Fields.ToDictionary(static f => f.FieldKey);
        DatasetField Visible(string key) =>
            fields.TryGetValue(key, out var f) && f.Visibility != FieldVisibility.Hidden
                ? f
                : throw new DataAccessDeniedException($"Field not available: {key}");
        DatasetField Clear(string key) =>
            Visible(key) is { Visibility: FieldVisibility.Visible } f
                ? f
                : throw new DataAccessDeniedException($"Field not available for filtering or sorting: {key}");

        var selected = request.Fields.Select(Visible).ToList();
        var sql = new StringBuilder("SELECT ")
            .AppendJoin(", ", selected.Select(static f => Quote(f.SourceColumn)))
            .Append(" FROM ").Append(QuoteTable(dataset.SourceTable));

        await using var connection = new SqlConnection(_db.Database.GetConnectionString());
        await using var command = connection.CreateCommand();

        for (var i = 0; i < request.Filters.Count; i++)
        {
            var filter = request.Filters[i];
            if (!SupportedOps.Contains(filter.Op))
                throw new DataAccessDeniedException($"Unsupported filter operator: {filter.Op}");
            var field = Clear(filter.Field);
            sql.Append(i == 0 ? " WHERE " : " AND ")
                .Append(Quote(field.SourceColumn))
                .Append(filter.Op switch { "eq" => " = ", "gte" => " >= ", _ => " <= " })
                .Append("@p").Append(i);
            command.Parameters.Add(ToParameter($"@p{i}", field.DataType, filter.Value));
        }

        if (request.OrderBy.Count > 0)
            sql.Append(" ORDER BY ").AppendJoin(", ", request.OrderBy.Select(k => Quote(Clear(k).SourceColumn)));

        command.CommandText = sql.ToString();
        await connection.OpenAsync(ct);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);

        var masked = selected.Select(static f => f.Visibility == FieldVisibility.Masked).ToArray();
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[selected.Count];
            for (var i = 0; i < row.Length; i++)
            {
                var value = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
                row[i] = value switch
                {
                    null => null,
                    _ when masked[i] => MaskPartial(Convert.ToString(value, CultureInfo.InvariantCulture)!),
                    DateTime d when selected[i].DataType == "date" => DateOnly.FromDateTime(d),
                    _ => value,
                };
            }

            yield return row;
        }
    }

    /// <summary>Keeps the first and last four characters, as on a bank card slip (#15 partial mask).</summary>
    private static string MaskPartial(string value) =>
        value.Length <= 8
            ? new string('•', value.Length)
            : string.Concat(value.AsSpan(0, 4), new string('•', value.Length - 8), value.AsSpan(value.Length - 4));

    private static SqlParameter ToParameter(string name, string dataType, object value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return dataType switch
        {
            "date" => new SqlParameter(name, SqlDbType.Date) { Value = DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue) },
            "number" => new SqlParameter(name, SqlDbType.Decimal) { Value = decimal.Parse(text, CultureInfo.InvariantCulture), Precision = 18, Scale = 2 },
            _ => new SqlParameter(name, SqlDbType.NVarChar, 400) { Value = text },
        };
    }

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static string QuoteTable(string table) => string.Join('.', table.Split('.').Select(Quote));
}

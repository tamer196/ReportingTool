using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace ReportingTool.Reports.Services.Rendering;

/// <summary>
/// Formatted-report XLSX with ClosedXML (MIT; SixLabors.Fonts pinned to Apache-2.0 1.0.1).
/// One worksheet per table component (#45 AC2), right-to-left sheets for RTL reports, real number
/// and date cells, a frozen header row, group headers and subtotals. Excel and LibreOffice shape
/// Arabic themselves, so the file only needs correct Unicode text.
/// </summary>
public sealed partial class XlsxReportWriter : IXlsxWriter
{
    private const int HeaderRow = 4;

    private readonly IQueryService _query;

    public XlsxReportWriter(IQueryService query) => _query = query;

    public async Task<long> WriteAsync(RenderContext ctx, string outputPath, CancellationToken ct)
    {
        using var workbook = new XLWorkbook { RightToLeft = ctx.IsRtl };
        long rows = 0;
        var tables = ctx.Definition.Layout.Body.OfType<TableComponent>().ToList();

        for (var t = 0; t < tables.Count; t++)
        {
            var table = tables[t];
            var binding = ctx.Binding(table.Id);
            var sheet = workbook.Worksheets.Add(SheetName(table.Title, t));
            sheet.RightToLeft = ctx.IsRtl;
            sheet.Style.Font.FontName = ctx.IsRtl ? "Noto Sans Arabic" : "Noto Sans";

            SetText(sheet.Cell(1, 1), ctx.ReportName);
            sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
            SetText(sheet.Cell(2, 1), string.Join(" · ", ctx.Definition.Parameters
                .Where(p => !string.IsNullOrWhiteSpace(ctx.Parameters.GetValueOrDefault(p.Name)))
                .Select(p => $"{p.Label}: {ctx.Parameters[p.Name]}")));

            for (var c = 0; c < table.Columns.Count; c++)
            {
                var column = table.Columns[c];
                var header = sheet.Cell(HeaderRow, c + 1);
                SetText(header, column.Header);
                header.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#E8EDF7"));
                var xlColumn = sheet.Column(c + 1);
                xlColumn.Width = Math.Max(8, (double)column.WidthPct * 1.3);
                xlColumn.Style.NumberFormat.Format = column.Format switch
                {
                    "amount" => "#,##0.00",
                    "integer" => "#,##0",
                    "date" or "dateLong" => "yyyy-mm-dd",
                    _ => "@",
                };
            }

            sheet.SheetView.FreezeRows(HeaderRow);

            var schema = ctx.Schema(binding.DatasetId);
            var headerFields = table.GroupHeader is null ? [] : FieldTokenRegex().Matches(table.GroupHeader).Select(static m => m.Groups[1].Value.Trim());
            var fields = table.Columns.Select(static c => c.Field)
                .Concat(table.GroupBy is null ? [] : [table.GroupBy])
                .Concat(headerFields)
                .Where(f => schema.Fields.Any(x => x.Key == f))
                .Distinct()
                .ToList();
            var at = fields.Select(static (f, i) => (f, i)).ToDictionary(static x => x.f, static x => x.i);
            var filters = binding.Filters
                .Select(f => (f, value: ctx.Parameters.GetValueOrDefault(f.Parameter)))
                .Where(static x => !string.IsNullOrWhiteSpace(x.value))
                .Select(static x => new QueryFilter(x.f.Field, x.f.Op, x.value!))
                .ToList();

            var r = HeaderRow;
            string? group = null;
            var subtotal = new decimal[table.Columns.Count];
            await foreach (var row in _query.StreamRowsAsync(ctx.Subject, new QueryRequest(binding.DatasetId, fields, filters, binding.Sort), ct))
            {
                if (table.GroupBy is not null && Convert.ToString(row[at[table.GroupBy]], CultureInfo.InvariantCulture) is var key && key != group)
                {
                    if (group is not null)
                        r = WriteSubtotal(sheet, table, subtotal, r, ctx);
                    group = key;
                    Array.Clear(subtotal);
                    r++;
                    var groupCell = sheet.Cell(r, 1);
                    // Same group heading as the PDF: "{Field: X}" tokens filled from the group's first row.
                    SetText(groupCell, table.GroupHeader is null
                        ? key
                        : FieldTokenRegex().Replace(table.GroupHeader, m =>
                            at.TryGetValue(m.Groups[1].Value.Trim(), out var i) ? Convert.ToString(row[i], CultureInfo.InvariantCulture) ?? string.Empty : m.Value));
                    sheet.Range(r, 1, r, table.Columns.Count).Merge().Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#F3F5F9"));
                }

                r++;
                for (var c = 0; c < table.Columns.Count; c++)
                {
                    var value = row[at[table.Columns[c].Field]];
                    var cell = sheet.Cell(r, c + 1);
                    switch (value)
                    {
                        case decimal d:
                            cell.Value = d;
                            if (table.Columns[c].Total)
                                subtotal[c] += d;
                            break;
                        case DateOnly date:
                            cell.Value = date.ToDateTime(TimeOnly.MinValue);
                            break;
                        case null:
                            break;
                        default:
                            SetText(cell, Convert.ToString(value, CultureInfo.InvariantCulture));
                            break;
                    }
                }

                rows++;
            }

            if (group is not null)
                WriteSubtotal(sheet, table, subtotal, r, ctx);
        }

        workbook.SaveAs(outputPath);
        return rows;
    }

    private static int WriteSubtotal(IXLWorksheet sheet, TableComponent table, decimal[] totals, int r, RenderContext ctx)
    {
        if (!table.Columns.Any(static c => c.Total))
            return r;
        r++;
        sheet.Cell(r, 1).Value = ctx.IsRtl ? "المجموع الفرعي" : "Subtotal";
        for (var c = 0; c < table.Columns.Count; c++)
        {
            if (table.Columns[c].Total)
                sheet.Cell(r, c + 1).Value = totals[c];
        }

        sheet.Row(r).Style.Font.SetBold();
        return r;
    }

    /// <summary>
    /// Writes author or data text as a text cell, never a formula. A value a spreadsheet would read
    /// as a formula (it starts with = + - @, tab or CR) also gets Excel's quote prefix, so it stays
    /// text even when someone edits the cell (doc 09 SEC-08, #31; OWASP CSV injection).
    /// </summary>
    private static void SetText(IXLCell cell, string? text)
    {
        cell.Value = text ?? string.Empty;
        if (text is ['=' or '+' or '-' or '@' or '\t' or '\r', ..])
            cell.Style.IncludeQuotePrefix = true;
    }

    [GeneratedRegex(@"\{\s*Field:\s*([^{}]+)\}")]
    private static partial Regex FieldTokenRegex();

    private static string SheetName(string? title, int index)
    {
        var name = new string((title ?? $"Table {index + 1}").Where(static ch => ch is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray());
        return (name.Length > 28 ? name[..28] : name) + (index > 0 ? $" {index + 1}" : string.Empty);
    }
}

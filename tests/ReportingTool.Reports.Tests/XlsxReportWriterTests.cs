using System.IO.Compression;
using ClosedXML.Excel;
using ReportingTool.Reports.Models.Layout;
using ReportingTool.Reports.Models.Rendering;
using ReportingTool.Reports.Services.Rendering;
using ReportingTool.Reports.Tests.Fakes;

namespace ReportingTool.Reports.Tests;

/// <summary>
/// Formula injection (doc 09 SEC-08, #31; Security1's finding 2 on RT-108). Author and data text
/// that a spreadsheet would read as a formula must reach the file as text. The saved workbook is
/// read back, because only the file proves what ClosedXML actually stored.
/// </summary>
public sealed class XlsxReportWriterTests : IDisposable
{
    private const int HeaderRow = 4;
    private const int FirstDataRow = HeaderRow + 1;
    private const int NotesColumn = 2;
    private const int AmountColumn = 3;

    private readonly List<string> _files = [];

    public void Dispose()
    {
        foreach (var file in _files)
            File.Delete(file);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://203.0.113.10/\",\"Click\")")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3+cmd|' /C calc'!A0")]
    [InlineData("@SUM(1+1)*cmd|' /C calc'!A0")]
    [InlineData("\t=1+1")]
    [InlineData("\r=1+1")]
    public async Task Formula_like_text_is_saved_as_quoted_text_never_as_a_formula(string payload)
    {
        var table = Definitions.Table() with
        {
            Columns =
            [
                new TableColumn { Field = "ClientId", Header = "Client", WidthPct = 20, Format = "text" },
                new TableColumn { Field = "Notes", Header = payload, WidthPct = 50, Format = "text" },
                new TableColumn { Field = "Amount", Header = "Amount", WidthPct = 30, Format = "amount" },
            ],
        };

        var path = await WriteAsync(Definitions.Report(body: [table]), payload, [Definitions.Row("CL-1", payload, -1234.5m)]);

        using (var workbook = new XLWorkbook(path))
        {
            var sheet = workbook.Worksheet(1);
            foreach (var cell in new[] { sheet.Cell(1, 1), sheet.Cell(HeaderRow, NotesColumn), sheet.Cell(FirstDataRow, NotesColumn) })
            {
                Assert.False(cell.HasFormula, cell.Address.ToString());
                Assert.Equal(XLDataType.Text, cell.DataType);
                // XML turns a bare CR into LF when the file is read, so compare it that way.
                Assert.Equal(payload.Replace('\r', '\n'), cell.GetText());
                Assert.True(cell.Style.IncludeQuotePrefix, cell.Address.ToString());
            }
        }

        Assert.DoesNotMatch("<(x:)?f[ >]", SheetXml(path));
    }

    [Fact]
    public async Task Numbers_stay_numbers_and_plain_text_gets_no_quote_prefix()
    {
        var path = await WriteAsync(Definitions.Report(), "Client statement", [Definitions.Row("CL-1", "Cash deposit", -1234.5m)]);

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        var amount = sheet.Cell(FirstDataRow, AmountColumn);
        Assert.Equal(XLDataType.Number, amount.DataType);
        Assert.Equal(-1234.5, amount.GetDouble());
        Assert.False(amount.Style.IncludeQuotePrefix);
        Assert.False(sheet.Cell(FirstDataRow, NotesColumn).Style.IncludeQuotePrefix);
    }

    private async Task<string> WriteAsync(ReportDefinition definition, string reportName, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var writer = new XlsxReportWriter(new FakeQueryService(Definitions.AuthorSchema, rows));
        var context = new RenderContext(definition, reportName, "author.ar", new Dictionary<string, string?>(),
            new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.Zero), Definitions.AuthorSchema);
        var path = Path.Combine(Path.GetTempPath(), $"rt108-{Path.GetRandomFileName()}.xlsx");
        _files.Add(path);
        await writer.WriteAsync(context, path, CancellationToken.None);
        return path;
    }

    private static string SheetXml(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return reader.ReadToEnd();
    }
}

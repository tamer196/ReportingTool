using ReportingTool.Reports.Models.Catalog;
using ReportingTool.Reports.Models.Layout;

namespace ReportingTool.Reports.Tests.Fakes;

/// <summary>A small catalog and a one-table report, shaped like the PoC's client-transactions dataset.</summary>
public static class Definitions
{
    public const string Dataset = "client-transactions";

    /// <summary>What author.ar sees: NationalId is hidden (absent), Iban is masked.</summary>
    public static IReadOnlyList<DatasetSchema> AuthorSchema { get; } =
    [
        new(Dataset, "Client transactions", "حركات العملاء",
        [
            new("ClientId", "Client ID", "رقم العميل", "text", false),
            new("ClientNameAr", "Client name (Arabic)", "اسم العميل", "text", false),
            new("TradeDate", "Trade date", "تاريخ الصفقة", "date", false),
            new("Amount", "Amount", "المبلغ", "number", false),
            new("Notes", "Notes", "ملاحظات", "text", false),
            new("Iban", "IBAN", "رقم الآيبان", "text", true),
        ]),
    ];

    public static TextStyle Style { get; } = new() { FontSizePt = 9, Bold = false, Align = "start" };

    public static TableComponent Table(string? groupBy = null, bool pageBreakPerGroup = false, params string[] fields) => new()
    {
        Id = "tbl",
        Columns = (fields.Length == 0 ? ["ClientId", "Notes", "Amount"] : fields)
            .Select(f => new TableColumn { Field = f, Header = f, WidthPct = 20, Format = f == "Amount" ? "amount" : "text", Total = f == "Amount" })
            .ToList(),
        GroupBy = groupBy,
        GroupHeader = groupBy is null ? null : $"{{Field: {groupBy}}}",
        PageBreakPerGroup = pageBreakPerGroup,
    };

    public static ReportDefinition Report(
        IReadOnlyList<BodyComponent>? body = null,
        string direction = "ltr",
        string numbering = "document",
        IReadOnlyList<ComponentBinding>? bindings = null,
        IReadOnlyList<BandItem>? header = null) => new()
        {
            SchemaVersion = 1,
            Engine = "rt-html-chromium/0.1-poc",
            Layout = new ReportLayout
            {
                Direction = direction,
                Culture = direction == "rtl" ? "ar-AE" : "en-GB",
                Page = new PageSetup { Size = "A4", Orientation = "portrait", MarginTopMm = 15, MarginBottomMm = 15, MarginStartMm = 12, MarginEndMm = 12 },
                PageHeader = new Band { HeightMm = 10, Items = header ?? [] },
                PageFooter = new Band
                {
                    HeightMm = 8,
                    Items = [new BandItem { Id = "pn", Kind = "text", XMm = 0, YMm = 0, WidthMm = 60, HeightMm = 5, Text = "Page {Page} of {Total Pages}", Style = Style }],
                },
                PageNumbering = numbering,
                Body = body ?? [Table()],
            },
            Bindings = bindings ?? [Binding("tbl")],
            Parameters = [new ReportParameter { Name = "ToClient", Label = "Up to client", Type = "text", Required = false }],
        };

    public static ComponentBinding Binding(string componentId, string dataset = Dataset, IReadOnlyList<BindingFilter>? filters = null,
        IReadOnlyList<string>? sort = null) => new()
        {
            ComponentId = componentId,
            DatasetId = dataset,
            Filters = filters ?? [new BindingFilter { Field = "ClientId", Op = "lte", Parameter = "ToClient" }],
            Sort = sort ?? ["ClientId"],
        };

    public static IReadOnlyDictionary<string, object?> Row(string clientId, string notes, decimal amount) =>
        new Dictionary<string, object?>
        {
            ["ClientId"] = clientId,
            ["ClientNameAr"] = "شركة النور للتجارة",
            ["TradeDate"] = new DateOnly(2026, 7, 1),
            ["Amount"] = amount,
            ["Notes"] = notes,
            ["Iban"] = "JO12••••••••••••••••••••••3456",
        };
}

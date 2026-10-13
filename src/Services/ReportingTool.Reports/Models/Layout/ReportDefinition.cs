using System.Text.Json.Serialization;

namespace ReportingTool.Reports.Models.Layout;

/// <summary>
/// The Report-kind TemplateVersion definition (TDD v2 §3.5) under option 1 of the revised
/// ADR-001: the layout is <b>our own</b> JSON model, not a vendor format. Bindings and parameters
/// sit next to the layout, so dependency analysis and permission checks never parse the layout.
/// </summary>
public sealed record ReportDefinition
{
    public required int SchemaVersion { get; init; }

    /// <summary>Renderer that produced the outputs, recorded on every run (ADR-001 §9 rule 4).</summary>
    public required string Engine { get; init; }

    public required ReportLayout Layout { get; init; }
    public required IReadOnlyList<ComponentBinding> Bindings { get; init; }
    public required IReadOnlyList<ReportParameter> Parameters { get; init; }
}

/// <summary>
/// Page setup, the repeating page header and footer bands (free-form), and the body flow.
/// Positions are logical (measured from the inline start), so switching
/// <see cref="Direction"/> mirrors the whole layout without rewriting it.
/// </summary>
public sealed record ReportLayout
{
    /// <summary><c>ltr</c> or <c>rtl</c>.</summary>
    public required string Direction { get; init; }

    /// <summary>Culture for month names and long dates, for example <c>ar-AE</c> or <c>en-GB</c>.</summary>
    public required string Culture { get; init; }

    public required PageSetup Page { get; init; }
    public required Band PageHeader { get; init; }
    public required Band PageFooter { get; init; }

    /// <summary><c>document</c>, or <c>perGroup</c> to restart {Page}/{Total Pages} for each group (statements).</summary>
    public required string PageNumbering { get; init; }

    /// <summary>Optional diagonal watermark on every page (#84 restricted outputs).</summary>
    public string? Watermark { get; init; }

    public required IReadOnlyList<BodyComponent> Body { get; init; }
}

public sealed record PageSetup
{
    /// <summary><c>A4</c> or <c>Letter</c>.</summary>
    public required string Size { get; init; }

    /// <summary><c>portrait</c> or <c>landscape</c>.</summary>
    public required string Orientation { get; init; }

    public required decimal MarginTopMm { get; init; }
    public required decimal MarginBottomMm { get; init; }
    public required decimal MarginStartMm { get; init; }
    public required decimal MarginEndMm { get; init; }
}

/// <summary>A free-form band: items are placed at absolute (logical) positions inside it.</summary>
public sealed record Band
{
    public required decimal HeightMm { get; init; }
    public required IReadOnlyList<BandItem> Items { get; init; }
}

public sealed record BandItem
{
    public required string Id { get; init; }

    /// <summary><c>text</c> or <c>line</c>.</summary>
    public required string Kind { get; init; }

    public required decimal XMm { get; init; }
    public required decimal YMm { get; init; }
    public required decimal WidthMm { get; init; }
    public required decimal HeightMm { get; init; }

    /// <summary>Text with dynamic fields: {Report Name}, {Run Date}, {Run By}, {Parameter: X}, {Page}, {Total Pages}, {Group}.</summary>
    public string? Text { get; init; }

    public TextStyle? Style { get; init; }
}

public sealed record TextStyle
{
    public required decimal FontSizePt { get; init; }
    public required bool Bold { get; init; }

    /// <summary><c>start</c>, <c>center</c> or <c>end</c> (logical, so it mirrors in RTL).</summary>
    public required string Align { get; init; }
}

/// <summary>A component in the body flow. Tables flow across pages; the rest keep together.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextComponent), "text")]
[JsonDerivedType(typeof(TableComponent), "table")]
[JsonDerivedType(typeof(ChartComponent), "chart")]
[JsonDerivedType(typeof(KpiComponent), "kpi")]
[JsonDerivedType(typeof(PageBreakComponent), "pageBreak")]
public abstract record BodyComponent
{
    public required string Id { get; init; }
}

public sealed record TextComponent : BodyComponent
{
    public required string Text { get; init; }
    public required TextStyle Style { get; init; }

    /// <summary>Marks the text as a heading for tagged PDF and screen readers.</summary>
    public bool Heading { get; init; }
}

public sealed record TableComponent : BodyComponent
{
    public string? Title { get; init; }
    public required IReadOnlyList<TableColumn> Columns { get; init; }

    /// <summary>Field to group by. Rows must arrive sorted by it (the binding's sort).</summary>
    public string? GroupBy { get; init; }

    /// <summary>Group heading with field tokens, for example "{Field: ClientNameAr} — {Field: ClientId}".</summary>
    public string? GroupHeader { get; init; }

    /// <summary>One group per page run: per-record statements (#37 AC5, #38 AC4).</summary>
    public bool PageBreakPerGroup { get; init; }

    public bool GrandTotal { get; init; }
}

public sealed record TableColumn
{
    public required string Field { get; init; }
    public required string Header { get; init; }
    public required decimal WidthPct { get; init; }

    /// <summary><c>text</c>, <c>date</c>, <c>dateLong</c>, <c>amount</c> or <c>integer</c>.</summary>
    public required string Format { get; init; }

    /// <summary>Sum this column in group subtotals and the grand total.</summary>
    public bool Total { get; init; }
}

public sealed record ChartComponent : BodyComponent
{
    public required string Title { get; init; }

    /// <summary><c>bar</c>, <c>line</c> or <c>pie</c>.</summary>
    public required string ChartType { get; init; }

    public required string CategoryField { get; init; }
    public required string ValueField { get; init; }
    public required decimal HeightMm { get; init; }
}

public sealed record KpiComponent : BodyComponent
{
    public required string Label { get; init; }
    public required string ValueField { get; init; }

    /// <summary><c>sum</c>, <c>count</c> or <c>avg</c>.</summary>
    public required string Aggregate { get; init; }

    public required string Format { get; init; }
}

public sealed record PageBreakComponent : BodyComponent;

/// <summary>Which data a component shows (TDD v2 §3.5 <c>bindings[]</c>): a dataset plus filters fed by report parameters.</summary>
public sealed record ComponentBinding
{
    public required string ComponentId { get; init; }
    public required string DatasetId { get; init; }
    public required IReadOnlyList<BindingFilter> Filters { get; init; }
    public required IReadOnlyList<string> Sort { get; init; }
}

public sealed record BindingFilter
{
    public required string Field { get; init; }

    /// <summary><c>eq</c>, <c>gte</c> or <c>lte</c>.</summary>
    public required string Op { get; init; }

    /// <summary>The report parameter that supplies the value. An empty value skips the filter.</summary>
    public required string Parameter { get; init; }
}

public sealed record ReportParameter
{
    public required string Name { get; init; }
    public required string Label { get; init; }

    /// <summary><c>text</c>, <c>date</c> or <c>int</c>.</summary>
    public required string Type { get; init; }

    public string? Default { get; init; }
    public bool Required { get; init; }
}

using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ReportingTool.Reports.Services.Rendering;

/// <summary>
/// Builds print HTML from our layout model. The browser does the hard typography: Arabic shaping
/// and joining (HarfBuzz), the bidi algorithm, table header repetition and "never split a row".
/// Every author-supplied string is HTML-encoded and no author content becomes markup or script,
/// so a template cannot make the browser fetch anything (G3).
/// </summary>
public sealed partial class HtmlReportComposer : IReportComposer
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly Lazy<string> EchartsScript = new(static () =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "echarts.min.js")));

    private readonly IQueryService _query;
    private readonly RenderingOptions _options;

    public HtmlReportComposer(IQueryService query, IOptions<RenderingOptions> options)
    {
        _query = query;
        _options = options.Value;
    }

    public async IAsyncEnumerable<BodyChunk> ComposeBodyAsync(RenderContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        var sink = new ChunkSink(BodyHead(ctx));
        foreach (var component in ctx.Definition.Layout.Body)
        {
            switch (component)
            {
                case TextComponent text:
                    AppendText(sink.Current, text, ctx);
                    break;
                case PageBreakComponent:
                    sink.Current.Html.Append("<div class=\"pb\"></div>");
                    break;
                case KpiComponent kpi:
                    await AppendKpiAsync(sink.Current, kpi, ctx, ct);
                    break;
                case ChartComponent chart:
                    await AppendChartAsync(sink.Current, chart, ctx, ct);
                    break;
                case TableComponent table:
                    await foreach (var done in AppendTableAsync(sink, table, ctx, ct))
                        yield return done;
                    break;
            }
        }

        yield return sink.Finish();
    }

    public string ComposeOverlay(RenderContext ctx, OverlayPlan plan)
    {
        var layout = ctx.Definition.Layout;
        var (w, h) = PageSize(layout.Page);
        var html = new StringBuilder(64 * 1024 + plan.TotalPages * 700);
        html.Append(string.Create(Invariant, $$"""
            <!doctype html><html lang="{{Lang(ctx)}}" dir="{{layout.Direction}}"><head><meta charset="utf-8"><style>
            @page { size: {{Mm(w)}} {{Mm(h)}}; margin: 0; }
            html, body { margin: 0; padding: 0; font-family: {{FontStack(ctx)}}; color: #16181d; }
            .pg { position: relative; width: {{Mm(w)}}; height: {{Mm(h - 0.4m)}}; overflow: hidden; break-after: page; }
            .pg:last-child { break-after: auto; }
            .band { position: absolute; inset-inline-start: {{Mm(layout.Page.MarginStartMm)}}; width: {{Mm(w - layout.Page.MarginStartMm - layout.Page.MarginEndMm)}}; }
            .band.h { top: {{Mm(layout.Page.MarginTopMm)}}; height: {{Mm(layout.PageHeader.HeightMm)}}; }
            .band.f { bottom: {{Mm(layout.Page.MarginBottomMm)}}; height: {{Mm(layout.PageFooter.HeightMm)}}; }
            .it { position: absolute; overflow: hidden; white-space: nowrap; }
            .it.line { border-top: 0.3mm solid #98a2b3; }
            .wm { position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%) rotate(-30deg); font-size: 40pt; font-weight: 700; color: rgba(180, 35, 24, 0.13); white-space: nowrap; }
            </style></head><body>
            """));

        var perGroup = layout.PageNumbering == "perGroup" && plan.Groups.Count > 0;
        var g = -1;
        for (var page = 1; page <= plan.TotalPages; page++)
        {
            while (g + 1 < plan.Groups.Count && plan.Groups[g + 1].FirstPage <= page)
                g++;
            var group = g >= 0 ? plan.Groups[g] : null;
            var (pageNo, total) = perGroup && group is not null
                ? (page - group.FirstPage + 1, group.PageCount)
                : (page, plan.TotalPages);

            string? Resolve(string token) => token switch
            {
                "Page" => pageNo.ToString(Invariant),
                "Total Pages" => total.ToString(Invariant),
                "Group" => group?.Label ?? string.Empty,
                _ => ResolveCommon(ctx, token),
            };

            html.Append("<div class=\"pg\">");
            AppendBand(html, layout.PageHeader, "h", Resolve);
            AppendBand(html, layout.PageFooter, "f", Resolve);
            if (!string.IsNullOrWhiteSpace(layout.Watermark))
                html.Append("<div class=\"wm\">").Append(ResolveText(layout.Watermark, Resolve)).Append("</div>");
            html.Append("</div>");
        }

        return html.Append("</body></html>").ToString();
    }

    // ----- body components -----

    private static void AppendText(ChunkBuilder b, TextComponent text, RenderContext ctx)
    {
        var tag = text.Heading ? "h1" : "div";
        if (text.Heading)
            b.Headings.Add(false);
        b.Html.Append('<').Append(tag).Append(" class=\"c-text\" style=\"").Append(Style(text.Style)).Append("\">")
            .Append(ResolveText(text.Text, t => ResolveCommon(ctx, t)))
            .Append("</").Append(tag).Append('>');
    }

    private async Task AppendKpiAsync(ChunkBuilder b, KpiComponent kpi, RenderContext ctx, CancellationToken ct)
    {
        var binding = ctx.Binding(kpi.Id);
        decimal sum = 0;
        long count = 0;
        await foreach (var row in _query.StreamRowsAsync(ctx.Subject, new QueryRequest(binding.DatasetId, [kpi.ValueField], Filters(binding, ctx), []), ct))
        {
            count++;
            if (row[0] is decimal d)
                sum += d;
        }

        var value = kpi.Aggregate switch
        {
            "count" => count,
            "avg" => count == 0 ? 0 : sum / count,
            _ => sum,
        };
        b.Html.Append("<div class=\"c-kpi\"><div class=\"v\"><bdi dir=\"ltr\">").Append(E(FormatValue(value, kpi.Format, ctx.Culture)))
            .Append("</bdi></div><div class=\"l\">").Append(E(kpi.Label)).Append("</div></div>");
    }

    private async Task AppendChartAsync(ChunkBuilder b, ChartComponent chart, RenderContext ctx, CancellationToken ct)
    {
        var binding = ctx.Binding(chart.Id);
        var totals = new Dictionary<string, decimal>();
        await foreach (var row in _query.StreamRowsAsync(ctx.Subject,
                           new QueryRequest(binding.DatasetId, [chart.CategoryField, chart.ValueField], Filters(binding, ctx), []), ct))
        {
            var key = Convert.ToString(row[0], Invariant) ?? string.Empty;
            totals[key] = totals.GetValueOrDefault(key) + (row[1] is decimal d ? d : 1);
        }

        var categories = totals.Keys.Order(StringComparer.Ordinal).ToList();
        var values = categories.Select(c => Math.Round((double)totals[c], 2)).ToList();
        object option = chart.ChartType == "pie"
            ? new
            {
                animation = false,
                textStyle = new { fontFamily = FontStack(ctx).Replace("\"", string.Empty, StringComparison.Ordinal) },
                series = new[] { new { type = "pie", radius = "65%", data = categories.Select((c, i) => new { name = c, value = values[i] }), label = new { show = true } } },
            }
            : new
            {
                animation = false,
                textStyle = new { fontFamily = FontStack(ctx).Replace("\"", string.Empty, StringComparison.Ordinal) },
                grid = new { left = 8, right = 8, top = 28, bottom = 8, containLabel = true },
                xAxis = new { type = "category", data = categories, inverse = ctx.IsRtl, axisLabel = new { interval = 0, rotate = categories.Count > 6 ? 30 : 0 } },
                yAxis = new { type = "value", position = ctx.IsRtl ? "right" : "left" },
                // Labels are formatted here because a JSON option cannot carry a formatter function.
                series = new[]
                {
                    new
                    {
                        type = chart.ChartType,
                        itemStyle = new { color = "#1d4ed8" },
                        data = values.Select(v => new { value = v, label = new { show = true, position = "top", formatter = v.ToString("#,##0", Invariant) } }),
                    },
                },
            };

        var id = "ch-" + b.Charts.Count.ToString(Invariant);
        b.Charts.Add(new { id, option });
        // dir="ltr" on the drawing surface: ECharts flips SVG text anchors inside an RTL element,
        // which pushes axis labels into the plot. The RTL look comes from the inverted axis instead.
        b.Html.Append("<div class=\"c-chart\"><div class=\"ttl\">").Append(E(chart.Title)).Append("</div><div dir=\"ltr\" id=\"").Append(id)
            .Append("\" role=\"img\" aria-label=\"").Append(E(chart.Title)).Append("\" style=\"height:").Append(Mm(chart.HeightMm))
            .Append("\"></div></div>");
    }

    private async IAsyncEnumerable<BodyChunk> AppendTableAsync(ChunkSink sink, TableComponent table, RenderContext ctx,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var binding = ctx.Binding(table.Id);
        var schema = ctx.Schema(binding.DatasetId);
        var headerFields = table.GroupHeader is null ? [] : FieldTokenRegex().Matches(table.GroupHeader).Select(static m => m.Groups[1].Value.Trim());
        var fetch = table.Columns.Select(static c => c.Field)
            .Concat(table.GroupBy is null ? [] : [table.GroupBy])
            .Concat(headerFields)
            .Where(f => schema.Fields.Any(x => x.Key == f))
            .Distinct()
            .ToList();
        var at = fetch.Select(static (f, i) => (f, i)).ToDictionary(static x => x.f, static x => x.i);
        var request = new QueryRequest(binding.DatasetId, fetch, Filters(binding, ctx), binding.Sort);

        var tableOpen = new StringBuilder("<table class=\"t\"><colgroup>");
        foreach (var c in table.Columns)
            tableOpen.Append("<col style=\"width:").Append(c.WidthPct.ToString(Invariant)).Append("%\">");
        tableOpen.Append("</colgroup><thead><tr>");
        foreach (var c in table.Columns)
            tableOpen.Append(IsNumeric(c) ? "<th class=\"n\">" : "<th>").Append(E(c.Header)).Append("</th>");
        tableOpen.Append("</tr></thead><tbody>");

        var b = sink.Current;
        if (table.Title is not null)
            b.Html.Append("<div class=\"t-title\">").Append(E(table.Title)).Append("</div>");
        if (!table.PageBreakPerGroup)
            b.Html.Append(tableOpen);

        var groupTotals = new decimal[table.Columns.Count];
        var grandTotals = new decimal[table.Columns.Count];
        string? currentKey = null;
        var subtotalLabel = ctx.IsRtl ? "المجموع الفرعي" : "Subtotal";

        await foreach (var row in _query.StreamRowsAsync(ctx.Subject, request, ct))
        {
            if (table.GroupBy is not null)
            {
                var key = Convert.ToString(row[at[table.GroupBy]], Invariant) ?? string.Empty;
                if (key != currentKey)
                {
                    if (currentKey is not null)
                    {
                        CloseGroup(b, table, groupTotals, subtotalLabel, ctx);
                        if (table.PageBreakPerGroup && b.Rows >= _options.ChunkRowTarget)
                        {
                            yield return sink.Cut();
                            b = sink.Current;
                        }
                    }

                    currentKey = key;
                    Array.Clear(groupTotals);
                    var label = table.GroupHeader is null ? key : ResolvePlain(table.GroupHeader, t => FieldToken(t, row, at, ctx));
                    if (table.PageBreakPerGroup)
                    {
                        b.Html.Append(b.GroupLabels.Count == 0 && b.Rows == 0 ? "<section class=\"grp first\">" : "<section class=\"grp\">")
                            .Append("<h2 class=\"grp-title\">").Append(E(label)).Append("</h2>").Append(tableOpen);
                        b.Headings.Add(true);
                        b.GroupLabels.Add(label);
                    }
                    else
                    {
                        b.Html.Append("<tr class=\"gh\"><td colspan=\"").Append(table.Columns.Count.ToString(Invariant)).Append("\">")
                            .Append(E(label)).Append("</td></tr>");
                    }
                }
            }

            b.Html.Append("<tr>");
            for (var c = 0; c < table.Columns.Count; c++)
            {
                var column = table.Columns[c];
                var value = row[at[column.Field]];
                if (column.Total && value is decimal d)
                {
                    groupTotals[c] += d;
                    grandTotals[c] += d;
                }

                AppendCell(b.Html, column, value, ctx);
            }

            b.Html.Append("</tr>");
            b.Rows++;
        }

        if (currentKey is not null)
            CloseGroup(b, table, groupTotals, subtotalLabel, ctx);

        if (table.GrandTotal)
        {
            if (table.PageBreakPerGroup)
                b.Html.Append(tableOpen);
            AppendTotalRow(b.Html, table, grandTotals, "gt", ctx.IsRtl ? "المجموع الكلي" : "Grand total", ctx);
            if (table.PageBreakPerGroup)
                b.Html.Append("</tbody></table>");
        }

        if (!table.PageBreakPerGroup)
            b.Html.Append("</tbody></table>");
    }

    private static void CloseGroup(ChunkBuilder b, TableComponent table, decimal[] totals, string label, RenderContext ctx)
    {
        if (table.Columns.Any(static c => c.Total))
            AppendTotalRow(b.Html, table, totals, "st", label, ctx);
        if (table.PageBreakPerGroup)
            b.Html.Append("</tbody></table></section>");
    }

    private static void AppendTotalRow(StringBuilder html, TableComponent table, decimal[] totals, string css, string label, RenderContext ctx)
    {
        // The label spans every column before the first total, so it is never cut off.
        var firstTotal = Math.Max(1, table.Columns.ToList().FindIndex(static c => c.Total));
        html.Append("<tr class=\"").Append(css).Append("\"><td colspan=\"").Append(firstTotal.ToString(Invariant)).Append("\">")
            .Append(E(label)).Append("</td>");
        for (var c = firstTotal; c < table.Columns.Count; c++)
        {
            var column = table.Columns[c];
            if (column.Total)
                AppendCell(html, column, totals[c], ctx);
            else
                html.Append("<td></td>");
        }

        html.Append("</tr>");
    }

    private static void AppendCell(StringBuilder html, TableColumn column, object? value, RenderContext ctx)
    {
        var text = E(FormatValue(value, column.Format, ctx.Culture));
        // Numbers, dates and codes are isolated left-to-right runs: in an RTL table "-1,234.50"
        // must not become "1,234.50-" (G1 "mixed numbers").
        if (IsNumeric(column) || column.Format is "date")
            html.Append("<td class=\"n\"><bdi dir=\"ltr\">").Append(text).Append("</bdi></td>");
        else
            html.Append("<td>").Append(text).Append("</td>");
    }

    // ----- shared helpers -----

    private static IReadOnlyList<QueryFilter> Filters(ComponentBinding binding, RenderContext ctx) =>
        binding.Filters
            .Select(f => (f, value: ctx.Parameters.GetValueOrDefault(f.Parameter)))
            .Where(static x => !string.IsNullOrWhiteSpace(x.value))
            .Select(static x => new QueryFilter(x.f.Field, x.f.Op, x.value!))
            .ToList();

    private static string FormatValue(object? value, string format, CultureInfo culture) => value switch
    {
        null => string.Empty,
        DateOnly d when format == "dateLong" => d.ToString("d MMMM yyyy", culture),
        DateOnly d => d.ToString("yyyy-MM-dd", Invariant),
        decimal m when format == "integer" => m.ToString("#,##0", Invariant),
        decimal m => m.ToString("#,##0.00", Invariant),
        long l => l.ToString("#,##0", Invariant),
        _ => Convert.ToString(value, Invariant) ?? string.Empty,
    };

    private static bool IsNumeric(TableColumn c) => c.Format is "amount" or "integer";

    private static string? ResolveCommon(RenderContext ctx, string token) => token switch
    {
        "Report Name" => ctx.ReportName,
        "Run Date" => ctx.RunAtUtc.UtcDateTime.ToString("d MMMM yyyy HH:mm", ctx.Culture) + " UTC",
        "Run By" => ctx.Subject,
        _ when token.StartsWith("Parameter:", StringComparison.Ordinal) =>
            ctx.Parameters.GetValueOrDefault(token["Parameter:".Length..].Trim()) ?? string.Empty,
        _ => null,
    };

    private static string? FieldToken(string token, object?[] row, Dictionary<string, int> at, RenderContext ctx) =>
        token.StartsWith("Field:", StringComparison.Ordinal) && at.TryGetValue(token["Field:".Length..].Trim(), out var i)
            ? FormatValue(row[i], "text", ctx.Culture)
            : ResolveCommon(ctx, token);

    /// <summary>Replaces {Token}s and HTML-encodes everything, literal text and values alike.</summary>
    private static string ResolveText(string template, Func<string, string?> resolve) =>
        TokenRegex().Replace(E(template), m =>
        {
            var value = resolve(WebUtility.HtmlDecode(m.Groups[1].Value).Trim());
            return value is null ? m.Value : "<bdi>" + E(value) + "</bdi>";
        });

    private static string ResolvePlain(string template, Func<string, string?> resolve) =>
        TokenRegex().Replace(template, m => resolve(m.Groups[1].Value.Trim()) ?? m.Value);

    private static void AppendBand(StringBuilder html, Band band, string css, Func<string, string?> resolve)
    {
        html.Append("<div class=\"band ").Append(css).Append("\">");
        foreach (var item in band.Items)
        {
            html.Append("<div class=\"it").Append(item.Kind == "line" ? " line" : string.Empty).Append("\" style=\"")
                .Append("inset-inline-start:").Append(Mm(item.XMm)).Append(";top:").Append(Mm(item.YMm))
                .Append(";width:").Append(Mm(item.WidthMm)).Append(";height:").Append(Mm(item.HeightMm)).Append(';');
            if (item.Style is not null)
                html.Append(Style(item.Style)).Append(";line-height:").Append(Mm(item.HeightMm));
            html.Append("\">");
            if (item.Kind == "text" && item.Text is not null)
                html.Append(ResolveText(item.Text, resolve));
            html.Append("</div>");
        }

        html.Append("</div>");
    }

    private static string Style(TextStyle s) =>
        string.Create(Invariant, $"font-size:{s.FontSizePt}pt;font-weight:{(s.Bold ? 700 : 400)};text-align:{(s.Align is "center" or "end" ? s.Align : "start")}");

    private static string BodyHead(RenderContext ctx)
    {
        var layout = ctx.Definition.Layout;
        var page = layout.Page;
        var (w, h) = PageSize(page);
        var top = page.MarginTopMm + layout.PageHeader.HeightMm + 2;
        var bottom = page.MarginBottomMm + layout.PageFooter.HeightMm + 2;
        var (left, right) = ctx.IsRtl ? (page.MarginEndMm, page.MarginStartMm) : (page.MarginStartMm, page.MarginEndMm);

        return string.Create(Invariant, $$"""
            <!doctype html><html lang="{{Lang(ctx)}}" dir="{{layout.Direction}}"><head><meta charset="utf-8"><style>
            @page { size: {{Mm(w)}} {{Mm(h)}}; margin: {{Mm(top)}} {{Mm(right)}} {{Mm(bottom)}} {{Mm(left)}}; }
            html { font-family: {{FontStack(ctx)}}; font-size: 9pt; color: #16181d; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
            body { margin: 0; }
            .c-text { margin: 0 0 3mm; white-space: pre-wrap; }
            .t-title { font-weight: 700; font-size: 10.5pt; margin: 2mm 0 1.5mm; }
            table.t { width: 100%; border-collapse: collapse; table-layout: fixed; margin-bottom: 4mm; }
            table.t th { background: #e8edf7; color: #1e2a3b; font-weight: 700; text-align: start; padding: 1.2mm 1.5mm; border-bottom: 0.4mm solid #1d4ed8; }
            table.t td { padding: 0.9mm 1.5mm; border-bottom: 0.15mm solid #d5dae1; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
            table.t tr { break-inside: avoid; }
            table.t .n { text-align: end; }
            table.t tr.gh td { background: #f3f5f9; font-weight: 700; padding-top: 1.6mm; }
            table.t tr.gh { break-after: avoid; }
            table.t tr.st td { font-weight: 700; border-top: 0.3mm solid #98a2b3; border-bottom: 0.3mm solid #98a2b3; }
            table.t tr.gt td { font-weight: 700; background: #e8edf7; border-top: 0.5mm double #1e2a3b; }
            section.grp { break-before: page; }
            section.grp.first { break-before: auto; }
            h2.grp-title { font-size: 11pt; margin: 0 0 2mm; }
            h1.c-text { margin: 0 0 3mm; }
            .c-chart { break-inside: avoid; margin-bottom: 4mm; }
            .c-chart .ttl { font-weight: 700; margin-bottom: 1mm; }
            .c-kpi { display: inline-block; min-width: 45mm; border: 0.3mm solid #c3c8d1; border-radius: 2mm; padding: 2mm 3mm; margin: 0 0 4mm; margin-inline-end: 3mm; break-inside: avoid; }
            .c-kpi .v { font-size: 16pt; font-weight: 700; }
            .c-kpi .l { color: #545b66; }
            .pb { break-after: page; }
            </style></head><body>
            """);
    }

    private static (decimal W, decimal H) PageSize(PageSetup page)
    {
        var (w, h) = page.Size == "Letter" ? (215.9m, 279.4m) : (210m, 297m);
        return page.Orientation == "landscape" ? (h, w) : (w, h);
    }

    /// <summary>Same order as the designer canvas, so the preview and the PDF pick the same glyphs (WYSIWYG).</summary>
    private static string FontStack(RenderContext ctx) =>
        ctx.IsRtl ? "\"Noto Sans Arabic\", \"Noto Sans\", sans-serif" : "\"Noto Sans\", \"Noto Sans Arabic\", sans-serif";

    private static string Lang(RenderContext ctx) => ctx.Culture.TwoLetterISOLanguageName;

    private static string Mm(decimal value) => value.ToString("0.###", Invariant) + "mm";

    private static string E(string value) => WebUtility.HtmlEncode(value);

    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\{\s*Field:\s*([^{}]+)\}")]
    private static partial Regex FieldTokenRegex();

    private sealed class ChunkBuilder(int index, string head)
    {
        public int Index { get; } = index;
        public StringBuilder Html { get; } = new StringBuilder(256 * 1024).Append(head);
        public List<bool> Headings { get; } = [];
        public List<string> GroupLabels { get; } = [];
        public List<object> Charts { get; } = [];
        public long Rows { get; set; }
    }

    /// <summary>Holds the chunk being written; <see cref="Cut"/> closes it and starts the next one.</summary>
    private sealed class ChunkSink(string head)
    {
        public ChunkBuilder Current { get; private set; } = new(0, head);

        public BodyChunk Cut()
        {
            var done = Close(Current);
            Current = new ChunkBuilder(Current.Index + 1, head);
            return done;
        }

        public BodyChunk Finish() => Close(Current);

        private static BodyChunk Close(ChunkBuilder b)
        {
            if (b.Charts.Count > 0)
            {
                // The page signals readiness only after every chart has finished drawing, so the
                // PDF never captures a half-rendered chart. JSON is safe inside <script>: the
                // default encoder escapes <, > and &.
                b.Html.Append("<script>").Append(EchartsScript.Value).Append("</script><script>(function(){var specs=")
                    .Append(JsonSerializer.Serialize(b.Charts))
                    .Append(";var left=specs.length;specs.forEach(function(s){var c=echarts.init(document.getElementById(s.id),null,{renderer:'svg'});")
                    .Append("c.on('finished',function(){if(--left===0){window.__rtReady=true;}});c.setOption(s.option);});})();</script>");
            }

            b.Html.Append("</body></html>");
            return new BodyChunk(b.Index, b.Html.ToString(), b.Headings, b.GroupLabels, b.Rows, b.Charts.Count > 0);
        }
    }
}

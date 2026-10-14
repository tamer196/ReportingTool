using Microsoft.Extensions.Options;
using ReportingTool.Reports.Models.Configuration;
using ReportingTool.Reports.Models.Layout;
using ReportingTool.Reports.Models.Rendering;
using ReportingTool.Reports.Services.Rendering;
using ReportingTool.Reports.Tests.Fakes;

namespace ReportingTool.Reports.Tests;

/// <summary>
/// The print HTML the browser turns into PDF. Author text and data must never become markup
/// (G3: nothing in a template can make the browser fetch anything), RTL output must isolate
/// numbers (G1), and statements must break and number pages per group (#37 AC5, #38 AC4).
/// </summary>
public sealed class HtmlReportComposerTests
{
    private const string Subject = "author.ar";

    [Fact]
    public async Task Author_text_is_encoded_not_rendered_as_markup()
    {
        var probe = "<script>fetch('http://203.0.113.10/')</script><img src=\"http://203.0.113.10/x.png\">";
        var definition = Definitions.Report(body: [new TextComponent { Id = "t", Text = probe, Style = Definitions.Style }], bindings: []);

        var html = await ComposeAsync(definition, []);

        Assert.DoesNotContain("<script>fetch", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;fetch(", html);
    }

    [Fact]
    public async Task Data_values_and_headers_are_encoded()
    {
        var table = Definitions.Table() with
        {
            Columns = [new TableColumn { Field = "Notes", Header = "<b onmouseover=x>Notes</b>", WidthPct = 50, Format = "text" }],
        };
        var definition = Definitions.Report(body: [table]);

        var html = await ComposeAsync(definition, [Definitions.Row("CL-1", "<iframe src=\"http://203.0.113.10/\"></iframe>", 1m)]);

        Assert.DoesNotContain("<iframe", html);
        Assert.DoesNotContain("<b onmouseover", html);
        Assert.Contains("&lt;iframe", html);
    }

    [Fact]
    public async Task Rtl_reports_isolate_numbers_as_left_to_right_runs()
    {
        var html = await ComposeAsync(Definitions.Report(direction: "rtl"), [Definitions.Row("CL-1", "إيداع نقدي", -1234.5m)]);

        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("lang=\"ar\"", html);
        Assert.Contains("<bdi dir=\"ltr\">-1,234.50</bdi>", html);
        Assert.Contains("إيداع نقدي", html);
    }

    [Fact]
    public async Task The_renderer_reads_data_only_as_the_requester_and_only_the_fields_it_shows()
    {
        var query = new FakeQueryService(Definitions.AuthorSchema, [Definitions.Row("CL-1", "x", 1m)]);
        var composer = Composer(query, chunkRowTarget: 100);

        await foreach (var _ in composer.ComposeBodyAsync(Context(Definitions.Report(), parameters: new() { ["ToClient"] = "CL-00009" }), CancellationToken.None))
        {
        }

        var (subject, request) = Assert.Single(query.Requests);
        Assert.Equal(Subject, subject);
        Assert.Equal(new[] { "ClientId", "Notes", "Amount" }, request.Fields);
        var filter = Assert.Single(request.Filters);
        Assert.Equal(("ClientId", "lte", "CL-00009"), (filter.Field, filter.Op, (string)filter.Value));
    }

    [Fact]
    public async Task An_empty_parameter_skips_its_filter()
    {
        var query = new FakeQueryService(Definitions.AuthorSchema, []);

        await foreach (var _ in Composer(query, 100).ComposeBodyAsync(Context(Definitions.Report(), parameters: new() { ["ToClient"] = " " }), CancellationToken.None))
        {
        }

        Assert.Empty(query.Requests.Single().Request.Filters);
    }

    [Fact]
    public async Task Statements_start_each_group_on_a_new_page_and_cut_chunks_only_between_groups()
    {
        // Three clients with 60 rows each and a 100-row chunk target: the cut comes after the
        // second client, never inside one, so no statement is split across two browser renders.
        var rows = new[] { "CL-1", "CL-2", "CL-3" }
            .SelectMany(c => Enumerable.Range(1, 60).Select(i => Definitions.Row(c, $"row {i}", i)))
            .ToList();
        var definition = Definitions.Report(body: [Definitions.Table(groupBy: "ClientId", pageBreakPerGroup: true)], numbering: "perGroup");
        var composer = Composer(new FakeQueryService(Definitions.AuthorSchema, rows), chunkRowTarget: 100);

        var chunks = new List<BodyChunk>();
        await foreach (var chunk in composer.ComposeBodyAsync(Context(definition), CancellationToken.None))
            chunks.Add(chunk);

        Assert.Equal(2, chunks.Count);
        Assert.Equal(new[] { "CL-1", "CL-2" }, chunks[0].GroupLabels);
        Assert.Equal(new[] { "CL-3" }, chunks[1].GroupLabels);
        Assert.Equal(new[] { 120L, 60L }, chunks.Select(static c => c.Rows));
        Assert.All(chunks, static c => Assert.StartsWith("<!doctype html>", c.Html.TrimStart()));
        Assert.Contains("<section class=\"grp first\">", chunks[0].Html);
        Assert.Contains("section.grp { break-before: page; }", chunks[0].Html);
    }

    [Fact]
    public void Per_group_page_numbers_restart_for_each_group()
    {
        var definition = Definitions.Report(numbering: "perGroup");
        var plan = new OverlayPlan(3, [new GroupSpan("CL-1", 1, 2), new GroupSpan("CL-2", 3, 1)]);

        var html = Composer(new FakeQueryService(Definitions.AuthorSchema, []), 100).ComposeOverlay(Context(definition), plan);

        var footers = System.Text.RegularExpressions.Regex.Matches(html, @"Page <bdi>(\d+)</bdi> of <bdi>(\d+)</bdi>")
            .Select(static m => $"{m.Groups[1].Value}/{m.Groups[2].Value}")
            .ToList();
        Assert.Equal(new[] { "1/2", "2/2", "1/1" }, footers);
    }

    [Fact]
    public void Parameter_values_in_bands_are_encoded()
    {
        var header = new BandItem { Id = "h", Kind = "text", XMm = 0, YMm = 0, WidthMm = 80, HeightMm = 5, Text = "Up to {Parameter: ToClient}", Style = Definitions.Style };
        var definition = Definitions.Report(header: [header]);

        var html = Composer(new FakeQueryService(Definitions.AuthorSchema, []), 100)
            .ComposeOverlay(Context(definition, parameters: new() { ["ToClient"] = "<script>x</script>" }), new OverlayPlan(1, []));

        Assert.DoesNotContain("<script>x", html);
        Assert.Contains("Up to <bdi>&lt;script&gt;x&lt;/script&gt;</bdi>", html);
    }

    private static HtmlReportComposer Composer(FakeQueryService query, int chunkRowTarget) =>
        new(query, Options.Create(new RenderingOptions
        {
            ChromiumPath = "/usr/bin/chromium",
            QpdfPath = "/usr/bin/qpdf",
            WorkDirectory = "/tmp",
            ChunkRowTarget = chunkRowTarget,
            MaxPdfPages = 2000,
            MaxConcurrentRenders = 1,
            BrowserRecycleAfterRenders = 50,
            ChunkTimeoutSeconds = 60,
        }));

    private static RenderContext Context(ReportDefinition definition, Dictionary<string, string?>? parameters = null) =>
        new(definition, "Test report", Subject, parameters ?? new() { ["ToClient"] = "CL-9" },
            new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero), Definitions.AuthorSchema);

    private static async Task<string> ComposeAsync(ReportDefinition definition, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var composer = Composer(new FakeQueryService(Definitions.AuthorSchema, rows), 100);
        var html = new System.Text.StringBuilder();
        await foreach (var chunk in composer.ComposeBodyAsync(Context(definition), CancellationToken.None))
            html.Append(chunk.Html);
        return html.ToString();
    }
}

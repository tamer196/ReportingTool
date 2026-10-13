using Microsoft.Extensions.Options;
using PuppeteerSharp;

namespace ReportingTool.Reports.Services.Rendering;

/// <summary>
/// Prints HTML files to PDF with the distribution's Chromium (BSD-3, D-16), driven by
/// PuppeteerSharp (MIT). One browser per replica, one tab per chunk. Every request except the
/// chunk file itself is aborted, so the renderer cannot reach any data or service (G3) even if a
/// template managed to inject markup. The browser restarts every
/// <see cref="RenderingOptions.BrowserRecycleAfterRenders"/> renders to keep its memory flat (G4).
/// </summary>
public sealed class ChromiumPdfRenderer : IPdfRenderer, IAsyncDisposable
{
    /// <summary>
    /// The sandbox needs user namespaces, which Docker's default seccomp profile blocks; the
    /// mitigations are in ADR-001 §8 (no egress, escaped content, request blocking, non-root).
    /// The dead proxy is a second fence behind request interception.
    /// </summary>
    private static readonly string[] BrowserArgs =
    [
        "--no-sandbox", "--disable-dev-shm-usage", "--disable-gpu", "--font-render-hinting=none",
        "--disable-extensions", "--disable-background-networking", "--disable-component-update",
        "--disable-default-apps", "--disable-sync", "--no-first-run", "--mute-audio",
        "--proxy-server=127.0.0.1:9", "--proxy-bypass-list=<-loopback>",
    ];

    private readonly RenderingOptions _options;
    private readonly ILoggerService _logger;
    private readonly IGlobalExceptionHandler _boundary;
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private IBrowser? _browser;
    private int _renders;
    private int _active;

    public ChromiumPdfRenderer(IOptions<RenderingOptions> options, ILoggerService logger, IGlobalExceptionHandler boundary)
    {
        _options = options.Value;
        _logger = logger;
        _boundary = boundary;
    }

    public async Task<string> GetVersionAsync(CancellationToken ct) => await (await GetBrowserAsync(ct)).GetVersionAsync();

    public async Task<PdfRenderResult> RenderAsync(string htmlPath, string pdfPath, bool waitForCharts, CancellationToken ct)
    {
        var browser = await GetBrowserAsync(ct);
        var allowed = new Uri(htmlPath).AbsoluteUri;
        var timeout = _options.ChunkTimeoutSeconds * 1000;
        var blocked = 0;

        Interlocked.Increment(ref _active);
        try
        {
            await using var page = await browser.NewPageAsync();
            await page.SetRequestInterceptionAsync(true);
            page.Request += (_, e) =>
            {
                if (e.Request.Url == allowed)
                {
                    _ = _boundary.RunAsync(() => e.Request.ContinueAsync(), "continue chunk request", LogLevel.Warning);
                    return;
                }

                Interlocked.Increment(ref blocked);
                _logger.Warning("[RENDER] Blocked a request from the report page: {Url}", e.Request.Url);
                _ = _boundary.RunAsync(() => e.Request.AbortAsync(), "abort blocked request", LogLevel.Warning);
            };

            await page.GoToAsync(allowed, new NavigationOptions { Timeout = timeout, WaitUntil = [WaitUntilNavigation.Load] });
            if (waitForCharts)
                await page.WaitForFunctionAsync("() => window.__rtReady === true", new WaitForFunctionOptions { Timeout = timeout });

            // Tagged PDF with an outline: the outline is how the job finds each group's first page.
            await using var pdf = await page.PdfStreamAsync(new PdfOptions
            {
                PrintBackground = true,
                PreferCSSPageSize = true,
                Tagged = true,
                Outline = true,
            });
            await using var file = File.Create(pdfPath);
            await pdf.CopyToAsync(file, ct);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
            Interlocked.Increment(ref _renders);
        }

        return new PdfRenderResult(blocked);
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.CloseAsync();
        _launchGate.Dispose();
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken ct)
    {
        await _launchGate.WaitAsync(ct);
        try
        {
            var recycle = _browser is not null && _renders >= _options.BrowserRecycleAfterRenders && Volatile.Read(ref _active) == 0;
            if (_browser is { IsConnected: true } && !recycle)
                return _browser;

            if (_browser is not null)
            {
                _logger.Info("[RENDER] Restarting the browser after {Renders} renders.", _renders);
                await _browser.CloseAsync();
            }

            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                ExecutablePath = _options.ChromiumPath,
                Args = BrowserArgs,
                Timeout = 60_000,
            });
            _renders = 0;
            _logger.Info("[RENDER] Browser started: {Version}", await _browser.GetVersionAsync());
            return _browser;
        }
        finally
        {
            _launchGate.Release();
        }
    }
}

using System.Diagnostics;
using System.Globalization;

namespace ReportingTool.Reports.Services.Rendering;

/// <summary>
/// G4 evidence: samples the container's memory while a run renders. "Container" is the cgroup's
/// usage without reclaimable file cache (what <c>docker stats</c> shows and what an OOM kill
/// compares with the limit). "Browser" is the proportional set size of the Chromium processes, so
/// pages they share are not counted twice. Linux only; elsewhere it records zeros.
/// </summary>
public sealed class ProcessMemorySampler : IAsyncDisposable
{
    private const string CgroupCurrent = "/sys/fs/cgroup/memory.current";
    private const string CgroupStat = "/sys/fs/cgroup/memory.stat";

    private readonly CancellationTokenSource _stop = new();
    private readonly List<MemorySample> _samples = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly IGlobalExceptionHandler _boundary;
    private readonly Task _loop;

    public ProcessMemorySampler(TimeSpan interval, IGlobalExceptionHandler boundary)
    {
        _boundary = boundary;
        _loop = LoopAsync(interval);
    }

    public double PeakContainerMb { get; private set; }
    public double PeakBrowserMb { get; private set; }
    public double PeakManagedMb { get; private set; }

    public IReadOnlyList<MemorySample> Stop()
    {
        _stop.Cancel();
        return Thin(_samples, 400);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _loop.ContinueWith(static _ => { }, TaskScheduler.Default);
        _stop.Dispose();
    }

    private async Task LoopAsync(TimeSpan interval)
    {
        while (!_stop.IsCancellationRequested)
        {
            var container = ContainerMb();
            var browser = BrowserMb();
            var managed = GC.GetTotalMemory(false) / 1048576d;
            PeakContainerMb = Math.Max(PeakContainerMb, container);
            PeakBrowserMb = Math.Max(PeakBrowserMb, browser);
            PeakManagedMb = Math.Max(PeakManagedMb, managed);
            lock (_samples)
                _samples.Add(new MemorySample(Math.Round(_clock.Elapsed.TotalSeconds, 2), Math.Round(container, 1), Math.Round(browser, 1)));

            // Cancellation ends the wait early; it is the normal way the loop stops.
            await Task.Delay(interval, _stop.Token).ContinueWith(static _ => { }, TaskScheduler.Default);
        }
    }

    private static double ContainerMb()
    {
        if (!File.Exists(CgroupCurrent))
            return 0;
        var current = long.Parse(File.ReadAllText(CgroupCurrent).Trim(), CultureInfo.InvariantCulture);
        var inactiveFile = File.ReadLines(CgroupStat)
            .Select(static l => l.Split(' '))
            .Where(static p => p[0] == "inactive_file")
            .Select(static p => long.Parse(p[1], CultureInfo.InvariantCulture))
            .FirstOrDefault();
        return (current - inactiveFile) / 1048576d;
    }

    private double BrowserMb()
    {
        if (!Directory.Exists("/proc"))
            return 0;
        long kb = 0;
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            var name = Path.GetFileName(dir);
            if (!name.All(char.IsAsciiDigit))
                continue;
            if (!SafeRead(Path.Combine(dir, "comm")).Contains("chrom", StringComparison.Ordinal))
                continue;
            var pss = SafeRead(Path.Combine(dir, "smaps_rollup")).Split('\n').FirstOrDefault(static l => l.StartsWith("Pss:", StringComparison.Ordinal));
            if (pss is not null)
                kb += long.Parse(pss.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture);
        }

        return kb / 1024d;
    }

    /// <summary>A process can exit between listing and reading; that sample then counts it as gone.</summary>
    private string SafeRead(string path) =>
        _boundary.Run(() => ReadProc(path), "read /proc for memory sampling", string.Empty, LogLevel.Debug);

    /// <summary>procfs files report length 0, so they are read as a stream to the end.</summary>
    private static string ReadProc(string path)
    {
        using var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.ReadWrite });
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static List<MemorySample> Thin(List<MemorySample> samples, int max)
    {
        lock (samples)
        {
            if (samples.Count <= max)
                return [.. samples];
            var step = (double)samples.Count / max;
            return Enumerable.Range(0, max).Select(i => samples[(int)(i * step)]).Append(samples[^1]).ToList();
        }
    }
}

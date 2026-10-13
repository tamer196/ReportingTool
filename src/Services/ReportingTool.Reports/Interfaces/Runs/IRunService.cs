namespace ReportingTool.Reports.Interfaces.Runs;

/// <summary>Starts report runs as background jobs and serves their status and outputs to the requester only.</summary>
public interface IRunService
{
    /// <summary>Queues a run of the caller's own template, or returns null when the template is not theirs (404).</summary>
    Task<RunResponse?> StartAsync(string subject, StartRunRequest request, CancellationToken ct);

    Task<RunResponse?> FindAsync(string subject, Guid runId, CancellationToken ct);

    /// <summary>The output file path for <paramref name="format"/>, or null when not the caller's run or not produced.</summary>
    Task<string?> FindOutputAsync(string subject, Guid runId, string format, CancellationToken ct);
}

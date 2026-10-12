namespace ReportingTool.Api.Models.Greetings;

/// <summary>
/// The hello-world slice's only table. It exists to prove the whole path end to end (Angular →
/// API → SQL Server, EF Core migrations run by the deployment, UTC storage under BR-14) before
/// any story owns a real table. Remove it, with its endpoints and a migration that drops it,
/// once Sprint 1 stories have their own tables.
/// </summary>
public sealed class Greeting
{
    /// <summary>Column size, and the request validation limit, for <see cref="Message"/>.</summary>
    public const int MessageMaxLength = 200;

    public int Id { get; set; }

    public string Message { get; set; } = null!;

    /// <summary>When the greeting was saved, in UTC (BR-14).</summary>
    public DateTime CreatedAtUtc { get; set; }
}

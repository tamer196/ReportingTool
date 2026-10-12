using System.ComponentModel.DataAnnotations;

namespace ReportingTool.Api.Models.Greetings;

/// <summary>A saved greeting as the API returns it. <c>CreatedAtUtc</c> serializes with a trailing <c>Z</c>.</summary>
public sealed record GreetingResponse(int Id, string Message, DateTime CreatedAtUtc);

/// <summary>
/// Body of <c>POST /api/greetings</c>. Validated by the minimal-API validation filter before the
/// endpoint runs: a missing, blank or over-long message is answered with a 400 problem-details
/// response listing the field errors.
/// </summary>
public sealed record CreateGreetingRequest
{
    [Required]
    [StringLength(Greeting.MessageMaxLength)]
    public required string Message { get; init; }
}

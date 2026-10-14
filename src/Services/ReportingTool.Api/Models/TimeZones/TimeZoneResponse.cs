namespace ReportingTool.Api.Models.TimeZones;

/// <summary>
/// One option of the time zone picker (#95 AC3). The offset is the one in force when the list
/// was requested, so the picker can show <c>Asia/Dubai (UTC+4)</c>; it is a label, never used to
/// convert a timestamp, because daylight saving changes it.
/// </summary>
public sealed record TimeZoneResponse(string Id, int CurrentOffsetMinutes);

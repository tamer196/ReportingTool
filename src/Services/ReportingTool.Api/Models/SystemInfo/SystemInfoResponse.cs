namespace ReportingTool.Api.Models.SystemInfo;

/// <summary>
/// What is running where. QA and the deployment smoke test read <see cref="Version"/> to confirm
/// that the test environment runs the same build that was verified in dev.
/// </summary>
/// <param name="Service">The service's application name.</param>
/// <param name="Version">Informational version, including the git commit the image was built from.</param>
/// <param name="Environment">The hosting environment name (Development, Dev, Test, ...).</param>
/// <param name="ServerTimeUtc">The server clock in UTC, for checking clock skew between environments.</param>
public sealed record SystemInfoResponse(string Service, string Version, string Environment, DateTime ServerTimeUtc);

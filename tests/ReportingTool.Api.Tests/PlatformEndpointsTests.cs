using System.Net;
using System.Net.Http.Json;
using ReportingTool.Api.Models.SystemInfo;

namespace ReportingTool.Api.Tests;

/// <summary>The endpoints the orchestrator, the pipeline smoke test and QA rely on.</summary>
public sealed class PlatformEndpointsTests
{
    private readonly ApiFactory _api;
    private readonly HttpClient _client;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public PlatformEndpointsTests(ApiFactory api)
    {
        _api = api;
        _client = api.CreateClient();
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_report_healthy_when_sql_server_is_up(string path)
    {
        var response = await _client.GetAsync(path, _ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(_ct));
    }

    [Fact]
    public async Task System_info_reports_the_environment_version_and_utc_server_time()
    {
        // Other test classes run in parallel and may move the shared clock forward meanwhile.
        var before = _api.Time.GetUtcNow().UtcDateTime;
        var info = await _client.GetFromJsonAsync<SystemInfoResponse>("/api/system/info", _ct);
        var after = _api.Time.GetUtcNow().UtcDateTime;

        Assert.Equal("ReportingTool.Api", info!.Service);
        Assert.Equal("Testing", info.Environment);
        // The milestone version that matches the Git tags (Delivery/00 §5), e.g. "0.1.0-m0+3f2a…".
        // "-m<n>" is required so the SDK default 1.0.0 fails; drop it when v1.0.0 is released.
        Assert.Matches(@"^\d+\.\d+\.\d+-m\d+(\+.+)?$", info.Version);
        Assert.InRange(info.ServerTimeUtc, before, after);
        Assert.Equal(DateTimeKind.Utc, info.ServerTimeUtc.Kind);
    }

    [Fact]
    public async Task Unknown_routes_get_a_problem_details_404()
    {
        var response = await _client.GetAsync("/api/does-not-exist", _ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

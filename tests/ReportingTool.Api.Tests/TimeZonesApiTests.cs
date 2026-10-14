using System.Net.Http.Json;
using ReportingTool.Api.Models.TimeZones;

namespace ReportingTool.Api.Tests;

/// <summary>The time zone picker's list (#95 AC3).</summary>
public sealed class TimeZonesApiTests
{
    private readonly HttpClient _client;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public TimeZonesApiTests(ApiFactory api)
    {
        _client = api.CreateClient();
    }

    [Fact]
    public async Task The_list_starts_with_utc_and_gives_each_zone_its_current_offset()
    {
        var zones = await _client.GetFromJsonAsync<List<TimeZoneResponse>>("/api/time-zones", _ct);

        Assert.Equal(new TimeZoneResponse("UTC", 0), zones![0]);
        Assert.Contains(new TimeZoneResponse("Asia/Dubai", 4 * 60), zones);
        Assert.Contains(new TimeZoneResponse("Asia/Kathmandu", 5 * 60 + 45), zones);
        Assert.Contains(new TimeZoneResponse("America/Sao_Paulo", -3 * 60), zones);
    }

    [Fact]
    public async Task The_offset_follows_daylight_saving_at_the_time_of_the_request()
    {
        // ApiFactory's clock starts on 12 Oct 2026 and only moves forward by minutes: London is
        // still on summer time (BST, UTC+1) until 25 Oct.
        var zones = await _client.GetFromJsonAsync<List<TimeZoneResponse>>("/api/time-zones", _ct);

        Assert.Contains(new TimeZoneResponse("Europe/London", 60), zones!);
    }

    [Fact]
    public async Task Legacy_aliases_and_fixed_offset_zones_are_not_listed()
    {
        var ids = (await _client.GetFromJsonAsync<List<TimeZoneResponse>>("/api/time-zones", _ct))!
            .Select(static zone => zone.Id)
            .ToList();

        Assert.Contains("Asia/Kolkata", ids);
        Assert.DoesNotContain("Asia/Calcutta", ids);
        Assert.DoesNotContain("Etc/GMT-4", ids);
        Assert.DoesNotContain("Etc/UTC", ids);
    }
}

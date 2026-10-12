using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ReportingTool.Api.Models.Greetings;

namespace ReportingTool.Api.Tests;

/// <summary>The hello-world slice: API → SQL Server and back (RT-106 AC 3).</summary>
public sealed class GreetingsApiTests
{
    private readonly ApiFactory _api;
    private readonly HttpClient _client;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public GreetingsApiTests(ApiFactory api)
    {
        _api = api;
        _client = api.CreateClient();
    }

    [Fact]
    public async Task The_seeded_greeting_is_returned_from_the_database()
    {
        var greetings = await _client.GetFromJsonAsync<List<GreetingResponse>>("/api/greetings", _ct);

        Assert.Contains(greetings!, static g => g.Message == "Hello from SQL Server");
    }

    [Fact]
    public async Task A_new_greeting_is_saved_in_utc_and_listed_first()
    {
        _api.Time.Advance(TimeSpan.FromMinutes(5));
        var expectedTime = _api.Time.GetUtcNow().UtcDateTime;

        var response = await _client.PostAsJsonAsync("/api/greetings", new { message = "  Hello from the tests  " }, _ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<GreetingResponse>(_ct);
        Assert.Equal("Hello from the tests", created!.Message);
        Assert.Equal(expectedTime, created.CreatedAtUtc);
        Assert.Equal($"/api/greetings/{created.Id}", response.Headers.Location!.OriginalString);

        // BR-14: the timestamp is labelled as UTC on the wire.
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_ct));
        Assert.EndsWith("Z", json.RootElement.GetProperty("createdAtUtc").GetString());

        var greetings = await _client.GetFromJsonAsync<List<GreetingResponse>>("/api/greetings", _ct);
        Assert.Equal(created, greetings![0]);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task A_blank_message_is_rejected_with_problem_details(string? message)
    {
        var response = await _client.PostAsJsonAsync("/api/greetings", new { message }, _ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task A_message_longer_than_the_column_is_rejected_with_the_field_error()
    {
        var response = await _client.PostAsJsonAsync("/api/greetings",
            new { message = new string('x', Greeting.MessageMaxLength + 1) }, _ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(_ct));
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("Message", out _));
    }
}

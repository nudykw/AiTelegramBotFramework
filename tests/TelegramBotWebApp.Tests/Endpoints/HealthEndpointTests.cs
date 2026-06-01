using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TelegramBotWebApp.Tests.Fixtures;

namespace TelegramBotWebApp.Tests.Endpoints;

/// <summary>
/// Integration tests for GET /health.
/// Uses <see cref="WebAppFactory"/> to spin up the real pipeline in-process.
/// </summary>
public class HealthEndpointTests : IClassFixture<WebAppFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_Health_Returns200()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_Health_ReturnsJsonWithStatus()
    {
        var response = await _client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("status", out var status));
        Assert.Equal("healthy", status.GetString());
    }

    [Fact]
    public async Task Get_Health_ReturnsTimestamp()
    {
        var response = await _client.GetAsync("/health");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(json.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task Get_Health_ContentTypeIsJson()
    {
        var response = await _client.GetAsync("/health");
        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("application/json", contentType);
    }
}

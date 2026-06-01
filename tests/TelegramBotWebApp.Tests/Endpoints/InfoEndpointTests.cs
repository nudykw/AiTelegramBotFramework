using System.Net;
using System.Text.Json;
using TelegramBotWebApp.Tests.Fixtures;

namespace TelegramBotWebApp.Tests.Endpoints;

/// <summary>
/// Integration tests for GET /api/info.
/// </summary>
public class InfoEndpointTests
{
    // ── Polling mode ──────────────────────────────────────────────────────────

    public class InPollingMode : IClassFixture<WebAppFactory>
    {
        private readonly HttpClient _client;
        private readonly string _token = "1234567890:AABBCCDDEEFFaabbccddeeff-TestToken00";

        public InPollingMode(WebAppFactory factory)
        {
            // BaseApiUrl not set → Polling mode
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Get_Info_Returns200()
        {
            var response = await _client.GetAsync("/api/info");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Get_Info_ReturnsPollingMode()
        {
            var json = await GetInfoJson();
            Assert.Equal("polling", json.GetProperty("mode").GetString());
        }

        [Fact]
        public async Task Get_Info_WebhookUrlIsNullInPollingMode()
        {
            var json = await GetInfoJson();
            Assert.True(json.TryGetProperty("webhookUrl", out var wh));
            Assert.Equal(JsonValueKind.Null, wh.ValueKind);
        }

        [Fact]
        public async Task Get_Info_TokenIsMasked()
        {
            var json = await GetInfoJson();
            var maskedToken = json.GetProperty("token").GetString()!;

            // Must not contain the full token
            Assert.DoesNotContain(_token, maskedToken);
            // Must contain ellipsis
            Assert.Contains("...", maskedToken);
        }

        [Fact]
        public async Task Get_Info_ContainsTimestamp()
        {
            var json = await GetInfoJson();
            Assert.True(json.TryGetProperty("timestamp", out _));
        }

        [Fact]
        public async Task Get_Info_ContainsVersion()
        {
            var json = await GetInfoJson();
            Assert.True(json.TryGetProperty("version", out _));
        }

        private async Task<JsonElement> GetInfoJson()
        {
            var response = await _client.GetAsync("/api/info");
            var body = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(body).RootElement;
        }
    }

    // ── Webhook mode ──────────────────────────────────────────────────────────

    public class InWebhookMode : IClassFixture<WebhookWebAppFactory>
    {
        private readonly HttpClient _client;

        public InWebhookMode(WebhookWebAppFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task Get_Info_ReturnsWebhookMode()
        {
            var json = await GetInfoJson();
            Assert.Equal("webhook", json.GetProperty("mode").GetString());
        }

        [Fact]
        public async Task Get_Info_WebhookUrlEndsWithAibot()
        {
            var json = await GetInfoJson();
            var url = json.GetProperty("webhookUrl").GetString();
            Assert.NotNull(url);
            Assert.EndsWith("/aibot", url);
        }

        [Fact]
        public async Task Get_Info_WebhookUrlNormalisesTrailingSlash()
        {
            var json = await GetInfoJson();
            var url = json.GetProperty("webhookUrl").GetString()!;
            // Should not contain double slash before aibot
            Assert.DoesNotContain("//aibot", url);
        }

        private async Task<JsonElement> GetInfoJson()
        {
            var response = await _client.GetAsync("/api/info");
            var body = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(body).RootElement;
        }
    }
}

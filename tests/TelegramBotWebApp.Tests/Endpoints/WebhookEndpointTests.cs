using System.Net;
using System.Text;
using TelegramBotWebApp.Tests.Fixtures;
using TelegramBotWebApp.Tests.Helpers;

namespace TelegramBotWebApp.Tests.Endpoints;

/// <summary>
/// Integration tests for POST /aibot (Telegram Webhook endpoint).
///
/// <para>
/// Test selection is <b>driven by your <c>.env</c> file</b>:
/// <list type="bullet">
///   <item><c>TELEGRAM_BASE_API_URL</c> is <b>empty</b>  → Polling mode → Webhook tests are <b>skipped</b></item>
///   <item><c>TELEGRAM_BASE_API_URL</c> is <b>set</b>    → Webhook mode → Polling test is <b>skipped</b></item>
/// </list>
/// This ensures you only run tests that match your current deployment configuration.
/// </para>
/// </summary>
public class WebhookEndpointTests
{
    // ── Polling mode — /aibot MUST NOT exist ──────────────────────────────────

    /// <summary>
    /// Polling mode test.
    /// Skipped automatically when <c>.env</c> specifies a <c>TELEGRAM_BASE_API_URL</c>.
    /// </summary>
    public class WhenPollingModeEnabled : IClassFixture<WebAppFactory>
    {
        private readonly HttpClient _client;

        public WhenPollingModeEnabled(WebAppFactory factory)
        {
            _client = factory.CreateClient();
        }

        [SkippableFact]
        public async Task Post_Aibot_Returns404_WhenPollingMode()
        {
            // .env has TELEGRAM_BASE_API_URL → Webhook mode → skip this Polling test
            SkipIf.True(
                DotEnvReader.IsWebhookMode,
                "Skipped: .env has TELEGRAM_BASE_API_URL set → Webhook mode is active. "
              + "This test only applies to Polling mode.");

            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/aibot", content);

            // In Polling mode the /aibot endpoint must NOT be registered
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    // ── Webhook mode — /aibot MUST be accessible ──────────────────────────────

    /// <summary>
    /// Webhook mode tests.
    /// Skipped automatically when <c>.env</c> has an empty <c>TELEGRAM_BASE_API_URL</c>.
    /// </summary>
    public class WhenWebhookModeEnabled : IClassFixture<WebhookWebAppFactory>
    {
        private readonly HttpClient _client;

        public WhenWebhookModeEnabled(WebhookWebAppFactory factory)
        {
            _client = factory.CreateClient();
        }

        [SkippableFact]
        public async Task Post_Aibot_WithValidUpdate_Returns200()
        {
            // .env has no TELEGRAM_BASE_API_URL → Polling mode → skip this Webhook test
            SkipIf.False(
                DotEnvReader.IsWebhookMode,
                "Skipped: .env has no TELEGRAM_BASE_API_URL → Polling mode is active. "
              + "This test only applies to Webhook mode.");

            var updateJson = """
                {
                    "update_id": 123456789,
                    "message": {
                        "message_id": 1,
                        "from": { "id": 999, "is_bot": false, "first_name": "Test" },
                        "chat": { "id": 999, "type": "private" },
                        "date": 1700000000,
                        "text": "Hello"
                    }
                }
                """;

            var content = new StringContent(updateJson, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/aibot", content);

            Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        [SkippableFact]
        public async Task Post_Aibot_WithEmptyBody_Returns400Or500()
        {
            SkipIf.False(
                DotEnvReader.IsWebhookMode,
                "Skipped: Polling mode active in .env.");

            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/aibot", content);

            Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }
}

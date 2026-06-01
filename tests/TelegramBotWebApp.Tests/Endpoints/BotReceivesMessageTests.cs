using System.Net;
using System.Text;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using TelegramBotWebApp.Tests.Fixtures;

namespace TelegramBotWebApp.Tests.Endpoints;

/// <summary>
/// Tests that verify the bot correctly receives a plain text message from a user
/// and delegates processing to <see cref="UpdateHandler"/>.
/// Uses a mocked <see cref="UpdateHandler"/> — no real Telegram API or AI calls are made.
/// </summary>
public class BotReceivesMessageTests : IClassFixture<MockedWebhookWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly MockedWebhookWebAppFactory _factory;

    public BotReceivesMessageTests(MockedWebhookWebAppFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal valid Telegram Update JSON with the given text message.
    /// </summary>
    private static StringContent BuildTextMessage(string text, long userId = 42, long chatId = 42, int updateId = 1)
    {
        var json = $$"""
            {
                "update_id": {{updateId}},
                "message": {
                    "message_id": 1,
                    "from": {
                        "id": {{userId}},
                        "is_bot": false,
                        "first_name": "TestUser",
                        "username": "testuser",
                        "language_code": "en"
                    },
                    "chat": {
                        "id": {{chatId}},
                        "type": "private",
                        "first_name": "TestUser"
                    },
                    "date": 1700000000,
                    "text": "{{text}}"
                }
            }
            """;
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Aibot_WithHiMessage_Returns200()
    {
        // Arrange
        var content = BuildTextMessage("Hi");

        // Act
        var response = await _client.PostAsync("/aibot", content);

        // Assert — endpoint returns 200 OK (handler is fire-and-forget)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_Aibot_WithHiMessage_CallsUpdateHandler()
    {
        // Arrange
        _factory.HandlerMock.Invocations.Clear(); // reset between tests
        var content = BuildTextMessage("Hi", updateId: 2);

        // Act
        await _client.PostAsync("/aibot", content);

        // Assert — UpdateHandler.HandleUpdateAsync was called exactly once
        _factory.HandlerMock.Verify(
            h => h.HandleUpdateAsync(
                It.IsAny<ITelegramBotClient>(),
                It.IsAny<Update>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "UpdateHandler.HandleUpdateAsync must be invoked once per webhook update");
    }

    [Fact]
    public async Task Post_Aibot_WithHiMessage_PassesCorrectUpdateToHandler()
    {
        // Arrange
        Update? capturedUpdate = null;
        _factory.HandlerMock
            .Setup(h => h.HandleUpdateAsync(
                It.IsAny<ITelegramBotClient>(),
                It.IsAny<Update>(),
                It.IsAny<CancellationToken>()))
            .Callback<ITelegramBotClient, Update, CancellationToken>(
                (_, update, _) => capturedUpdate = update)
            .Returns(Task.CompletedTask);

        var content = BuildTextMessage("Hi", updateId: 3, userId: 777, chatId: 777);

        // Act
        await _client.PostAsync("/aibot", content);

        // Assert — the Update object handed to the handler contains the correct text
        Assert.NotNull(capturedUpdate);
        Assert.Equal("Hi", capturedUpdate.Message?.Text);
        Assert.Equal(777L, capturedUpdate.Message?.From?.Id);
    }

    [Theory]
    [InlineData("Hi")]
    [InlineData("Hello bot!")]
    [InlineData("/help")]
    [InlineData("Привіт!")]
    public async Task Post_Aibot_WithVariousTextMessages_Returns200(string text)
    {
        // Arrange
        var content = BuildTextMessage(text);

        // Act
        var response = await _client.PostAsync("/aibot", content);

        // Assert — all user messages must be accepted (200 OK)
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

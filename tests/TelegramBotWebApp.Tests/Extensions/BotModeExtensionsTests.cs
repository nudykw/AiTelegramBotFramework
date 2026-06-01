using ServiceLayer.Services.Telegram.Configuretions;
using TelegramBotWebApp.Extensions;

namespace TelegramBotWebApp.Tests.Extensions;

/// <summary>
/// Unit tests for <see cref="BotModeExtensions"/>.
/// Covers mode detection and URL normalisation logic.
/// </summary>
public class BotModeExtensionsTests
{
    // ── IsWebhookMode ─────────────────────────────────────────────────────────

    [Fact]
    public void IsWebhookMode_WhenBaseApiUrlIsSet_ReturnsTrue()
    {
        var config = new TelegramBotConfiguration { BaseApiUrl = "https://example.com" };
        Assert.True(config.IsWebhookMode());
    }

    [Fact]
    public void IsWebhookMode_WhenBaseApiUrlIsNull_ReturnsFalse()
    {
        var config = new TelegramBotConfiguration { BaseApiUrl = null };
        Assert.False(config.IsWebhookMode());
    }

    [Fact]
    public void IsWebhookMode_WhenBaseApiUrlIsEmpty_ReturnsFalse()
    {
        var config = new TelegramBotConfiguration { BaseApiUrl = "" };
        Assert.False(config.IsWebhookMode());
    }

    [Fact]
    public void IsWebhookMode_WhenBaseApiUrlIsWhitespace_ReturnsFalse()
    {
        var config = new TelegramBotConfiguration { BaseApiUrl = "   " };
        Assert.False(config.IsWebhookMode());
    }

    // ── GetWebhookUrl ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.com",  "https://example.com/aibot")]
    [InlineData("https://example.com/", "https://example.com/aibot")]
    [InlineData("https://example.com//","https://example.com/aibot")]
    [InlineData("http://localhost:8080", "http://localhost:8080/aibot")]
    [InlineData("http://localhost:8080/","http://localhost:8080/aibot")]
    public void GetWebhookUrl_NormalisesTrailingSlash_AndAppendsAibot(
        string baseApiUrl, string expectedUrl)
    {
        var config = new TelegramBotConfiguration { BaseApiUrl = baseApiUrl };
        Assert.Equal(expectedUrl, config.GetWebhookUrl());
    }

    [Fact]
    public void GetWebhookUrl_WhenBaseApiUrlIsNull_ThrowsInvalidOperationException()
    {
        var config = new TelegramBotConfiguration { BaseApiUrl = null };
        Assert.Throws<InvalidOperationException>(() => config.GetWebhookUrl());
    }

    [Fact]
    public void GetWebhookUrl_WhenBaseApiUrlIsEmpty_ThrowsInvalidOperationException()
    {
        var config = new TelegramBotConfiguration { BaseApiUrl = "" };
        Assert.Throws<InvalidOperationException>(() => config.GetWebhookUrl());
    }

    [Fact]
    public void GetWebhookUrl_WhenBaseApiUrlIsWhitespace_ThrowsInvalidOperationException()
    {
        var config = new TelegramBotConfiguration { BaseApiUrl = "   " };
        Assert.Throws<InvalidOperationException>(() => config.GetWebhookUrl());
    }

    // ── Path suffix ───────────────────────────────────────────────────────────

    [Fact]
    public void GetWebhookUrl_AlwaysEndsWithAibot()
    {
        var config = new TelegramBotConfiguration { BaseApiUrl = "https://mybot.example.org" };
        Assert.EndsWith("/aibot", config.GetWebhookUrl());
    }
}

using ServiceLayer.Services.Telegram.Configuretions;

namespace TelegramBotWebApp.Extensions;

/// <summary>
/// Determines the bot operating mode based on <see cref="TelegramBotConfiguration.BaseApiUrl"/>.
/// </summary>
public static class BotModeExtensions
{
    /// <summary>
    /// Returns <c>true</c> when <see cref="TelegramBotConfiguration.BaseApiUrl"/> is set,
    /// indicating the bot should run in Webhook mode.
    /// </summary>
    public static bool IsWebhookMode(this TelegramBotConfiguration config)
        => !string.IsNullOrWhiteSpace(config.BaseApiUrl);

    /// <summary>
    /// Returns the fully-qualified webhook URL: <c>{BaseApiUrl}/aibot</c>.
    /// Trailing slashes in <see cref="TelegramBotConfiguration.BaseApiUrl"/> are removed automatically.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="TelegramBotConfiguration.BaseApiUrl"/> is not set.</exception>
    public static string GetWebhookUrl(this TelegramBotConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.BaseApiUrl))
            throw new InvalidOperationException(
                "BaseApiUrl is not configured. Cannot build webhook URL in Polling mode.");

        return $"{config.BaseApiUrl.TrimEnd('/')}/aibot";
    }
}

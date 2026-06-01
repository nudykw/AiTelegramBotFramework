using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace TelegramBotWebApp.Endpoints;

public static class WebhookEndpoints
{
    /// <summary>
    /// Maps POST /aibot — receives Telegram webhook updates.
    /// Only called when the bot is running in Webhook mode.
    /// </summary>
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        app.MapPost("/aibot", async (
            Update update,
            ITelegramBotClient botClient,
            IUpdateHandler handler,
            CancellationToken cancellationToken) =>
        {
            await handler.HandleUpdateAsync(botClient, update, cancellationToken);
            return Results.Ok();
        })
        .WithName("TelegramWebhook")
        .WithSummary("Telegram Webhook receiver")
        .WithDescription("Accepts Telegram update payloads. This endpoint is called by the Telegram servers when Webhook mode is active.")
        .WithTags("Telegram")
        .AllowAnonymous();
    }
}


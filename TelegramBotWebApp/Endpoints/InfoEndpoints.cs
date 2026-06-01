using ServiceLayer.Services.Telegram.Configuretions;
using TelegramBotWebApp.Extensions;

namespace TelegramBotWebApp.Endpoints;

public static class InfoEndpoints
{
    /// <summary>Maps GET /api/info — returns basic bot information (token is masked).</summary>
    public static void MapInfoEndpoints(this WebApplication app)
    {
        app.MapGet("/api/info", (TelegramBotConfiguration config) =>
        {
            var token = config.BotToken;
            var maskedToken = token.Length > 10
                ? $"{token[..6]}...{token[^4..]}"
                : "***";

            return Results.Ok(new
            {
                mode      = config.IsWebhookMode() ? "webhook" : "polling",
                webhookUrl = config.IsWebhookMode() ? config.GetWebhookUrl() : null,
                ownerId   = config.OwnerId,
                token     = maskedToken,
                version   = typeof(InfoEndpoints).Assembly.GetName().Version?.ToString() ?? "unknown",
                timestamp = DateTimeOffset.UtcNow
            });
        })
        .WithName("BotInfo")
        .WithSummary("Bot information")
        .WithDescription("Returns basic information about the bot. The token is partially masked.")
        .WithTags("System")
        .AllowAnonymous();
    }
}

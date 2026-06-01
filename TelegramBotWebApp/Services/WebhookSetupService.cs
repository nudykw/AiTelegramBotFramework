using ServiceLayer.Services.Telegram.Configuretions;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using TelegramBotWebApp.Extensions;

namespace TelegramBotWebApp.Services;

/// <summary>
/// Hosted service that registers the Telegram Webhook on application start
/// and deletes it on graceful shutdown.
/// Only activated when <see cref="TelegramBotConfiguration.BaseApiUrl"/> is set.
/// </summary>
public sealed class WebhookSetupService : IHostedService
{
    private readonly ITelegramBotClient _botClient;
    private readonly TelegramBotConfiguration _config;
    private readonly ILogger<WebhookSetupService> _logger;

    public WebhookSetupService(
        ITelegramBotClient botClient,
        TelegramBotConfiguration config,
        ILogger<WebhookSetupService> logger)
    {
        _botClient = botClient;
        _config    = config;
        _logger    = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var webhookUrl = _config.GetWebhookUrl();
        _logger.LogInformation("Registering Telegram webhook at {Url}", webhookUrl);

        await _botClient.SetWebhook(
            url: webhookUrl,
            allowedUpdates: Array.Empty<UpdateType>(), // receive all update types
            cancellationToken: cancellationToken);

        _logger.LogInformation("Webhook registered successfully.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Removing Telegram webhook on shutdown...");
        await _botClient.DeleteWebhook(cancellationToken: cancellationToken);
        _logger.LogInformation("Webhook removed.");
    }
}

using Microsoft.Extensions.Logging;
using ServiceLayer.Constans;
using global::Telegram.Bot;
using global::Telegram.Bot.Types;
using global::Telegram.Bot.Types.Enums;
using global::Telegram.Bot.Polling;
using Microsoft.Extensions.Localization;
using ServiceLayer.Resources;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace ServiceLayer.Services.Telegram
{
    /// <summary>
    /// An abstract class to compose Receiver Service and Update Handler classes
    /// </summary>
    /// <typeparam name="TUpdateHandler">Update Handler to use in Update Receiver</typeparam>
    public abstract class ReceiverServiceBase<TUpdateHandler> : BaseService, IReceiverService
        where TUpdateHandler : IUpdateHandler
    {
        private readonly global::Telegram.Bot.ITelegramBotClient _botClient;
        private readonly IUpdateHandler _updateHandler;
        private readonly IStringLocalizerFactory _localizerFactory;

        internal ReceiverServiceBase(IServiceProvider serviceProvider,
            ILogger<ReceiverServiceBase<TUpdateHandler>> logger,
            global::Telegram.Bot.ITelegramBotClient botClient,
            TUpdateHandler updateHandler)
            : base(serviceProvider, logger)
        {
            _botClient = botClient;
            _updateHandler = updateHandler;
            _localizerFactory = serviceProvider.GetRequiredService<IStringLocalizerFactory>();
            logger.LogInformation($"EnvironmentVersion: {Environment.Version}");
        }

        /// <summary>
        /// Start to service Updates with provided Update Handler class
        /// </summary>
        /// <param name="stoppingToken"></param>
        /// <returns></returns>
        public async Task ReceiveAsync(CancellationToken stoppingToken)
        {
            // ToDo: we can inject ReceiverOptions through IOptions container
            var receiverOptions = new ReceiverOptions()
            {
                AllowedUpdates = Array.Empty<UpdateType>(),
                DropPendingUpdates = true,
            };

            var me = await _botClient.GetMe(stoppingToken);
            _logger.LogInformation("Start receiving updates for {BotName}", me.Username ?? "My Awesome Bot");

            // Register bot commands
            await RegisterBotCommandsAsync(stoppingToken);

            // Start receiving updates
            await _botClient.ReceiveAsync(
                updateHandler: _updateHandler,
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken);
        }

        private async Task RegisterBotCommandsAsync(CancellationToken ct)
        {
            // 1. Register default commands (English from localizer)
            // This serves as the fallback for all languages not explicitly registered
            await SetLocalizedCommandsAsync(null, ct);

            // 2. Register English explicitly
            await SetLocalizedCommandsAsync("en", ct);

            // 3. Register Ukrainian commands
            await SetLocalizedCommandsAsync("uk", ct);

            _logger.LogInformation("Bot commands registered successfully: English (default/en) and Ukrainian (uk).");
        }

        private async Task SetLocalizedCommandsAsync(string languageCode, CancellationToken ct)
        {
            var appSettings = _serviceProvider.GetRequiredService<AppSettings>();
            var localizer = _localizerFactory.Create(typeof(BotMessages));
            using (new CultureSwitcher(languageCode))
            {
                var filteredCommands = ServiceLayer.Constans.BotCommand.GetAll();
                if (appSettings?.Scheduler?.Enabled != true)
                {
                    filteredCommands = filteredCommands.Where(x => x.Value != ServiceLayer.Constans.BotCommand.Schedule.Value && x.Value != ServiceLayer.Constans.BotCommand.Shedule.Value).ToList();
                }

                var commands = filteredCommands.Select(x => {
                    var descriptionKey = "Desc_" + x.Value.TrimStart('/');
                    var localizedDescription = localizer[descriptionKey];
                    return new global::Telegram.Bot.Types.BotCommand
                    {
                        Command = x.Value.TrimStart('/'),
                        Description = localizedDescription.ResourceNotFound ? x.Description : localizedDescription.Value
                    };
                })
                .OrderBy(x => x.Command)
                .ToList();

                await _botClient.SetMyCommands(commands, languageCode: languageCode, cancellationToken: ct);
            }
        }

        private class CultureSwitcher : IDisposable
        {
            private readonly CultureInfo _originalCulture;
            private readonly CultureInfo _originalUiCulture;

            public CultureSwitcher(string? languageCode)
            {
                _originalCulture = CultureInfo.CurrentCulture;
                _originalUiCulture = CultureInfo.CurrentUICulture;
                var culture = new CultureInfo(languageCode ?? "");
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }

            public void Dispose()
            {
                CultureInfo.CurrentCulture = _originalCulture;
                CultureInfo.CurrentUICulture = _originalUiCulture;
            }
        }
    }
}

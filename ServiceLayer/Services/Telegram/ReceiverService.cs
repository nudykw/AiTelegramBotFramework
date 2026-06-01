using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace ServiceLayer.Services.Telegram;
// Compose Receiver and UpdateHandler implementation
public class ReceiverService : ReceiverServiceBase<UpdateHandler>
{
    public ReceiverService(
        IServiceProvider serviceProvider,
        ILogger<ReceiverServiceBase<UpdateHandler>> logger,
        ITelegramBotClient botClient,
        UpdateHandler updateHandler)
        : base(serviceProvider,logger, botClient, updateHandler)
    {
    }
}

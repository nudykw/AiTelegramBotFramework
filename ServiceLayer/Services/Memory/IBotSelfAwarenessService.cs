using System.Threading.Tasks;

namespace ServiceLayer.Services.Memory
{
    public interface IBotSelfAwarenessService
    {
        Task<BotMetadata> GetBotMetadataAsync(long? userId);
        Task<string> GetBotSystemSummaryPromptAsync(long? userId);
    }
}

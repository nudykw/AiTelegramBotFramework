using ServiceLayer.Models;
using System.Threading.Tasks;

namespace ServiceLayer.Services.MessageProcessor
{
    public interface ISummaryService
    {
        Task<ChatSummary?> GetSummary(long chatId);
        Task SaveSummary(long chatId, ChatSummary summary);
        Task ClearSummary(long chatId);
        
        Task<string?> GetToolSummary(long chatId, long messageId);
        Task SetToolSummary(long chatId, long messageId, string summary);
    }
}

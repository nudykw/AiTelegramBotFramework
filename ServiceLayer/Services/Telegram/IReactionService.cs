using DataBaseLayer.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ServiceLayer.Services.Telegram
{
    public interface IReactionService
    {
        /// <summary>
        /// Toggles a reaction. If the reaction is from a mutually exclusive group, 
        /// it replaces the old one. If it's the same reaction, it removes it.
        /// </summary>
        Task ToggleReaction(long chatId, long messageId, long userId, MessageReactionType type);

        /// <summary>
        /// Gets the total counts for each reaction type for a message.
        /// </summary>
        Task<Dictionary<MessageReactionType, int>> GetReactionCounts(long chatId, long messageId);
        Task<Dictionary<long, Dictionary<MessageReactionType, int>>> GetReactionCountsForMessages(long chatId, List<long> messageIds);

        /// <summary>
        /// Gets the reaction set by a specific user for a message.
        /// </summary>
        Task<MessageReactionType> GetUserReaction(long chatId, long messageId, long userId);
    }
}

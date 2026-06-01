using DataBaseLayer.Enums;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ServiceLayer.Services.Telegram
{
    public class ReactionService : BaseService, IReactionService
    {
        private readonly IRepository<MessageReaction> _reactionRepository;

        public ReactionService(IServiceProvider serviceProvider, ILogger<ReactionService> logger,
            IRepository<MessageReaction> reactionRepository)
            : base(serviceProvider, logger)
        {
            _reactionRepository = reactionRepository;
        }

        public async Task ToggleReaction(long chatId, long messageId, long userId, MessageReactionType type)
        {
            var reaction = await _reactionRepository.Get(p => p.ChatId == chatId && p.MessageId == messageId && p.UserId == userId);

            if (reaction == null)
            {
                reaction = new MessageReaction
                {
                    ChatId = chatId,
                    MessageId = messageId,
                    UserId = userId,
                    Reaction = type,
                    CreatedAt = DateTime.UtcNow,
                    ModifiedAt = DateTime.UtcNow
                };
                _reactionRepository.Add(reaction);
            }
            else
            {
                // Group logic
                var current = reaction.Reaction;
                
                // Group 1: Like/Dislike
                if (type == MessageReactionType.Like || type == MessageReactionType.Dislike)
                {
                    if (current.HasFlag(type))
                    {
                        current &= ~type; // Remove if already set
                    }
                    else
                    {
                        current &= ~(MessageReactionType.Like | MessageReactionType.Dislike); // Clear group
                        current |= type; // Set new
                    }
                }
                // Group 2: Laugh/Sad
                else if (type == MessageReactionType.Laugh || type == MessageReactionType.Sad)
                {
                    if (current.HasFlag(type))
                    {
                        current &= ~type;
                    }
                    else
                    {
                        current &= ~(MessageReactionType.Laugh | MessageReactionType.Sad);
                        current |= type;
                    }
                }
                // Group 3: Think/Vomit
                else if (type == MessageReactionType.Think || type == MessageReactionType.Vomit)
                {
                    if (current.HasFlag(type))
                    {
                        current &= ~type;
                    }
                    else
                    {
                        current &= ~(MessageReactionType.Think | MessageReactionType.Vomit);
                        current |= type;
                    }
                }

                reaction.Reaction = current;
                reaction.ModifiedAt = DateTime.UtcNow;
                _reactionRepository.Update(reaction);
            }

            await _reactionRepository.SaveChanges();
        }

        public async Task<Dictionary<long, Dictionary<MessageReactionType, int>>> GetReactionCountsForMessages(long chatId, List<long> messageIds)
        {
            var reactions = _reactionRepository.GetAll()
                .Where(p => p.ChatId == chatId && messageIds.Contains(p.MessageId))
                .ToList();

            var result = new Dictionary<long, Dictionary<MessageReactionType, int>>();
            var types = Enum.GetValues(typeof(MessageReactionType)).Cast<MessageReactionType>()
                .Where(v => v != MessageReactionType.None).ToList();

            foreach (var messageId in messageIds)
            {
                var msgReactions = reactions.Where(r => r.MessageId == messageId).ToList();
                var counts = new Dictionary<MessageReactionType, int>();
                foreach (var type in types)
                {
                    counts[type] = msgReactions.Count(r => r.Reaction.HasFlag(type));
                }
                result[messageId] = counts;
            }

            return result;
        }

        public async Task<Dictionary<MessageReactionType, int>> GetReactionCounts(long chatId, long messageId)
        {
            var counts = await GetReactionCountsForMessages(chatId, new List<long> { messageId });
            return counts[messageId];
        }

        public async Task<MessageReactionType> GetUserReaction(long chatId, long messageId, long userId)
        {
            var reaction = await _reactionRepository.Get(p => p.ChatId == chatId && p.MessageId == messageId && p.UserId == userId);
            return reaction?.Reaction ?? MessageReactionType.None;
        }
    }
}

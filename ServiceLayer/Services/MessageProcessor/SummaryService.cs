using Microsoft.Extensions.Caching.Memory;
using ServiceLayer.Models;
using System;
using System.Threading.Tasks;

namespace ServiceLayer.Services.MessageProcessor
{
    public class SummaryService : ISummaryService
    {
        private readonly IMemoryCache _cache;
        private const string CacheKeyPrefix = "Summary:";

        public SummaryService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public Task<ChatSummary?> GetSummary(long chatId)
        {
            _cache.TryGetValue(GetCacheKey(chatId), out ChatSummary? summary);
            return Task.FromResult(summary);
        }

        public Task SaveSummary(long chatId, ChatSummary summary)
        {
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromHours(24));
            
            _cache.Set(GetCacheKey(chatId), summary, cacheOptions);
            return Task.CompletedTask;
        }

        public Task ClearSummary(long chatId)
        {
            _cache.Remove(GetCacheKey(chatId));
            return Task.CompletedTask;
        }

        public Task<string?> GetToolSummary(long chatId, long messageId)
        {
            _cache.TryGetValue(GetToolCacheKey(chatId, messageId), out string? summary);
            return Task.FromResult(summary);
        }

        public Task SetToolSummary(long chatId, long messageId, string summary)
        {
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromHours(24));
            
            _cache.Set(GetToolCacheKey(chatId, messageId), summary, cacheOptions);
            return Task.CompletedTask;
        }

        private string GetCacheKey(long chatId) => $"{CacheKeyPrefix}{chatId}";
        private string GetToolCacheKey(long chatId, long messageId) => $"ToolSummary:{chatId}:{messageId}";
    }
}

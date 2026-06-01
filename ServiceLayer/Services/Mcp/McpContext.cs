using System;
using System.Threading;

namespace ServiceLayer.Services.Mcp
{
    public static class McpContext
    {
        private static readonly AsyncLocal<McpContextData?> _context = new();

        public static long? ChatId => _context.Value?.ChatId;
        public static long? UserId => _context.Value?.UserId;

        public static IDisposable Symbolize(long chatId, long userId)
        {
            _context.Value = new McpContextData(chatId, userId);
            return new ContextScope();
        }

        private class ContextScope : IDisposable
        {
            public void Dispose()
            {
                _context.Value = null;
            }
        }
    }

    internal class McpContextData
    {
        public long ChatId { get; }
        public long UserId { get; }

        public McpContextData(long chatId, long userId)
        {
            ChatId = chatId;
            UserId = userId;
        }
    }
}

using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServiceLayer.Services.Memory;

namespace ServiceLayer.Services.Mcp
{
    public class BotMetadataMcpTool : INativeMcpTool
    {
        private readonly IBotSelfAwarenessService _botSelfAwarenessService;
        private readonly ILogger<BotMetadataMcpTool> _logger;

        public string Name => "get_bot_metadata";
        public string ServerName => "native-bot";


        public string Description => "Retrieves detailed technical specifications and status for the bot, " +
                                     "including active AI model features, context window size, output limits, " +
                                     "and current user's balance and billing state.";

        public string JsonSchema => """
            {
              "type": "object",
              "properties": {}
            }
            """;

        public BotMetadataMcpTool(IBotSelfAwarenessService botSelfAwarenessService, ILogger<BotMetadataMcpTool> logger)
        {
            _botSelfAwarenessService = botSelfAwarenessService;
            _logger = logger;
        }

        public async Task<string> ExecuteAsync(string argumentsJson)
        {
            var userId = McpContext.UserId;
            _logger.LogInformation("Executing get_bot_metadata MCP tool for UserId={UserId}", userId);

            try
            {
                var metadata = await _botSelfAwarenessService.GetBotMetadataAsync(userId);
                return JsonSerializer.Serialize(metadata, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error executing get_bot_metadata MCP tool for UserId={UserId}", userId);
                return JsonSerializer.Serialize(new
                {
                    error = "Failed to retrieve bot metadata",
                    details = ex.Message
                });
            }
        }
    }
}

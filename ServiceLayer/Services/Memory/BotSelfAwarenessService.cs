using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataBaseLayer.Enums;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.Logging;
using ServiceLayer.Constans;
using Telegram.Bot;

namespace ServiceLayer.Services.Memory
{
    public class BotSelfAwarenessService : IBotSelfAwarenessService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IRepository<TelegramUserInfo> _telegramUserInfoRepository;
        private readonly AppSettings _appSettings;
        private readonly IChatServiceFactory _chatServiceFactory;
        private readonly ILogger<BotSelfAwarenessService> _logger;

        private string? _botName;
        private string? _botUsername;
        private readonly SemaphoreSlim _identityLock = new(1, 1);

        // Standard specifications dictionary for model families
        private static readonly Dictionary<string, (int ContextWindow, int OutputLimit, string[] Features)> ModelSpecs = new(StringComparer.OrdinalIgnoreCase)
        {
            // Gemini Flash
            { "gemini-1.5-flash", (1048576, 8192, new[] { "structured_json", "vision", "audio", "tool_calling" }) },
            { "gemini-2.0-flash", (1048576, 8192, new[] { "structured_json", "vision", "audio", "tool_calling" }) },
            { "gemini-2.5-flash", (1048576, 8192, new[] { "structured_json", "vision", "audio", "tool_calling" }) },
            { "gemini-3-flash-preview", (1048576, 8192, new[] { "structured_json", "vision", "audio", "tool_calling" }) },

            // Gemini Pro
            { "gemini-pro", (2097152, 8192, new[] { "structured_json", "vision", "audio", "tool_calling" }) },
            { "gemini-1.5-pro", (2097152, 8192, new[] { "structured_json", "vision", "audio", "tool_calling" }) },
            { "gemini-2.5-pro", (2097152, 8192, new[] { "structured_json", "vision", "audio", "tool_calling" }) },
            { "gemini-3-pro-preview", (2097152, 8192, new[] { "structured_json", "vision", "audio", "tool_calling" }) },
            { "gemini-3.1-pro-preview", (2097152, 8192, new[] { "structured_json", "vision", "audio", "tool_calling" }) },

            // OpenAI GPT-4o
            { "gpt-4o", (128000, 16384, new[] { "structured_json", "vision", "tool_calling" }) },
            { "gpt-4o-mini", (128000, 16384, new[] { "structured_json", "vision", "tool_calling" }) },

            // OpenAI Reasoning
            { "o1", (200000, 32768, new[] { "structured_json", "tool_calling" }) },
            { "o1-pro", (200000, 32768, new[] { "structured_json", "tool_calling" }) },
            { "o3-mini", (200000, 32768, new[] { "structured_json", "tool_calling" }) },

            // DeepSeek
            { "deepseek-chat", (64000, 8000, new[] { "structured_json", "tool_calling" }) },
            { "deepseek-reasoner", (64000, 8000, new[] { "tool_calling" }) },

            // Grok
            { "grok-2", (128000, 4096, new[] { "structured_json", "tool_calling" }) },
            { "grok-3", (128000, 4096, new[] { "structured_json", "tool_calling" }) },
            { "grok-3-mini", (128000, 4096, new[] { "structured_json", "tool_calling" }) }
        };

        public BotSelfAwarenessService(
            ITelegramBotClient botClient,
            IRepository<TelegramUserInfo> telegramUserInfoRepository,
            AppSettings appSettings,
            IChatServiceFactory chatServiceFactory,
            ILogger<BotSelfAwarenessService> logger)
        {
            _botClient = botClient;
            _telegramUserInfoRepository = telegramUserInfoRepository;
            _appSettings = appSettings;
            _chatServiceFactory = chatServiceFactory;
            _logger = logger;
        }

        public async Task<BotMetadata> GetBotMetadataAsync(long? userId)
        {
            await EnsureBotIdentityResolvedAsync();
            var (activeProvider, activeModel) = await ResolveActiveProviderAndModelAsync(userId);
            var (contextWindow, outputLimit, features) = GetModelSpecs(activeModel);
            var balanceInfo = await ResolveUserBalanceAsync(userId);

            return new BotMetadata
            {
                BotName = _botName ?? "AI Assistant",
                BotUsername = _botUsername ?? "gpt_assistant_bot",
                ActiveModel = activeModel,
                ActiveProvider = activeProvider?.DisplayName ?? activeProvider?.Value ?? "Unknown",
                ContextWindow = contextWindow,
                OutputLimit = outputLimit,
                SupportedFeatures = features,
                UserBalance = balanceInfo
            };
        }

        public async Task<string> GetBotSystemSummaryPromptAsync(long? userId)
        {
            var metadata = await GetBotMetadataAsync(userId);
            var now = DateTime.UtcNow;
            var timeString = $"{now:dddd, MMMM dd, yyyy HH:mm:ss} UTC (Unix timestamp: {new DateTimeOffset(now).ToUnixTimeSeconds()})";

            string balanceStatus = metadata.UserBalance != null
                ? (metadata.UserBalance.IsUnlimited ? "Unlimited" : $"${metadata.UserBalance.Balance.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}")
                : "Unknown";

            string capabilitiesJoined = metadata.SupportedFeatures.Length > 0
                ? string.Join(", ", metadata.SupportedFeatures)
                : "general chat capability";

            var prompt = $@"You are {metadata.BotName} (@{metadata.BotUsername}), a highly intelligent and helpful AI assistant running on Telegram.
You are powered by the {metadata.ActiveModel} model via the {metadata.ActiveProvider} provider.

Current System Time: {timeString}

TECHNICAL SPECIFICATIONS:
- Model: {metadata.ActiveModel}
- Provider: {metadata.ActiveProvider}
- Context Window Size: {metadata.ContextWindow} tokens
- Max Output Token Limit: {metadata.OutputLimit} tokens
- Supported Model Capabilities: {capabilitiesJoined}

USER CONTEXT:
- Your current user: ID {userId ?? 0}
- Balance status: {balanceStatus}

NEWSLETTER SCHEDULER & TEMPORAL SAFETY GUARDRAILS:
You have access to scheduled recurring newsletter management tools:
- create_scheduled_newsletter(prompt, cronExpression, friendlyScheduleName?): Creates a recurring newsletter.
- list_scheduled_newsletters(): Lists active newsletters for this chat.
- delete_scheduled_newsletter(newsletterId): Deletes a newsletter by ID.

CRITICAL RULES FOR SCHEDULER TIME EXPRESSIONS:
When the user asks to schedule, create, or modify a recurring delivery (e.g., ""send me Kyiv weather news every Monday morning""):
1. Proactively convert the natural language time expression into a standard 5-field UTC cron expression (e.g., ""every Monday at 9am UTC"" -> ""0 9 * * 1"").
2. NEVER include absolute dates (e.g., ""June 1, 2026"") or relative calendar offsets (e.g., ""next Monday"") in the 'prompt' argument of create_scheduled_newsletter.
3. The 'prompt' argument must remain ABSTRACT and EVERGREEN (e.g., ""Weather forecast in Kyiv for the upcoming week starting from current Monday""). This ensures the generated content is dynamically updated in the future.
4. During actual background execution of the newsletter, you will have access to external time tools to determine the precise execution date. Do not freeze today's date in the prompt.
5. NEVER include ChatId or UserId in tool arguments; they are securely resolved from execution context.";

            if (userId.HasValue)
            {
                var user = await _telegramUserInfoRepository.Get(p => p.Id == userId.Value);
                if (user != null && !string.IsNullOrWhiteSpace(user.SystemPrompt))
                {
                    prompt += $"\n\n=========================================\n" +
                              $"CRITICAL PERSONA & COMMUNICATION RULES (SET BY USER):\n" +
                              $"You must strictly follow these instructions for all your responses to the user:\n" +
                              $"{user.SystemPrompt}\n" +
                              $"=========================================";
                }
            }

            return prompt;
        }

        private async Task EnsureBotIdentityResolvedAsync()
        {
            if (_botUsername != null) return;

            await _identityLock.WaitAsync();
            try
            {
                if (_botUsername != null) return;

                var me = await _botClient.GetMe();
                _botName = me.FirstName;
                _botUsername = me.Username;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve bot identity dynamically. Using safe fallbacks.");
                _botName = "AI Assistant";
                _botUsername = "gpt_assistant_bot";
            }
            finally
            {
                _identityLock.Release();
            }
        }

        private async Task<(AiProvider? Provider, string ModelName)> ResolveActiveProviderAndModelAsync(long? userId)
        {
            var providers = _chatServiceFactory.GetAvailableProviders().ToList();
            if (!providers.Any())
            {
                return (AiProvider.OpenAI, "gpt-4o-mini");
            }

            ChatProviderConfig? resolvedConfig = null;
            string? selectedModel = null;

            if (userId.HasValue)
            {
                var user = await _telegramUserInfoRepository.Get(p => p.Id == userId.Value);
                if (user != null)
                {
                    selectedModel = user.SelectedModel;
                    if (user.PreferredProvider != ChatStrategy.Auto)
                    {
                        var aiProvider = user.PreferredProvider switch
                        {
                            ChatStrategy.OpenAI => AiProvider.OpenAI,
                            ChatStrategy.Gemini => AiProvider.Gemini,
                            ChatStrategy.DeepSeek => AiProvider.DeepSeek,
                            ChatStrategy.Grok => AiProvider.Grok,
                            _ => null
                        };

                        if (aiProvider != null)
                        {
                            resolvedConfig = providers.FirstOrDefault(p => p.ProviderType == aiProvider);
                        }
                    }
                }
            }

            if (resolvedConfig == null)
            {
                resolvedConfig = providers.First();
            }

            var activeProvider = resolvedConfig.ProviderType;
            var activeModel = !string.IsNullOrEmpty(selectedModel) ? selectedModel : (resolvedConfig.ModelName ?? "unknown-model");

            return (activeProvider, activeModel);
        }

        private (int ContextWindow, int OutputLimit, string[] Features) GetModelSpecs(string modelName)
        {
            if (ModelSpecs.TryGetValue(modelName, out var specs))
            {
                return specs;
            }

            foreach (var key in ModelSpecs.Keys)
            {
                if (modelName.Contains(key, StringComparison.OrdinalIgnoreCase) || key.Contains(modelName, StringComparison.OrdinalIgnoreCase))
                {
                    return ModelSpecs[key];
                }
            }

            return (128000, 4096, new[] { "tool_calling" });
        }

        private async Task<UserBalanceInfo?> ResolveUserBalanceAsync(long? userId)
        {
            if (!userId.HasValue) return null;

            var user = await _telegramUserInfoRepository.Get(p => p.Id == userId.Value);
            if (user == null) return null;

            bool isUnlimited = _appSettings.TelegramBotConfiguration.IgnoredBalanceUserIds?.Contains(userId.Value) == true ||
                               _appSettings.TelegramBotConfiguration.OwnerId == userId.Value;

            return new UserBalanceInfo
            {
                Balance = user.Balance,
                IsUnlimited = isUnlimited
            };
        }
    }
}

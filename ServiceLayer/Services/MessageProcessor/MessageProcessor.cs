using DataBaseLayer.Enums;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI.Images;
using ServiceLayer.Constans;
using ServiceLayer.Services.OpenAI;
using ServiceLayer.Utils;
using ServiceLayer.Services.Localization;
using ServiceLayer.Services.Telegram;
using ServiceLayer.Services.Telegram.Configuretions;
using System.Globalization;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Threading;
using ServiceLayer.Models;
using ServiceLayer.Services.Memory;
using ServiceLayer.Services.Mcp;
using Microsoft.Extensions.Caching.Memory;

namespace ServiceLayer.Services.MessageProcessor;
public class MessageProcessor : BaseService
{
    private readonly IRepository<HistoryMessage> _historyMessageRepository;
    private readonly IRepository<TelegramUserInfo> _telegramUserInfoRepository;
    private readonly IChatService _chatService;
    private readonly IChatServiceFactory _chatServiceFactory;
    private readonly ITelegramBotClient _telegramBotClient;
    private readonly IDynamicLocalizer _localizer;
    private readonly IReactionService _reactionService;
    private readonly ISummaryService _summaryService;
    private readonly ISemanticMemoryService _semanticMemoryService;
    private readonly IBotSelfAwarenessService _botSelfAwarenessService;
    private readonly McpServerManager _mcpServerManager;
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> _chatLocks = new();
    private readonly string[] drawWords = new[]
    {
        "draw", "рисуй", "покажи", "малю"
    };
    private readonly TelegramBotConfiguration _telegramBotConfiguration;
    private int _newSummariesInThisRequest = 0;
    private const int MaxNewSummariesPerRequest = 2;
    private const int LargeMessageThreshold = 30000;

    public MessageProcessor(IServiceProvider serviceProvider, ILogger<MessageProcessor> logger,
        IRepository<HistoryMessage> historyMessageRepository, IChatService chatService,
        IChatServiceFactory chatServiceFactory,
        ITelegramBotClient telegramBotClient, IRepository<TelegramUserInfo> telegramUserInfoRepository,
        IDynamicLocalizer localizer, IReactionService reactionService,
        ISummaryService summaryService, ISemanticMemoryService semanticMemoryService,
        IBotSelfAwarenessService botSelfAwarenessService, McpServerManager mcpServerManager)
        : base(serviceProvider, logger)
    {
        _historyMessageRepository = historyMessageRepository;
        _chatService = chatService;
        _chatServiceFactory = chatServiceFactory;
        _telegramBotClient = telegramBotClient;
        _telegramUserInfoRepository = telegramUserInfoRepository;
        _localizer = localizer;
        _reactionService = reactionService;
        _summaryService = summaryService;
        _semanticMemoryService = semanticMemoryService;
        _botSelfAwarenessService = botSelfAwarenessService;
        _mcpServerManager = mcpServerManager;
        AppSettings? appConfig = _serviceProvider.GetConfiguration<AppSettings>();
        _telegramBotConfiguration = appConfig.TelegramBotConfiguration;
    }

    private async Task<(string message, bool isRelevant)> PreProcessMessageAsync(long chatId, long fromUserId, string message)
    {
        if (message.StartsWith(MessageMarkers.VoiceMessage))
        {
            var isPrivateMessage = message.StartsWith(MessageMarkers.VoiceMessageToBot);
            var cleanedMessage = message.Replace(MessageMarkers.VoiceMessageToBot, string.Empty)
                .Replace(MessageMarkers.VoiceMessage, string.Empty);
            
            if (!(isPrivateMessage || await IsVoiceMessageToBot(chatId, fromUserId, cleanedMessage)))
            {
                _logger.LogInformation("This voice message not for me.");
                return (cleanedMessage, false);
            }
            return (cleanedMessage, true);
        }
        return (message, true);
    }

    public async Task<string> ProcessMessage(long chatId, long messageId, long? parentMessageId,
        long fromUserId, string message, CancellationToken cancellationToken, bool isEdit = false, 
        bool wantsVoiceResponse = false, string? transcription = null)
    {
        var (cleanedMessage, isRelevant) = await PreProcessMessageAsync(chatId, fromUserId, message);
        if (!isRelevant) return string.Empty;
        message = cleanedMessage;
        // 1. Ensure the current user message is saved to history
        var userHistory = await SaveHistory(chatId, messageId, parentMessageId, (int)global::OpenAI.Role.User, message, null, null, fromUserId.ToString());
        
        // Identify preferred language for prompt attachment
        var user = await _telegramUserInfoRepository.Get(p => p.Id == fromUserId);
        var langCode = user?.LanguageCode ?? LanguageCode.English;
        var culture = new CultureInfo(langCode);
        var languageName = culture.EnglishName.Split(' ')[0]; // Use English name for- [x] Create/Fix `SaveToHistory` helper methods
        // - [x] Extract and fix `GetConversationContext` logic
        // - [x] Extract image processing logic
        // - [x] Extract Telegram response sending logic
        // - [x] Rebuild `ProcessMessage` orchestration
        // - [x] Verify fix for conversation chain
        // 

        // 1. Fetch user selected model & provider
        var targetModel = user?.SelectedModel;
        var preferredProvider = user?.PreferredProvider ?? ChatStrategy.Auto;

        if (string.IsNullOrEmpty(targetModel))
        {
            var defaultProvider = _chatServiceFactory.GetAvailableProviders().FirstOrDefault();
            targetModel = defaultProvider?.ModelName ?? (string)AiModel.Gpt4oMini;
        }

        // Get MCP tools and filter based on user's DisabledTools preference
        var mcpTools = await _mcpServerManager.GetAllToolsAsync();
        var disabledList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (user != null && !string.IsNullOrEmpty(user.DisabledTools))
        {
            var split = user.DisabledTools.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var d in split)
            {
                disabledList.Add(d.Trim());
            }
        }
        var activeToolsCount = mcpTools.Count(t => !disabledList.Contains(t.Name));

        // Check if provider/model is OpenAI-compatible and enforces 128 tools limit
        bool isLimited = (preferredProvider == ChatStrategy.OpenAI 
                          || preferredProvider == ChatStrategy.Grok 
                          || preferredProvider == ChatStrategy.DeepSeek
                          || targetModel.Contains("grok", StringComparison.OrdinalIgnoreCase) 
                          || targetModel.Contains("gpt-", StringComparison.OrdinalIgnoreCase)
                          || targetModel.Contains("o1-", StringComparison.OrdinalIgnoreCase)
                          || targetModel.Contains("deepseek", StringComparison.OrdinalIgnoreCase));

        // Check if model explicitly does NOT support function calling
        bool supportsTools = true;
        if (OpenAIService._liveModelsInfo.TryGetValue(targetModel, out var modelInfo))
        {
            if (modelInfo.SupportsFunctionCalling == false)
            {
                supportsTools = false;
            }
        }

        if (supportsTools && isLimited && activeToolsCount > 128)
        {
            _logger.LogWarning("User {UserId} exceeded active tools limit for model {ModelName} ({Count} tools active, max 128). Blocking request and showing tools settings.", fromUserId, targetModel, activeToolsCount);
            
            // Format warning text based on preferred language
            string warningMessage = langCode.ToLowerInvariant() switch
            {
                "ua" or "uk" => $"⚠️ <b>Перевищено ліміт інструментів для вибраної моделі!</b>\n\nМодель <code>{targetModel}</code> підтримує не більше 128 інструментів одночасно, а у вас зараз увімкнено <b>{activeToolsCount}</b>.\n\nБудь ласка, вимкніть щонайменше <b>{activeToolsCount - 128}</b> неіспользуваних інструментів у списку нижче, щоб продовжить спілкування:",
                _ => $"⚠️ <b>Tools limit exceeded for the selected model!</b>\n\nModel <code>{targetModel}</code> supports a maximum of 128 tools simultaneously, but you currently have <b>{activeToolsCount}</b> enabled.\n\nPlease disable at least <b>{activeToolsCount - 128}</b> unused tools in the list below to continue:"
            };

            // Build tools keyboard dynamically
            var keyboard = await BuildMcpToolsKeyboardAsync(fromUserId);

            // Send warning and settings keyboard to Telegram
            var sentMsg = await _telegramBotClient.SendMessage(
                chatId: chatId,
                text: warningMessage,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
                
            if (sentMsg != null)
            {
                await SaveHistory(chatId, sentMsg.MessageId, messageId, (int)global::OpenAI.Role.Assistant, warningMessage, "System", targetModel);
            }

            return warningMessage;
        }

        var query = await GetConversationContext(userHistory, languageName, fromUserId);

        string toSendText = string.Empty;
        string responceText = string.Empty;
        string? usedProviderName = null;
        string? usedModelName = null;

        var imageProcessingResult = await HandleImageContextAsync(chatId, messageId, fromUserId, message, parentMessageId, cancellationToken);
        if (imageProcessingResult != null)
        {
            return imageProcessingResult;
        }
        
        // Retrieve user balance and name before billing
        decimal? balanceBefore = user?.Balance;
        var userFullName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : "Unknown User";

        (responceText, toSendText, usedProviderName, usedModelName, var aiResponse) = await ExecuteChatRequestAsync(chatId, fromUserId, query);

        // Retrieve user balance after billing (billing happens inside ExecuteChatRequestAsync)
        decimal? balanceAfter = null;
        if (user != null)
        {
            var userAfter = await _telegramUserInfoRepository.Get(p => p.Id == fromUserId);
            balanceAfter = userAfter?.Balance;
        }

        TgMessage telegramResponce = await SendResponseToTelegram(
            chatId, 
            messageId, 
            toSendText, 
            responceText, 
            usedProviderName, 
            usedModelName, 
            isEdit, 
            fromUserId, 
            aiResponse,
            balanceBefore,
            balanceAfter,
            userFullName,
            query,
            cancellationToken);

        if (telegramResponce != null)
        {
            var appConfig = _serviceProvider.GetConfiguration<AppSettings>();
            if (appConfig?.MemorySettings?.Enabled == true)
            {
                // Фоновое асинхронное сохранение эмбеддингов
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _semanticMemoryService.SaveMessageAsync(chatId, fromUserId, "User", message, messageId);
                        await _semanticMemoryService.SaveMessageAsync(chatId, fromUserId, "Assistant", responceText, telegramResponce.MessageId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to asynchronously save chat history message vectors to Qdrant");
                    }
                }, cancellationToken);
            }
        }

        if (telegramResponce != null && !string.IsNullOrEmpty(transcription))
        {
            var youSaidText = _localizer["YouSaid", transcription];
            var spoilerTranscription = youSaidText.WrapWithSpoiler(_telegramBotConfiguration.DefaultParseMode);
            responceText = $"{spoilerTranscription}\n\n{responceText}";
            
            try
            {
                await _telegramBotClient.EditMessageText(chatId, telegramResponce.MessageId, responceText, 
                    parseMode: _telegramBotConfiguration.DefaultParseMode, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to edit message to include transcription spoiler");
            }
        }

        if (telegramResponce != null && wantsVoiceResponse && !string.IsNullOrEmpty(responceText))
        {
            await SendVoiceResponse(chatId, fromUserId, messageId, responceText, cancellationToken);
        }
        return responceText;
    }

    private async Task SendVoiceResponse(long chatId, long fromUserId, long messageId, string responceText, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Attempting voice response for chat {0}", chatId);
            await _telegramBotClient.SendChatAction(chatId, ChatAction.RecordVoice, cancellationToken: cancellationToken);
            
            var dbUser = await _telegramUserInfoRepository.Get(p => p.Id == fromUserId);
            var voiceId = dbUser?.VoiceId ?? "alloy";
            
            using (var voiceStream = await _chatService.TextToSpeech(chatId, fromUserId, responceText, voice: voiceId))
            {
                if (voiceStream != null)
                {
                    _logger.LogInformation("Voice response stream generated with voice {0}, sending to Telegram", voiceId);
                    await _telegramBotClient.SendVoice(
                        chatId: chatId,
                        voice: new InputFileStream(voiceStream, "response.ogg"),
                        replyParameters: new ReplyParameters() { MessageId = (int)messageId },
                        cancellationToken: cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send voice response");
        }
    }

    private async Task<(string responseText, string toSendText, string? usedProviderName, string? usedModelName, ChatServiceResponse aiResponse)> ExecuteChatRequestAsync(long chatId, long fromUserId, List<AiMessage> query)
    {
        var aiResponse = await _chatService.SendMessages2ChatAsync(chatId, fromUserId, query);
        var responseText = string.Join("\n", aiResponse.Choices);
        _logger.LogInformation("Message from AI: {0}", responseText);
        
        var usedToolsStr = aiResponse.UsedTools?.Any() == true ? $" | Tools: {string.Join(", ", aiResponse.UsedTools)}" : "";
        var truncatedStr = aiResponse.TruncatedToolsCount > 0 ? $" | ⚠️ Truncated {aiResponse.TruncatedToolsCount} tools" : "";
        var balanceDisplay = await GetBalanceDisplay(fromUserId);
        
        var summary = $"{aiResponse.ProviderName} | {aiResponse.ModelName} | " +
                      $"In:{aiResponse.PromptTokens} Out:{aiResponse.CompletionTokens}" +
                      (aiResponse.ReasoningTokens > 0 ? $" (R:{aiResponse.ReasoningTokens})" : "") +
                      $" | T:{aiResponse.TotalTokens} | {aiResponse.TokensPerSecond:F1}t/s | {aiResponse.LatencySeconds:F1}s | ${aiResponse.Cost:F4}{usedToolsStr}{truncatedStr}{balanceDisplay}";

        var formattedResponse = _telegramBotConfiguration.DefaultParseMode == ParseMode.Html 
            ? responseText.ConvertMarkdownToHtml() 
            : responseText;
            
        var toSendText = formattedResponse;
        if (summary != null)
        {
            toSendText += $"\n\n{summary.WrapWithSpoiler(_telegramBotConfiguration.DefaultParseMode)}";
        }
        
        return (responseText, toSendText, aiResponse.ProviderName, aiResponse.ModelName, aiResponse);
    }

    private async Task<TgMessage?> SendResponseToTelegram(
        long chatId, 
        long messageId, 
        string toSendText, 
        string responceText, 
        string? usedProviderName, 
        string? usedModelName, 
        bool isEdit, 
        long fromUserId, 
        ChatServiceResponse aiResponse, 
        decimal? balanceBefore, 
        decimal? balanceAfter, 
        string userFullName, 
        List<AiMessage> query, 
        CancellationToken cancellationToken)
    {
        var chunks = SplitHtmlText(toSendText, 4000).ToList();
        TgMessage? telegramResponce = null;
        
        var reactionMarkup = await GetReactionMarkup(chatId, 0, fromUserId); // Initially counts are 0

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            bool isLastChunk = i == chunks.Count - 1;
            InlineKeyboardMarkup? markup = isLastChunk ? reactionMarkup : null;

            try
            {
                if (isEdit)
                {
                    // Find the previous response to this message
                    var previousResponse = await _historyMessageRepository.Get(p => p.ChatId == chatId && p.ParentMessageId == messageId && p.RoleId == (int)global::OpenAI.Role.Assistant);
                    if (previousResponse != null)
                    {
                        telegramResponce = await _telegramBotClient.EditMessageText(
                            chatId: chatId,
                            messageId: (int)previousResponse.MessageId,
                            text: chunk,
                            parseMode: _telegramBotConfiguration.DefaultParseMode,
                            replyMarkup: markup,
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        // Fallback to send if not found or if we have multiple chunks and only one was in history
                        telegramResponce = await _telegramBotClient.SendMessage(
                            chatId: chatId,
                            text: chunk,
                            parseMode: _telegramBotConfiguration.DefaultParseMode,
                            replyParameters: new ReplyParameters() { MessageId = (int)messageId },
                            replyMarkup: markup,
                            cancellationToken: cancellationToken);
                    }
                }
                else
                {
                    telegramResponce = await _telegramBotClient.SendMessage(
                            chatId: chatId,
                            text: chunk,
                            parseMode: _telegramBotConfiguration.DefaultParseMode,
                            replyParameters: new ReplyParameters() { MessageId = (int)messageId },
                            replyMarkup: markup,
                            cancellationToken: cancellationToken);
                }
            }
            catch (global::Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.ErrorCode == 400 && (ex.Message.Contains("can't parse entities") || ex.Message.Contains("bad request")))
            {
                _logger.LogWarning(ex, "Failed to send chunk with formatting, falling back to None. Message: {Message}", ex.Message);
                
                telegramResponce = await _telegramBotClient.SendMessage(
                        chatId: chatId,
                        text: chunk,
                        parseMode: global::Telegram.Bot.Types.Enums.ParseMode.None,
                        replyParameters: new ReplyParameters() { MessageId = (int)messageId },
                        replyMarkup: markup,
                        cancellationToken: cancellationToken);
            }
        }

        if (telegramResponce != null)
        {
            await SaveHistory(telegramResponce, responceText, usedProviderName, usedModelName, (int)global::OpenAI.Role.Assistant, messageId);
            
            // Re-fetch counts if we actually saved the message (though for a new message they are still 0)
            // But we need to update the markup with the correct MessageId in callbacks
            if (telegramResponce == null) return null;

            bool hasThoughts = !string.IsNullOrEmpty(aiResponse.Thoughts);
            bool hasUsedTools = aiResponse.UsedTools != null && aiResponse.UsedTools.Any();

            // Capture thoughts and statistics in cache if they exist or tools were used
            if (hasThoughts || hasUsedTools)
            {
                var systemMessage = query.FirstOrDefault(m => m.Role == global::OpenAI.Role.System || m.Role == global::OpenAI.Role.Developer);
                var activeSystemPrompt = systemMessage?.Content as string;

                var thoughtsAndStats = new ThoughtsAndStats
                {
                    Thoughts = aiResponse.Thoughts ?? string.Empty,
                    ModelName = aiResponse.ModelName,
                    ProviderName = aiResponse.ProviderName,
                    PromptTokens = aiResponse.PromptTokens,
                    CompletionTokens = aiResponse.CompletionTokens,
                    TotalTokens = aiResponse.TotalTokens,
                    ReasoningTokens = aiResponse.ReasoningTokens,
                    Cost = aiResponse.Cost,
                    LatencySeconds = aiResponse.LatencySeconds,
                    TokensPerSecond = aiResponse.TokensPerSecond,
                    UsedTools = aiResponse.UsedTools ?? new List<string>(),
                    ToolCalls = aiResponse.ToolCalls ?? new List<ToolCallInfo>(),
                    SystemPrompt = activeSystemPrompt,
                    UserBalanceBefore = balanceBefore,
                    UserBalanceAfter = balanceAfter,
                    UserFullName = userFullName,
                    UserId = fromUserId
                };

                var cache = _serviceProvider.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                if (cache != null)
                {
                    var secureToken = Guid.NewGuid().ToString("N");
                    var cacheKey = $"AiThoughts:{telegramResponce.MessageId}";
                    var tokenKey = $"AiThoughtsToken:{telegramResponce.MessageId}";
                    
                    cache.Set(cacheKey, thoughtsAndStats, TimeSpan.FromDays(2));
                    cache.Set(tokenKey, secureToken, TimeSpan.FromDays(2));
                    _logger.LogInformation("Cached AI thoughts & stats for MessageId {MessageId} with secure token {Token}", telegramResponce.MessageId, secureToken);
                }
            }

            var finalMarkup = await GetReactionMarkup(chatId, telegramResponce.MessageId, fromUserId);
            await _telegramBotClient.EditMessageReplyMarkup(
                chatId: chatId, 
                messageId: telegramResponce.MessageId, 
                replyMarkup: finalMarkup, 
                cancellationToken: cancellationToken);
        }
        return telegramResponce;
    }

    public async Task<InlineKeyboardMarkup> GetReactionMarkup(long chatId, long messageId, long fromUserId = 0, Dictionary<MessageReactionType, int>? counts = null)
    {
        if (counts == null)
        {
            counts = await _reactionService.GetReactionCounts(chatId, messageId);
        }

        string GetLabel(MessageReactionType type, string emoji)
        {
            return counts.TryGetValue(type, out var count) && count > 0 ? $"{emoji} {count}" : emoji;
        }

        var buttonsRow = new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData(GetLabel(MessageReactionType.Like, MessageMarkers.Reactions.Like), $"Reaction:Like:{messageId}"),
            InlineKeyboardButton.WithCallbackData(GetLabel(MessageReactionType.Dislike, MessageMarkers.Reactions.Dislike), $"Reaction:Dislike:{messageId}"),
            InlineKeyboardButton.WithCallbackData(GetLabel(MessageReactionType.Laugh, MessageMarkers.Reactions.Laugh), $"Reaction:Laugh:{messageId}"),
            InlineKeyboardButton.WithCallbackData(GetLabel(MessageReactionType.Sad, MessageMarkers.Reactions.Sad), $"Reaction:Sad:{messageId}"),
            InlineKeyboardButton.WithCallbackData(GetLabel(MessageReactionType.Think, MessageMarkers.Reactions.Think), $"Reaction:Think:{messageId}"),
            InlineKeyboardButton.WithCallbackData(GetLabel(MessageReactionType.Vomit, MessageMarkers.Reactions.Vomit), $"Reaction:Vomit:{messageId}")
        };

        var rows = new List<InlineKeyboardButton[]>();
        rows.Add(buttonsRow.ToArray());

        // Check if user is the bot owner
        bool isOwner = _telegramBotConfiguration.OwnerId.HasValue && fromUserId == _telegramBotConfiguration.OwnerId.Value;
        if (isOwner && messageId > 0)
        {
            var cache = _serviceProvider.GetService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            if (cache != null)
            {
                var tokenKey = $"AiThoughtsToken:{messageId}";
                if (cache.TryGetValue(tokenKey, out string? secureToken) && !string.IsNullOrEmpty(secureToken))
                {
                    // Build base URL (prefer DashboardBaseUrl, fallback to BaseApiUrl, fallback to localhost)
                    var baseUrl = _telegramBotConfiguration.DashboardBaseUrl;
                    if (string.IsNullOrWhiteSpace(baseUrl))
                    {
                        baseUrl = _telegramBotConfiguration.BaseApiUrl;
                    }
                    if (string.IsNullOrWhiteSpace(baseUrl))
                    {
                        var port = Environment.GetEnvironmentVariable("BOT_PORT") ?? "8080";
                        baseUrl = $"http://localhost:{port}";
                    }
                    baseUrl = baseUrl.TrimEnd('/');

                    // Telegram Bot API rejects 'localhost' or local/private IP URLs in inline button URLs.
                    // We dynamically rewrite 'localhost' to '127.0.0.1.nip.io', and any raw IP address (e.g. Tailscale '100.82.239.59') to '{ip}.nip.io'.
                    if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
                    {
                        var host = uri.Host;
                        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                        {
                            var builder = new UriBuilder(uri) { Host = "127.0.0.1.nip.io" };
                            baseUrl = builder.Uri.ToString().TrimEnd('/');
                        }
                        else if (System.Net.IPAddress.TryParse(host, out var ip))
                        {
                            var builder = new UriBuilder(uri) { Host = $"{ip}.nip.io" };
                            baseUrl = builder.Uri.ToString().TrimEnd('/');
                        }
                    }
                    
                    var thoughtsUrl = $"{baseUrl}/admin/thoughts/{messageId}?token={secureToken}";
                    var buttonText = _localizer["ThoughtsAndStatsButton"];
                     rows.Add(new[]
                     {
                         InlineKeyboardButton.WithUrl(buttonText, thoughtsUrl)
                     });
                }
            }
        }

        return new InlineKeyboardMarkup(rows);
    }

    private async Task<HistoryMessage?> SaveHistory(TgMessage? telegramMessage, string text, string? providerName, string? modelName, int roleId, long? originalMessageId = null)
    {
        if (telegramMessage == null || telegramMessage.Chat == null) return null;

        return await SaveHistory(telegramMessage.Chat.Id, telegramMessage.MessageId, 
            (long?)(telegramMessage.ReplyToMessage?.MessageId ?? originalMessageId), 
            roleId, text, providerName, modelName);
    }

    private async Task<HistoryMessage> SaveHistory(long chatId, long messageId, long? parentMessageId, int roleId, string text, string? providerName, string? modelName, string? fromUserName = null)
    {
        var historyMessage = await _historyMessageRepository.Get(p => p.ChatId == chatId && p.MessageId == messageId);
        if (historyMessage == null)
        {
            historyMessage = new HistoryMessage
            {
                ChatId = chatId,
                MessageId = messageId,
                ParentMessageId = parentMessageId,
                RoleId = roleId,
                Text = text,
                ProviderName = providerName,
                ModelName = modelName,
                FromUserName = fromUserName,
                CreationDate = DateTime.UtcNow,
                ModifiedDate = DateTime.UtcNow
            };
            _historyMessageRepository.Add(historyMessage);
        }
        else
        {
            historyMessage.Text = text;
            historyMessage.RoleId = roleId;
            historyMessage.ParentMessageId = parentMessageId;
            historyMessage.ProviderName = providerName;
            historyMessage.ModelName = modelName;
            historyMessage.ModifiedDate = DateTime.UtcNow;
            _historyMessageRepository.Update(historyMessage);
        }
        
        await _historyMessageRepository.SaveChanges();
        return historyMessage;
    }

    private async Task<string?> HandleImageContextAsync(long chatId, long messageId, long fromUserId, string message, long? parentMessageId, CancellationToken cancellationToken)
    {
        if (await IsNeedDrawImage(chatId, fromUserId, message))
        {
            await ProcessDrawCommand(chatId, messageId, fromUserId, message, cancellationToken);
            return "Image generated"; 
        }

        if (parentMessageId.HasValue)
        {
            string? originalPrompt = null;
            string? imageUrl = null;
            string? botAnalysisText = null;
            string? telegramFileId = null;
            
            var promptRegex = new Regex(@"\[Image Prompt: (.*?) \| URL: (.*?)\]", RegexOptions.Singleline);
            var fileIdRegex = new Regex(@"\[Telegram FileId: (.*?)\]", RegexOptions.Singleline);
            var parentMsg = await _historyMessageRepository.Get(p => p.ChatId == chatId && p.MessageId == parentMessageId.Value);
            
            var currentChainMsg = parentMsg;
            int depth = 0;
            while (currentChainMsg != null && depth < 5)
            {
                var match = promptRegex.Match(currentChainMsg.Text);
                var fileIdMatch = fileIdRegex.Match(currentChainMsg.Text);

                if (fileIdMatch.Success) telegramFileId = fileIdMatch.Groups[1].Value;

                if (match.Success)
                {
                    originalPrompt = match.Groups[1].Value;
                    imageUrl = match.Groups[2].Value;
                    if (string.IsNullOrEmpty(originalPrompt))
                        botAnalysisText = currentChainMsg.Text.Replace(match.Value, "").Replace(fileIdMatch.Value, "").Trim();
                    break;
                }
                else if (fileIdMatch.Success) break;
                else if (currentChainMsg.Text.Contains("http"))
                {
                    var matches = new Regex(@"http[^\s]+").Matches(currentChainMsg.Text);
                    foreach (Match m in matches)
                    {
                        if (m.Success && IsImageUrl(m.Value, out var cleanedUrl))
                        {
                            imageUrl = cleanedUrl;
                            break;
                        }
                    }
                    if (imageUrl != null) break;
                }
                
                if (currentChainMsg.ParentMessageId.HasValue)
                {
                    currentChainMsg = await _historyMessageRepository.Get(p => p.ChatId == chatId && p.MessageId == currentChainMsg.ParentMessageId.Value);
                    depth++;
                }
                else break;
            }

            if (!string.IsNullOrEmpty(imageUrl) || !string.IsNullOrEmpty(telegramFileId))
            {
                string? workingFilePath = null;
                bool isTempFile = false;
                try 
                {
                    if (!string.IsNullOrEmpty(telegramFileId))
                    {
                        var file = await _telegramBotClient.GetFile(telegramFileId);
                        workingFilePath = Path.GetTempFileName() + ".png";
                        using (var stream = new FileStream(workingFilePath, FileMode.Create))
                            await _telegramBotClient.DownloadFile(file.FilePath, stream);
                        isTempFile = true;
                    }

                    bool isAnalysis = true; 
                    try 
                    {
                        var classificationProvider = _telegramBotConfiguration.AiSettings.Classification?.ProviderName ?? "OpenAI";
                        var classificationService = _chatServiceFactory.CreateService(classificationProvider);
                        var classificationPrompt = $"Task: Classify intent for: '{message}'. ANALYZE or EDIT. Russian 'Сделай' etc are EDIT. Return ONLY 'ANALYZE' or 'EDIT'.";
                        var classificationResponse = await classificationService.Ask(chatId, fromUserId, classificationPrompt);
                        isAnalysis = !classificationResponse.Trim().Contains("EDIT", StringComparison.OrdinalIgnoreCase);
                    } catch { isAnalysis = true; }

                    if (isAnalysis)
                    {
                        var visionProviderName = _telegramBotConfiguration.AiSettings.Vision?.ProviderName ?? "OpenAI";
                        var visionService = _chatServiceFactory.CreateService(visionProviderName);
                        var visionPrompt = $"{message}\n\nIMPORTANT: Describe directly. No instructions/tutorials.";
                        var result = await visionService.AnalyzeImageAsync(chatId, fromUserId, imageUrl, workingFilePath, visionPrompt);
                        var finalResText = string.Join(" ", result.Choices.Where(p => !string.IsNullOrWhiteSpace(p)));
                        
                        if (originalPrompt != null || imageUrl != null || telegramFileId != null)
                        {
                            string historyPrefix = "";
                            if (telegramFileId != null) historyPrefix += $"[Telegram FileId: {telegramFileId}]\n";
                            if (imageUrl != null) historyPrefix += $"[Image Prompt: {originalPrompt} | URL: {imageUrl}]\n";
                            finalResText = historyPrefix + finalResText;
                        }
                        return finalResText;
                    }
                    else
                    {
                        var visionProviderName = _telegramBotConfiguration.AiSettings.Vision?.ProviderName ?? "OpenAI";
                        var visionService = _chatServiceFactory.CreateService(visionProviderName);
                        string basePrompt = originalPrompt;
                        if (string.IsNullOrEmpty(basePrompt) && !string.IsNullOrEmpty(imageUrl))
                        {
                            basePrompt = botAnalysisText ?? string.Join(" ", (await visionService.AnalyzeImageAsync(chatId, fromUserId, imageUrl, null, "Describe image for DALL-E.")).Choices);
                        }
                        var refinementPrompt = $"Task: New DALL-E prompt. Base: '{basePrompt}'. Change: '{message}'. Return ONLY English prompt.";
                        var newPrompt = await visionService.Ask(chatId, fromUserId, refinementPrompt);
                        await ProcessDrawCommand(chatId, messageId, fromUserId, newPrompt, cancellationToken);
                        return "Refining image with DALL-E 3 based on context description";
                    }
                }
                finally { if (isTempFile && File.Exists(workingFilePath)) try { File.Delete(workingFilePath); } catch { } }
            }
        }
        return null;
    }

    internal async Task<List<AiMessage>> GetConversationContext(HistoryMessage currentMessage, string languageName, long? userId = null)
    {
        _newSummariesInThisRequest = 0; // Reset for each request
        var query = new List<AiMessage>();
        var cleanRegex = new Regex(@"^\[(?:Image Prompt|Telegram FileId):.*?\]\n?", RegexOptions.Singleline);
        
        var chain = new List<HistoryMessage>();
        var historyItem = currentMessage;
        int depth = 0;
        // Fetch all (with sane limit to avoid infinite loops)
        while (historyItem != null && depth < 500)
        {
            chain.Add(historyItem);
            if (historyItem.ParentMessageId.HasValue)
            {
                historyItem = await _historyMessageRepository.Get(p => p.ChatId == historyItem.ChatId
                    && p.MessageId == historyItem.ParentMessageId.Value);
                depth++;
            }
            else break;
        }

        // Batch fetch reactions for recent assistant messages
        var assistantMsgIds = chain.Where(m => m.RoleId == (int)global::OpenAI.Role.Assistant).Select(m => m.MessageId).ToList();
        var allReactions = await _reactionService.GetReactionCountsForMessages(currentMessage.ChatId, assistantMsgIds);

        // Limit to 20 recent messages + optional summary
        var recentChain = chain.Take(20).ToList();
        var oldChain = chain.Skip(20).ToList();

        // Only summarize if total length > 50
        if (chain.Count > 50 && oldChain.Any())
        {
            var summary = await GetOrUpdateSummary(currentMessage.ChatId, oldChain);
            if (summary != null && !string.IsNullOrEmpty(summary.Content))
            {
                query.Insert(0, new AiMessage(global::OpenAI.Role.System, $"Summary of previous conversation: {summary.Content}", "System"));
            }
        }
        else
        {
            // If <= 50, just include everything as is
            recentChain = chain; 
        }

        foreach (var item in recentChain)
        {
            string text = item.Text;
            
            if (item.RoleId == (int)global::OpenAI.Role.Assistant && allReactions.TryGetValue(item.MessageId, out var reactions))
            {
                var activeReactions = reactions.Where(p => p.Value > 0).ToList();
                if (activeReactions.Any())
                {
                    var feedback = string.Join(", ", activeReactions.Select(p => 
                    {
                        var emoji = p.Key switch
                        {
                            MessageReactionType.Like => MessageMarkers.Reactions.Like,
                            MessageReactionType.Dislike => MessageMarkers.Reactions.Dislike,
                            MessageReactionType.Laugh => MessageMarkers.Reactions.Laugh,
                            MessageReactionType.Sad => MessageMarkers.Reactions.Sad,
                            MessageReactionType.Think => MessageMarkers.Reactions.Think,
                            MessageReactionType.Vomit => MessageMarkers.Reactions.Vomit,
                            _ => ""
                        };
                        return emoji;
                    }).Where(e => !string.IsNullOrEmpty(e)));
                    
                    text = $"[Feedback: {feedback}] {text}";
                }
            }

            if (item == currentMessage)
            {
                text = $"{text} (Respond in {languageName})";
            }
            
            // PREVENTIVE PROTECTION: Smart Minification
            text = await GetSmartMessageContent(item, text);
            
            string cleanedText = cleanRegex.Replace(text, "");
            string senderName = item.RoleId == (int)global::OpenAI.Role.Assistant 
                ? "Assistant" 
                : (!string.IsNullOrEmpty(item.FromUserName) ? $"U{item.FromUserName}" : "User");

            query.Insert(0, new AiMessage((global::OpenAI.Role)item.RoleId, cleanedText, senderName));
        }

        // --- 🧠 RAG (Long-Term Semantic Memory) ---
        var appConfig = _serviceProvider.GetConfiguration<AppSettings>();
        if (appConfig?.MemorySettings?.Enabled == true)
        {
            try
            {
                long resolvedUserId = userId ?? (long.TryParse(currentMessage.FromUserName, out var parsedId) ? parsedId : currentMessage.ChatId);
                _logger.LogInformation("Attempting semantic memory lookup for ChatId={ChatId}, UserId={UserId}", currentMessage.ChatId, resolvedUserId);
                
                var memoryFacts = await _semanticMemoryService.SearchMemoryAsync(currentMessage.ChatId, resolvedUserId, currentMessage.Text);
                if (memoryFacts != null && memoryFacts.Any())
                {
                    var memoryContext = "You have the following facts from past conversation history with this user. Use them to maintain context and remember user preferences:\n" +
                        string.Join("\n", memoryFacts.Select(f => $"- [{f.Timestamp:yyyy-MM-dd} {f.Role}]: {f.Text}"));
                    
                    query.Insert(0, new AiMessage(global::OpenAI.Role.System, memoryContext, "System"));
                    _logger.LogInformation("Injected {Count} semantic memory facts into LLM prompt context", memoryFacts.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to inject semantic memory facts into context");
            }
        }
        // ------------------------------------------

        // --- 🤖 Bot Self-Awareness & Newsletter Scheduler Proactive Prompt ---
        try
        {
            var systemPrompt = await _botSelfAwarenessService.GetBotSystemSummaryPromptAsync(userId);
            query.Insert(0, new AiMessage(global::OpenAI.Role.System, systemPrompt, "System"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inject bot self-awareness system prompt");
        }
        // ---------------------------------------------------------------------



        return query;
    }

    private async Task<string> GetSmartMessageContent(HistoryMessage item, string originalText)
    {
        if (originalText.Length <= LargeMessageThreshold)
            return originalText;

        // For non-tool messages, just truncate
        if (item.RoleId != (int)global::OpenAI.Role.Tool)
        {
            return TruncateMessage(originalText, "Message too long, truncated");
        }

        // For tool messages, try smart summarization
        var cachedSummary = await _summaryService.GetToolSummary(item.ChatId, item.MessageId);
        if (cachedSummary != null)
            return cachedSummary;

        // Limit new summaries to avoid latency spikes
        if (_newSummariesInThisRequest < MaxNewSummariesPerRequest)
        {
            try
            {
                _newSummariesInThisRequest++;
                var summary = await SummarizeToolOutput(item.ChatId, originalText);
                if (!string.IsNullOrEmpty(summary))
                {
                    await _summaryService.SetToolSummary(item.ChatId, item.MessageId, summary);
                    return summary;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to summarize tool output for message {MessageId}", item.MessageId);
            }
        }

        // Fallback to truncation
        return TruncateToolOutput(originalText);
    }

    private string TruncateMessage(string text, string reason)
    {
        return text.Substring(0, LargeMessageThreshold - 500) + $"\n... [{reason}] ...";
    }

    private string TruncateToolOutput(string text)
    {
        // Keep 15k from start and 5k from end
        int startKeep = 15000;
        int endKeep = 5000;
        if (text.Length <= startKeep + endKeep) return text;
        
        return text.Substring(0, startKeep) + 
               "\n... [Tool output truncated: too large for context] ...\n" + 
               text.Substring(text.Length - endKeep);
    }

    private async Task<string> SummarizeToolOutput(long chatId, string content)
    {
        var summarizerModel = _telegramBotConfiguration.AiSettings?.Summarizer?.ModelName ?? AiModel.Gpt4oMini;
        var extensionEnv = _telegramBotConfiguration.AiSettings?.Summarizer?.ExtensionEnv 
            ?? "This is a technical tool output. Summarize it very concisely, preserving all key facts, prices, and parameters. Remove noise.";
        
        var summaryService = _chatServiceFactory.CreateService(_telegramBotConfiguration.AiSettings?.Summarizer?.ProviderName ?? "OpenAI", summarizerModel);
        
        var prompt = $"{extensionEnv}\n\nCONTENT TO SUMMARIZE:\n{content}";
        return await summaryService.Ask(chatId, 0, prompt);
    }

    private async Task<ChatSummary?> GetOrUpdateSummary(long chatId, List<HistoryMessage> oldChain)
    {
        var semaphore = _chatLocks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            var existingSummary = await _summaryService.GetSummary(chatId);
            var lastMsgIdInChain = oldChain.First().MessageId; // Chain is reversed, so first is most recent of old ones

            if (existingSummary != null && existingSummary.LastSummarizedMessageId == lastMsgIdInChain)
            {
                return existingSummary;
            }

            // Need to summarize or update
            var summarizerConfig = _telegramBotConfiguration.AiSettings?.Summarizer;
            if (summarizerConfig == null || string.IsNullOrEmpty(summarizerConfig.ModelName))
            {
                return null;
            }

            var summarizer = _chatServiceFactory.CreateService(summarizerConfig.ProviderName ?? "OpenAI", summarizerConfig.ModelName!);
            
            // Collect messages to summarize that aren't already in the summary
            var toSummarize = existingSummary == null 
                ? oldChain 
                : oldChain.Where(m => m.MessageId > existingSummary.LastSummarizedMessageId).ToList();

            if (!toSummarize.Any() && existingSummary != null) return existingSummary;

            var sb = new StringBuilder();
            foreach (var m in toSummarize.OrderBy(x => x.MessageId))
            {
                string sender = m.RoleId == (int)global::OpenAI.Role.Assistant ? "Assistant" : (m.FromUserName ?? "User");
                sb.AppendLine($"{sender}: {m.Text}");
            }

            string prompt = summarizerConfig.ExtensionEnv ?? "Summarize the following conversation history part concisely, preserving key facts and user preferences. Include info about who said what.";
            if (existingSummary != null)
            {
                prompt = $"{prompt}\nExisting summary of older messages: {existingSummary.Content}\nNew messages to integrate: {sb.ToString()}";
            }
            else
            {
                prompt = $"{prompt}\nMessages: {sb.ToString()}";
            }

            var response = await summarizer.SendMessages2ChatAsync(chatId, 0, new List<AiMessage> 
            { 
                new AiMessage(global::OpenAI.Role.User, prompt, "System") 
            });

            var newSummary = new ChatSummary
            {
                Content = response.Choices.FirstOrDefault() ?? string.Empty,
                LastSummarizedMessageId = lastMsgIdInChain,
                TotalMessagesSummarized = (existingSummary?.TotalMessagesSummarized ?? 0) + toSummarize.Count
            };

            await _summaryService.SaveSummary(chatId, newSummary);
            return newSummary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate or update summary for chat {ChatId}", chatId);
            return null;
        }
        finally
        {
            semaphore.Release();
        }
    }

    internal IEnumerable<string> SplitHtmlText(string html, int maxLength)
    {
        if (string.IsNullOrEmpty(html)) yield break;

        var openTags = new Stack<string>(); // Stores full opening tags like "<b>" or "<a href=\"...\">"
        var currentChunk = new StringBuilder();

        int i = 0;
        while (i < html.Length)
        {
            // If the current chunk is getting close to maxLength, consider splitting.
            // Let's estimate the overhead of closing all open tags.
            int closingTagsLength = 0;
            foreach (var tag in openTags)
            {
                closingTagsLength += GetClosingTag(tag).Length;
            }

            // If we are exceeding the maxLength (leaving a buffer for safety, e.g. 100 chars)
            if (currentChunk.Length + closingTagsLength >= maxLength - 100)
            {
                // We need to split. Let's find a good split point in the current chunk.
                string chunkText = currentChunk.ToString();
                int splitIndex = FindSafeSplitIndex(chunkText, chunkText.Length);
                if (splitIndex == 0)
                {
                    splitIndex = chunkText.Length;
                }
                
                string firstPart = chunkText.Substring(0, splitIndex);
                string secondPart = chunkText.Substring(splitIndex);

                // Close all open tags in reverse order for the first part
                var closeSb = new StringBuilder();
                var reopenTags = new List<string>();
                foreach (var tag in openTags)
                {
                    closeSb.Append(GetClosingTag(tag));
                    reopenTags.Insert(0, tag); // Reopen in original order
                }

                yield return firstPart + closeSb.ToString();

                // Start the next chunk
                currentChunk.Clear();
                foreach (var tag in reopenTags)
                {
                    currentChunk.Append(tag);
                }
                currentChunk.Append(secondPart);
            }

            if (i >= html.Length) break;

            char c = html[i];
            if (c == '<')
            {
                int closeIndex = html.IndexOf('>', i);
                if (closeIndex > i)
                {
                    string fullTag = html.Substring(i, closeIndex - i + 1);

                    // Parse tag
                    string tagInner = fullTag.Substring(1, fullTag.Length - 2).Trim();
                    if (tagInner.StartsWith("/"))
                    {
                        if (openTags.Count > 0) openTags.Pop();
                    }
                    else if (!tagInner.EndsWith("/")) // Not self-closing
                    {
                        string tagName = tagInner.Split(' ')[0].ToLower();
                        if (IsTelegramTag(tagName))
                        {
                            openTags.Push(fullTag);
                        }
                    }

                    currentChunk.Append(fullTag);
                    i = closeIndex + 1;
                    continue;
                }
            }

            currentChunk.Append(c);
            i++;
        }

        if (currentChunk.Length > 0)
        {
            // Close any remaining open tags for the last chunk
            foreach (var tag in openTags)
            {
                currentChunk.Append(GetClosingTag(tag));
            }
            yield return currentChunk.ToString();
        }
    }

    private static bool IsTelegramTag(string name)
    {
        name = name.ToLower();
        return name == "b" || name == "strong" || name == "i" || name == "em" || 
               name == "u" || name == "ins" || name == "s" || name == "strike" || 
               name == "del" || name == "span" || name == "a" || name == "code" || 
               name == "pre" || name == "blockquote" || name == "tg-spoiler";
    }

    private static string GetClosingTag(string openTag)
    {
        int spaceIndex = openTag.IndexOf(' ');
        string tagName = spaceIndex > 0 
            ? openTag.Substring(1, spaceIndex - 1) 
            : openTag.Substring(1, openTag.Length - 2);
        return $"</{tagName.Trim().Replace("/", "")}>";
    }

    private static int FindSafeSplitIndex(string text, int fallbackIndex)
    {
        int startSearch = Math.Max(0, text.Length - 500);
        int bestIndex = -1;
        for (int j = text.Length - 1; j >= startSearch; j--)
        {
            if (text[j] == '\n' || text[j] == ' ')
            {
                if (!IsIndexInsideHtmlTag(text, j))
                {
                    bestIndex = j + 1;
                    break;
                }
            }
        }
        return bestIndex > 0 ? bestIndex : fallbackIndex;
    }

    private static bool IsIndexInsideHtmlTag(string text, int index)
    {
        int lastOpen = text.LastIndexOf('<', index);
        if (lastOpen >= 0)
        {
            int lastClose = text.LastIndexOf('>', index);
            return lastClose < lastOpen;
        }
        return false;
    }

    public async Task<TgMessage> ProcessDrawCommand(long chatId, long messageId, long fromUserId, string prompt, CancellationToken cancellationToken)
    {
        var response = await InternalProcessDrawImage(chatId, fromUserId, prompt);
        if (response.Choices != null && response.Choices.Any())
        {
            var imageUrl = response.Choices.First();
            if (!string.IsNullOrEmpty(imageUrl))
            {
                var summary = $"{response.ProviderName} | {response.ModelName} | " +
                              $"L:{response.LatencySeconds:F1}s | ${response.Cost:F4}" +
                              await GetBalanceDisplay(fromUserId);
                
                var spoilerSummary = summary.WrapWithSpoiler(_telegramBotConfiguration.DefaultParseMode);

                byte[] imageBytes;
                if (imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    using var httpClient = _serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
                    imageBytes = await httpClient.GetByteArrayAsync(imageUrl);
                }
                else
                {
                    imageBytes = Convert.FromBase64String(imageUrl);
                }

                using var ms = new MemoryStream(imageBytes);

                var telegramResponce = await _telegramBotClient.SendPhoto(
                    chatId: chatId,
                    photo: new InputFileStream(ms, "image.png"),
                    caption: spoilerSummary,
                    parseMode: _telegramBotConfiguration.DefaultParseMode,
                    replyParameters: new ReplyParameters() { MessageId = (int)messageId },
                    cancellationToken: cancellationToken);
                    
                if (telegramResponce != null)
                {
                    string fileIdStr = (telegramResponce.Photo != null && telegramResponce.Photo.Any())
                        ? $"[Telegram FileId: {telegramResponce.Photo.Last().FileId}]\n"
                        : string.Empty;

                    string historyText = imageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? $"{fileIdStr}[Image Prompt: {prompt} | URL: {imageUrl}]"
                        : $"{fileIdStr}[Image Prompt: {prompt} | Base64: {imageUrl.Substring(0, Math.Min(100, imageUrl.Length))}...]";

                    await SaveHistory(telegramResponce, historyText, response.ProviderName, response.ModelName, (int)global::OpenAI.Role.Assistant, messageId);
                }
                
                return telegramResponce;
            }
        }

        var errorResponce = await _telegramBotClient.SendMessage(
                chatId: chatId,
                text: _localizer["Error_ImageGenerationFailed"],
                replyParameters: new ReplyParameters() { MessageId = (int)messageId },
                cancellationToken: cancellationToken);
                
        return errorResponce;
    }

    private async Task<ChatServiceResponse> InternalProcessDrawImage(long chatId, long fromUserId, string message)
    {
        var botInfo = await _telegramBotClient.GetMe();
        var msg = message.Replace(botInfo.Username ?? "", string.Empty);
        try
        {
            // Use pinned Drawing provider and model from config
            var drawingProviderName = _telegramBotConfiguration.AiSettings.Drawing?.ProviderName ?? "OpenAI";
            var drawingModelName = _telegramBotConfiguration.AiSettings.Drawing?.ModelName ?? "dall-e-3";
            
            var drawingService = _chatServiceFactory.CreateService(drawingProviderName);
            return await drawingService.GenerateImage(chatId, fromUserId, msg, drawingModelName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate image.");
            return new ChatServiceResponse { ProviderName = "Error", ModelName = "Error", Choices = new List<string>() };
        }
    }


    internal async Task<IReadOnlyList<global::OpenAI.Models.Model>> GetAIModels(long userId)
    {
        IReadOnlyList<global::OpenAI.Models.Model> result = await _chatService.GetAvailibleModels(userId);
        return result;
    }

    internal async Task<TgMessage> ProcessImage(long chatId, int messageId, int? replyToMessagemessageId,
        long userId, string? messageText, string filePath, string telegramFileId, CancellationToken cancellationToken)
    {
        // 1. Save user message to history
        string userText = $"[Telegram FileId: {telegramFileId}]\n" + (messageText ?? "[Image]");
        await SaveHistory(chatId, messageId, replyToMessagemessageId, (int)global::OpenAI.Role.User, userText, null, null, userId.ToString());

        // 2. Handle image context (Analyze or Edit)
        // We can reuse HandleImageContextAsync but it might need slight adjustment if we already have the filePath
        // For simplicity, let's keep it here but use the SaveHistory helper
        
        bool isAnalysis = true;
        if (!string.IsNullOrWhiteSpace(messageText))
        {
            try 
            {
                var classificationProvider = _telegramBotConfiguration.AiSettings.Classification?.ProviderName ?? "OpenAI";
                var classificationService = _chatServiceFactory.CreateService(classificationProvider);
                var classificationPrompt = $"Task: Classify intent for: '{messageText}'. ANALYZE or EDIT. Return ONLY 'ANALYZE' or 'EDIT'.";
                var classification = await classificationService.Ask(chatId, userId, classificationPrompt);
                isAnalysis = !classification.Contains("EDIT", StringComparison.OrdinalIgnoreCase);
            }
            catch { isAnalysis = true; }
        }

        ChatServiceResponse aiImageResponse;
        if (isAnalysis)
        {
            var visionProviderName = _telegramBotConfiguration.AiSettings.Vision?.ProviderName ?? "OpenAI";
            var visionService = _chatServiceFactory.CreateService(visionProviderName);
            aiImageResponse = await visionService.AnalyzeImageAsync(chatId, userId, null, filePath, messageText);
            
            var responseText = string.Join("\n", aiImageResponse.Choices.Where(p => !string.IsNullOrWhiteSpace(p)));
            var balanceDisplay = await GetBalanceDisplay(userId);
            var summary = $"{aiImageResponse.ProviderName} | {aiImageResponse.ModelName} | " +
                          $"In:{aiImageResponse.PromptTokens} Out:{aiImageResponse.CompletionTokens} | " +
                          $"L:{aiImageResponse.LatencySeconds:F1}s | ${aiImageResponse.Cost:F4}{balanceDisplay}";

            var toSendText = $"{responseText.ConvertMarkdownToHtml()}\n\n{summary.WrapWithSpoiler(_telegramBotConfiguration.DefaultParseMode)}";
            
            var telegramResponce = await _telegramBotClient.SendMessage(
                    chatId: chatId,
                    text: toSendText,
                    parseMode: _telegramBotConfiguration.DefaultParseMode,
                    replyParameters: new ReplyParameters() { MessageId = messageId },
                    replyMarkup: new ReplyKeyboardRemove(),
                    cancellationToken: cancellationToken);

            if (telegramResponce != null)
            {
                string historyText = $"[Telegram FileId: {telegramFileId}]\n" + toSendText;
                await SaveHistory(telegramResponce, historyText, aiImageResponse.ProviderName, aiImageResponse.ModelName, (int)global::OpenAI.Role.Assistant, messageId);
            }
            return telegramResponce;
        }
        else
        {
            // Edit intent: Generate prompt from description and draw
            var visionProviderName = _telegramBotConfiguration.AiSettings.Vision?.ProviderName ?? "OpenAI";
            var visionService = _chatServiceFactory.CreateService(visionProviderName);
            var descriptionResponse = await visionService.AnalyzeImageAsync(chatId, userId, null, filePath, "Describe image for DALL-E prompt generation.");
            var basePrompt = string.Join(" ", descriptionResponse.Choices);
            
            var refinementPrompt = $"New DALL-E prompt. Base: '{basePrompt}'. Change: '{messageText}'. Return ONLY English prompt.";
            var newPrompt = await visionService.Ask(chatId, userId, refinementPrompt);
            return await ProcessDrawCommand(chatId, messageId, userId, newPrompt, cancellationToken);
        }
    }

    internal async Task<(bool, string)> SelectAIModel(string? modelName, long userId)
    {
        return await _chatService.SetGPTModel(modelName, userId);
    }

    internal async Task SetPreferredProvider(long userId, ChatStrategy strategy)
    {
        var userRepository = _serviceProvider.GetRequiredService<IRepository<TelegramUserInfo>>();
        var user = await userRepository.Get(p => p.Id == userId);
        if (user != null)
        {
            user.PreferredProvider = strategy;
        userRepository.Update(user);
            await userRepository.SaveChanges();
        }
    }

    public async Task<string?> IdentifyLanguage(long chatId, long userId, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }
        
        var normalizedInput = input.Trim().ToLowerInvariant();
        if (normalizedInput == LanguageCode.UkraineShorthand || normalizedInput == LanguageCode.Ukrainian) return LanguageCode.Ukrainian;
        if (normalizedInput == LanguageCode.Russian) return LanguageCode.Russian;
        if (normalizedInput == LanguageCode.English) return LanguageCode.English;

        var prompt = $"Identify the TARGET language being requested or mentioned in this input: '{input}'. " +
                     $"Return ONLY the BCP-47 language code (e.g., '{LanguageCode.English}', '{LanguageCode.Ukrainian}', 'de', 'fr', 'es', '{LanguageCode.Russian}'). " +
                     "Do NOT return the language of the input word itself if it's naming a different language (e.g., if input is 'Spanish' or 'Испанский', return 'es'). " +
                     "Do NOT translate. Just return the 2-letter or standard code. " +
                     $"If you are not sure, return '{LanguageCode.English}'.";
        
        var classificationProvider = _telegramBotConfiguration.AiSettings.Classification?.ProviderName ?? "OpenAI";
        var classificationService = _chatServiceFactory.CreateService(classificationProvider);
        var result = await classificationService.Ask(chatId, userId, prompt);
        var langCode = result.Trim().ToLowerInvariant();
        
        if (langCode == LanguageCode.UkraineShorthand) langCode = LanguageCode.Ukrainian;
        
        // Basic validation
        if (langCode.Length > 10 || langCode.Any(char.IsWhiteSpace))
        {
            return LanguageCode.English;
        }

        return langCode;
    }

    private async Task<bool> IsNeedDrawImage(long charId, long userId, string text)
    {
        var isContainsWords = drawWords.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (!isContainsWords)
        {
            return false;
        }
        // We use the localizer to get the English version of the prompt 
        // to ensure AI stability, regardless of the user's current culture.
        var oldCulture = CultureInfo.CurrentUICulture;
        string confirmPhrase;
        string prompt;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(LanguageCode.English);
            confirmPhrase = _localizer["ConfirmPhrase_Yes"];
            prompt = _localizer["Prompt_IsNeedDraw", text, confirmPhrase];
        }
        finally
        {
            CultureInfo.CurrentUICulture = oldCulture;
        }

        var classificationProvider = _telegramBotConfiguration.AiSettings.Classification?.ProviderName ?? "OpenAI";
        var classificationService = _chatServiceFactory.CreateService(classificationProvider);
        var isNeewDrawResponce = await classificationService.Ask(charId, userId, prompt);
        _logger.LogInformation("Intent detection response: {Response}", isNeewDrawResponce);
        return isNeewDrawResponce.Contains(confirmPhrase, StringComparison.OrdinalIgnoreCase);

    }
    private async Task<bool> IsVoiceMessageToBot(long charId, long userId, string text)
    {
        var oldCulture = CultureInfo.CurrentUICulture;
        string confirmPhrase;
        string prompt;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(LanguageCode.English);
            confirmPhrase = _localizer["ConfirmPhrase_Yes"];
            prompt = _localizer["Prompt_IsBotMentioned", text, confirmPhrase];
        }
        finally
        {
            CultureInfo.CurrentUICulture = oldCulture;
        }

        var classificationProvider = _telegramBotConfiguration.AiSettings.Classification?.ProviderName ?? "OpenAI";
        var classificationService = _chatServiceFactory.CreateService(classificationProvider);
        string isNeewDrawResponce = await classificationService.Ask(charId, userId, prompt);
        _logger.LogInformation($"GPT Bot response: {isNeewDrawResponce}");
        return isNeewDrawResponce.Contains(confirmPhrase, StringComparison.OrdinalIgnoreCase);

    }

    internal async Task<string> GetBalanceDisplay(long userId)
    {
        var user = await _telegramUserInfoRepository.Get(p => p.Id == userId);
        if (user == null) return string.Empty;
        
        bool isIgnored = _telegramBotConfiguration.IgnoredBalanceUserIds?.Contains(userId) == true ||
                         _telegramBotConfiguration.OwnerId == userId;
        
        if (isIgnored) return " (Unlimited)";
        
        return $" | B: ${user.Balance.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private bool IsImageUrl(string url, out string cleanedUrl)
    {
        cleanedUrl = url;
        if (string.IsNullOrEmpty(url)) return false;

        char[] trailingChars = new char[] { '"', '\'', '.', ',', ')', ']', '}', '>', '\\', '*', ';', '!', '?' };
        cleanedUrl = url.TrimEnd(trailingChars);

        try
        {
            if (!Uri.TryCreate(cleanedUrl, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var path = uri.AbsolutePath.ToLowerInvariant();
            
            if (path.EndsWith(".png") || path.EndsWith(".jpg") || path.EndsWith(".jpeg") || 
                path.EndsWith(".webp") || path.EndsWith(".gif") || path.EndsWith(".bmp") || 
                path.EndsWith(".tiff") || path.EndsWith(".svg"))
            {
                return true;
            }

            var host = uri.Host.ToLowerInvariant();
            if (host.Contains("googleusercontent.com") || host.Contains("imgur.com") || host.Contains("images.unsplash.com"))
            {
                return true;
            }

            var query = uri.Query.ToLowerInvariant();
            if (query.Contains("format=png") || query.Contains("format=jpg") || query.Contains("format=jpeg") || query.Contains("format=webp") ||
                query.Contains("fm=png") || query.Contains("fm=jpg") || query.Contains("fm=jpeg") || query.Contains("fm=webp"))
            {
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public async Task<InlineKeyboardMarkup> BuildMcpToolsKeyboardAsync(long userId)
    {
        var mcpTools = await _mcpServerManager.GetAllToolsAsync();
        var disabledList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var dbUser = await _telegramUserInfoRepository.Get(p => p.Id == userId);
        if (dbUser != null && !string.IsNullOrEmpty(dbUser.DisabledTools))
        {
            var split = dbUser.DisabledTools.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var d in split)
            {
                disabledList.Add(d.Trim());
            }
        }

        var rows = new List<List<InlineKeyboardButton>>();
        
        // Group tools by group name and order groups alphabetically
        var groupedTools = mcpTools
            .GroupBy(t => GetToolGroup(t.Name))
            .OrderBy(g => g.Key);

        foreach (var group in groupedTools)
        {
            var groupToolsList = group.OrderBy(t => t.Name).ToList();
            
            // Build localized group header button (without showing status)
            string groupLabel = _localizer["McpTools_GroupHeader", group.Key];
            var groupCallbackData = $"mcp_tgroup:{group.Key}";
            if (groupCallbackData.Length > 64)
            {
                groupCallbackData = groupCallbackData.Substring(0, 64);
            }
            
            var groupHeaderButton = InlineKeyboardButton.WithCallbackData(groupLabel, groupCallbackData);
            rows.Add(new List<InlineKeyboardButton> { groupHeaderButton });
            
            // Add all tools belonging to this group
            foreach (var tool in groupToolsList)
            {
                bool isEnabled = !disabledList.Contains(tool.Name);
                var label = isEnabled ? $"✅ {tool.Name}" : $"❌ {tool.Name}";
                
                var callbackData = $"mcp_toggle:{tool.Name}";
                if (callbackData.Length > 64)
                {
                    callbackData = callbackData.Substring(0, 64);
                }
                
                var button = InlineKeyboardButton.WithCallbackData(label, callbackData);
                rows.Add(new List<InlineKeyboardButton> { button });
            }
        }

        return new InlineKeyboardMarkup(rows);
    }

    private string GetToolGroup(string toolName)
    {
        var serverName = _mcpServerManager.GetServerNameForTool(toolName);
        if (!string.IsNullOrEmpty(serverName))
        {
            return serverName
                .Replace("native-", "", StringComparison.OrdinalIgnoreCase)
                .ToUpperInvariant();
        }
        return "EXTERNAL";
    }
}

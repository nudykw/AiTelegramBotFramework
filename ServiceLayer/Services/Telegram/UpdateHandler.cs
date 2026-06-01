using System.Linq;
using DataBaseLayer.Enums;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using DataBaseLayer.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceLayer.Constans;
using BotCommand = ServiceLayer.Constans.BotCommand;
using ServiceLayer.Services.AudioTranscriptor;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Microsoft.Extensions.Localization;
using ServiceLayer.Resources;
using ServiceLayer.Services.Localization;
using System.CodeDom.Compiler;
using System.Globalization;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;
using Telegram.Bot.Types.ReplyMarkups;
using ServiceLayer.Utils;
using ServiceLayer.Services.Mcp;
using Image = SixLabors.ImageSharp.Image;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Net.Http;

namespace ServiceLayer.Services.Telegram;
public class UpdateHandler : BaseService, IUpdateHandler
{

    private readonly ITelegramBotClient _botClient;
    private static TgUser? _botInfo;
    private static readonly SemaphoreSlim _botInfoSemaphore = new(1, 1);
    private readonly MessageProcessor.MessageProcessor _messageProcessor;
    private readonly AudioTranscriptorService _audioTranscriptorService;
    private readonly IRepository<TelegramChatInfo>? _telegramChatInfoRepository;
    private readonly IRepository<TelegramUserInfo>? _telegramUserInfoRepository;
    private readonly IRepository<AIBilingItem>? _aiBilingItemRepository;
    private readonly IRepository<BalanceHistory>? _balanceHistoryRepository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDynamicLocalizer _localizer;
    private readonly IUserContext _userContext;
    private readonly AppSettings _appSettings;
    private readonly IChatService _chatService;
    private readonly McpServerManager _mcpServerManager;
    private readonly IReactionService _reactionService;
    private class OpenRouterModelListResponse
    {
        public List<OpenRouterModelInfo>? Data { get; set; }
    }

    private class OpenRouterModelInfo
    {
        public string? Id { get; set; }
        public long? Created { get; set; }
        public string? Description { get; set; }
    }

    private static readonly ConcurrentDictionary<string, DateTime> _modelReleaseDates = new();
    private static readonly ConcurrentDictionary<string, string> _modelDescriptions = new();
    private static DateTime _lastReleaseDatesUpdate = DateTime.MinValue;
    private static readonly object _releaseDatesLock = new();

    private static string NormalizeModelId(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return string.Empty;
        var parts = modelId.ToLowerInvariant().Split('/');
        return parts.Last(); // e.g. "google/gemini-2.5-flash" -> "gemini-2.5-flash"
    }

    private async Task RefreshModelReleaseDatesAsync()
    {
        lock (_releaseDatesLock)
        {
            if ((DateTime.UtcNow - _lastReleaseDatesUpdate).TotalHours < 24) return;
            _lastReleaseDatesUpdate = DateTime.UtcNow;
        }

        try
        {
            var httpClientFactory = _serviceProvider.GetService<IHttpClientFactory>();
            if (httpClientFactory == null) return;

            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "GPTChatTelegramBot-ReleaseDateFetcher");
            var response = await client.GetAsync("https://openrouter.ai/api/v1/models");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var data = JsonSerializer.Deserialize<OpenRouterModelListResponse>(content, options);
                if (data?.Data != null)
                {
                    foreach (var m in data.Data)
                    {
                        if (!string.IsNullOrEmpty(m.Id) && m.Created.HasValue)
                        {
                            var date = DateTimeOffset.FromUnixTimeSeconds(m.Created.Value).UtcDateTime;
                            var normalizedId = NormalizeModelId(m.Id);
                            _modelReleaseDates[normalizedId] = date;
                            if (!string.IsNullOrEmpty(m.Description))
                            {
                                _modelDescriptions[normalizedId] = m.Description;
                            }
                        }
                    }
                    _logger.LogInformation("Successfully loaded {Count} model release dates and descriptions from OpenRouter", _modelReleaseDates.Count);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh model release dates from OpenRouter");
        }
    }

    private async Task<string> GetOrTranslateModelDescriptionAsync(string modelId, string targetLangCode, CancellationToken cancellationToken)
    {
        var normalizedId = NormalizeModelId(modelId);
        
        // 1. Define base English descriptions
        string englishDesc = normalizedId switch
        {
            "gemini-2.5-flash" => "Gemini 2.5 Flash (Google) - A lightweight, fast, and cost-efficient multimodal AI model. Perfect for quick responses, text processing, and general-purpose tasks.",
            "gemini-2.5-pro" => "Gemini 2.5 Pro (Google) - A flagship, high-intelligence model. Excels at complex coding tasks, in-depth analysis, and logical reasoning.",
            "gemini-3.5-flash" => "Gemini 3.5 Flash (Google) - The next-generation fast model. Features improved speed, accuracy, and an expanded context window.",
            "gpt-4o" => "GPT-4o (OpenAI) - OpenAI's leading multimodal model. Fast, smart, with excellent natural language understanding and advanced reasoning.",
            "gpt-4o-mini" => "GPT-4o Mini (OpenAI) - A lightweight version of GPT-4o. Ultrafast and highly cost-effective for everyday tasks.",
            "deepseek-chat" or "deepseek-v4-flash" => "DeepSeek V4 (DeepSeek) - DeepSeek's advanced model featuring outstanding capabilities in math, coding, and precise reasoning at an extremely low cost.",
            _ => null
        };

        if (englishDesc == null)
        {
            if (_modelDescriptions.TryGetValue(normalizedId, out var desc) && !string.IsNullOrEmpty(desc))
            {
                englishDesc = desc;
            }
            else
            {
                englishDesc = $"Model: {modelId}. Additional information about costs and features is available in the provider menu.";
            }
        }

        // Standardize language code to lowercase
        var lang = targetLangCode?.ToLowerInvariant() ?? "en";
        if (lang == "ua") lang = "uk"; // Handle Ukraine shorthand mapping

        // 2. If target language is English or Russian, return directly in English
        if (lang == "en" || lang == "ru")
        {
            return englishDesc;
        }

        // 3. Translate and cache logic
        var cacheKey = $"model:desc:{normalizedId}";
        var cacheRepo = _serviceProvider.GetService<IRepository<CachedTranslation>>();
        if (cacheRepo == null)
        {
            return englishDesc;
        }

        try
        {
            var cached = cacheRepo.GetAll().FirstOrDefault(p => p.LanguageCode == lang && p.ResourceKey == cacheKey);
            if (cached != null && cached.OriginalText == englishDesc && !string.IsNullOrEmpty(cached.TranslatedText))
            {
                return cached.TranslatedText;
            }

            // Get target language name
            string languageName = lang switch
            {
                "uk" => "Ukrainian",
                "ru" => "Russian",
                _ => "its native language"
            };

            // Use IChatService to translate using AI
            var prompt = $"Translate the following model description from English to {languageName} ({lang}). " +
                         "CRITICAL: Translate accurately, preserve model names in English, keep the tone professional. " +
                         "Output ONLY the translated text.\n\n" +
                         $"Text: {englishDesc}";

            _logger.LogInformation("Translating model description for {ModelId} to {Lang} via AI...", normalizedId, lang);
            
            // Try to use a safe ChatId/UserId for translation billing, fallback to system if 0
            long targetChatId = _userContext.ChatId != 0 ? _userContext.ChatId : 0;
            long targetUserId = _userContext.UserId != 0 ? _userContext.UserId : 0;
            
            string translatedText;
            if (targetChatId != 0 && targetUserId != 0)
            {
                translatedText = await _chatService.Ask(targetChatId, targetUserId, prompt);
            }
            else
            {
                translatedText = englishDesc;
            }

            translatedText = translatedText?.Trim() ?? englishDesc;

            if (cached == null)
            {
                cached = new CachedTranslation
                {
                    LanguageCode = lang,
                    ResourceKey = cacheKey,
                    OriginalText = englishDesc,
                    TranslatedText = translatedText,
                    UpdatedAt = DateTime.UtcNow
                };
                cacheRepo.Add(cached);
            }
            else
            {
                cached.OriginalText = englishDesc;
                cached.TranslatedText = translatedText;
                cached.UpdatedAt = DateTime.UtcNow;
                cacheRepo.Update(cached);
            }

            await cacheRepo.SaveChanges();
            return translatedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to translate model description for {ModelId} to {Lang}", normalizedId, lang);
            return englishDesc;
        }
    }

    private static Dictionary<string, string> aiModelsCosts = new Dictionary<string, string>()
    {
        {AiModel.Gpt4Turbo,  "$0.01/$0.03"},
        {AiModel.Gpt4o,  "$0.01/$0.03"},
        {"gpt-4-32k",  "$0.06/$0.12"},
        {"gpt-3.5-turbo-1106",  "$0.001/$0.002"},
        {AiModel.Gpt35Turbo,  "$0.0015/$0.002"},
    };

    private static readonly Dictionary<string, string> _voiceAliases = new()
    {
        { "alloy", "Universal" },
        { "onyx", "Atlas" },
        { "echo", "Echo" },
        { "nova", "Oracle" },
        { "shimmer", "Muse" },
        { "fable", "Storyteller" }
    };

    public UpdateHandler(IServiceProvider serviceProvider, ILogger<UpdateHandler> logger,
        ITelegramBotClient botClient, MessageProcessor.MessageProcessor messageProcessor,
        AudioTranscriptorService audioTranscriptorService,
        IServiceScopeFactory scopeFactory,
        IDynamicLocalizer localizer,
        IUserContext userContext,
        AppSettings appSettings,
        IChatService chatService,
        McpServerManager mcpServerManager,
        IReactionService reactionService)
        : base(serviceProvider, logger)
    {
        _botClient = botClient;
        // _botInfo is now initialized asynchronously in ProcessUpdateInternalAsync
        _messageProcessor = messageProcessor;
        _audioTranscriptorService = audioTranscriptorService;
        _scopeFactory = scopeFactory;
        _localizer = localizer;
        _userContext = userContext;
        _appSettings = appSettings;
        _chatService = chatService;
        _mcpServerManager = mcpServerManager;
        _reactionService = reactionService;
        _telegramChatInfoRepository = _serviceProvider.GetService<IRepository<TelegramChatInfo>>();
        _telegramUserInfoRepository = _serviceProvider.GetService<IRepository<TelegramUserInfo>>();
        _aiBilingItemRepository = _serviceProvider.GetService<IRepository<AIBilingItem>>();
        _balanceHistoryRepository = _serviceProvider.GetService<IRepository<BalanceHistory>>();
    }

    public Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received update {UpdateId} of type {UpdateType}", update.Id, update.Type);
        // We do NOT await this Task.Run, allowing the Telegram polling loop
        // to receive the next update immediately.
        _ = Task.Run(async () =>
        {
            try
            {
                // Create a new scope for EACH update to ensure separate DbContext instances
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<UpdateHandler>();
                await processor.ProcessUpdateInternalAsync(botClient, update, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Detailed error processing update {UpdateId}: {Message}", update.Id, ex.Message);
            }
        });

        return Task.CompletedTask;
    }

    internal async Task ProcessUpdateInternalAsync(ITelegramBotClient _, Update update, CancellationToken cancellationToken)
    {
        if (_botInfo == null)
        {
            await _botInfoSemaphore.WaitAsync(cancellationToken);
            try
            {
                _botInfo ??= await _botClient.GetMe(cancellationToken);
            }
            finally
            {
                _botInfoSemaphore.Release();
            }
        }

        var user = update switch
        {
            { Message.From: { } f } => f,
            { EditedMessage.From: { } f } => f,
            { CallbackQuery.From: { } f } => f,
            { InlineQuery.From: { } f } => f,
            { ChosenInlineResult.From: { } f } => f,
            _ => null
        };

        if (user != null && _telegramUserInfoRepository != null)
        {
            _userContext.UserId = user.Id;
            var dbUser = await _telegramUserInfoRepository.Get(p => p.Id == user.Id);
            var langCode = dbUser?.LanguageCode ?? user.LanguageCode ?? "en";
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(langCode);
            }
            catch
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(LanguageCode.English);
            }
        }

        try
        {
            var handler = update switch
            {
                { Message: { } message } => BotOnMessageReceived(message, cancellationToken),
                { EditedMessage: { } message } => BotOnMessageReceived(message, cancellationToken, isEdit: true),
                { CallbackQuery: { } callbackQuery } => BotOnCallbackQueryReceived(callbackQuery, cancellationToken),
                { InlineQuery: { } inlineQuery } => BotOnInlineQueryReceived(inlineQuery, cancellationToken),
                { ChosenInlineResult: { } chosenInlineResult } => BotOnChosenInlineResultReceived(chosenInlineResult, cancellationToken),
                _ => UnknownUpdateHandlerAsync(update, cancellationToken)
            };

            await handler;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in HandleUpdateAsync for update {UpdateId}", update.Id);
            try
            {
                long? chatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id;
                if (chatId.HasValue)
                {
                    var errorHeader = _localizer["GenericError"];
                    var fullErrorMsg = $"{errorHeader}\n\n{_localizer["Error_Details"]} {ex.Message}";
                    await _botClient.SendMessage(chatId.Value, fullErrorMsg, cancellationToken: cancellationToken);
                }
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to send error message to user");
            }
        }
    }

    private async Task BotOnMessageReceived(TgMessage message, CancellationToken cancellationToken, bool isEdit = false)
    {
        if (message.From == null) return;
        _userContext.UserId = message.From.Id;
        _userContext.ChatId = message.Chat.Id;

        Chat chat = message.Chat;
        TgUser user = message.From;
        await TrySaveMessageInfoAsync(chat, user);

        if (message.Contact != null)
        {
            await HandleContactMessageAsync(message, cancellationToken);
            return;
        }

        if (message.Location != null)
        {
            await HandleLocationMessageAsync(message, cancellationToken);
            return;
        }

        _logger.LogInformation("Receive message type: {MessageType}", message.Type);
        
        // Voice messages always need AI if we want to transcribe them
        if (message.Voice != null)
        {
            if (!await CheckBalanceAndReplenish(message.From.Id, message.Chat.Id, cancellationToken))
            {
                return;
            }
        }

        var dbUser = _telegramUserInfoRepository != null ? await _telegramUserInfoRepository.Get(p => p.Id == message.From.Id) : null;
        var voiceMessage = await VoiceMessageToText(_botClient, message, dbUser?.LanguageCode);
        if (voiceMessage != null)
        {
            message.Text = voiceMessage;
        }

        if (message.Text is not { } && message.Caption is not { } && message.Photo == null && message.Document == null)
            return;
        
        var messageText = message.Text ?? message.Caption ?? string.Empty;
        _logger.LogInformation("Text: {MessageType}", messageText);

        // Check if message is a reply to language prompt
        if (message.ReplyToMessage != null && 
            (message.ReplyToMessage.Text?.Contains(_localizer["EnterLanguagePrompt"]) == true))
        {
            await ProcessLanguageCommand(_botClient, message, string.Empty, cancellationToken);
            return;
        }

        // Check if message is a reply to schedule prompt
        if (message.ReplyToMessage != null && 
            (message.ReplyToMessage.Text?.Contains("enter a topic or prompt for your newsletter") == true))
        {
            await ProcessSchedulePromptReply(_botClient, message, cancellationToken);
            return;
        }

        // Check if message is a reply to custom system prompt prompt
        if (message.ReplyToMessage != null && 
            (message.ReplyToMessage.Text?.Contains(_localizer["Prompt_EnterNew"]) == true))
        {
            if (messageText.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
            {
                await _botClient.SendMessage(message.Chat.Id, _localizer["Cancel"], cancellationToken: cancellationToken);
                return;
            }
            await ProcessPromptCommand(_botClient, message, string.Empty, cancellationToken);
            return;
        }

        if (!IsMe(message))
        {
            _logger.LogInformation("This message not for me.");
            return;
        }

        var actionText = messageText.Split(' ')[0].Replace($"@{_botInfo?.Username ?? ""}", string.Empty);
        var command = BotCommand.FromString(actionText);

        if (command != null)
        {
            if (command.Value == BotCommand.Schedule.Value || command.Value == BotCommand.Shedule.Value)
            {
                if (_appSettings.Scheduler?.Enabled != true)
                {
                    command = null; // Treat as normal message if scheduler is disabled
                }
                else
                {
                    var isAllowed = await IsUserAllowedForSchedulerAsync(user.Id);
                    if (!isAllowed)
                    {
                        _logger.LogWarning("User {UserId} was denied access to scheduler in chat {ChatId}", user.Id, chat.Id);
                        if (chat.Type == ChatType.Private)
                        {
                            await _botClient.SendMessage(chat.Id, _localizer["PermissionDenied"], cancellationToken: cancellationToken);
                        }
                        return;
                    }
                }
            }
        }

        if (command != null)
        {
            var userScopes = await GetUserScopesAsync(user, chat);
            if ((command.RequiredScope & userScopes) == 0)
            {
                _logger.LogWarning("User {UserId} tried to execute restricted command {Command} in chat {ChatId}", user.Id, actionText, chat.Id);
                // Return null or send a message if in private chat
                if (chat.Type == ChatType.Private)
                {
                    await _botClient.SendMessage(chat.Id, _localizer["PermissionDenied"], cancellationToken: cancellationToken);
                }
                return;
            }
        }

        // Identify if this is an AI action that costs balance
        bool isAiAction = (command?.Value == BotCommand.Draw) || 
                         (message.Photo != null) || 
                         (message.Document != null && (message.Document.MimeType?.Contains("image") ?? false)) ||
                         (command == null && !new[] { "/keyboard", "/remove", "/photo", "/request", "/inline_mode", "/throw", "/help" }.Contains(actionText));

        if (isAiAction)
        {
            if (!await CheckBalanceAndReplenish(message.From.Id, message.Chat.Id, cancellationToken))
            {
                return;
            }
        }

        var action = command?.Value switch
        {
            var v when v == BotCommand.Billing => SendBillingInlineKeyboard(_botClient, message, cancellationToken),
            var v when v == BotCommand.Model => SendModelInlineKeyboard(_botClient, message.Chat.Id, message.From.Id, cancellationToken),
            var v when v == BotCommand.Provider => SendProviderInlineKeyboard(_botClient, message.Chat.Id, message.From.Id, cancellationToken),
            var v when v == BotCommand.Voice => ProcessVoiceCommand(_botClient, message, cancellationToken),
            var v when v == BotCommand.Lang => ProcessLanguageCommand(_botClient, message, actionText, cancellationToken),
            var v when v == BotCommand.Draw => _messageProcessor.ProcessDrawCommand(message.Chat.Id, message.MessageId, message.From.Id, messageText.Replace(actionText, string.Empty).Trim(), cancellationToken),
            var v when v == BotCommand.Help => Usage(_botClient, message, cancellationToken),
            var v when v == BotCommand.Restart => FailingHandler(_botClient, message, cancellationToken),
            var v when v == BotCommand.UsersBalance => ShowUsersBalance(_botClient, message, cancellationToken),
            var v when v == BotCommand.SetBalance => SetUserBalance(_botClient, message, cancellationToken),
            var v when v == BotCommand.RefreshModels => RefreshModelsCache(_botClient, message, cancellationToken),
            var v when v == BotCommand.McpTools => ShowMcpTools(_botClient, message, cancellationToken),
            var v when v == BotCommand.Schedule || v == BotCommand.Shedule => ProcessScheduleCommand(_botClient, message, cancellationToken),
            var v when v == BotCommand.Prompt => ProcessPromptCommand(_botClient, message, actionText, cancellationToken),
            _ => actionText switch
            {
                "/keyboard" => SendReplyKeyboard(_botClient, message, cancellationToken),
                "/remove" => RemoveKeyboard(_botClient, message, cancellationToken),
                "/photo" => SendFile(_botClient, message, cancellationToken),
                "/request" => RequestContactAndLocation(_botClient, message, cancellationToken),
                "/inline_mode" => StartInlineQuery(_botClient, message, cancellationToken),
                "/throw" => FailingHandler(_botClient, message, cancellationToken),
                "/help" => Usage(_botClient, message, cancellationToken),
                _ => ProcessMessage(_botClient, message, cancellationToken, isEdit, voiceMessage)
            }
        };
        TgMessage sentMessage = await action;
        if (sentMessage != null)
        {
            _logger.LogInformation("The message was sent with id: {SentMessageId}", sentMessage.MessageId);
        }

        // Send inline keyboard 
        async Task<TgMessage> SendBillingInlineKeyboard(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            await botClient.SendChatAction(
                chatId: message.Chat.Id,
                action: ChatAction.Typing,
                cancellationToken: cancellationToken);

            var dates = _aiBilingItemRepository?
                .GetAll()
                .Select(p => new { p.CreationDate.Year, p.CreationDate.Month })
                .Distinct()
                .ToList()
                .Select(p => $"{p.Year}-{p.Month:D2}")
                .OrderByDescending(p => p)
                .ToList() ?? new List<string>();

            if (!dates.Any())
            {
                return await botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: _localizer["Billing_NoData"],
                    cancellationToken: cancellationToken);
            }

            var keyboard = TelegramKeyboardHelper.CreateInlineKeyboard(
                items: dates,
                callbackDataSelector: date => $"{BotCommand.Billing}:{date}",
                buttonTextSelector: date => date,
                columns: 2
            );

            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _localizer["Billing_SelectPeriod"],
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }

        async Task<TgMessage> ProcessVoiceCommand(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            var user = _telegramUserInfoRepository != null ? await _telegramUserInfoRepository.Get(p => p.Id == message.From.Id) : null;
            var currentVoice = user?.VoiceId ?? "alloy";
            
            var voices = _appSettings.TelegramBotConfiguration.AvailableVoices;
            
            var replyMarkup = TelegramKeyboardHelper.CreateSelectionKeyboard(
                voices,
                v => _voiceAliases.GetValueOrDefault(v, v),
                v => $"{BotCommand.Voice}:{v}",
                currentVoice,
                columns: 1);
            
            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _localizer["SelectVoice"],
                replyMarkup: replyMarkup,
                cancellationToken: cancellationToken);
        }

        async Task<TgMessage> ProcessLanguageCommand(ITelegramBotClient botClient, TgMessage message, string actionText, CancellationToken cancellationToken)
        {
            var messageText = message.Text ?? message.Caption ?? string.Empty;
            var argument = string.IsNullOrEmpty(actionText) 
                ? messageText 
                : messageText.Replace(actionText, string.Empty).Trim();

            if (string.IsNullOrEmpty(argument))
            {
                return await SendLanguageInlineKeyboard(botClient, message, cancellationToken);
            }

            await botClient.SendChatAction(message.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken);
            var langCode = await _messageProcessor.IdentifyLanguage(message.Chat.Id, message.From.Id, argument);

            if (langCode == LanguageCode.English || langCode == LanguageCode.Ukrainian)
            {
                // Native language, update immediately
                var dbUser = _telegramUserInfoRepository != null ? await _telegramUserInfoRepository.Get(p => p.Id == message.From.Id) : null;
                if (dbUser != null)
                {
                    dbUser.LanguageCode = langCode;
                    _telegramUserInfoRepository.Update(dbUser);
                    await _telegramUserInfoRepository.SaveChanges();
                }

                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(langCode);
                }
                catch
                {
                    CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(LanguageCode.English);
                }
                
                string nativeName = langCode;
                
                var oldCulture = CultureInfo.CurrentUICulture;
                string englishText;
                try {
                    CultureInfo.CurrentUICulture = new CultureInfo(LanguageCode.English);
                    englishText = _localizer["LanguageChanged", nativeName];
                } finally {
                    CultureInfo.CurrentUICulture = oldCulture;
                }
                
                var translatedText = _localizer["LanguageChanged", nativeName];
                var confirmation = (langCode == LanguageCode.English) ? translatedText : $"{englishText} {translatedText}";
                
                return await botClient.SendMessage(message.Chat.Id, confirmation, cancellationToken: cancellationToken);
            }

            // Non-native language, ask for confirmation
            var confirmKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData(_localizer["Confirm"], $"{BotCommand.Lang}:confirm:{langCode}"),
                    InlineKeyboardButton.WithCallbackData(_localizer["Cancel"], $"{BotCommand.Lang}:cancel")
                }
            });

            var warning = _localizer["DynamicLanguageWarning", argument, langCode];
            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: warning,
                replyMarkup: confirmKeyboard,
                cancellationToken: cancellationToken);
        }

        async Task<TgMessage> SendLanguageInlineKeyboard(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            await botClient.SendChatAction(
                chatId: message.Chat.Id,
                action: ChatAction.Typing,
                cancellationToken: cancellationToken);
            var choises = new TgKeyboardGrid
            {
                new TgKeyboardRow
                {
                    InlineKeyboardButton.WithCallbackData("🇬🇧 English", $"{BotCommand.Lang}:{LanguageCode.English}"),
                    InlineKeyboardButton.WithCallbackData("🇺🇦 Українська", $"{BotCommand.Lang}:{LanguageCode.Ukrainian}")
                },
                new TgKeyboardRow
                {
                     InlineKeyboardButton.WithCallbackData("🌍 " + _localizer["OtherLanguage"], $"{BotCommand.Lang}:prompt")
                }
            };

            InlineKeyboardMarkup replyMarkup = new(choises);
            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _localizer["SelectLanguage"],
                replyMarkup: replyMarkup,
                cancellationToken: cancellationToken);
        }

        async Task<TgMessage> ProcessPromptCommand(ITelegramBotClient botClient, TgMessage message, string actionText, CancellationToken cancellationToken)
        {
            var messageText = message.Text ?? message.Caption ?? string.Empty;
            var argument = string.IsNullOrEmpty(actionText) 
                ? messageText 
                : messageText.Replace(actionText, string.Empty).Trim();

            if (string.IsNullOrEmpty(argument))
            {
                return await SendPromptInlineKeyboard(botClient, message, cancellationToken);
            }

            await botClient.SendChatAction(message.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken);
            
            var dbUser = _telegramUserInfoRepository != null ? await _telegramUserInfoRepository.Get(p => p.Id == message.From.Id) : null;
            if (dbUser != null)
            {
                if (argument.Equals("reset", StringComparison.OrdinalIgnoreCase))
                {
                    dbUser.SystemPrompt = null;
                    _telegramUserInfoRepository.Update(dbUser);
                    await _telegramUserInfoRepository.SaveChanges();
                    return await botClient.SendMessage(message.Chat.Id, _localizer["Prompt_ResetSuccess"], cancellationToken: cancellationToken);
                }
                else
                {
                    if (argument.Length > 4000)
                    {
                        argument = argument.Substring(0, 4000);
                    }
                    dbUser.SystemPrompt = argument;
                    _telegramUserInfoRepository.Update(dbUser);
                    await _telegramUserInfoRepository.SaveChanges();
                    return await botClient.SendMessage(message.Chat.Id, _localizer["Prompt_Updated"], cancellationToken: cancellationToken);
                }
            }
            
            return await botClient.SendMessage(message.Chat.Id, _localizer["GenericError"], cancellationToken: cancellationToken);
        }

        async Task<TgMessage> SendPromptInlineKeyboard(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            await botClient.SendChatAction(
                chatId: message.Chat.Id,
                action: ChatAction.Typing,
                cancellationToken: cancellationToken);

            var dbUser = _telegramUserInfoRepository != null ? await _telegramUserInfoRepository.Get(p => p.Id == message.From.Id) : null;
            var currentPrompt = dbUser?.SystemPrompt;
            var promptMessage = string.IsNullOrEmpty(currentPrompt)
                ? _localizer["Prompt_NotSet"]
                : _localizer["Prompt_Current", currentPrompt];

            var buttons = new List<InlineKeyboardButton[]>();
            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(_localizer["Prompt_Change"], $"{BotCommand.Prompt}:prompt") });
            
            if (!string.IsNullOrEmpty(currentPrompt))
            {
                buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(_localizer["Prompt_Reset"], $"{BotCommand.Prompt}:reset") });
            }
            
            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData(_localizer["Prompt_Cancel"], $"{BotCommand.Prompt}:cancel") });

            InlineKeyboardMarkup replyMarkup = new(buttons);
            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: promptMessage,
                parseMode: ParseMode.Html,
                replyMarkup: replyMarkup,
                cancellationToken: cancellationToken);
        }


        async Task<TgMessage> SendReplyKeyboard(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            ReplyKeyboardMarkup replyKeyboardMarkup = new(
                new[]
                {
                        new KeyboardButton[] { "1.1", "1.2" },
                        new KeyboardButton[] { "2.1", "2.2" },
                })
            {
                ResizeKeyboard = true
            };

            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _localizer["Choose"],
                replyMarkup: replyKeyboardMarkup,
                cancellationToken: cancellationToken);
        }

        async Task<TgMessage> RemoveKeyboard(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _localizer["RemovingKeyboard"],
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
        }

        async Task<TgMessage> SendFile(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            await botClient.SendChatAction(
                message.Chat.Id,
                ChatAction.UploadPhoto,
                cancellationToken: cancellationToken);

            const string filePath = "Files/tux.png";
            await using FileStream fileStream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var fileName = filePath.Split(Path.DirectorySeparatorChar).Last();

            return await botClient.SendPhoto(
                chatId: message.Chat.Id,
                photo: new InputFileStream(fileStream, fileName),
                caption: _localizer["NicePicture"],
                cancellationToken: cancellationToken);
        }

        async Task<TgMessage> RequestContactAndLocation(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            ReplyKeyboardMarkup RequestReplyKeyboard = new(
                new[]
                {
                    KeyboardButton.WithRequestLocation(_localizer["Location"]),
                    KeyboardButton.WithRequestContact(_localizer["Contact"]),
                });

            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _localizer["WhoWhereAreYou"],
                replyMarkup: RequestReplyKeyboard,
                cancellationToken: cancellationToken);
        }

        async Task<TgMessage> ShowMcpTools(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            await botClient.SendChatAction(message.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken);
            var tools = await _mcpServerManager.GetAllToolsAsync();
            
            if (tools == null || !tools.Any())
            {
                return await botClient.SendMessage(message.Chat.Id, _localizer["McpTools_Empty"], cancellationToken: cancellationToken);
            }

            var dbUser = _telegramUserInfoRepository != null ? await _telegramUserInfoRepository.Get(p => p.Id == message.From.Id) : null;
            var langCode = dbUser?.LanguageCode ?? message.From?.LanguageCode ?? "en";

            string headerText = langCode.ToLowerInvariant() switch
            {
                "ua" or "uk" => "🛠️ <b>Керування інструментами MCP</b>\n\nНатисніть на будь-який інструмент нижче, щоб увімкнути або вимкнути його для ваших запитів ШІ:",
                _ => "🛠️ <b>Manage MCP Tools</b>\n\nClick on any tool below to enable or disable it for your AI interactions:"
            };

            var keyboard = await _messageProcessor.BuildMcpToolsKeyboardAsync(message.From.Id);

            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: headerText,
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }

        async Task<TgMessage> Usage(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            var userScopes = await GetUserScopesAsync(message.From, message.Chat);
            var availableCommands = BotCommand.GetAll()
                .Where(cmd => (cmd.RequiredScope & userScopes) != 0)
                .ToList();

            if (_appSettings.Scheduler?.Enabled != true)
            {
                availableCommands.RemoveAll(cmd => cmd.Value == BotCommand.Schedule.Value || cmd.Value == BotCommand.Shedule.Value);
            }
            else
            {
                var isAllowed = await IsUserAllowedForSchedulerAsync(message.From!.Id);
                if (!isAllowed)
                {
                    availableCommands.RemoveAll(cmd => cmd.Value == BotCommand.Schedule.Value || cmd.Value == BotCommand.Shedule.Value);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(_localizer["HelpHeader"]);
            sb.AppendLine();

            var dbUser = await _telegramUserInfoRepository.Get(p => p.Id == message.From.Id);

            foreach (var cmd in availableCommands)
            {
                var descriptionKey = "Desc_" + cmd.Value.TrimStart('/');
                var line = $"{cmd.Value} - {_localizer[descriptionKey]}";
                
                string? currentValue = null;
                if (cmd.Value == BotCommand.Model.Value)
                {
                    currentValue = await _chatService.GetSelectedModel(message.From.Id);
                }
                else if (cmd.Value == BotCommand.Provider.Value)
                {
                    var provider = dbUser?.PreferredProvider ?? ChatStrategy.Auto;
                    currentValue = provider == ChatStrategy.Auto 
                        ? _localizer["AutoRotation"] 
                        : provider.ToString();
                }
                else if (cmd.Value == BotCommand.Lang.Value)
                {
                    currentValue = dbUser?.LanguageCode ?? message.From.LanguageCode;
                }

                if (!string.IsNullOrEmpty(currentValue))
                {
                    line += _localizer["CurrentValue", currentValue];
                }

                sb.AppendLine(line);
            }

            sb.AppendLine();
            sb.AppendLine(_localizer["GroupContextInfo"]);

            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: sb.ToString(),
                parseMode: ParseMode.Markdown,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
        }
        async Task<TgMessage> ProcessMessage(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken, bool isEdit = false, string? transcription = null)
        {
            string messageText = message.Text;
            if (message.Voice != null)
            {
                var voice = message.Voice;
                message.Voice = null;
                messageText = $"{(IsMe(message) ? MessageMarkers.VoiceMessageToBot : MessageMarkers.VoiceMessage)}{messageText}";
                message.Voice = voice;
            }
            if (message.Document != null && message.Document.MimeType.Contains("image"))
            {
                var file = await botClient.GetFile(message.Document.FileId);
                var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{Path.GetExtension(message.Document.FileName)}");
                try
                {
                    using (var tempFileStream = new FileStream(tempFilePath, FileMode.Create))
                    {
                        await botClient.DownloadFile(file.FilePath, tempFileStream);
                    }
                    await ActionWithShowTypeng(message.Chat.Id, cancellationToken, _messageProcessor.ProcessImage(message.Chat.Id,
                            message.MessageId, message.ReplyToMessage?.MessageId, message.From?.Id ?? 0,
                            message.Caption, tempFilePath, file.FileId, cancellationToken));
                }
                finally
                {
                    if (System.IO.File.Exists(tempFilePath))
                        System.IO.File.Delete(tempFilePath);
                }

                var filePath = file.FilePath;
                //var fileStream = System.IO.File.OpenRead(filePath);
                return null;
            }
            if (message.Photo != null)
            {
                var photo = message.Photo.Last(p => p.FileSize < 4 * 1024000);
                var file = await botClient.GetFile(photo.FileId);
                var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
                try
                {
                    using (var telegramImageStream = new MemoryStream())
                    {
                        await botClient.DownloadFile(file.FilePath, telegramImageStream);
                        telegramImageStream.Flush();
                        telegramImageStream.Seek(0, SeekOrigin.Begin);
                        using (var image = Image.Load<Rgba32>(telegramImageStream))
                        {
                            image.Save(tempFilePath);
                        }
                    }
                    await ActionWithShowTypeng(message.Chat.Id, cancellationToken, _messageProcessor.ProcessImage(message.Chat.Id,
                            message.MessageId, message.ReplyToMessage?.MessageId, message.From?.Id ?? 0,
                            message.Caption, tempFilePath, photo.FileId, cancellationToken)
                    );
                }
                finally
                {
                    if (System.IO.File.Exists(tempFilePath))
                        System.IO.File.Delete(tempFilePath);
                }

                var filePath = file.FilePath;
                //var fileStream = System.IO.File.OpenRead(filePath);
                return null;
            }
            bool wantsVoiceResponse = message.Voice != null;
            if (wantsVoiceResponse && !string.IsNullOrEmpty(messageText))
            {
                var lowerText = messageText.ToLowerInvariant();
                if (lowerText.Contains("ответь текстом") || lowerText.Contains("ответ текстом") || 
                    lowerText.Contains("respond with text") || lowerText.Contains("text response"))
                {
                    wantsVoiceResponse = false;
                }
            }

            string responce = await ActionWithShowTypeng(message.Chat.Id, cancellationToken, _messageProcessor.ProcessMessage(message.Chat.Id,
                    message.MessageId, message.ReplyToMessage?.MessageId, message.From?.Id ?? 0,
                    messageText,
                    cancellationToken,
                    isEdit,
                    wantsVoiceResponse,
                    transcription));
            return null;
            /*
            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: responce,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
            */
        }
        async Task<string> VoiceMessageToText(ITelegramBotClient botClient,
            TgMessage message, string? language = null)
        {
            if (message.Voice != null)
            {
                Voice voiceMessage = message.Voice;
                var file = await botClient.GetFile(voiceMessage.FileId);
                var voiceFilePath = Path.Combine(Path.GetTempPath(), $"{voiceMessage.FileId}.ogg");

                try
                {
                    using (var saveVoiceStream = new FileStream(voiceFilePath, FileMode.Create))
                    {
                        await botClient.DownloadFile(file.FilePath, saveVoiceStream);
                        saveVoiceStream.Seek(0, SeekOrigin.Begin);
                        var messageText = await _audioTranscriptorService
                            .AudioTranscription(message.Chat.Id, message.From?.Id ?? 0, saveVoiceStream, voiceFilePath, language: language);
                        return messageText;
                    }
                }
                finally
                {
                    if (System.IO.File.Exists(voiceFilePath))
                        System.IO.File.Delete(voiceFilePath);
                }
            }
            return null;
        }
        async Task<TgMessage> StartInlineQuery(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            InlineKeyboardMarkup inlineKeyboard = new(
                InlineKeyboardButton.WithSwitchInlineQueryCurrentChat(_localizer["InlineMode"]));

            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _localizer["StartInlineQuery"],
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken);
        }

#pragma warning disable RCS1163 // Unused parameter.
#pragma warning disable IDE0060 // Remove unused parameter
        static Task<TgMessage> FailingHandler(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            Environment.Exit(1);
            throw new IndexOutOfRangeException();
        }
#pragma warning restore IDE0060 // Remove unused parameter
#pragma warning restore RCS1163 // Unused parameter.
    }

    private async Task<TgMessage> ShowUsersBalance(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
    {
        await botClient.SendChatAction(
            chatId: message.Chat.Id,
            action: ChatAction.Typing,
            cancellationToken: cancellationToken);

        var users = _telegramUserInfoRepository.GetAll()
            .OrderBy(u => u.Id)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine(_localizer["UsersBalanceHeader"]);
        sb.AppendLine();

        foreach (var u in users)
        {
            var timeAgo = FormatTimeAgo(u.BalanceModifiedAt);
            // UsersBalanceItem format: {0} ({1}): {2} (last changed: {3})
            sb.AppendLine(_localizer["UsersBalanceItem", 
                u.FirstName + (string.IsNullOrEmpty(u.LastName) ? "" : " " + u.LastName), 
                u.Id, 
                u.Balance.ToString("0.######"), 
                timeAgo]);
        }

        return await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: sb.ToString(),
            cancellationToken: cancellationToken);
    }

    private async Task<TgMessage> SetUserBalance(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
    {
        await botClient.SendChatAction(
            chatId: message.Chat.Id,
            action: ChatAction.Typing,
            cancellationToken: cancellationToken);

        var text = message.Text ?? string.Empty;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3)
        {
            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _localizer["SetBalance_Usage"],
                cancellationToken: cancellationToken);
        }

        bool userIdParsed = long.TryParse(parts[1], out long targetUserId);
        bool amountParsed = decimal.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal amount)
                           || decimal.TryParse(parts[2], out amount);

        if (!userIdParsed || !amountParsed)
        {
            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _localizer["SetBalance_InvalidFormat"],
                cancellationToken: cancellationToken);
        }

        var dbUser = _telegramUserInfoRepository != null ? await _telegramUserInfoRepository.Get(u => u.Id == targetUserId) : null;
        if (dbUser == null)
        {
            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _localizer["SetBalance_UserNotFound", targetUserId],
                cancellationToken: cancellationToken);
        }

        var oldBalance = dbUser.Balance;
        dbUser.Balance = amount;
        dbUser.BalanceModifiedAt = DateTime.UtcNow;
        _telegramUserInfoRepository.Update(dbUser);

        if (_balanceHistoryRepository != null)
        {
            var historyRecord = new BalanceHistory
            {
                UserId = targetUserId,
                Amount = amount - oldBalance, // delta
                ModifiedById = message.From?.Id,
                CreatedAt = DateTime.UtcNow,
                Source = "Admin"
            };
            _balanceHistoryRepository.Add(historyRecord);
            await _balanceHistoryRepository.SaveChanges();
        }

        await _telegramUserInfoRepository.SaveChanges();

        return await botClient.SendMessage(
            chatId: message.Chat.Id,
            text: _localizer["SetBalance_Success", 
                dbUser.FirstName + (string.IsNullOrEmpty(dbUser.LastName) ? "" : " " + dbUser.LastName), 
                dbUser.Id, 
                dbUser.Balance.ToString("0.######")],
            cancellationToken: cancellationToken);
    }

    private async Task<TgMessage> RefreshModelsCache(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await ActionWithShowTypeng(message.Chat.Id, cancellationToken, Task.Run(async () =>
            {
                await _chatService.RefreshAvailibleModels();
                return true;
            }));

            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _localizer["RefreshModels_Success"],
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual refresh of models failed");
            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: _localizer["RefreshModels_Error", ex.Message],
                cancellationToken: cancellationToken);
        }
    }

    private string FormatTimeAgo(DateTime? dateTime)
    {
        if (!dateTime.HasValue) return _localizer["TimeAgo_Never"];
        
        var diff = DateTime.UtcNow - dateTime.Value;
        if (diff.TotalSeconds < 60) return _localizer["TimeAgo_JustNow"];
        if (diff.TotalMinutes < 60) return _localizer["TimeAgo_Minutes", (int)diff.TotalMinutes];
        if (diff.TotalHours < 24) return _localizer["TimeAgo_Hours", (int)diff.TotalHours];
        return _localizer["TimeAgo_Days", (int)diff.TotalDays];
    }

    private async Task TrySaveMessageInfoAsync(Chat chat, TgUser? user)
    {
        if (chat == null) return;

        bool changed = false;
        TelegramChatInfo? dbChat = await (_telegramChatInfoRepository?.Get(p => p.Id == chat.Id) ?? Task.FromResult<TelegramChatInfo?>(null));
        if (dbChat == null)
        {
            dbChat = new TelegramChatInfo
            {
                Id = chat.Id,
                ChatType = chat.Type.ToString(),
                Title = chat.Title,
                Username = chat.Username
            };
            _telegramChatInfoRepository?.Add(dbChat);
            changed = true;
        }
        else
        {
            // Keep chat info updated
            if (dbChat.Title != chat.Title || dbChat.Username != chat.Username || 
                dbChat.ChatType != chat.Type.ToString())
            {
                dbChat.Title = chat.Title;
                dbChat.Username = chat.Username;
                dbChat.ChatType = chat.Type.ToString();
                _telegramChatInfoRepository?.Update(dbChat);
                changed = true;
            }
        }

        if (user != null)
        {
            TelegramUserInfo? dbUser = await (_telegramUserInfoRepository?.Get(p => p.Id == user.Id) ?? Task.FromResult<TelegramUserInfo?>(null));
            if (dbUser == null)
            {
                dbUser = new TelegramUserInfo
                {
                    Id = user.Id,
                    FirstName = user.FirstName,
                    IsBot = user.IsBot,
                    IsPremium = user.IsPremium,
                    LastName = user.LastName,
                    Username = user.Username,
                    LanguageCode = user.LanguageCode,
                    Balance = _appSettings.TelegramBotConfiguration.InitialBalance,
                    BalanceModifiedAt = DateTime.UtcNow
                };
                _telegramUserInfoRepository?.Add(dbUser);
                changed = true;
            }
            else
            {
                if (dbUser.FirstName != user.FirstName || dbUser.LastName != user.LastName || 
                    dbUser.Username != user.Username || (string.IsNullOrEmpty(dbUser.LanguageCode) && !string.IsNullOrEmpty(user.LanguageCode)))
                {
                    dbUser.FirstName = user.FirstName;
                    dbUser.LastName = user.LastName;
                    dbUser.Username = user.Username;
                    if (string.IsNullOrEmpty(dbUser.LanguageCode))
                    {
                        dbUser.LanguageCode = user.LanguageCode;
                    }
                    _telegramUserInfoRepository?.Update(dbUser);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            // Both repositories share the same StoreContext, so saving via either (or the context itself) persists everything.
            if (_telegramUserInfoRepository != null)
            {
                await _telegramUserInfoRepository.SaveChanges();
            }
            else if (_telegramChatInfoRepository != null)
            {
                await _telegramChatInfoRepository.SaveChanges();
            }
        }
    }

    private bool IsMe(Message message)
    {
        if (message == null)
        {
            return false;
        }
        if (message.Voice != null)
        {
            _logger.LogInformation("It's voice message");
            return true;
        }
        if (message.Chat.Type == ChatType.Private)
        {
            _logger.LogInformation("It's private message");
            return true;
        }
        var messageText = message.Text ?? message.Caption;
        if (messageText != null && messageText.Contains($"@{_botInfo?.Username ?? ""}", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Message for this bot");
            return true;
        }
        if (message.ReplyToMessage?.From?.Id == _botInfo?.Id)
        {
            _logger.LogInformation("Reply to this bot message");
            return true;
        }
        return false;
    }

    // Process Inline Keyboard callback data
    private async Task BotOnCallbackQueryReceived(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        _userContext.UserId = callbackQuery.From.Id;
        _userContext.ChatId = callbackQuery.Message?.Chat.Id ?? 0;

        _logger.LogInformation("Received inline keyboard callback from: {CallbackQueryId}", callbackQuery.Id);
        
        // Ensure user and chat info are saved before processing callback
        if (callbackQuery.Message?.Chat != null)
        {
            await TrySaveMessageInfoAsync(callbackQuery.Message.Chat, callbackQuery.From);
        }

        var dataSet = callbackQuery?.Data?.Split(':');
        if (dataSet == null || dataSet.Length < 2)
        {
            return;
        }
        var originalMessageType = dataSet[0];
        var data = string.Join(":", dataSet.Skip(1));

        if (originalMessageType == "mcp_toggle")
        {
            var dbUser = _telegramUserInfoRepository != null ? await _telegramUserInfoRepository.Get(p => p.Id == callbackQuery.From.Id) : null;
            if (dbUser != null)
            {
                var disabledTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(dbUser.DisabledTools))
                {
                    var split = dbUser.DisabledTools.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var d in split)
                    {
                        disabledTools.Add(d.Trim());
                    }
                }

                bool isEnabled;
                if (disabledTools.Contains(data))
                {
                    disabledTools.Remove(data);
                    isEnabled = true;
                }
                else
                {
                    disabledTools.Add(data);
                    isEnabled = false;
                }

                dbUser.DisabledTools = string.Join(",", disabledTools);
                _telegramUserInfoRepository.Update(dbUser);
                await _telegramUserInfoRepository.SaveChanges();

                // Re-render keyboard
                var keyboard = await _messageProcessor.BuildMcpToolsKeyboardAsync(callbackQuery.From.Id);
                
                try
                {
                    await _botClient.EditMessageReplyMarkup(
                        chatId: callbackQuery.Message!.Chat.Id,
                        messageId: callbackQuery.Message.MessageId,
                        replyMarkup: keyboard,
                        cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update MCP tools keyboard markup");
                }

                var langCode = dbUser.LanguageCode ?? callbackQuery.From.LanguageCode ?? "en";
                string alertText = langCode.ToLowerInvariant() switch
                {
                    "ua" or "uk" => isEnabled ? $"✅ Інструмент [{data}] увімкнено!" : $"❌ Інструмент [{data}] вимкнено!",
                    _ => isEnabled ? $"✅ Tool [{data}] enabled!" : $"❌ Tool [{data}] disabled!"
                };

                await _botClient.AnswerCallbackQuery(
                    callbackQueryId: callbackQuery.Id,
                    text: alertText,
                    cancellationToken: cancellationToken);
            }
            return;
        }

        if (originalMessageType == "mcp_tgroup")
        {
            var dbUser = _telegramUserInfoRepository != null ? await _telegramUserInfoRepository.Get(p => p.Id == callbackQuery.From.Id) : null;
            if (dbUser != null)
            {
                var disabledTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(dbUser.DisabledTools))
                {
                    var split = dbUser.DisabledTools.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var d in split)
                    {
                        disabledTools.Add(d.Trim());
                    }
                }

                // 1. Get all tools belonging to this group
                var mcpTools = await _mcpServerManager.GetAllToolsAsync();
                
                // Helper to get group name — mirrors GetToolGroup in MessageProcessor
                Func<string, string> getGroup = (toolName) =>
                {
                    var sName = _mcpServerManager.GetServerNameForTool(toolName);
                    if (!string.IsNullOrEmpty(sName))
                    {
                        return sName
                            .Replace("native-", "", StringComparison.OrdinalIgnoreCase)
                            .ToUpperInvariant();
                    }
                    return "EXTERNAL";
                };

                var groupTools = mcpTools.Where(t => getGroup(t.Name).Equals(data, StringComparison.OrdinalIgnoreCase)).ToList();
                if (groupTools.Any())
                {
                    // Check if all tools in this group are currently enabled
                    bool allEnabled = groupTools.All(t => !disabledTools.Contains(t.Name));
                    
                    if (allEnabled)
                    {
                        // Disable all tools in the group
                        foreach (var t in groupTools)
                        {
                            disabledTools.Add(t.Name);
                        }
                    }
                    else
                    {
                        // Enable all tools in the group
                        foreach (var t in groupTools)
                        {
                            disabledTools.Remove(t.Name);
                        }
                    }

                    dbUser.DisabledTools = string.Join(",", disabledTools);
                    _telegramUserInfoRepository.Update(dbUser);
                    await _telegramUserInfoRepository.SaveChanges();

                    // Re-render keyboard
                    var keyboard = await _messageProcessor.BuildMcpToolsKeyboardAsync(callbackQuery.From.Id);
                    
                    try
                    {
                        await _botClient.EditMessageReplyMarkup(
                            chatId: callbackQuery.Message!.Chat.Id,
                            messageId: callbackQuery.Message.MessageId,
                            replyMarkup: keyboard,
                            cancellationToken: cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to update MCP tools keyboard markup");
                    }

                    // Dynamically localize alert message
                    string alertText = allEnabled
                        ? _localizer["McpTools_Group_DisabledAlert", data]
                        : _localizer["McpTools_Group_EnabledAlert", data];

                    await _botClient.AnswerCallbackQuery(
                        callbackQueryId: callbackQuery.Id,
                        text: alertText,
                        cancellationToken: cancellationToken);
                }
            }
            return;
        }

        if (originalMessageType == BotCommand.Model)
        {
            if (data.StartsWith("info:"))
            {
                var modelId = data.Substring("info:".Length);
                
                var dbUser = _telegramUserInfoRepository?.GetAll()?.FirstOrDefault(u => u.Id == callbackQuery.From.Id);
                var langCode = dbUser?.LanguageCode ?? callbackQuery.From.LanguageCode ?? "en";
                var lang = langCode.ToLowerInvariant();
                if (lang == "ua") lang = "uk";

                var description = await GetOrTranslateModelDescriptionAsync(modelId, lang, cancellationToken);
                
                string suffix = lang switch
                {
                    "uk" => "...\n\n(Повний опис надіслано в чат)",
                    _ => "...\n\n(Full description sent to chat)"
                };

                // Telegram AnswerCallbackQuery text parameter has a hard limit of 200 characters.
                // We truncate the description if it exceeds this limit to prevent MESSAGE_TOO_LONG errors.
                var alertText = description;
                if (!string.IsNullOrEmpty(alertText) && alertText.Length > 200)
                {
                    alertText = alertText.Substring(0, 200 - suffix.Length) + suffix;
                }

                await _botClient.AnswerCallbackQuery(
                    callbackQueryId: callbackQuery.Id,
                    text: alertText,
                    showAlert: true,
                    cancellationToken: cancellationToken);

                // Also send the beautiful full description as a message to the chat
                if (callbackQuery.Message?.Chat != null)
                {
                    string title = lang switch
                    {
                        "uk" => $"ℹ️ <b>Інформація про модель {modelId}</b>",
                        _ => $"ℹ️ <b>Model Info: {modelId}</b>"
                    };

                    var chatMessageText = $"{title}\n\n{description}";
                    await _botClient.SendMessage(
                        chatId: callbackQuery.Message.Chat.Id,
                        text: chatMessageText,
                        parseMode: ParseMode.Html,
                        cancellationToken: cancellationToken);
                }
                return;
            }

            var (isSuckes, errorMessage) = await _messageProcessor.SelectAIModel(data, callbackQuery.From.Id);
            if (!isSuckes)
            {
                var errorText = _localizer["ModelSelectionError", data, errorMessage ?? string.Empty];
                await _botClient.AnswerCallbackQuery(
                    callbackQueryId: callbackQuery.Id,
                    text: errorText,
                    cancellationToken: cancellationToken);

                await _botClient.SendMessage(
                    chatId: callbackQuery.Message!.Chat.Id,
                    text: errorText,
                    cancellationToken: cancellationToken);
                return;
            }
            await _botClient.AnswerCallbackQuery(
                callbackQueryId: callbackQuery.Id,
                text: _localizer["ModelSelected", data],
                cancellationToken: cancellationToken);

            await _botClient.SendMessage(
                chatId: callbackQuery.Message!.Chat.Id,
                text: _localizer["ModelSelected", data],
                cancellationToken: cancellationToken);
            return;
        }

        if (originalMessageType == "dbquery")
        {
            await HandleDbQueryCallback(callbackQuery, data, cancellationToken);
            return;
        }
        if (originalMessageType == BotCommand.Voice)
        {
            await HandleVoiceCallback(callbackQuery, data, cancellationToken);
            return;
        }
        if (originalMessageType == BotCommand.Schedule.Value || originalMessageType == BotCommand.Shedule.Value)
        {
            await HandleScheduleCallback(callbackQuery, data, cancellationToken);
            return;
        }
        if (originalMessageType == BotCommand.Provider)
        {
            if (Enum.TryParse<ChatStrategy>(data, out var strategy))
            {
                await _messageProcessor.SetPreferredProvider(callbackQuery.From.Id, strategy);
                
                string strategyName = strategy switch
                {
                    ChatStrategy.Auto => _localizer["AutoRotation"],
                    ChatStrategy.OpenAI => AiProvider.OpenAI.DisplayName,
                    ChatStrategy.Gemini => AiProvider.Gemini.DisplayName,
                    ChatStrategy.DeepSeek => AiProvider.DeepSeek.DisplayName,
                    ChatStrategy.Grok => AiProvider.Grok.DisplayName,
                    _ => strategy.ToString()
                };

                string responseText = _localizer["ProviderSelected", strategyName];

                await _botClient.AnswerCallbackQuery(
                    callbackQueryId: callbackQuery.Id,
                    text: responseText,
                    cancellationToken: cancellationToken);

                await _botClient.SendMessage(
                    chatId: callbackQuery.Message!.Chat.Id,
                    text: responseText,
                    cancellationToken: cancellationToken);

                // Immediately show model selection as if /model was called
                await SendModelInlineKeyboard(_botClient, callbackQuery.Message.Chat.Id, callbackQuery.From.Id, cancellationToken);
            }
            return;
        }
        if (originalMessageType == BotCommand.Lang)
        {
            if (data == "cancel")
            {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                await _botClient.DeleteMessage(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, cancellationToken);
                return;
            }

            if (data == "prompt")
            {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                await _botClient.SendMessage(
                    chatId: callbackQuery.Message!.Chat.Id,
                    text: _localizer["EnterLanguagePrompt"],
                    replyMarkup: new ForceReplyMarkup { Selective = true },
                    cancellationToken: cancellationToken);
                return;
            }

            string langCode = data;
            if (data.StartsWith("confirm:"))
            {
                langCode = data.Replace("confirm:", string.Empty);
                // Here we could add a billing record for "Language Setup" if we wanted, 
                // but for now we'll just set the language and the localizer will bill for subsequent translations.
            }

            var dbUser = _telegramUserInfoRepository != null ? await _telegramUserInfoRepository.Get(p => p.Id == callbackQuery.From.Id) : null;
            if (dbUser != null)
            {
                dbUser.LanguageCode = langCode;
                _telegramUserInfoRepository.Update(dbUser);
                await _telegramUserInfoRepository.SaveChanges();
            }

            // Update current culture for the immediate response
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(langCode);
            }
            catch
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(LanguageCode.English);
            }

            string nativeName = langCode;

            var oldCulture = CultureInfo.CurrentUICulture;
            string englishText;
            try {
                CultureInfo.CurrentUICulture = new CultureInfo(LanguageCode.English);
                englishText = _localizer["LanguageChanged", nativeName];
            } finally {
                CultureInfo.CurrentUICulture = oldCulture;
            }
            
            var translatedText = _localizer["LanguageChanged", nativeName];
            var confirmation = (langCode == LanguageCode.English) ? translatedText : $"{englishText} {translatedText}";

            await _botClient.AnswerCallbackQuery(
                callbackQueryId: callbackQuery.Id,
                text: confirmation,
                cancellationToken: cancellationToken);

            await _botClient.SendMessage(
                chatId: callbackQuery.Message!.Chat.Id,
                text: confirmation,
                cancellationToken: cancellationToken);
            
            if (data.StartsWith("confirm:"))
            {
                await _botClient.DeleteMessage(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, cancellationToken);
            }
            return;
        }
        if (originalMessageType == BotCommand.Prompt)
        {
            if (data == "cancel")
            {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                await _botClient.DeleteMessage(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, cancellationToken);
                return;
            }

            if (data == "reset")
            {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                var dbUser = _telegramUserInfoRepository != null ? await _telegramUserInfoRepository.Get(p => p.Id == callbackQuery.From.Id) : null;
                if (dbUser != null)
                {
                    dbUser.SystemPrompt = null;
                    _telegramUserInfoRepository.Update(dbUser);
                    await _telegramUserInfoRepository.SaveChanges();
                }
                
                await _botClient.DeleteMessage(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, cancellationToken);
                await _botClient.SendMessage(callbackQuery.Message.Chat.Id, _localizer["Prompt_ResetSuccess"], cancellationToken: cancellationToken);
                return;
            }

            if (data == "prompt")
            {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                await _botClient.DeleteMessage(callbackQuery.Message.Chat.Id, callbackQuery.Message.MessageId, cancellationToken);
                await _botClient.SendMessage(
                    chatId: callbackQuery.Message!.Chat.Id,
                    text: _localizer["Prompt_EnterNew"],
                    replyMarkup: new ForceReplyMarkup { Selective = true },
                    cancellationToken: cancellationToken);
                return;
            }
            return;
        }
        if (originalMessageType == BotCommand.Billing)
        {
            var strData = data.Split('-');
            if (strData.Length < 2) return;

            var startData = new DateTime(int.Parse(strData[0]), int.Parse(strData[1]), 1, 0, 0, 0, DateTimeKind.Utc);
            var endData = startData.AddMonths(1);

            var isAdmin = callbackQuery.From.Id == _appSettings.TelegramBotConfiguration.OwnerId;

            // Grouping costs by user, chat and model for the selected period
            var billingByUsers = _aiBilingItemRepository?.GetAll()
                .AsNoTracking()
                .Include(p => p.TelegramUserInfo)
                .Include(p => p.TelegramChatInfo)
                .Where(p => p.CreationDate >= startData && p.CreationDate < endData)
                .Where(p => isAdmin || p.TelegramUserInfoId == callbackQuery.From.Id) 
                .GroupBy(p => new { p.TelegramUserInfoId, p.TelegramChatInfoId, p.ModelName })
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine(_localizer["BillingHeader", startData.ToString("MMMM yyyy")]);
            sb.AppendLine();

            if (billingByUsers == null || !billingByUsers.Any())
            {
                sb.AppendLine(_localizer["Billing_NoDataForPeriod"]);
            }
            else
            {
                foreach (var b in billingByUsers)
                {
                    var f = b.First();
                    var user = f.TelegramUserInfo;
                    var chat = f.TelegramChatInfo;
                    var model = f.ModelName;

                    var totalTokens = b.Sum(p => p.TotalTokens);
                    var promptTokens = b.Sum(p => p.PromptTokens);
                    var completionTokens = b.Sum(p => p.CompletionTokens);
                    var cost = b.Sum(p => p.Cost);
                    
                    var chatTitle = chat?.Title ?? chat?.Username ?? "Private";
                    sb.AppendLine($"👤 {user?.FirstName} {user?.LastName}");
                    sb.AppendLine($"💬 {chatTitle} | 🤖 {model}");
                    sb.AppendLine($"📊 {promptTokens} / {completionTokens} / {totalTokens} tokens");
                    sb.AppendLine($"💰 ${cost:F4}");
                    sb.AppendLine("────────────────────");
                }
            }

            await _botClient.AnswerCallbackQuery(
                callbackQueryId: callbackQuery.Id,
                text: _localizer["Billing"],
                cancellationToken: cancellationToken);

            await _botClient.SendMessage(
                chatId: callbackQuery.Message!.Chat.Id,
                text: sb.ToString(),
                cancellationToken: cancellationToken);
        }
        
        if (originalMessageType == "Reaction")
        {
            if (dataSet.Length < 3) return;
            var reactionTypeStr = dataSet[1];
            var targetMessageId = long.Parse(dataSet[2]);

            if (Enum.TryParse<MessageReactionType>(reactionTypeStr, out var reactionType))
            {
                await _reactionService.ToggleReaction(callbackQuery.Message!.Chat.Id, targetMessageId, callbackQuery.From.Id, reactionType);
                
                // Update keyboard
                var newMarkup = await _messageProcessor.GetReactionMarkup(callbackQuery.Message.Chat.Id, targetMessageId, callbackQuery.From.Id);
                
                try
                {
                    await _botClient.EditMessageReplyMarkup(
                        chatId: callbackQuery.Message.Chat.Id,
                        messageId: (int)targetMessageId,
                        replyMarkup: newMarkup,
                        cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update reaction keyboard for message {MessageId}", targetMessageId);
                }

                await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            }
        }
    }

    #region Inline Mode

    private async Task BotOnInlineQueryReceived(InlineQuery inlineQuery, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received inline query from: {InlineQueryFromId}", inlineQuery.From.Id);

        InlineQueryResult[] results = {
            // displayed result
            new InlineQueryResultArticle(
                id: "1",
                title: "TgBots",
                inputMessageContent: new InputTextMessageContent("hello"))
        };

        await _botClient.AnswerInlineQuery(
            inlineQueryId: inlineQuery.Id,
            results: results,
            cacheTime: 0,
            isPersonal: true,
            cancellationToken: cancellationToken);
    }

    private async Task BotOnChosenInlineResultReceived(ChosenInlineResult chosenInlineResult, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received inline result: {ChosenInlineResultId}", chosenInlineResult.ResultId);

        await _botClient.SendMessage(
            chatId: chosenInlineResult.From.Id,
            text: _localizer["YouChoseResult", chosenInlineResult.ResultId],
            cancellationToken: cancellationToken);
    }

    #endregion

#pragma warning disable IDE0060 // Remove unused parameter
#pragma warning disable RCS1163 // Unused parameter.
    private Task UnknownUpdateHandlerAsync(Update update, CancellationToken cancellationToken)
#pragma warning restore RCS1163 // Unused parameter.
#pragma warning restore IDE0060 // Remove unused parameter
    {
        _logger.LogInformation("Unknown update type: {UpdateType}", update.Type);
        return Task.CompletedTask;
    }

    public async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
    {
        var ErrorMessage = exception switch
        {
            ApiRequestException apiRequestException => $"{_localizer["TelegramApiError"]}\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        _logger.LogInformation("HandleError: {ErrorMessage}", ErrorMessage);

        // Cooldown in case of network connection error
        if (exception is RequestException)
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }

    private async Task<T> ActionWithShowTypeng<T>(ChatId chatId, CancellationToken cancellationToken, Task<T> action)

    {
        try
        {
            // Send initial typing action
            await _botClient.SendChatAction(chatId: chatId, action: ChatAction.Typing, cancellationToken: cancellationToken);

            // While the main action is running, periodically refresh the typing indicator
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!action.IsCompleted && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(4000, cancellationToken);
                        if (!action.IsCompleted)
                        {
                            await _botClient.SendChatAction(chatId: chatId, action: ChatAction.Typing, cancellationToken: cancellationToken);
                        }
                    }
                }
                catch { /* Ignore typing errors */ }
            }, cancellationToken);

            return await action;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ActionWithShowTypeng for chat {ChatId}", chatId);
            
            string errorMessage = ex is AggregateException agg ? agg.Flatten().Message : ex.Message;
            await _botClient.SendMessage(
                chatId: chatId,
                text: _localizer["ErrorOccurred", errorMessage],
                cancellationToken: cancellationToken);
                
            throw; 
        }
    }

    private async Task<ServiceLayer.Constans.BotCommandScope> GetUserScopesAsync(TgUser? user, Chat chat)
    {
        var scope = ServiceLayer.Constans.BotCommandScope.Default;

        if (user == null) return scope;

        // Check private/group
        if (chat.Type == ChatType.Private)
            scope |= ServiceLayer.Constans.BotCommandScope.AllPrivateChats;
        else
            scope |= ServiceLayer.Constans.BotCommandScope.AllGroupChats;

        // Check owner
        if (_appSettings.TelegramBotConfiguration.OwnerId.HasValue && user.Id == _appSettings.TelegramBotConfiguration.OwnerId.Value)
        {
            scope |= ServiceLayer.Constans.BotCommandScope.Owner;
        }

        // Check group admins
        if (chat.Type != ChatType.Private)
        {
            try
            {
                var member = await _botClient.GetChatMember(chat.Id, user.Id);
                if (member.Status == ChatMemberStatus.Administrator || member.Status == ChatMemberStatus.Creator)
                {
                    scope |= ServiceLayer.Constans.BotCommandScope.AllChatAdmins;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get chat member status for user {UserId} in chat {ChatId}", user.Id, chat.Id);
            }
        }

        return scope;
    }

    private async Task<TgMessage> SendModelInlineKeyboard(ITelegramBotClient botClient, long chatId, long userId, CancellationToken cancellationToken)
    {
        await botClient.SendChatAction(
            chatId: chatId,
            action: ChatAction.Typing,
            cancellationToken: cancellationToken);

        // Fetch release dates from OpenRouter in background (does not block keyboard generation)
        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshModelReleaseDatesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to refresh release dates in background: {Message}", ex.Message);
            }
        });

        var currentModel = await _chatService.GetSelectedModel(userId);
        var models = await _chatService.GetAvailibleModels(userId);
        
        // Sort models by date, newest first. Models without a valid date will naturally go to the bottom.
        var sortedModels = models.OrderByDescending(m => {
            var modelDate = m.CreatedAt;
            if (modelDate.Year <= 2020 && _modelReleaseDates.TryGetValue(NormalizeModelId(m.Id), out var cachedDate))
            {
                modelDate = cachedDate;
            }
            return modelDate;
        }).ToList();

        var rows = new List<List<InlineKeyboardButton>>();
        foreach (var m in sortedModels)
        {
            var modelDate = m.CreatedAt;
            if (modelDate.Year <= 2020 && _modelReleaseDates.TryGetValue(NormalizeModelId(m.Id), out var cachedDate))
            {
                modelDate = cachedDate;
            }

            var hasValidDate = modelDate.Year > 2020;
            var dateStr = hasValidDate ? $" ({modelDate.ToShortDateString()})" : "";
            var label = $"{m.Id}{dateStr}";
            if (aiModelsCosts.TryGetValue(m.Id, out var costs))
            {
                label = hasValidDate 
                    ? $"{m.Id} ({modelDate.ToShortDateString()}. {costs})" 
                    : $"{m.Id} ({costs})";
            }

            if (m.Id == currentModel)
            {
                label = $"✅ {label}";
            }

            var selectButton = InlineKeyboardButton.WithCallbackData(label, $"{BotCommand.Model}:{m.Id}");
            var infoButton = InlineKeyboardButton.WithCallbackData("ℹ️", $"{BotCommand.Model}:info:{m.Id}");
            rows.Add(new List<InlineKeyboardButton> { selectButton, infoButton });
        }
        var replyMarkup = new InlineKeyboardMarkup(rows);

        var result = await botClient.SendMessage(
            chatId: chatId,
            text: _localizer["SelectModel"],
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
        return result;
    }

    private async Task<TgMessage> SendProviderInlineKeyboard(ITelegramBotClient botClient, long chatId, long userId, CancellationToken cancellationToken)
    {
        await botClient.SendChatAction(
            chatId: chatId,
            action: ChatAction.Typing,
            cancellationToken: cancellationToken);

        var dbUser = await _telegramUserInfoRepository.Get(p => p.Id == userId);
        var currentProvider = ((int)(dbUser?.PreferredProvider ?? ChatStrategy.Auto)).ToString();
        var strategies = Enum.GetValues(typeof(ChatStrategy)).Cast<ChatStrategy>();

        var replyMarkup = TelegramKeyboardHelper.CreateSelectionKeyboard(
            strategies,
            strategy => strategy switch
            {
                ChatStrategy.Auto => _localizer["AutoRotation"],
                ChatStrategy.OpenAI => AiProvider.OpenAI.DisplayName,
                ChatStrategy.Gemini => AiProvider.Gemini.DisplayName,
                ChatStrategy.DeepSeek => AiProvider.DeepSeek.DisplayName,
                ChatStrategy.Grok => AiProvider.Grok.DisplayName,
                _ => strategy.ToString()
            },
            strategy => $"{BotCommand.Provider}:{(int)strategy}",
            currentProvider,
            columns: 1);

        return await botClient.SendMessage(
            chatId: chatId,
            text: _localizer["SelectProvider"] + "\n\n" + _localizer["SelectProviderHelp"],
            replyMarkup: replyMarkup,
            parseMode: ParseMode.Markdown,
            cancellationToken: cancellationToken);
    }

    internal async Task<bool> CheckBalanceAndReplenish(long userId, long chatId, CancellationToken cancellationToken)
    {
        // Public project: Billing operates in telemetry-only mode. Access is always granted.
        return true;
    }

    private async Task HandleVoiceCallback(CallbackQuery callbackQuery, string voiceId, CancellationToken cancellationToken)
        {
            var user = await _telegramUserInfoRepository.Get(p => p.Id == callbackQuery.From.Id);
            if (user != null)
            {
                user.VoiceId = voiceId;
                _telegramUserInfoRepository.Update(user);
                await _telegramUserInfoRepository.SaveChanges();
                
                var alias = _voiceAliases.GetValueOrDefault(voiceId, voiceId);
                await _botClient.AnswerCallbackQuery(
                    callbackQueryId: callbackQuery.Id,
                    text: _localizer["VoiceSelected", alias],
                    cancellationToken: cancellationToken);
                    
                // Update the message to move the checkmark
                var voices = _appSettings.TelegramBotConfiguration.AvailableVoices;
                var replyMarkup = TelegramKeyboardHelper.CreateSelectionKeyboard(
                    voices,
                    v => _voiceAliases.GetValueOrDefault(v, v),
                    v => $"{BotCommand.Voice}:{v}",
                    voiceId,
                    columns: 1);
                
                try 
                {
                    await _botClient.EditMessageReplyMarkup(
                        chatId: callbackQuery.Message!.Chat.Id,
                        messageId: callbackQuery.Message.MessageId,
                        replyMarkup: replyMarkup,
                        cancellationToken: cancellationToken);
                }
                catch { /* ignore edit errors if same markup */ }

                // Send preview
                await _botClient.SendMessage(
                    chatId: callbackQuery.Message!.Chat.Id,
                    text: _localizer["VoiceSelectedPreview", alias],
                    cancellationToken: cancellationToken);
                    
                await _botClient.SendChatAction(callbackQuery.Message.Chat.Id, ChatAction.RecordVoice, cancellationToken: cancellationToken);
                
                var previewText = _localizer["VoicePreviewText"];
                using (var voiceStream = await _chatService.TextToSpeech(callbackQuery.Message.Chat.Id, callbackQuery.From.Id, previewText, voice: voiceId))
                {
                    await _botClient.SendVoice(
                        chatId: callbackQuery.Message.Chat.Id,
                        voice: new InputFileStream(voiceStream, "preview.ogg"),
                        cancellationToken: cancellationToken);
                }
            }
        }

        private async Task<bool> IsUserAllowedForSchedulerAsync(long userId)
        {
            var isOwner = _appSettings.TelegramBotConfiguration.OwnerId.HasValue && userId == _appSettings.TelegramBotConfiguration.OwnerId.Value;

            using var scope = _scopeFactory.CreateScope();
            var ruleRepo = scope.ServiceProvider.GetRequiredService<IRepository<SchedulerAccessRule>>();

            // Check if user is explicitly blacklisted (blacklist always wins)
            var blacklistRule = await ruleRepo.Get(r => r.UserId == userId && !r.IsAllowed);
            if (blacklistRule != null)
            {
                return false;
            }

            if (isOwner) return true;

            // Check if there is a whitelist active (any records with IsAllowed == true)
            var whitelistRules = await ruleRepo.GetAll().Where(r => r.IsAllowed).ToListAsync();
            if (whitelistRules.Any())
            {
                // If whitelist is active, user must be explicitly allowed
                return whitelistRules.Any(r => r.UserId == userId);
            }

            // If whitelist is empty, everyone except blacklisted is allowed
            return true;
        }

        private async Task<TgMessage> ProcessScheduleCommand(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            await botClient.SendChatAction(message.Chat.Id, ChatAction.Typing, cancellationToken: cancellationToken);

            using var scope = _scopeFactory.CreateScope();
            var newsletterRepo = scope.ServiceProvider.GetRequiredService<IRepository<ScheduledNewsletter>>();
            var newsletters = await newsletterRepo.GetAll().Where(n => n.UserId == message.From!.Id && n.IsActive).ToListAsync(cancellationToken);

            var sb = new StringBuilder();
            sb.AppendLine("⏰ **Scheduled Newsletter Management**");
            sb.AppendLine();

            if (!newsletters.Any())
            {
                sb.AppendLine("You don't have any active newsletters yet. Click the button below to create one!");
            }
            else
            {
                sb.AppendLine("Your active newsletters:");
                foreach (var n in newsletters)
                {
                    var cronDesc = GetFriendlyCronDescription(n.CronExpression);
                    sb.AppendLine($"• **Newsletter #{n.Id}**");
                    sb.AppendLine($"  Prompt: *\"{n.Prompt}\"*");
                    sb.AppendLine($"  Schedule: {cronDesc}");
                    sb.AppendLine();
                }
            }

            var buttons = new List<InlineKeyboardButton[]>();
            
            // Newsletters individual management buttons
            foreach (var n in newsletters)
            {
                buttons.Add(new[]
                {
                    InlineKeyboardButton.WithCallbackData($"⚡ #{n.Id}", $"{BotCommand.Schedule.Value}:trigger:{n.Id}"),
                    InlineKeyboardButton.WithCallbackData($"🗑️ Delete #{n.Id}", $"{BotCommand.Schedule.Value}:delete:{n.Id}")
                });
            }

            // Add "Create new" button
            buttons.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ Create new newsletter", $"{BotCommand.Schedule.Value}:create") });

            var keyboard = new InlineKeyboardMarkup(buttons);

            return await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: sb.ToString(),
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }

        private static string GetFriendlyCronDescription(string cron)
        {
            return cron switch
            {
                "0 9 * * *" => "🌅 Every morning (9:00)",
                "0 21 * * *" => "🏙️ Every evening (21:00)",
                "0 10 * * 1" => "📅 Once a week (Mon, 10:00)",
                "0 */2 * * *" => "⏳ Every 2 hours",
                _ => $"Cron: `{cron}`"
            };
        }

        private async Task ProcessSchedulePromptReply(ITelegramBotClient botClient, TgMessage message, CancellationToken cancellationToken)
        {
            var prompt = message.Text?.Trim();
            if (string.IsNullOrEmpty(prompt))
            {
                await botClient.SendMessage(message.Chat.Id, "Prompt text cannot be empty. Please try invoking /schedule again.", cancellationToken: cancellationToken);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var newsletterRepo = scope.ServiceProvider.GetRequiredService<IRepository<ScheduledNewsletter>>();

            // Create pending newsletter
            var pendingNewsletter = new ScheduledNewsletter
            {
                UserId = message.From!.Id,
                ChatId = message.Chat.Id,
                Prompt = prompt,
                CronExpression = "PENDING",
                IsActive = false
            };

            newsletterRepo.Add(pendingNewsletter);
            await newsletterRepo.SaveChanges();

            // Send frequency selection keyboard
            var buttons = new[]
            {
                new[] { InlineKeyboardButton.WithCallbackData("🌅 Every morning (9:00)", $"{BotCommand.Schedule.Value}:save:{pendingNewsletter.Id}:daily_morning") },
                new[] { InlineKeyboardButton.WithCallbackData("🏙️ Every evening (21:00)", $"{BotCommand.Schedule.Value}:save:{pendingNewsletter.Id}:daily_evening") },
                new[] { InlineKeyboardButton.WithCallbackData("📅 Once a week (Mon, 10:00)", $"{BotCommand.Schedule.Value}:save:{pendingNewsletter.Id}:weekly") },
                new[] { InlineKeyboardButton.WithCallbackData("⏳ Every 2 hours", $"{BotCommand.Schedule.Value}:save:{pendingNewsletter.Id}:every_2h") },
                new[] { InlineKeyboardButton.WithCallbackData("❌ Cancel", $"{BotCommand.Schedule.Value}:cancel_create:{pendingNewsletter.Id}") }
            };

            var keyboard = new InlineKeyboardMarkup(buttons);

            await botClient.SendMessage(
                chatId: message.Chat.Id,
                text: $"Excellent! Newsletter prompt: *\"{prompt}\"*\nNow select how often to send it:",
                parseMode: ParseMode.Markdown,
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
        }

        private async Task HandleScheduleCallback(CallbackQuery callbackQuery, string data, CancellationToken cancellationToken)
        {
            var parts = data.Split(':');
            var action = parts[0];

            using var scope = _scopeFactory.CreateScope();
            var newsletterRepo = scope.ServiceProvider.GetRequiredService<IRepository<ScheduledNewsletter>>();
            var schedulerService = _serviceProvider.GetRequiredService<NewsletterSchedulerService>();
            var botClient = _botClient;

            if (action == "create")
            {
                await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                
                // Ask for prompt using ForceReply
                var forceReply = new ForceReplyMarkup { Selective = true };
                await botClient.SendMessage(
                    chatId: callbackQuery.Message!.Chat.Id,
                    text: "Please enter a topic or prompt for your newsletter (in reply to this message):\nExample: *Send me news about C# and .NET*",
                    parseMode: ParseMode.Markdown,
                    replyMarkup: forceReply,
                    cancellationToken: cancellationToken);
                return;
            }

            if (action == "save" && parts.Length >= 3)
            {
                var id = int.Parse(parts[1]);
                var cronType = parts[2];

                var newsletter = await newsletterRepo.Get(n => n.Id == id);
                if (newsletter != null)
                {
                    var cron = cronType switch
                    {
                        "daily_morning" => "0 9 * * *",
                        "daily_evening" => "0 21 * * *",
                        "weekly" => "0 10 * * 1",
                        "every_2h" => "0 */2 * * *",
                        _ => "0 9 * * *"
                    };

                    newsletter.CronExpression = cron;
                    newsletter.IsActive = true;
                    newsletterRepo.Update(newsletter);
                    await newsletterRepo.SaveChanges();

                    // Register with Hangfire
                    schedulerService.ScheduleJob(newsletter);

                    await botClient.AnswerCallbackQuery(callbackQuery.Id, "✅ Newsletter created successfully!", cancellationToken: cancellationToken);
                    await botClient.DeleteMessage(callbackQuery.Message!.Chat.Id, callbackQuery.Message.MessageId, cancellationToken);
                    
                    // Show updated schedule list
                    var dummyMsg = new TgMessage
                    {
                        Chat = callbackQuery.Message.Chat,
                        From = callbackQuery.From
                    };
                    await ProcessScheduleCommand(botClient, dummyMsg, cancellationToken);
                }
                return;
            }

            if (action == "cancel_create" && parts.Length >= 2)
            {
                var id = int.Parse(parts[1]);
                var newsletter = await newsletterRepo.Get(n => n.Id == id);
                if (newsletter != null)
                {
                    newsletterRepo.Delete(newsletter);
                    await newsletterRepo.SaveChanges();
                }

                await botClient.AnswerCallbackQuery(callbackQuery.Id, "❌ Creation cancelled.", cancellationToken: cancellationToken);
                await botClient.DeleteMessage(callbackQuery.Message!.Chat.Id, callbackQuery.Message.MessageId, cancellationToken);
                return;
            }

            if (action == "trigger" && parts.Length >= 2)
            {
                var id = int.Parse(parts[1]);
                schedulerService.TriggerJobImmediately(id);
                await botClient.AnswerCallbackQuery(callbackQuery.Id, "⚡ Run request sent to Hangfire!", cancellationToken: cancellationToken);
                return;
            }

            if (action == "delete" && parts.Length >= 2)
            {
                var id = int.Parse(parts[1]);
                var newsletter = await newsletterRepo.Get(n => n.Id == id);
                if (newsletter != null)
                {
                    // Remove from Hangfire
                    schedulerService.UnscheduleJob(id);

                    // Delete from DB
                    newsletterRepo.Delete(newsletter);
                    await newsletterRepo.SaveChanges();

                    await botClient.AnswerCallbackQuery(callbackQuery.Id, "🗑️ Newsletter deleted successfully!", cancellationToken: cancellationToken);
                    await botClient.DeleteMessage(callbackQuery.Message!.Chat.Id, callbackQuery.Message.MessageId, cancellationToken);

                    // Show updated list
                    var dummyMsg = new TgMessage
                    {
                        Chat = callbackQuery.Message.Chat,
                        From = callbackQuery.From
                    };
                    await ProcessScheduleCommand(botClient, dummyMsg, cancellationToken);
                }
                return;
            }
        }

        private async Task HandleDbQueryCallback(CallbackQuery callbackQuery, string data, CancellationToken cancellationToken)
        {
            var user = callbackQuery.From;
            var chat = callbackQuery.Message?.Chat;
            if (chat == null) return;

            var parts = data.Split(':');
            if (parts.Length < 3 || parts[0] != "page") return;

            if (!int.TryParse(parts[1], out int sessionId) || !int.TryParse(parts[2], out int pageNum))
            {
                return;
            }

            _logger.LogInformation("Processing DbQuery callback for user {UserId}, session {SessionId}, page {PageNum}", user.Id, sessionId, pageNum);

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<StoreContext>();

            var session = await dbContext.DbQuerySessions.FirstOrDefaultAsync(s => s.Id == sessionId);
            if (session == null)
            {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, "Query session expired or not found. Please execute the query again.", cancellationToken: cancellationToken);
                return;
            }

            if (session.AdminUserId != user.Id)
            {
                await _botClient.AnswerCallbackQuery(callbackQuery.Id, "You do not have permissions to view this query session.", cancellationToken: cancellationToken);
                return;
            }

            await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
            await _botClient.SendChatAction(chat.Id, ChatAction.Typing, cancellationToken: cancellationToken);

            var isOwner = user.Id == _appSettings.TelegramBotConfiguration.OwnerId;
            var allowedChats = new List<long>();
            if (!isOwner)
            {
                var knownChats = await dbContext.TelegramChatInfos.AsNoTracking().Select(c => c.Id).ToListAsync();
                foreach (var cId in knownChats)
                {
                    try
                    {
                        var member = await _botClient.GetChatMember(cId, user.Id);
                        if (member.Status == ChatMemberStatus.Creator || member.Status == ChatMemberStatus.Administrator)
                        {
                            allowedChats.Add(cId);
                        }
                    }
                    catch { /* Ignore kicked chats */ }
                }
            }

            var secureSql = session.SqlQuery;
            // Automatically double-quote unquoted known tables with correct casing (Postgres case-sensitivity)
            secureSql = ServiceLayer.Services.Mcp.DatabaseQueryMcpTools.EnsureTableNamesQuoted(secureSql, dbContext);

            // Remove existing LIMIT and OFFSET clauses to prevent syntax errors on double-appending
            var limitRegex = new System.Text.RegularExpressions.Regex(@"\s+LIMIT\s+\d+(\s+OFFSET\s+\d+)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            secureSql = limitRegex.Replace(secureSql, "");

            if (!isOwner)
            {
                var allowedChatsStr = allowedChats.Any() ? string.Join(",", allowedChats) : "0";
                var ctes = new List<string>();

                if (secureSql.Contains("AIBilingItem", StringComparison.OrdinalIgnoreCase))
                {
                    ctes.Add($@"""AIBilingItem"" AS (
                        SELECT * FROM ""AIBilingItem"" 
                        WHERE ""TelegramChatInfoId"" IN ({allowedChatsStr})
                    )");
                }
                if (secureSql.Contains("ScheduledNewsletters", StringComparison.OrdinalIgnoreCase))
                {
                    ctes.Add($@"""ScheduledNewsletters"" AS (
                        SELECT * FROM ""ScheduledNewsletters"" 
                        WHERE ""ChatId"" IN ({allowedChatsStr})
                    )");
                }
                if (secureSql.Contains("MessageReactions", StringComparison.OrdinalIgnoreCase))
                {
                    ctes.Add($@"""MessageReactions"" AS (
                        SELECT * FROM ""MessageReactions"" 
                        WHERE ""ChatId"" IN ({allowedChatsStr})
                    )");
                }
                if (secureSql.Contains("BalanceHistories", StringComparison.OrdinalIgnoreCase))
                {
                    ctes.Add($@"""BalanceHistories"" AS (
                        SELECT * FROM ""BalanceHistories"" 
                        WHERE ""UserId"" IN (
                            SELECT ""TelegramUserInfoId"" FROM ""AIBilingItem"" WHERE ""TelegramChatInfoId"" IN ({allowedChatsStr})
                        )
                        AND ""UserId"" NOT IN (
                            SELECT ""TelegramUserInfoId"" FROM ""AIBilingItem"" WHERE ""TelegramChatInfoId"" NOT IN ({allowedChatsStr})
                        )
                    )");
                }
                if (secureSql.Contains("TelegramUserInfos", StringComparison.OrdinalIgnoreCase))
                {
                    ctes.Add($@"""TelegramUserInfos"" AS (
                        SELECT * FROM ""TelegramUserInfos"" 
                        WHERE ""Id"" IN (
                            SELECT ""TelegramUserInfoId"" FROM ""AIBilingItem"" WHERE ""TelegramChatInfoId"" IN ({allowedChatsStr})
                        )
                        AND ""Id"" NOT IN (
                            SELECT ""TelegramUserInfoId"" FROM ""AIBilingItem"" WHERE ""TelegramChatInfoId"" NOT IN ({allowedChatsStr})
                        )
                    )");
                }

                if (ctes.Any())
                {
                    secureSql = "WITH " + string.Join(",\n", ctes) + "\n" + secureSql;
                }
            }

            var offset = (pageNum - 1) * session.PageSize;
            var finalSql = $"{secureSql} LIMIT {session.PageSize} OFFSET {offset}";

            _logger.LogInformation("Executing secure dynamic database query for pagination (page {PageNum}): {FinalSql}", pageNum, finalSql);

            try
            {
                var dataTable = new System.Data.DataTable();
                var connection = dbContext.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = finalSql;
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        dataTable.Load(reader);
                    }
                }

                var tableMarkdown = FormatDataTableToMarkdown(dataTable);
                if (tableMarkdown.Length > 4096)
                {
                    await _botClient.SendMessage(chat.Id, "⚠️ Query result on this page exceeds 4096 characters. Please narrow down your query.", cancellationToken: cancellationToken);
                    return;
                }

                var hasMore = dataTable.Rows.Count == session.PageSize;
                var keyboardButtons = new List<InlineKeyboardButton>();

                if (pageNum > 1)
                {
                    keyboardButtons.Add(InlineKeyboardButton.WithCallbackData("◀️ Back", $"dbquery:page:{sessionId}:{pageNum - 1}"));
                }
                
                keyboardButtons.Add(InlineKeyboardButton.WithCallbackData($"Page {pageNum}", $"dbquery:noop"));

                if (hasMore)
                {
                    keyboardButtons.Add(InlineKeyboardButton.WithCallbackData("Forward ▶️", $"dbquery:page:{sessionId}:{pageNum + 1}"));
                }

                var markup = new InlineKeyboardMarkup(new[] { keyboardButtons.ToArray() });

                await _botClient.EditMessageText(
                    chatId: chat.Id,
                    messageId: callbackQuery.Message.MessageId,
                    text: tableMarkdown,
                    replyMarkup: markup,
                    cancellationToken: cancellationToken);

                // Run combined session cleanup
                var oldThreshold = DateTime.UtcNow.AddHours(-1);
                var expiredSessions = await dbContext.DbQuerySessions
                    .Where(s => s.CreatedAt < oldThreshold)
                    .ToListAsync();
                if (expiredSessions.Any())
                {
                    dbContext.DbQuerySessions.RemoveRange(expiredSessions);
                    await dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to paginate db query session {SessionId}", sessionId);
                await _botClient.SendMessage(chat.Id, $"❌ Error executing query on this page: {ex.Message}", cancellationToken: cancellationToken);
            }
        }

        private string FormatDataTableToMarkdown(System.Data.DataTable table)
        {
            if (table == null || table.Rows.Count == 0)
            {
                return "*No records found on this page.*";
            }

            var sb = new StringBuilder();
            var columns = table.Columns.Cast<System.Data.DataColumn>().ToList();
            var widths = new Dictionary<string, int>();

            foreach (var col in columns)
            {
                var maxLen = col.ColumnName.Length;
                foreach (System.Data.DataRow row in table.Rows)
                {
                    var valStr = row[col]?.ToString() ?? "";
                    if (valStr.Length > maxLen) maxLen = valStr.Length;
                }
                widths[col.ColumnName] = Math.Min(30, maxLen);
            }

            sb.AppendLine("```text");
            var header = string.Join(" | ", columns.Select(c => c.ColumnName.PadRight(widths[c.ColumnName]).Substring(0, widths[c.ColumnName])));
            sb.AppendLine(header);

            var divider = string.Join("-|-", columns.Select(c => new string('-', widths[c.ColumnName])));
            sb.AppendLine(divider);

            foreach (System.Data.DataRow row in table.Rows)
            {
                var rowStr = string.Join(" | ", columns.Select(c => (row[c]?.ToString() ?? "").PadRight(widths[c.ColumnName]).Substring(0, widths[c.ColumnName])));
                sb.AppendLine(rowStr);
            }

            sb.AppendLine("```");
            return sb.ToString();
        }

        private async Task HandleContactMessageAsync(TgMessage message, CancellationToken cancellationToken)
        {
            var contact = message.Contact;
            var from = message.From;
            if (contact == null || from == null) return;

            var cache = _serviceProvider.GetService<IMemoryCache>();
            if (cache != null)
            {
                var cacheKey = $"UserContact:{from.Id}";
                var contactInfo = new
                {
                    phoneNumber = contact.PhoneNumber,
                    firstName = contact.FirstName,
                    lastName = contact.LastName,
                    sharedAt = DateTime.UtcNow
                };
                cache.Set(cacheKey, JsonSerializer.Serialize(contactInfo), new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromHours(24)
                });
                _logger.LogInformation("Saved shared contact for UserId={UserId} in memory cache", from.Id);
            }

            var text = _localizer["Contact_ReceivedRAMOnly"];
            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: text,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
        }

        private async Task HandleLocationMessageAsync(TgMessage message, CancellationToken cancellationToken)
        {
            var location = message.Location;
            var from = message.From;
            if (location == null || from == null) return;

            var cache = _serviceProvider.GetService<IMemoryCache>();
            if (cache != null)
            {
                var cacheKey = $"UserLocation:{from.Id}";
                var locationInfo = new
                {
                    latitude = location.Latitude,
                    longitude = location.Longitude,
                    sharedAt = DateTime.UtcNow
                };
                cache.Set(cacheKey, JsonSerializer.Serialize(locationInfo), new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromHours(24)
                });
                _logger.LogInformation("Saved shared location for UserId={UserId} in memory cache", from.Id);
            }

            var text = _localizer["Location_ReceivedRAMOnly"];
            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: text,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
        }
    }

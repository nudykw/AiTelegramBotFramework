using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using ServiceLayer.Constans;
using ServiceLayer.Services.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Images;
using OpenAI.Models;

using ServiceLayer.Models;
using ServiceLayer.Services.OpenAI.Models;
using ServiceLayer.Services.Mcp;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http;
using ServiceLayer.Utils;
using System.Diagnostics;

namespace ServiceLayer.Services.OpenAI;

internal class OpenAIService : BaseService, IChatService
{
    private class OpenAIModelCache
    {
        internal DateTime? LastUpdates { get; set; }
        internal required IReadOnlyList<Model> Models { get; set; }
    }
    private const int defTokens = 1000;
    internal static readonly ConcurrentDictionary<string, AIModelCost> _liveModelsCosts = new();
    internal static readonly ConcurrentDictionary<string, LiteLlmModelInfo> _liveModelsInfo = new();
    internal static DateTime _lastPriceUpdate = DateTime.MinValue;
    internal static readonly object _priceLock = new();

    private static Dictionary<string, AIModelCost> aiModelsCosts = new Dictionary<string, AIModelCost>()
    {
        {"gpt-4-1106-preview",  new AIModelCost(defTokens, 0.01M, 0.03M)},
        {"gpt-4-1106-vision-preview",  new AIModelCost(defTokens, 0.01M, 0.03M)},
        {"gpt-4",  new AIModelCost(defTokens, 0.03M, 0.06M)},
        {"gpt-4-32k",  new AIModelCost(defTokens, 0.06M, 0.12M)},
        {"gpt-3.5-turbo-1106",  new AIModelCost(defTokens, 0.001M, 0.002M)},
        {"gpt-3.5-turbo-instruct",  new AIModelCost(defTokens, 0.0015M, 0.002M)},
        {AiModel.Gpt4oMini,  new AIModelCost(defTokens, 0.00015M, 0.0006M)},
        {AiModel.Gpt4o,  new AIModelCost(defTokens, 0.005M, 0.015M)},
        {AiModel.DeepSeekChat, new AIModelCost(defTokens, 0.00007M, 0.0011M)},
        {AiModel.GrokBeta, new AIModelCost(defTokens, 0.005M, 0.015M)},

        {"Code interpreter",  new AIModelCost(1, 0.03M, 0.0M)},
        {"Retrieval",  new AIModelCost(1, 0.2M, 0.0M)},

        {Model.GPT3_5_Turbo,  new AIModelCost(defTokens, 0.003M, 0.006M)},
        {Model.Davinci,  new AIModelCost(defTokens, 0.012M, 0.012M)},
        {Model.Babbage,  new AIModelCost(defTokens, 0.0016M, 0.0016M)},

        {Model.DallE_3,  new AIModelCost(1, 0.0M, 0.04M)},
        {Model.DallE_2,  new AIModelCost(1, 0.0M, 0.02M)},
        {AiModel.GptImage2,  new AIModelCost(1, 0.0M, 0.04M)},
        {AiModel.GptImage1Mini, new AIModelCost(1, 0.0M, 0.02M)},

        {Model.Whisper1,  new AIModelCost(1, 0.0M, 0.006M)},
        {Model.TTS_1,  new AIModelCost(1, 0.0M, 0.015M)},
        {Model.TTS_1HD,  new AIModelCost(1, 0.0M, 0.03M)},
    };

    private readonly OpenAIClient _api;
    private readonly ChatProviderConfig _chatProviderConfiguration;
    private readonly IRepository<AIBilingItem>? _aiBilingItemRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDynamicLocalizer _localizer;
    private readonly IRepository<CachedAIModel>? _cachedAIModelRepository;
    private readonly McpServerManager _mcpServerManager;
    private OpenAIModelCache? openAiModelCache = null;

    public OpenAIService(IServiceProvider serviceProvider, ILogger<OpenAIService> logger,
        ChatProviderConfig chatProviderConfig, IHttpClientFactory httpClientFactory, 
        IDynamicLocalizer localizer, McpServerManager mcpServerManager, OpenAIClient? openAiClient = null)
        : base(serviceProvider, logger)
    {
        _chatProviderConfiguration = chatProviderConfig;
        _mcpServerManager = mcpServerManager;
        if (openAiClient != null)
        {
            _api = openAiClient;
        }
        else
        {
            var auth = new OpenAIAuthentication(_chatProviderConfiguration.ApiKey);
            var domain = _chatProviderConfiguration.BaseUrl ?? "";
            if (!string.IsNullOrEmpty(domain))
            {
                // Sanitize: strip protocol and path to get only the domain
                domain = domain.Replace("https://", "").Replace("http://", "").Split('/')[0];
            }
            
            var settings = string.IsNullOrEmpty(domain) 
                ? new OpenAISettings() 
                : new OpenAISettings(domain: domain);
            
            var httpClient = httpClientFactory.CreateClient("OpenAIClient");
            httpClient.Timeout = TimeSpan.FromMinutes(_chatProviderConfiguration.TimeoutMinutes);
            _api = new OpenAIClient(auth, settings, httpClient);
        }
        _aiBilingItemRepository = _serviceProvider.GetService<IRepository<AIBilingItem>>();
        _cachedAIModelRepository = _serviceProvider.GetService<IRepository<CachedAIModel>>();
        _httpClientFactory = httpClientFactory;
        _localizer = localizer;
        
        // Initial async update
        _ = RefreshModelPricesAsync();
    }

    protected virtual string LogPrefix => $"OpenAI/{_chatProviderConfiguration.Name}";

    public async Task<IReadOnlyList<Model>> GetAvailibleModels(long? userId = null, bool validateModels = true)
    {
        var appSettings = _serviceProvider.GetConfiguration<AppSettings>();
        var expiryHours = appSettings?.TelegramBotConfiguration?.ModelCacheExpiryHours ?? 48;
        var providerName = _chatProviderConfiguration.Name;

        if (_cachedAIModelRepository != null)
        {
            var cachedModels = _cachedAIModelRepository.GetAll()
                .Where(p => p.ProviderName == providerName && p.IsAvailable)
                .ToList();

            if (cachedModels.Any())
            {
                var oldestChecked = cachedModels.Min(p => p.LastChecked);
                if ((DateTime.UtcNow - oldestChecked).TotalHours < expiryHours)
                {
                    return cachedModels.Select(p => new Model(p.ModelId)).ToList();
                }
            }
        }

        // Fallback to in-memory if repo is null or no cache
        if (openAiModelCache != null && openAiModelCache.LastUpdates.HasValue && (DateTime.UtcNow - openAiModelCache.LastUpdates.Value).TotalHours < expiryHours)
        {
            return openAiModelCache.Models;
        }

        await RefreshAvailibleModels(validateModels);
        return openAiModelCache?.Models ?? new List<Model>();
    }

    public async Task RefreshAvailibleModels(bool validate = true)
    {
        _logger.LogInformation("{LogPrefix} Refreshing models cache (validate={0})...", LogPrefix, validate);
        IReadOnlyList<Model> modelsResponce;
        try
        {
            modelsResponce = await _api.ModelsEndpoint.GetModelsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{LogPrefix} Failed to fetch models from API", LogPrefix);
            return;
        }

        var providerName = _chatProviderConfiguration.Name;
        var result = new ConcurrentBag<Model>();
        var filteredModels = modelsResponce.Where(model => 
        {
            string modelId = model.Id.ToLowerInvariant();
            
            // Skip non-chat models explicitly
            if (modelId.Contains("embedding") || 
                modelId.Contains("whisper") || 
                modelId.Contains("dall-e") || 
                modelId.Contains("gpt-image") ||
                modelId.Contains("tts") || 
                modelId.Contains("moderation"))
            {
                return false;
            }

            // Regular filtering
            return modelId.StartsWith("gpt-") || 
                   modelId.StartsWith("o1-") || 
                   modelId.StartsWith("o3-") ||
                   modelId.StartsWith("deepseek-") ||
                   modelId.StartsWith("grok-") ||
                   aiModelsCosts.ContainsKey(model.Id);
        }).ToList();

        if (validate)
        {
            _logger.LogInformation("{LogPrefix} Validating {0} models in parallel...", LogPrefix, filteredModels.Count);

            var validationTasks = filteredModels.Select(async model => 
            {
                try
                {
                    ChatRequest chatRequest = new ChatRequest(new[] { new AiMessage(Role.User, "Hi") }, model: model.Id);
                    await _api.ChatEndpoint.GetCompletionAsync(chatRequest);
                    result.Add(model);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("{LogPrefix} Model {0} validation failed: {1}", LogPrefix, model.Id, ex.Message);
                }
            });

            await Task.WhenAll(validationTasks);
        }
        else
        {
            foreach (var model in filteredModels)
            {
                result.Add(model);
            }
        }

        if (_cachedAIModelRepository != null)
        {
            var existingModels = _cachedAIModelRepository.GetAll()
                .Where(p => p.ProviderName == providerName)
                .ToList();
            
            foreach (var model in result)
            {
                var existing = existingModels.FirstOrDefault(p => p.ModelId == model.Id);
                if (existing != null)
                {
                    existing.LastChecked = DateTime.UtcNow;
                    existing.IsAvailable = true;
                    _cachedAIModelRepository.Update(existing);
                }
                else
                {
                    _cachedAIModelRepository.Add(new CachedAIModel
                    {
                        ModelId = model.Id,
                        ProviderName = providerName,
                        IsAvailable = true,
                        LastChecked = DateTime.UtcNow,
                        FriendlyName = model.Id
                    });
                }
            }
            
            // Mark those not in result as unavailable
            foreach (var existing in existingModels.Where(e => result.All(r => r.Id != e.ModelId)))
            {
                if (existing.IsAvailable)
                {
                    existing.IsAvailable = false;
                    existing.LastChecked = DateTime.UtcNow;
                    _cachedAIModelRepository.Update(existing);
                }
            }
            await _cachedAIModelRepository.SaveChanges();
        }

        openAiModelCache = new OpenAIModelCache
        {
            LastUpdates = DateTime.UtcNow,
            Models = result.ToList()
        };
        _logger.LogInformation("{LogPrefix} models cache refreshed. Found {1} available models.", LogPrefix, result.Count);
    }

    public async Task<string> Ask(long chatId, long userId, string message)
    {
        var chatServiceResponce = await SendMessages2ChatAsync(chatId, userId, new List<AiMessage>()
        {
            new AiMessage(Role.User, message)
        });
        _logger.LogInformation("{LogPrefix} Usage for ask '{0}: {1}", LogPrefix, message,
            chatServiceResponce.TotalTokens);
        var sb = new StringBuilder();
        foreach (var choice in chatServiceResponce.Choices)
        {
            sb.AppendLine(choice);
        }
        return sb.ToString();
    }
    public async Task<ChatServiceResponse> SendMessages2ChatAsync(long telegramChatId, long telegramUserId, List<AiMessage> messages, string? model = null)
    {
        using (ServiceLayer.Services.Mcp.McpContext.Symbolize(telegramChatId, telegramUserId))
        {
            var modelName = model;
            if (string.IsNullOrEmpty(modelName))
            {
                modelName = string.IsNullOrEmpty(_chatProviderConfiguration.ModelName) 
                    ? (string)AiModel.Gpt4oMini 
                    : _chatProviderConfiguration.ModelName;
            }

            // Get MCP tools and filter based on user's DisabledTools preference
            var mcpTools = await _mcpServerManager.GetAllToolsAsync();
            var disabledList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (telegramUserId > 0)
            {
                var userRepository = _serviceProvider.GetService<IRepository<TelegramUserInfo>>();
                if (userRepository != null)
                {
                    var user = await userRepository.Get(p => p.Id == telegramUserId);
                    if (user != null && !string.IsNullOrEmpty(user.DisabledTools))
                    {
                        var split = user.DisabledTools.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var d in split)
                        {
                            disabledList.Add(d.Trim());
                        }
                    }
                }
            }

            var activeMcpTools = mcpTools.Where(t => !disabledList.Contains(t.Name)).ToList();

            // Check if model explicitly does NOT support function calling
            bool supportsTools = true;
            if (_liveModelsInfo.TryGetValue(modelName, out var modelInfo))
            {
                if (modelInfo.SupportsFunctionCalling == false)
                {
                    supportsTools = false;
                }
            }

            // OpenAI and Grok/DeepSeek compatible endpoints have a strict limit of 128 tools
            bool isLimited = (_chatProviderConfiguration.ProviderType == AiProvider.OpenAI 
                              || _chatProviderConfiguration.ProviderType == AiProvider.Grok 
                              || _chatProviderConfiguration.ProviderType == AiProvider.DeepSeek
                              || modelName.Contains("grok", StringComparison.OrdinalIgnoreCase) 
                              || modelName.Contains("gpt-", StringComparison.OrdinalIgnoreCase)
                              || modelName.Contains("o1-", StringComparison.OrdinalIgnoreCase)
                              || modelName.Contains("deepseek", StringComparison.OrdinalIgnoreCase));

            int truncatedToolsCount = 0;
            var finalToolsList = activeMcpTools;
            if (supportsTools && isLimited && activeMcpTools.Count > 128)
            {
                _logger.LogWarning("{LogPrefix} Tools count ({0}) exceeds maximum limit (128) for {1}. Truncating to 128.", LogPrefix, activeMcpTools.Count, modelName);
                truncatedToolsCount = activeMcpTools.Count - 128;
                finalToolsList = activeMcpTools.Take(128).ToList();
            }

            var tools = supportsTools 
                ? finalToolsList.Select(t => new Tool(new Function(t.Name, t.Description, t.JsonSchema.GetRawText()))).ToList()
                : new List<Tool>();

            // Sanitization for Grok (disallow name for system/developer/assistant/tool messages)
            bool isGrok = (_chatProviderConfiguration.ProviderType == AiProvider.Grok 
                           || _chatProviderConfiguration.Name.Contains("grok", StringComparison.OrdinalIgnoreCase)
                           || modelName.Contains("grok", StringComparison.OrdinalIgnoreCase));

            var preparedMessages = messages;
            if (isGrok)
            {
                preparedMessages = messages.Select(msg => 
                {
                    if (msg.Role != Role.User && (!string.IsNullOrEmpty(msg.Name) || !string.IsNullOrEmpty(msg.ToolCallId)))
                    {
                        if (msg.Role == Role.Tool)
                        {
                            string? contentStr = msg.Content is string s ? s : msg.Content?.ToString();
                            return new AiMessage(Role.Tool, contentStr ?? "", msg.ToolCallId);
                        }
                        else if (msg.Content is IList<Content> contentList)
                        {
                            return new AiMessage(msg.Role, contentList);
                        }
                        else
                        {
                            string? contentStr = msg.Content is string s ? s : msg.Content?.ToString();
                            return new AiMessage(msg.Role, contentStr ?? "");
                        }
                    }
                    return msg;
                }).ToList();
            }

            ChatResponse? result = null;
            Stopwatch sw = Stopwatch.StartNew();
            var usedTools = new HashSet<string>();
            int totalPromptTokens = 0;
            int totalCompletionTokens = 0;
            bool contextCompressed = false;
            var toolCallsList = new List<ToolCallInfo>();
            
            // Loop for handling multiple tool calls
            int maxIterations = 5;
            while (maxIterations-- > 0)
            {
                ChatRequest chatRequest = tools.Any() 
                    ? new ChatRequest(preparedMessages, model: modelName, tools: tools)
                    : new ChatRequest(preparedMessages, model: modelName);
                try
                {
                    _logger.LogInformation("{LogPrefix} Request: Model={0}, MessagesCount={1}, ToolsCount={2}", LogPrefix, modelName, preparedMessages.Count, tools.Count);
                    result = await _api.ChatEndpoint.GetCompletionAsync(chatRequest);
                    
                    if (result == null || result.Choices == null || !result.Choices.Any())
                    {
                        _logger.LogWarning("{LogPrefix} returned an empty response for {0}", LogPrefix, modelName);
                        throw new Exception($"{LogPrefix} returned an empty response for {modelName}");
                    }

                    if (result.Usage != null)
                    {
                        totalPromptTokens += result.Usage.PromptTokens ?? 0;
                        totalCompletionTokens += result.Usage.CompletionTokens ?? 0;
                    }

                    var choice = result.Choices[0];
                    if (choice.FinishReason == "tool_calls" && choice.Message.ToolCalls != null && choice.Message.ToolCalls.Any())
                    {
                        _logger.LogInformation("{LogPrefix} requested {0} tool calls", LogPrefix, choice.Message.ToolCalls.Count);
                        messages.Add(choice.Message); // Add original turn assistant message to caller's context
                        preparedMessages.Add(choice.Message); // Add to sanitized context too

                        foreach (var toolCall in choice.Message.ToolCalls)
                        {
                            var toolName = toolCall.Function.Name;
                            usedTools.Add(toolName);
                             _logger.LogInformation("{LogPrefix} executing MCP tool: {0} with args: {1}", LogPrefix, toolName, toolCall.Function.Arguments);
                             var toolArgs = toolCall.Function.Arguments?.ToString() ?? "{}";
                             var toolOutput = await _mcpServerManager.ExecuteToolAsync(toolName, toolArgs);
                             _logger.LogInformation("{LogPrefix} MCP tool {0} returned: {1}", LogPrefix, toolName, toolOutput);
                             
                             toolCallsList.Add(new ToolCallInfo
                             {
                                 ToolName = toolName,
                                 Arguments = toolArgs,
                                 Response = toolOutput
                             });
                            
                            var toolMessage = new AiMessage(toolCall, toolOutput);
                            messages.Add(toolMessage); // Update caller's context
                            preparedMessages.Add(toolMessage); // Update sanitized context
                        }
                        continue; // Re-submit with tool results
                    }

                    break; // Final response received
                }
                catch (Exception ex)
                {
                    if (IsTokenLimitError(ex) && !contextCompressed)
                    {
                        _logger.LogWarning("{LogPrefix} Token limit exceeded. Compressing context and retrying...", LogPrefix);
                        CompressContext(preparedMessages);
                        contextCompressed = true;
                        maxIterations++; // Allow one more iteration for the retry
                        continue;
                    }
                    throw AiErrorHelper.HandleAndGetException(_logger, ex, _chatProviderConfiguration.Name, nameof(SendMessages2ChatAsync), _localizer);
                }
            }
            
            sw.Stop();

            var finalUsage = new AIUsage(totalPromptTokens, totalCompletionTokens, totalPromptTokens + totalCompletionTokens);
            decimal? cost = await SaveBilling(modelName, _chatProviderConfiguration.Name, telegramChatId, telegramUserId, finalUsage);

            var latency = sw.Elapsed.TotalSeconds;
            var tps = finalUsage.TotalTokens / latency;

            string? thoughts = null;
            var choices = result.Choices.Select(p => 
            {
                var content = p.Message?.ToString() ?? string.Empty;
                
                // Match <think>...</think>
                var match = global::System.Text.RegularExpressions.Regex.Match(content, @"<think>([\s\S]*?)</think>", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    thoughts = match.Groups[1].Value.Trim();
                }
                else
                {
                    // Match unclosed <think> tag at the end of the text
                    var openMatch = global::System.Text.RegularExpressions.Regex.Match(content, @"<think>([\s\S]*?)$", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (openMatch.Success)
                    {
                        thoughts = openMatch.Groups[1].Value.Trim();
                    }
                }

                content = global::System.Text.RegularExpressions.Regex.Replace(content, @"<think>[\s\S]*?</think>", "", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                content = global::System.Text.RegularExpressions.Regex.Replace(content, @"<think>[\s\S]*$", "", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return content.Trim();
            }).ToList();

            return new ChatServiceResponse
            {
                Choices = choices,
                TotalTokens = finalUsage.TotalTokens,
                PromptTokens = finalUsage.PromptTokens,
                CompletionTokens = finalUsage.CompletionTokens,
                Cost = cost,
                LatencySeconds = latency,
                TokensPerSecond = tps,
                ProviderName = _chatProviderConfiguration.Name,
                ModelName = modelName,
                UsedTools = usedTools.ToList(),
                ToolCalls = toolCallsList,
                TruncatedToolsCount = truncatedToolsCount,
                Thoughts = thoughts
            };
        }
    }

    private async Task<decimal?> SaveBilling(string modelName, string providerName, long telegramChatId, long telegramUserId, AIUsage? usage)
    {
        if (usage == null || _aiBilingItemRepository == null) return null;

        // Prevent foreign key violation when saving billing without a valid chat/user context (e.g. system errors)
        if (telegramChatId == 0 || telegramUserId == 0)
        {
            _logger.LogWarning("Skipping SaveBilling: telegramChatId={ChatId}, telegramUserId={UserId}", telegramChatId, telegramUserId);
            return null;
        }
        
        if ((DateTime.UtcNow - _lastPriceUpdate).TotalHours > 24)
        {
            _ = RefreshModelPricesAsync();
        }

        if (!_liveModelsCosts.TryGetValue(modelName, out AIModelCost? modelCost))
        {
            aiModelsCosts.TryGetValue(modelName, out modelCost);
        }

        decimal? cost = modelCost == null
            ? 0M
            : ((usage.PromptTokens * modelCost.Input) + (usage.CompletionTokens * modelCost.Output)) / modelCost.PerTokens;
        
        var dbAIBilingItem = new AIBilingItem
        {
            CreationDate = DateTime.UtcNow,
            ModelName = modelName,
            ModifiedDate = DateTime.UtcNow,
            TelegramChatInfoId = telegramChatId,
            TelegramUserInfoId = telegramUserId,
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.CompletionTokens,
            TotalTokens = usage.TotalTokens,
            Cost = cost,
            ProviderName = providerName
        };
        _aiBilingItemRepository.Add(dbAIBilingItem);
        // Update user balance
        var userRepository = _serviceProvider.GetService<IRepository<TelegramUserInfo>>();
        if (userRepository != null)
        {
            var user = await userRepository.Get(p => p.Id == telegramUserId);
            if (user != null)
            {
                var appConfig = _serviceProvider.GetConfiguration<AppSettings>();
                var config = appConfig?.TelegramBotConfiguration;
                bool isIgnored = (config?.IgnoredBalanceUserIds?.Contains(telegramUserId) == true) ||
                                 (config?.OwnerId == telegramUserId);
                bool isFree = config?.InitialBalance == 0;

                if (!isIgnored && !isFree)
                {
                    user.Balance -= cost ?? 0;
                    user.BalanceModifiedAt = DateTime.UtcNow;
                }
                user.LastAiInteraction = DateTime.UtcNow;
                userRepository.Update(user);
            }
        }

        return await _aiBilingItemRepository.SaveChanges() > 0 ? (decimal?)cost : null;
    }

    public async Task<ChatServiceResponse> GenerateImage(long chatId, long telegramUserId, string prompt, string? modelName = null)
    {
        // Determine the drawing model. DrawingModelName and DefaultDrawingModel are only set
        // for providers that actually support image generation (OpenAI → dall-e-3, Gemini → imagen-3).
        // Grok and DeepSeek have no image API; fail fast with NotSupportedException so
        // ResilientChatService can fall back to a provider that does support it.
        modelName ??= _chatProviderConfiguration.DrawingModelName
                  ?? _chatProviderConfiguration.ProviderType.DefaultDrawingModel;

        if (string.IsNullOrEmpty(modelName))
        {
            throw new NotSupportedException(
                $"Provider '{_chatProviderConfiguration.Name}' ({_chatProviderConfiguration.ProviderType.DisplayName}) " +
                "does not support image generation.");
        }

        Stopwatch sw = Stopwatch.StartNew();
        List<string> results = new List<string>();
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_chatProviderConfiguration.ApiKey}");
            
            var requestBody = new Dictionary<string, object>
            {
                { "prompt", prompt },
                { "model", modelName },
                { "n", 1 }
            };
            
            if (!modelName.Contains("gpt-image"))
            {
                requestBody.Add("response_format", "url");
            }

            var domain = _chatProviderConfiguration.BaseUrl ?? "";
            if (!string.IsNullOrEmpty(domain))
            {
                domain = domain.Replace("https://", "").Replace("http://", "").Split('/')[0];
            }
            var baseUrl = string.IsNullOrEmpty(domain) ? "https://api.openai.com/v1" : $"https://{domain}/v1";
            var endpoint = $"{baseUrl.TrimEnd('/')}/images/generations";

            var jsonPayload = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var httpResponse = await client.PostAsync(endpoint, content);
            
            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorContent = await httpResponse.Content.ReadAsStringAsync();
                _logger.LogError("{LogPrefix} Image generation failed. Status: {Status}, Error: {Error}", LogPrefix, httpResponse.StatusCode, errorContent);
                throw new Exception($"OpenAI image generation failed: {httpResponse.StatusCode} - {errorContent}");
            }

            var responseContent = await httpResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataArray.EnumerateArray())
                {
                    if (item.TryGetProperty("url", out var urlProp) && !string.IsNullOrEmpty(urlProp.GetString()))
                    {
                        results.Add(urlProp.GetString()!);
                    }
                    else if (item.TryGetProperty("b64_json", out var b64Prop) && !string.IsNullOrEmpty(b64Prop.GetString()))
                    {
                        results.Add(b64Prop.GetString()!);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw AiErrorHelper.HandleAndGetException(_logger, ex, _chatProviderConfiguration.Name, nameof(GenerateImage), _localizer);
        }
        sw.Stop();
        
        _logger.LogInformation("{LogPrefix} Use model: {0} for generate image", LogPrefix, modelName);
        
        var usage = new AIUsage(0, 1, 1);
        decimal? cost = await SaveBilling(modelName, _chatProviderConfiguration.Name, chatId, telegramUserId, usage);

        return new ChatServiceResponse
        {
            Choices = results,
            TotalTokens = usage.TotalTokens,
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.CompletionTokens,
            Cost = cost,
            LatencySeconds = sw.Elapsed.TotalSeconds,
            TokensPerSecond = 1.0 / sw.Elapsed.TotalSeconds, // 1 image per response
            ProviderName = _chatProviderConfiguration.Name,
            ModelName = modelName
        };
    }
    public async Task<string> AudioTranscription(long chatId, long telegramUserId,
        Stream audio, string audioName, string? model = null,
        string? prompt = null, AudioResponseFormat responseFormat = AudioResponseFormat.Json,
        int? temperature = null, string? language = null)
    {
        var request = new AudioTranscriptionRequest(
            audio: audio,
            audioName: audioName,
            model: model,
            prompt: prompt,
            responseFormat: responseFormat,
            temperature: (float?)temperature,
            language: language);
        string result;
        try
        {
            result = await _api.AudioEndpoint.CreateTranscriptionTextAsync(request);
        }
        catch (Exception ex)
        {
            throw AiErrorHelper.HandleAndGetException(_logger, ex, _chatProviderConfiguration.Name, nameof(AudioTranscription), _localizer);
        }
        await SaveBilling(request.Model, _chatProviderConfiguration.Name, chatId, telegramUserId, new AIUsage(0, 1, 1));
        return result;
    }
    
    public async Task<Stream> TextToSpeech(long chatId, long telegramUserId, string text, string? model = null, string voice = "alloy")
    {
        var ttsModel = model ?? Model.TTS_1;
        var ttsVoice = voice;
        
        try
        {
            _logger.LogInformation("{LogPrefix} Generating speech with model {0}, voice {1}", LogPrefix, ttsModel, ttsVoice);
            var request = new SpeechRequest(text, ttsModel, voice: ttsVoice);
            var result = await _api.AudioEndpoint.CreateSpeechAsync(request);
            
            // Billing for TTS is per character (but LiteLLM might have it per 1k characters)
            // Our SaveBilling expects AIUsage (tokens). We'll map chars to completions for simplicity in our billing system.
            await SaveBilling(ttsModel, _chatProviderConfiguration.Name, chatId, telegramUserId, new AIUsage(0, 0, text.Length));
            
            return new MemoryStream(result.ToArray());
        }
        catch (Exception ex)
        {
             throw AiErrorHelper.HandleAndGetException(_logger, ex, _chatProviderConfiguration.Name, nameof(TextToSpeech), _localizer);
        }
    }

    public async Task<ChatServiceResponse> AnalyzeImageAsync(long chatId, long telegramUserId, string? imageUrl, string? filePath = null, string? prompt = null, string? model = null)
    {
        var appSettings = _serviceProvider.GetConfiguration<AppSettings>();
        var visionModel = model ?? appSettings?.TelegramBotConfiguration?.AiSettings?.Vision?.ModelName ?? (string)AiModel.Gpt4o;
        var finalPrompt = prompt ?? "Describe this image.";

        string? finalImageUrl = imageUrl;
        string? finalFilePath = filePath;

        // Safety: if imageUrl looks like a local path, treat it as filePath
        if (!string.IsNullOrEmpty(finalImageUrl) && finalImageUrl.StartsWith("/"))
        {
            finalFilePath = finalImageUrl;
            finalImageUrl = null;
        }

        if (string.IsNullOrEmpty(finalImageUrl) && !string.IsNullOrEmpty(finalFilePath))
        {
            var bytes = await File.ReadAllBytesAsync(finalFilePath);
            var base64 = Convert.ToBase64String(bytes);
            var extension = Path.GetExtension(finalFilePath).TrimStart('.').ToLower();
            var mimeType = (extension == "png" || extension == "webp") ? $"image/{extension}" : "image/jpeg";
            finalImageUrl = $"data:{mimeType};base64,{base64}";
        }

        if (string.IsNullOrEmpty(finalImageUrl))
        {
            throw new ArgumentException("imageUrl");
        }

        _logger.LogInformation("{LogPrefix} Vision Request: Model={Model}, URL_Start={URLStart}", LogPrefix, visionModel, finalImageUrl.Length > 50 ? finalImageUrl.Substring(0, 50) : finalImageUrl);

        var messages = new List<AiMessage>
        {
            new AiMessage(Role.System, "You are a visual analysis assistant. Provide direct and concise descriptions or answers based on the image. Do not provide tutorials, advice on how to edit images, or general chat unless relevant to the visual content."),
            new AiMessage(Role.User, new List<Content>
            {
                finalPrompt,
                new ImageUrl(finalImageUrl)
            })
        };

        _logger.LogInformation("{LogPrefix} Analyzing image via SendMessages2ChatAsync: Model={0}", LogPrefix, visionModel);
        var response = await SendMessages2ChatAsync(chatId, telegramUserId, messages, visionModel);
        _logger.LogInformation("{LogPrefix} Image analysis completed: ChoicesCount={0}", LogPrefix, response.Choices?.Count ?? 0);
        return response;
    }

    private bool IsTokenLimitError(Exception ex)
    {
        var msg = ex.ToString().ToLower();
        return msg.Contains("maximum prompt length") || 
               msg.Contains("context_length_exceeded") || 
               msg.Contains("too many tokens") ||
               msg.Contains("rate_limit_exceeded") ||
               (ex is HttpRequestException && msg.Contains("badrequest") && (msg.Contains("token") || msg.Contains("limit")));
    }

    private void CompressContext(List<AiMessage> messages)
    {
        if (messages.Count <= 1) return;

        for (int i = 0; i < messages.Count - 1; i++)
        {
            var msg = messages[i];
            
            string? content = null;
            if (msg.Content is string s) content = s;
            else if (msg.Content is IList<Content> list) content = string.Join("\n", list.Select(c => c.Text));
            else content = msg.Content?.ToString();

            if (string.IsNullOrEmpty(content)) continue;

            // Aggressive compression
            if (msg.Role == Role.Tool)
            {
                if (content.Length > 1000)
                {
                    messages[i] = new AiMessage(msg.Role, content.Substring(0, 500) + "\n... [TOOL OUTPUT REMOVED TO FIT CONTEXT] ...", msg.ToolCallId);
                }
            }
            else if (content.Length > 2000)
            {
                messages[i] = new AiMessage(msg.Role, content.Substring(0, 1000) + "\n... [TRUNCATED] ...", msg.Name);
            }
        }
    }

    public async Task<ChatServiceResponse> CreateImageEditAsync(long chatId, long telegramUserId,
        string filePath, string? messageText)
    {
        var modelName = _chatProviderConfiguration.DrawingModelName 
            ?? _chatProviderConfiguration.ProviderType.DefaultDrawingModel 
            ?? _chatProviderConfiguration.ModelName 
            ?? (string)AiModel.DallE2; // Default for edits

        var imageEditRequest = new ImageEditRequest(prompt: messageText, imagePath: filePath,
            model: modelName, responseFormat: ImageResponseFormat.Url);
        
        IReadOnlyList<ImageResult> imagesResults;
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            imagesResults = await _api.ImagesEndPoint.CreateImageEditAsync(imageEditRequest);
        }
        catch (Exception ex)
        {
            throw AiErrorHelper.HandleAndGetException(_logger, ex, _chatProviderConfiguration.Name, nameof(CreateImageEditAsync), _localizer);
        }
        sw.Stop();
        var results = imagesResults.Select(p => p.Url).ToList();
        
        var usage = new AIUsage(0, 1, 1);
        decimal? cost = await SaveBilling(modelName, _chatProviderConfiguration.Name, chatId, telegramUserId, usage);

        return new ChatServiceResponse
        {
            Choices = results,
            TotalTokens = usage.TotalTokens,
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.CompletionTokens,
            Cost = cost,
            LatencySeconds = sw.Elapsed.TotalSeconds,
            TokensPerSecond = 1.0 / sw.Elapsed.TotalSeconds,
            ProviderName = _chatProviderConfiguration.Name,
            ModelName = modelName
        };
    }

    public async Task<(bool, string)> SetGPTModel(string? modelName, long? userId = null)
    {
        if (string.IsNullOrEmpty(modelName)) return (false, _localizer["EmptyModelName"]);
        try
        {
            ChatRequest chatRequest = new ChatRequest(new[] { new AiMessage(Role.User, "Hi") }, model: modelName);
            ChatResponse result = await _api.ChatEndpoint.GetCompletionAsync(chatRequest);
            _chatProviderConfiguration.ModelName = modelName;
            return (true, string.Empty);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Request to provider {Provider} was cancelled for {Method}", _chatProviderConfiguration.Name, nameof(SetGPTModel));
            return (false, _localizer["AiError_Timeout"]);
        }
        catch (Exception ex)
        {
            var exception = AiErrorHelper.HandleAndGetException(_logger, ex, _chatProviderConfiguration.Name, nameof(SetGPTModel), _localizer);
            if (exception is AiProviderException aiEx)
            {
                return (false, aiEx.Message);
            }
            return (false, _localizer["AiError_Default"]);
        }
    }

    public Task<string?> GetSelectedModel(long userId)
    {
        return Task.FromResult<string?>(_chatProviderConfiguration.ModelName);
    }

    internal async Task RefreshModelPricesAsync()
    {
        lock (_priceLock)
        {
            if ((DateTime.UtcNow - _lastPriceUpdate).TotalMinutes < 60) return;
            _lastPriceUpdate = DateTime.UtcNow;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("User-Agent", "GPTChatTelegramBot-PriceFetcher");
            var response = await client.GetAsync("https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<Dictionary<string, LiteLlmModelInfo>>(content);
                if (data != null)
                {
                    _logger.LogInformation("Downloaded {0} models from LiteLLM", data.Count);
                    foreach (var kvp in data)
                    {
                        var modelId = kvp.Key;
                        var info = kvp.Value;
                        if ((string.Equals(info.Provider, AiProvider.OpenAI.Value, StringComparison.OrdinalIgnoreCase) 
                             || string.Equals(info.Provider, AiProvider.DeepSeek.Value, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(info.Provider, AiProvider.Grok.Value, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(info.Provider, "google", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(info.Provider, "gemini", StringComparison.OrdinalIgnoreCase))
                            && info.InputCostPerToken.HasValue 
                            && info.OutputCostPerToken.HasValue)
                        {
                            var inputPrice = (decimal)info.InputCostPerToken.Value * defTokens;
                            var outputPrice = (decimal)info.OutputCostPerToken.Value * defTokens;
                            _liveModelsCosts[modelId] = new AIModelCost(defTokens, inputPrice, outputPrice);
                            _liveModelsInfo[modelId] = info;
                        }
                    }
                    _logger.LogInformation("Successfully updated {0} OpenAI model prices from LiteLLM", _liveModelsCosts.Count);
                }
                else
                {
                    _logger.LogWarning("Downloaded LiteLLM data is null");
                }
            }
            else
            {
                _logger.LogWarning("Failed to download LiteLLM prices: {0} {1}", response.StatusCode, response.ReasonPhrase);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update model prices from LiteLLM");
        }
    }
}

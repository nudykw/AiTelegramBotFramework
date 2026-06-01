using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Logging;
using OpenAI.Audio;
using OpenAI.Chat;

using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.DependencyInjection;
using ServiceLayer.Constans;
using ServiceLayer.Services.Localization;
using ServiceLayer.Services.Mcp;
using OpenAIModel = OpenAI.Models.Model;
using GoogleModel = Google.GenAI.Types.Model;
using GoogleContent = Google.GenAI.Types.Content;
using GooglePart = Google.GenAI.Types.Part;
using GoogleTool = Google.GenAI.Types.Tool;
using OpenAI;
using System.Linq;
using System.Collections.Concurrent;
using System.Text.Json;
using ServiceLayer.Utils;
using System.Diagnostics;
using ServiceLayer.Services.OpenAI;
using ServiceLayer.Models;


namespace ServiceLayer.Services.GeminiChat.DotNet
{
    internal class ChatGeminiService : BaseService, IChatService
    {
        private ChatProviderConfig _apiConfiguration;
        private IGeminiClient _client;
        private readonly IDynamicLocalizer _localizer;
        private readonly IRepository<AIBilingItem>? _aiBilingItemRepository;
        private readonly IRepository<CachedAIModel>? _cachedAIModelRepository;
        private readonly McpServerManager _mcpServerManager;

        private class GeminiModelCache
        {
            internal DateTime? LastUpdates { get; set; }
            internal required IReadOnlyList<OpenAIModel> Models { get; set; }
        }
        private GeminiModelCache? _geminiModelCache = null;

        public ChatGeminiService(IServiceProvider serviceProvider, ILogger<ChatGeminiService> logger,
            ChatProviderConfig chatProviderConfig, IDynamicLocalizer localizer, McpServerManager mcpServerManager)
            : this(serviceProvider, logger, chatProviderConfig, null, localizer, mcpServerManager)
        {
        }

        internal ChatGeminiService(IServiceProvider serviceProvider, ILogger<ChatGeminiService> logger,
            ChatProviderConfig chatProviderConfig, IGeminiClient? client, IDynamicLocalizer localizer, McpServerManager mcpServerManager)
            : base(serviceProvider, logger)
        {
            _apiConfiguration = chatProviderConfig;
            _client = client ?? new GeminiClientWrapper(_apiConfiguration.ApiKey);
            _aiBilingItemRepository = _serviceProvider.GetService<IRepository<AIBilingItem>>();
            _cachedAIModelRepository = _serviceProvider.GetService<IRepository<CachedAIModel>>();
            _localizer = localizer;
            _mcpServerManager = mcpServerManager;
        }

        private string GetModelName() => string.IsNullOrEmpty(_apiConfiguration.ModelName) 
            ? (string)AiModel.GeminiFlash 
            : _apiConfiguration.ModelName;

        public async Task<string> Ask(long chatId, long userId, string message)
        {
            var chatServiceResponce = await SendMessages2ChatAsync(chatId, userId, new List<AiMessage>()
            {
                new AiMessage(Role.User, message)
            });
            return string.Join("\n", chatServiceResponce.Choices);
        }

        public async Task<IReadOnlyList<OpenAIModel>> GetAvailibleModels(long? userId = null, bool validateModels = true)
        {
            var appConfig = _serviceProvider.GetConfiguration<AppSettings>();
            var expiryHours = appConfig?.TelegramBotConfiguration?.ModelCacheExpiryHours ?? 48;
            var providerName = _apiConfiguration.Name;

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
                        return cachedModels.Select(p => new OpenAIModel(p.ModelId)).ToList();
                    }
                }
            }

            if (_geminiModelCache != null && _geminiModelCache.LastUpdates.HasValue && (DateTime.UtcNow - _geminiModelCache.LastUpdates.Value).TotalHours < expiryHours)
            {
                return _geminiModelCache.Models;
            }

            await RefreshAvailibleModels(validateModels);
            return _geminiModelCache?.Models ?? new List<OpenAIModel> 
            { 
                new OpenAIModel((string)AiModel.GeminiPro),
                new OpenAIModel((string)AiModel.GeminiFlash)
            };
        }

        public async Task RefreshAvailibleModels(bool validate = true)
        {
            var providerName = _apiConfiguration.Name;
            _logger.LogInformation("Refreshing {0} models cache (validate={1})...", providerName, validate);
            var result = new ConcurrentBag<OpenAIModel>();
            try
            {
                var models = _client.ListModelsAsync();
                var modelInfos = new List<GoogleModel>();
                await foreach (var modelInfo in models)
                {
                    modelInfos.Add(modelInfo);
                }

                if (validate)
                {
                    var validationTasks = modelInfos.Select(async modelInfo => 
                    {
                        // Filter for models that support generating content
                        if (modelInfo.SupportedActions != null && 
                            modelInfo.SupportedActions.Any(a => string.Equals(a, "generateContent", StringComparison.OrdinalIgnoreCase)))
                        {
                            if (modelInfo.Name == null) return;
                            var modelName = modelInfo.Name.StartsWith("models/") 
                                ? modelInfo.Name.Substring("models/".Length) 
                                : modelInfo.Name;

                            if (!modelName.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
                            {
                                return;
                            }

                            try
                            {
                                await _client.GenerateContentAsync(modelInfo.Name, new List<GoogleContent> { new GoogleContent { Role = "user", Parts = new List<GooglePart> { new GooglePart { Text = "Hi" } } } });
                                result.Add(new OpenAIModel(modelName));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug("Gemini model {0} validation failed: {1}", modelInfo.Name, ex.Message);
                            }
                        }
                    });

                    await Task.WhenAll(validationTasks);
                }
                else
                {
                     foreach (var modelInfo in modelInfos)
                     {
                        if (modelInfo.Name == null) continue;
                        var modelName = modelInfo.Name.StartsWith("models/") 
                            ? modelInfo.Name.Substring("models/".Length) 
                            : modelInfo.Name;
                        if (modelName.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
                            result.Add(new OpenAIModel(modelName));
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

                _geminiModelCache = new GeminiModelCache
                {
                    LastUpdates = DateTime.UtcNow,
                    Models = result.ToList()
                };
                _logger.LogInformation("{0} models cache refreshed. Found {1} available models.", providerName, result.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing {0} models cache.", providerName);
            }
        }

        public async Task<ChatServiceResponse> SendMessages2ChatAsync(long telegramChatId, long telegramUserId, List<AiMessage> messages, string? model = null)
        {
            using (ServiceLayer.Services.Mcp.McpContext.Symbolize(telegramChatId, telegramUserId))
            {
                var modelName = model ?? GetModelName();
                
                string GetActualResponseText(GenerateContentResponse resp)
                {
                    if (resp?.Candidates != null && resp.Candidates.Any() && resp.Candidates[0].Content?.Parts != null)
                    {
                        var nonThoughtParts = resp.Candidates[0].Content.Parts
                            .Where(p => p.Text != null && p.Thought != true)
                            .Select(p => p.Text)
                            .ToList();
                        
                        if (nonThoughtParts.Any())
                        {
                            return string.Join("", nonThoughtParts);
                        }
                    }
                    return resp?.Text ?? string.Empty;
                }

                string? GetThoughts(GenerateContentResponse resp)
                {
                    if (resp?.Candidates != null && resp.Candidates.Any() && resp.Candidates[0].Content?.Parts != null)
                    {
                        var thoughtParts = resp.Candidates[0].Content.Parts
                            .Where(p => p.Text != null && p.Thought == true)
                            .Select(p => p.Text)
                            .ToList();
                        
                        if (thoughtParts.Any())
                        {
                            return string.Join("", thoughtParts).Trim();
                        }
                    }
                    return null;
                }
                
                // Separate system messages from conversation history
                var systemMessages = messages.Where(m => m.Role == Role.System || m.Role == Role.Developer).ToList();
                var nonSystemMessages = messages.Where(m => m.Role != Role.System && m.Role != Role.Developer).ToList();

                string? systemInstruction = null;
                if (systemMessages.Any())
                {
                    systemInstruction = string.Join("\n\n", systemMessages.Select(m => m.Content));
                }

                // Convert conversation history to Gemini format, ensuring alternating roles
                var contents = new List<GoogleContent>();
                foreach (var m in nonSystemMessages)
                {
                    var role = m.Role == Role.User ? "user" : "model";
                    
                    if (contents.Any() && contents.Last().Role == role)
                    {
                        // Merge consecutive turns of the same role
                        contents.Last().Parts.Add(new GooglePart { Text = m.Content });
                    }
                    else
                    {
                        contents.Add(new GoogleContent
                        {
                            Role = role,
                            Parts = new List<GooglePart> { new GooglePart { Text = m.Content } }
                        });
                    }
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
                var uniqueTools = activeMcpTools.GroupBy(t => t.Name).Select(g => g.First());
                var tools = uniqueTools.Select(t => new GoogleTool
                {
                    FunctionDeclarations = new List<FunctionDeclaration>
                    {
                        new FunctionDeclaration
                        {
                            Name = t.Name,
                            Description = t.Description,
                            Parameters = JsonSerializer.Deserialize<Schema>(t.JsonSchema.GetRawText())
                        }
                    }
                }).ToList();

                if (tools.Any())
                {
                    var toolInstruction = "CRITICAL: After successfully executing any tools, you MUST provide a concise and specific summary of what was done based on the tool results. Do not output empty text, and do not repeat system instructions.";
                    systemInstruction = string.IsNullOrEmpty(systemInstruction)
                        ? toolInstruction
                        : $"{systemInstruction}\n\n{toolInstruction}";
                }

                GenerateContentResponse response = null;
                var usedTools = new HashSet<string>();
                var toolCallsList = new List<ToolCallInfo>();
                int maxIterations = 5;
                
                Stopwatch sw = Stopwatch.StartNew();
                while (maxIterations-- > 0)
                {
                    var supportsThinking = modelName.Contains("2.") || modelName.Contains("3.") || modelName.Contains("thinking");
                    var config = new GenerateContentConfig 
                    { 
                        Tools = tools.Any() ? tools : null,
                        SystemInstruction = systemInstruction != null ? new GoogleContent
                        {
                            Parts = new List<GooglePart> { new GooglePart { Text = systemInstruction } }
                        } : null,
                        ThinkingConfig = supportsThinking ? new ThinkingConfig 
                        { 
                            IncludeThoughts = true,
                            ThinkingBudget = 2048
                        } : null
                    };

                    try
                    {
                        _logger.LogInformation("Gemini Request: Model={0}, ContentsCount={1}, ToolsCount={2}", modelName, contents.Count, tools.Count);
                        response = await _client.GenerateContentAsync(modelName, contents, config);
                        
                        if (response == null || response.Candidates == null || !response.Candidates.Any())
                        {
                            _logger.LogWarning("Gemini returned an empty response for {0}", modelName);
                            throw new Exception($"Gemini returned an empty response for {modelName}");
                        }

                        var candidate = response.Candidates[0];
                        var functionCalls = candidate.Content?.Parts?.Where(p => p.FunctionCall != null).ToList();

                        if (functionCalls != null && functionCalls.Any())
                        {
                            _logger.LogInformation("Gemini requested {0} function calls", functionCalls.Count);
                            contents.Add(candidate.Content); // Add model's request to history

                            var responseParts = new List<GooglePart>();
                            foreach (var call in functionCalls)
                            {
                                var toolName = call.FunctionCall.Name;
                                usedTools.Add(toolName);
                                var toolArgs = JsonSerializer.Serialize(call.FunctionCall.Args);
                                
                                _logger.LogInformation("Gemini executing MCP tool: {0} with args: {1}", toolName, toolArgs);
                                var toolOutput = await _mcpServerManager.ExecuteToolAsync(toolName, toolArgs);
                                _logger.LogInformation("MCP tool {0} returned: {1}", toolName, toolOutput);
                                
                                toolCallsList.Add(new ToolCallInfo
                                {
                                    ToolName = toolName,
                                    Arguments = toolArgs,
                                    Response = toolOutput
                                });

                                Dictionary<string, object>? parsedResponse;
                                try
                                {
                                    parsedResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(toolOutput);
                                }
                                catch
                                {
                                    parsedResponse = new Dictionary<string, object> { { "result", toolOutput } };
                                }

                                if (parsedResponse == null)
                                {
                                    parsedResponse = new Dictionary<string, object> { { "result", toolOutput } };
                                }

                                responseParts.Add(new GooglePart
                                {
                                    FunctionResponse = new FunctionResponse
                                    {
                                        Name = toolName,
                                        Response = parsedResponse
                                    }
                                });
                            }

                            // Group all responses into a single user turn to maintain strict role alternation
                            contents.Add(new GoogleContent
                            {
                                Role = "user",
                                Parts = responseParts
                            });

                            continue; // Re-submit with tool results
                        }

                        break; // Final response
                    }
                    catch (Exception ex)
                    {
                        throw AiErrorHelper.HandleAndGetException(_logger, ex, _apiConfiguration.Name, nameof(SendMessages2ChatAsync), _localizer);
                    }
                }

                // Gemini sometimes returns empty text after executing tools.
                // If tools were used and we got no text, re-prompt once in the SAME turn to get a confirmation summary.
                if (string.IsNullOrWhiteSpace(GetActualResponseText(response)) && usedTools.Any())
                {
                    _logger.LogWarning("Gemini returned empty text after tool execution ({Tools}). Re-prompting for confirmation.", string.Join(", ", usedTools));
                    
                    if (contents.Any() && contents.Last().Role == "user")
                    {
                        contents.Last().Parts.Add(new GooglePart 
                        { 
                            Text = "\n\nPlease summarize to the user what was just done based on the tool results above." 
                        });
                    }
                    else
                    {
                        contents.Add(new GoogleContent
                        {
                            Role = "user",
                            Parts = new List<GooglePart> { new GooglePart { Text = "Please summarize to the user what was just done based on the tool results above." } }
                        });
                    }

                    try
                    {
                        var confirmConfig = new GenerateContentConfig 
                        { 
                            Tools = null,
                            SystemInstruction = systemInstruction != null ? new GoogleContent
                            {
                                Parts = new List<GooglePart> { new GooglePart { Text = systemInstruction } }
                            } : null
                        };
                        response = await _client.GenerateContentAsync(modelName, contents, confirmConfig);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get Gemini confirmation response after tool execution.");
                    }
                }

                sw.Stop();

                decimal? cost = null;
                if (response.UsageMetadata != null)
                    cost = await SaveBilling(modelName, _apiConfiguration.Name, telegramChatId, telegramUserId, response.UsageMetadata);

                var latency = sw.Elapsed.TotalSeconds;
                var tps = response.UsageMetadata?.TotalTokenCount / latency;

                return new ChatServiceResponse
                {
                    Choices = new List<string> { GetActualResponseText(response) },
                    TotalTokens = response.UsageMetadata?.TotalTokenCount,
                    PromptTokens = response.UsageMetadata?.PromptTokenCount,
                    CompletionTokens = response.UsageMetadata?.CandidatesTokenCount,
                    Cost = cost,
                    LatencySeconds = latency,
                    TokensPerSecond = tps,
                    ProviderName = _apiConfiguration.Name,
                    ModelName = modelName,
                    UsedTools = usedTools.ToList(),
                    ToolCalls = toolCallsList,
                    Thoughts = GetThoughts(response)
                };
            }
        }

        private async Task<decimal?> SaveBilling(string modelName, string providerName, long telegramChatId, long telegramUserId, GenerateContentResponseUsageMetadata usage)
        {
            if (_aiBilingItemRepository == null) return null;

            // Prevent foreign key violation when saving billing without a valid chat/user context (e.g. system errors)
            if (telegramChatId == 0 || telegramUserId == 0)
            {
                _logger.LogWarning("Skipping SaveBilling (Gemini): telegramChatId={ChatId}, telegramUserId={UserId}", telegramChatId, telegramUserId);
                return null;
            }

            if (!OpenAIService._liveModelsCosts.TryGetValue(modelName, out AIModelCost? modelCost))
            {
                // Optionally add a default cost if not found in LiteLLM
                modelCost = null; 
            }

            decimal? cost = modelCost == null
                ? 0M
                : ((usage.PromptTokenCount * modelCost.Input) + (usage.CandidatesTokenCount * modelCost.Output)) / modelCost.PerTokens;

            var dbAIBilingItem = new AIBilingItem
            {
                CreationDate = DateTime.UtcNow,
                ModelName = modelName,
                ProviderName = providerName,
                ModifiedDate = DateTime.UtcNow,
                TelegramChatInfoId = telegramChatId,
                TelegramUserInfoId = telegramUserId,
                PromptTokens = usage.PromptTokenCount,
                CompletionTokens = usage.CandidatesTokenCount,
                TotalTokens = usage.TotalTokenCount,
                Cost = cost
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
                    bool isIgnored = appConfig?.TelegramBotConfiguration?.IgnoredBalanceUserIds?.Contains(telegramUserId) == true ||
                                     appConfig?.TelegramBotConfiguration?.OwnerId == telegramUserId;

                    if (!isIgnored)
                    {
                        user.Balance -= cost ?? 0;
                        user.BalanceModifiedAt = DateTime.UtcNow;
                    }
                    user.LastAiInteraction = DateTime.UtcNow;
                    userRepository.Update(user);
                }
            }
            
            return await _aiBilingItemRepository.SaveChanges() > 0 ? cost : null;
        }

        public Task<ChatServiceResponse> GenerateImage(long chatId, long telegramUserId, string prompt, string? modelName = null)
        {
            throw new NotSupportedException("Gemini provider does not support image generation yet.");
        }

        public Task<string> AudioTranscription(long chatId, long telegramUserId, Stream audio, string audioName, 
            string? model = null, string? prompt = null, AudioResponseFormat responseFormat = AudioResponseFormat.Json, 
            int? temperature = null, string? language = null)
        {
            throw new NotSupportedException("Gemini provider does not support audio transcription yet.");
        }

        public Task<ChatServiceResponse> CreateImageEditAsync(long chatId, long telegramUserId, string filePath, string? messageText)
        {
            throw new NotSupportedException("Gemini provider does not support image editing yet.");
        }

        public async Task<ChatServiceResponse> AnalyzeImageAsync(long chatId, long telegramUserId, string? imageUrl, string? filePath = null, string? prompt = null, string? model = null)
        {
            throw new NotSupportedException("Gemini provider analysis not implemented yet.");
        }

        public async Task<(bool, string)> SetGPTModel(string? modelName, long? userId = null)
        {
            if (string.IsNullOrEmpty(modelName)) return (false, _localizer["EmptyModelName"]);
            _apiConfiguration.ModelName = modelName;
            return (true, string.Empty);
        }

        public Task<string?> GetSelectedModel(long userId)
        {
            return Task.FromResult<string?>(_apiConfiguration.ModelName);
        }
        
        public Task<Stream> TextToSpeech(long chatId, long telegramUserId, string text, string? model = null, string voice = "alloy")
        {
             throw new NotSupportedException("Gemini does not support TTS yet.");
        }
    }
}

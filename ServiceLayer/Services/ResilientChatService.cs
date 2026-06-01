using Microsoft.Extensions.Logging;
using OpenAI.Audio;
using OpenAI.Chat;
using OpenAI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataBaseLayer.Enums;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using ServiceLayer.Constans;
using ServiceLayer.Services.Localization;
using ServiceLayer.Utils;

namespace ServiceLayer.Services;

public class ResilientChatService : IChatService
{
    private readonly IChatServiceFactory _chatServiceFactory;
    private readonly ILogger<ResilientChatService> _logger;
    private readonly IDynamicLocalizer _localizer;
    private readonly IRepository<TelegramUserInfo> _telegramUserInfoRepository;

    public ResilientChatService(IChatServiceFactory chatServiceFactory, ILogger<ResilientChatService> logger, 
        IRepository<TelegramUserInfo> telegramUserInfoRepository, IDynamicLocalizer localizer)
    {
        _chatServiceFactory = chatServiceFactory;
        _logger = logger;
        _telegramUserInfoRepository = telegramUserInfoRepository;
        _localizer = localizer;
    }

    private async Task<T> ExecuteWithFallback<T>(Func<IChatService, Task<T>> action, string methodName, long? userId = null)
    {
        var providers = _chatServiceFactory.GetAvailableProviders().ToList();
        
        if (userId.HasValue)
        {
            var user = await _telegramUserInfoRepository.Get(p => p.Id == userId.Value);
            if (user != null && user.PreferredProvider != ChatStrategy.Auto)
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
                    var preferredProvider = providers.FirstOrDefault(p => p.ProviderType == aiProvider);
                    if (preferredProvider != null)
                    {
                        _logger.LogInformation("Using preferred provider type {ProviderType} for {Method} (Strict Mode)", aiProvider.Value, methodName);
                        var service = _chatServiceFactory.CreateService(preferredProvider.Name);
                        try
                        {
                            return await action(service);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogWarning("Request to preferred provider {Provider} was cancelled for {Method}", preferredProvider.Name, methodName);
                            throw;
                        }
                        catch (NotSupportedException)
                        {
                            _logger.LogWarning("Preferred provider {Provider} does not support {Method}. Falling back to rotation.", preferredProvider.Name, methodName);
                        }
                        catch (Exception ex)
                        {
                            var exception = AiErrorHelper.HandleAndGetException(_logger, ex, preferredProvider.Name, methodName, _localizer);
                            throw;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Preferred provider type {ProviderType} not found in configuration. Falling back to rotation.", aiProvider.Value);
                    }
                }
            }
        }

        var exceptions = new List<Exception>();

        if (!providers.Any())
        {
            throw new InvalidOperationException(_localizer["NoProvidersConfigured"]);
        }

        foreach (var provider in providers)
        {
            try
            {
                _logger.LogInformation("Attempting {Method} with provider: {Provider}", methodName, provider.Name);
                var service = _chatServiceFactory.CreateService(provider.Name);
                return await action(service);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Request to provider {Provider} was cancelled for {Method}", provider.Name, methodName);
                throw;
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning("Provider {Provider} does not support {Method}: {Message}", provider.Name, methodName, ex.Message);
                exceptions.Add(ex);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Provider {Provider} failed for {Method}", provider.Name, methodName);
                exceptions.Add(ex);
            }
        }

        var combinedMessage = string.Join(" | ", exceptions.Select(e => e.Message).Distinct());
        throw new AggregateException($"All available providers returned an error for {methodName}: {combinedMessage}", exceptions);
    }

    public Task<string> Ask(long chatId, long userId, string message)
        => ExecuteWithFallback(s => s.Ask(chatId, userId, message), nameof(Ask), userId);

    public Task<IReadOnlyList<Model>> GetAvailibleModels(long? userId = null, bool validateModels = true)
        => ExecuteWithFallback(s => s.GetAvailibleModels(userId, validateModels), nameof(GetAvailibleModels), userId);

    public async Task<ChatServiceResponse> SendMessages2ChatAsync(long telegramChatId, long telegramUserId, List<AiMessage> messages, string? model = null)
    {
        var selectedModel = model;
        if (string.IsNullOrEmpty(selectedModel))
        {
            var user = await _telegramUserInfoRepository.Get(p => p.Id == telegramUserId);
            selectedModel = user?.SelectedModel;
        }
        return await ExecuteWithFallback(s => s.SendMessages2ChatAsync(telegramChatId, telegramUserId, messages, selectedModel), nameof(SendMessages2ChatAsync), telegramUserId);
    }

    public async Task<ChatServiceResponse> GenerateImage(long chatId, long telegramUserId, string prompt, string? modelName = null)
    {
        return await ExecuteWithFallback(s => s.GenerateImage(chatId, telegramUserId, prompt, modelName), nameof(GenerateImage), telegramUserId);
    }

    public Task<string> AudioTranscription(long chatId, long telegramUserId, Stream audio, string audioName, string? model = null, string? prompt = null, AudioResponseFormat responseFormat = AudioResponseFormat.Json, int? temperature = null, string? language = null)
        => ExecuteWithFallback(s => s.AudioTranscription(chatId, telegramUserId, audio, audioName, model, prompt, responseFormat, temperature, language), nameof(AudioTranscription), telegramUserId);

    public Task<ChatServiceResponse> CreateImageEditAsync(long chatId, long telegramUserId, string filePath, string? messageText)
        => ExecuteWithFallback(s => s.CreateImageEditAsync(chatId, telegramUserId, filePath, messageText), nameof(CreateImageEditAsync), telegramUserId);

    public Task<ChatServiceResponse> AnalyzeImageAsync(long chatId, long telegramUserId, string? imageUrl, string? filePath = null, string? prompt = null, string? model = null)
        => ExecuteWithFallback(s => s.AnalyzeImageAsync(chatId, telegramUserId, imageUrl, filePath, prompt, model), nameof(AnalyzeImageAsync), telegramUserId);

    public Task<Stream> TextToSpeech(long chatId, long telegramUserId, string text, string? model = null, string voice = "alloy")
        => ExecuteWithFallback(s => s.TextToSpeech(chatId, telegramUserId, text, model, voice), nameof(TextToSpeech), telegramUserId);

    public async Task<(bool, string)> SetGPTModel(string? modelName, long? userId = null)
    {
        var (isSuccess, error) = await ExecuteWithFallback(s => s.SetGPTModel(modelName, userId), nameof(SetGPTModel), userId);
        
        if (isSuccess && userId.HasValue)
        {
            var user = await _telegramUserInfoRepository.Get(p => p.Id == userId.Value);
            if (user != null)
            {
                user.SelectedModel = modelName;
                _telegramUserInfoRepository.Update(user);
                await _telegramUserInfoRepository.SaveChanges();
            }
        }
        
        return (isSuccess, error);
    }

    public async Task<string?> GetSelectedModel(long userId)
    {
        var user = await _telegramUserInfoRepository.Get(p => p.Id == userId);
        if (user != null && !string.IsNullOrEmpty(user.SelectedModel))
        {
            return user.SelectedModel;
        }

        return await ExecuteWithFallback(s => s.GetSelectedModel(userId), nameof(GetSelectedModel), userId);
    }

    public async Task RefreshAvailibleModels(bool validate = true)
    {
        var providers = _chatServiceFactory.GetAvailableProviders().ToList();
        var exceptions = new List<Exception>();

        foreach (var provider in providers)
        {
            try
            {
                _logger.LogInformation("Refreshing models for provider: {0} (validate={1})", provider.Name, validate);
                var service = _chatServiceFactory.CreateService(provider.Name);
                await service.RefreshAvailibleModels(validate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh models for provider {Provider}", provider.Name);
                exceptions.Add(ex);
            }
        }

        if (exceptions.Any())
        {
            throw new AggregateException("Some providers failed to refresh models.", exceptions);
        }
    }
}

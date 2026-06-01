using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceLayer.Constans;
using ServiceLayer.IntegrationTests.Fixtures;
using ServiceLayer.Services;
using ServiceLayer.Services.Localization;
using System.Linq;
using Xunit;

namespace ServiceLayer.IntegrationTests.Services;

public class ImageGenerationLogicTests : IClassFixture<TestAppFixture>
{
    private const string TestPrompt = "draw small point";
    private readonly IServiceProvider _serviceProvider;
    private readonly IRepository<TelegramUserInfo> _userRepo;
    private readonly ILogger<ResilientChatService> _resilientLogger;
    private readonly IChatServiceFactory _chatServiceFactory;
    private readonly AppSettings _appSettings;

    public ImageGenerationLogicTests(TestAppFixture fixture)
    {
        _serviceProvider = fixture.ServiceProvider;
        _appSettings = _serviceProvider.GetRequiredService<AppSettings>();
        
        // Check API key before creating services (validation happens in constructor)
        SkipIfApiKeyNotConfigured();
        
        _userRepo = _serviceProvider.GetRequiredService<IRepository<TelegramUserInfo>>();
        _resilientLogger = _serviceProvider.GetRequiredService<ILogger<ResilientChatService>>();
        _chatServiceFactory = _serviceProvider.GetRequiredService<IChatServiceFactory>();
    }

    private void SkipIfApiKeyNotConfigured()
    {
        var apiKey = _appSettings.TelegramBotConfiguration?.AiSettings?.ChatProviders
            .FirstOrDefault(p => p.ProviderType == AiProvider.OpenAI)?.ApiKey;
        
        if (string.IsNullOrWhiteSpace(apiKey) ||
            apiKey == "sk-test-placeholder" ||
            apiKey.StartsWith("sk-test-"))
        {
            throw new SkipException("Skipped: OpenAI API key is not configured or uses placeholder value. " +
                "Copy Configs/appsettings.Test.json.example to Configs/appsettings.Test.json and fill in your real API key.");
        }
    }

    /// <summary>
    /// Verifies that each configured provider either:
    ///   - successfully generates an image (returns a URL), or
    ///   - explicitly declares it does not support image generation (NotSupportedException).
    /// Any other exception (auth errors, network errors, etc.) fails the test.
    /// </summary>
    [SkippableFact]
    public async Task TestAllConfiguredProviders_ImageGeneration()
    {
        SkipIfApiKeyNotConfigured();
        
        var providers = _appSettings.TelegramBotConfiguration.AiSettings.ChatProviders;

        foreach (var provider in providers)
        {
            var service = _chatServiceFactory.CreateService(provider.Name);
            try
            {
                var result = await service.GenerateImage(0, 0, TestPrompt);
                Assert.NotNull(result);
                Assert.NotEmpty(result.Choices);
                Assert.True(result.Choices[0].StartsWith("http") || result.Choices[0].StartsWith("iVBORw"), "Expected image URL or base64-encoded PNG");
            }
            catch (NotSupportedException)
            {
                // Expected for providers that don't support image generation (Gemini, Grok, DeepSeek, etc.)
            }
        }
    }

    /// <summary>
    /// Real integration test of the fallback logic in ResilientChatService.
    /// Uses real providers: the service iterates through all configured providers,
    /// skips those that throw NotSupportedException, and succeeds when it reaches
    /// one that supports image generation (typically OpenAI/dall-e).
    /// </summary>
    [SkippableFact]
    public async Task ResilientGenerateImage_ShouldSucceedViaProviderFallback()
    {
        SkipIfApiKeyNotConfigured();
        
        // Arrange — all real dependencies, no mocks
        var localizer = _serviceProvider.GetRequiredService<IDynamicLocalizer>();
        var resilientService = new ResilientChatService(_chatServiceFactory, _resilientLogger, _userRepo, localizer);

        // Act — ResilientChatService will try providers in order until one supports image generation
        var result = await resilientService.GenerateImage(1, 1, TestPrompt);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Choices);
        Assert.True(result.Choices[0].StartsWith("http") || result.Choices[0].StartsWith("iVBORw"), "Expected image URL or base64-encoded PNG");
    }
}

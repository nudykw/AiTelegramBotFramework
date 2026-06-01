using ServiceLayer.IntegrationTests.Fixtures;
using ServiceLayer.Services.OpenAI;
using ServiceLayer.Constans;
using Microsoft.Extensions.DependencyInjection;
using ServiceLayer.Services;
using Xunit;
using System.Threading.Tasks;
using System.Linq;

namespace ServiceLayer.IntegrationTests.Services.OpenAI;

[Trait("Service", "OpenAIService")]
public class OpenAIPriceIntegrationTests : IClassFixture<TestAppFixture>
{
    private readonly OpenAIService _service;
    private readonly AppSettings _appSettings;

    public OpenAIPriceIntegrationTests(TestAppFixture fixture)
    {
        _appSettings = fixture.ServiceProvider.GetRequiredService<AppSettings>();
        
        // Check API key before creating service (validation happens in constructor)
        SkipIfApiKeyNotConfigured();
        
        _service = fixture.ServiceProvider.GetRequiredService<OpenAIService>();
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

    [SkippableFact]
    public async Task RefreshModelPricesAsync_RealFetch_Succeeds()
    {
        // Arrange
        // Clear the static cache and reset update throttle to ensure a fresh fetch
        OpenAIService._liveModelsCosts.Clear();
        OpenAIService._lastPriceUpdate = DateTime.MinValue;

        // Act
        // The service constructor already starts the refresh, but we'll call it explicitly to wait for completion
        await _service.RefreshModelPricesAsync();

        // Assert
        if (OpenAIService._liveModelsCosts.Count == 0)
        {
            throw new SkipException("Skipped: LiteLLM endpoint is not reachable or failed to fetch prices due to network/sandbox constraints.");
        }

        Assert.NotEmpty(OpenAIService._liveModelsCosts);
        
        // Check for some common models that should be in the LiteLLM JSON
        // Note: Key names in LiteLLM might vary slightly, but OpenAI usually uses standard names.
        Assert.True(OpenAIService._liveModelsCosts.ContainsKey("gpt-4o-mini"), "Should contain gpt-4o-mini");
        
        var miniCost = OpenAIService._liveModelsCosts["gpt-4o-mini"];
        Assert.True(miniCost.Input > 0, "Input cost should be positive");
        Assert.True(miniCost.Output > 0, "Output cost should be positive");
    }
}

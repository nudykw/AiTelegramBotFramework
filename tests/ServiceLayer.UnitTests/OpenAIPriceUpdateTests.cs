using Moq;
using Moq.Protected;
using Microsoft.Extensions.Logging;
using ServiceLayer.Services.OpenAI;
using ServiceLayer.Services;
using ServiceLayer.Constans;
using System.Net;
using System.Text.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ServiceLayer.Services.Telegram.Configuretions;
using ServiceLayer.Services.Localization;
using ServiceLayer.Services.Mcp;

namespace ServiceLayer.UnitTests.Services.OpenAI;

[Trait("Service", "OpenAIService")]
public class OpenAIPriceUpdateTests
{
    [Fact]
    public async Task RefreshModelPricesAsync_WithValidJson_UpdatesLiveModelsCosts()
    {
        // Arrange
        var mockServiceProvider = new Mock<IServiceProvider>();
        var mockLogger = new Mock<ILogger<OpenAIService>>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockLocalizer = new Mock<IDynamicLocalizer>();
        var appSettings = new AppSettings
        {
            TelegramBotConfiguration = new TelegramBotConfiguration { BotToken = "test-bot" }
        };
        var mockMcpManager = new Mock<McpServerManager>(mockServiceProvider.Object, new Mock<ILogger<McpServerManager>>().Object, appSettings);
        var mockHandler = new Mock<HttpMessageHandler>();

        var json = "{\"gpt-4o-mini\": {\"input_cost_per_token\": 0.00000015, \"output_cost_per_token\": 0.0000006, \"litellm_provider\": \"openai\"}}";

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", 
                ItExpr.IsAny<HttpRequestMessage>(), 
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json)
            });

        var httpClient = new HttpClient(mockHandler.Object);
        mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);


        // Clear the static cache before testing
        OpenAIService._liveModelsCosts.Clear();
        typeof(OpenAIService)
            .GetField("_lastPriceUpdate", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(null, DateTime.MinValue);

        var config = new ChatProviderConfig
        {
            Name = "OpenAI",
            ProviderType = AiProvider.OpenAI,
            ApiKey = "sk-test-key-1234567890",
            ModelName = "gpt-4o-mini"
        };

        var service = new OpenAIService(mockServiceProvider.Object, mockLogger.Object, config, mockHttpClientFactory.Object, mockLocalizer.Object, mockMcpManager.Object);

        // Act
        // Reset clock again right before manual call to ensure it runs even if constructor task started
        typeof(OpenAIService)
            .GetField("_lastPriceUpdate", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(null, DateTime.MinValue);
            
        await service.RefreshModelPricesAsync();

        // Assert with retry to handle background race from constructor
        bool found = false;
        for (int i = 0; i < 20; i++)
        {
            if (OpenAIService._liveModelsCosts.ContainsKey("gpt-4o-mini"))
            {
                found = true;
                break;
            }
            await Task.Delay(100);
        }

        Assert.True(found, "Model gpt-4o-mini should be in _liveModelsCosts");
        var cost = OpenAIService._liveModelsCosts["gpt-4o-mini"];
        Assert.Equal(0.00015M, cost.Input);
        Assert.Equal(0.0006M, cost.Output);
    }
}

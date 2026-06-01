using Moq;
using Microsoft.Extensions.Options;
using ServiceLayer.Services.GeminiChat.DotNet;
using ServiceLayer.Constans;
using ServiceLayer.Services;
using Microsoft.Extensions.Logging;
using Google.GenAI.Types;
using OpenAIModel = OpenAI.Models.Model;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Linq;
using ServiceLayer.Services.Localization;
using ServiceLayer.Services.Mcp;

namespace ServiceLayer.UnitTests.Services.GeminiChat
{
    public class ChatGeminiServiceTests
    {
        private readonly Mock<IGeminiClient> _mockClient;
        private readonly Mock<ILogger<ChatGeminiService>> _mockLogger;
        private readonly Mock<IServiceProvider> _mockServiceProvider;
        private readonly Mock<IDynamicLocalizer> _mockLocalizer;
        private readonly Mock<McpServerManager> _mockMcpManager;
        private readonly ChatProviderConfig _config;

        public ChatGeminiServiceTests()
        {
            _mockClient = new Mock<IGeminiClient>();
            _mockLogger = new Mock<ILogger<ChatGeminiService>>();
            _mockServiceProvider = new Mock<IServiceProvider>();
            _mockLocalizer = new Mock<IDynamicLocalizer>();
            _config = new ChatProviderConfig
            {
                Name = "Gemini",
                ApiKey = "test-key",
                ProviderType = AiProvider.Gemini
            };
            var appSettings = new AppSettings { TelegramBotConfiguration = new ServiceLayer.Services.Telegram.Configuretions.TelegramBotConfiguration() };
            _mockMcpManager = new Mock<McpServerManager>(_mockServiceProvider.Object, new Mock<ILogger<McpServerManager>>().Object, appSettings);
            
            var mockOptions = new Mock<IOptions<AppSettings>>();
            mockOptions.Setup(o => o.Value).Returns(appSettings);
            _mockServiceProvider.Setup(s => s.GetService(typeof(IOptions<AppSettings>))).Returns(mockOptions.Object);
        }

        [Fact]
        public async Task GetAvailibleModels_ReturnsFilteredGeminiModels()
        {
            // Arrange
            var models = new List<Model>
            {
                new Model { Name = "models/gemini-1.5-flash", SupportedActions = new List<string> { "generateContent" } },
                new Model { Name = "models/gemini-1.5-pro", SupportedActions = new List<string> { "generateContent" } },
                new Model { Name = "models/embedding-001", SupportedActions = new List<string> { "embedContent" } },
                new Model { Name = "models/other-model", SupportedActions = new List<string> { "generateContent" } }
            };

            _mockClient.Setup(c => c.ListModelsAsync()).Returns(ToAsyncEnumerable(models));

            var service = new ChatGeminiService(_mockServiceProvider.Object, _mockLogger.Object, _config, _mockClient.Object, _mockLocalizer.Object, _mockMcpManager.Object);

            // Act
            var result = await service.GetAvailibleModels(null, false);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Contains(result, m => m.Id == "gemini-1.5-flash");
            Assert.Contains(result, m => m.Id == "gemini-1.5-pro");
        }

        [Fact]
        public async Task GetAvailibleModels_CachesResults()
        {
            // Arrange
            var models = new List<Model>
            {
                new Model { Name = "models/gemini-1.5-flash", SupportedActions = new List<string> { "generateContent" } }
            };

            _mockClient.Setup(c => c.ListModelsAsync()).Returns(ToAsyncEnumerable(models));

            var service = new ChatGeminiService(_mockServiceProvider.Object, _mockLogger.Object, _config, _mockClient.Object, _mockLocalizer.Object, _mockMcpManager.Object);

            // Act
            await service.GetAvailibleModels(null, false);
            await service.GetAvailibleModels(null, false);

            // Assert
            _mockClient.Verify(c => c.ListModelsAsync(), Times.Once);
        }

        [Fact]
        public async Task GetAvailibleModels_ValidatesModelsWhenRequested()
        {
            // Arrange
            var models = new List<Model>
            {
                new Model { Name = "models/gemini-1.5-flash", SupportedActions = new List<string> { "generateContent" } },
                new Model { Name = "models/gemini-1.5-pro", SupportedActions = new List<string> { "generateContent" } }
            };

            _mockClient.Setup(c => c.ListModelsAsync()).Returns(ToAsyncEnumerable(models));
            
            // Mock to throw for ANYTHING containing "pro"
            _mockClient.Setup(c => c.GenerateContentAsync(It.Is<string>(s => s.Contains("pro")), It.IsAny<List<Content>>(), It.IsAny<GenerateContentConfig>()))
                .ThrowsAsync(new Exception("Validation failed"));
            _mockClient.Setup(c => c.GenerateContentAsync(It.Is<string>(s => s.Contains("pro")), It.IsAny<string>()))
                .ThrowsAsync(new Exception("Validation failed"));

            var service = new ChatGeminiService(_mockServiceProvider.Object, _mockLogger.Object, _config, _mockClient.Object, _mockLocalizer.Object, _mockMcpManager.Object);

            // Act
            var result = await service.GetAvailibleModels(null, true);

            // Assert
            Assert.Single(result);
            Assert.Contains(result, m => m.Id == "gemini-1.5-flash");
        }

        private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
        {
            foreach (var item in source)
            {
                yield return item;
            }
        }
    }
}

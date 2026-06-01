using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ServiceLayer.Services;
using ServiceLayer.Services.MessageProcessor;
using ServiceLayer.Services.Telegram;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using ServiceLayer.Services.Localization;
using Xunit;
using OpenAI.Chat;
using DataBaseLayer.Enums;
using AiMessage = OpenAI.Chat.Message;
using ServiceLayer.Services.Mcp;
using ModelContextProtocol.Client;
using Telegram.Bot;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Microsoft.Extensions.Caching.Memory;
using ServiceLayer.Models;

using MessageProcessorClass = ServiceLayer.Services.MessageProcessor.MessageProcessor;

namespace ServiceLayer.UnitTests.Services.MessageProcessor
{
    public class MessageProcessorTests
    {
        private readonly Mock<IServiceProvider> _serviceProviderMock = new();
        private readonly Mock<ILogger<MessageProcessorClass>> _loggerMock = new();
        private readonly Mock<IChatService> _chatServiceMock = new();
        private readonly Mock<IChatServiceFactory> _chatServiceFactoryMock = new();
        private readonly Mock<ITelegramBotClient> _botClientMock = new();
        private readonly Mock<IRepository<TelegramUserInfo>> _userInfoRepositoryMock = new();
        private readonly Mock<IDynamicLocalizer> _localizerMock = new();
        private readonly Mock<IRepository<HistoryMessage>> _historyRepositoryMock = new();
        private readonly Mock<IReactionService> _reactionServiceMock = new();
        private readonly Mock<ServiceLayer.Services.Memory.IBotSelfAwarenessService> _botSelfAwarenessServiceMock = new();
        private readonly Mock<McpServerManager> _mcpManagerMock;

        private readonly AppSettings _appSettings;
        private readonly MessageProcessorClass _sut;

        public MessageProcessorTests()
        {
            // Mock AppSettings
            _appSettings = new AppSettings { 
                TelegramBotConfiguration = new ServiceLayer.Services.Telegram.Configuretions.TelegramBotConfiguration
                {
                    OwnerId = 7342855906L,
                    InitialBalance = 0.1M,
                    DefaultParseMode = global::Telegram.Bot.Types.Enums.ParseMode.Html
                }
            };
            var optionsMock = new Mock<IOptions<AppSettings>>();
            optionsMock.Setup(x => x.Value).Returns(_appSettings);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IOptions<AppSettings>)))
                .Returns(optionsMock.Object);

            _mcpManagerMock = new Mock<McpServerManager>(
                _serviceProviderMock.Object,
                new Mock<ILogger<McpServerManager>>().Object,
                _appSettings);
            _mcpManagerMock.Setup(m => m.GetAllToolsAsync())
                .ReturnsAsync(new List<McpClientTool>());

            _chatServiceFactoryMock.Setup(f => f.CreateService(It.IsAny<string>()))
                .Returns(_chatServiceMock.Object);

            _reactionServiceMock.Setup(r => r.GetReactionCounts(It.IsAny<long>(), It.IsAny<long>()))
                .ReturnsAsync(new Dictionary<MessageReactionType, int>());

            _botSelfAwarenessServiceMock.Setup(s => s.GetBotSystemSummaryPromptAsync(It.IsAny<long?>()))
                .ReturnsAsync("Mock prompt containing create_scheduled_newsletter and EVERGREEN");

            _localizerMock.Setup(l => l[It.IsAny<string>()])
                .Returns((string key, object[] args) => key);

            _sut = new MessageProcessorClass(
                _serviceProviderMock.Object,
                _loggerMock.Object,
                _historyRepositoryMock.Object,
                _chatServiceMock.Object,
                _chatServiceFactoryMock.Object,
                _botClientMock.Object,
                _userInfoRepositoryMock.Object,
                _localizerMock.Object,
                _reactionServiceMock.Object,
                new Mock<ServiceLayer.Services.MessageProcessor.ISummaryService>().Object,
                new Mock<ServiceLayer.Services.Memory.ISemanticMemoryService>().Object,
                _botSelfAwarenessServiceMock.Object,
                _mcpManagerMock.Object);
        }

        [Theory]
        [InlineData("German", "de")]
        [InlineData("Spanish", "es")]
        [InlineData("fr", "fr")]
        [InlineData("Українська", "uk")]
        public async Task IdentifyLanguage_ShouldReturnCorrectCode_WhenAiResponds(string input, string expectedCode)
        {
            // Arrange
            var chatId = 1L;
            var userId = 2L;
            _chatServiceMock.Setup(c => c.Ask(chatId, userId, It.Is<string>(s => s.Contains(input))))
                .ReturnsAsync(expectedCode);

            // Act
            var result = await _sut.IdentifyLanguage(chatId, userId, input);

            // Assert
            Assert.Equal(expectedCode, result);
        }

        [Fact]
        public async Task IdentifyLanguage_ShouldReturnNull_WhenInputIsEmpty()
        {
            // Act
            var result = await _sut.IdentifyLanguage(1, 1, "");

            // Assert
            Assert.Null(result);
            _chatServiceMock.Verify(c => c.Ask(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task IdentifyLanguage_ShouldFallbackToEn_WhenAiReturnsInvalidResponse()
        {
            // Arrange
            _chatServiceMock.Setup(c => c.Ask(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>()))
                .ReturnsAsync("I am not sure, but maybe it is Tolkien language");

            // Act
            var result = await _sut.IdentifyLanguage(1, 1, "Some gibberish");

            // Assert
            Assert.Equal("en", result);
        }

        [Fact]
        public async Task GetBalanceDisplay_ShouldReturnCorrectFormatForRegularUser()
        {
            // Arrange
            var userId = 123L;
            var user = new TelegramUserInfo { Id = userId, Balance = 0.005M, FirstName = "Test", IsBot = false };
            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);

            // Act
            var result = await _sut.GetBalanceDisplay(userId);

            // Assert
            Assert.Equal(" | B: $0.0050", result);
        }

        [Fact]
        public async Task GetBalanceDisplay_ShouldReturnUnlimitedForOwner()
        {
            // Arrange
            var ownerId = 7342855906L;
            var user = new TelegramUserInfo { Id = ownerId, Balance = 0.0M, FirstName = "Owner", IsBot = false };
            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);

            // Act
            var result = await _sut.GetBalanceDisplay(ownerId);

            // Assert
            Assert.Equal(" (Unlimited)", result);
        }
        [Fact]
        public async Task ProcessMessage_ShouldNotIncludeHistory_WhenParentMessageIdIsNull()
        {
            // Arrange
            long chatId = 123;
            long messageId = 456;
            long userId = 789;
            string messageText = "Hello";

            _historyRepositoryMock.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<HistoryMessage, bool>>>()))
                .ReturnsAsync((HistoryMessage?)null);

            _chatServiceMock.Setup(c => c.SendMessages2ChatAsync(chatId, userId, It.IsAny<List<AiMessage>>()))
                .ReturnsAsync(new ChatServiceResponse { Choices = new List<string> { "Hi" }, ProviderName = "Mock", ModelName = "Mock" });

            // Using JSON deserialization to set read-only MessageId
            var messageJson = "{\"message_id\":789,\"date\":1715500000,\"chat\":{\"id\":123,\"type\":\"private\"}}";
            var mockMessage = System.Text.Json.JsonSerializer.Deserialize<global::Telegram.Bot.Types.Message>(messageJson);

            // Mock the underlying SendRequest instead of the extension method SendMessage
            _botClientMock.Setup(b => b.SendRequest(It.IsAny<global::Telegram.Bot.Requests.Abstractions.IRequest<global::Telegram.Bot.Types.Message>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockMessage!);
            
            // Also mock EditMessageReplyMarkup as it is called at the end
            _botClientMock.Setup(b => b.SendRequest(It.IsAny<global::Telegram.Bot.Requests.Abstractions.IRequest<global::Telegram.Bot.Types.Message?>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockMessage!);

            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<TelegramUserInfo, bool>>>()))
                .ReturnsAsync(new TelegramUserInfo { Id = userId, LanguageCode = "en", FirstName = "Test", IsBot = false });

            // Act
            await _sut.ProcessMessage(chatId, messageId, null, userId, messageText, CancellationToken.None);

            // Assert
            // 2 messages: [0] newsletter-tools system prompt, [1] the user message
            _chatServiceMock.Verify(c => c.SendMessages2ChatAsync(chatId, userId, It.Is<List<AiMessage>>(q =>
                q.Count == 2 &&
                q[0].Role == global::OpenAI.Role.System &&
                q[1].Role == global::OpenAI.Role.User), null), Times.Once);
        }

        [Theory]
        [InlineData("https://example.com/image.png", true, "https://example.com/image.png")]
        [InlineData("https://example.com/photo.JPEG", true, "https://example.com/photo.JPEG")]
        [InlineData("https://lh3.googleusercontent.com/a/AGNmyxY", true, "https://lh3.googleusercontent.com/a/AGNmyxY")]
        [InlineData("https://images.unsplash.com/photo-1234?w=500", true, "https://images.unsplash.com/photo-1234?w=500")]
        [InlineData("https://example.com/image.webp?width=300", true, "https://example.com/image.webp?width=300")]
        [InlineData("https://accounts.google.com/o/oauth2/auth\",.", false, "https://accounts.google.com/o/oauth2/auth")]
        [InlineData("https://example.com/page.html", false, "https://example.com/page.html")]
        [InlineData("not-a-url", false, "not-a-url")]
        public void IsImageUrl_ShouldCorrectlyValidateAndCleanUrls(string inputUrl, bool expectedResult, string expectedCleanedUrl)
        {
            // Arrange
            var method = typeof(MessageProcessorClass).GetMethod("IsImageUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(method);

            // Act
            var parameters = new object[] { inputUrl, null! };
            var result = (bool)method.Invoke(_sut, parameters)!;
            var cleanedUrl = (string)parameters[1];

            // Assert
            Assert.Equal(expectedResult, result);
            Assert.Equal(expectedCleanedUrl, cleanedUrl);
        }

        private McpClientTool CreateMockClientTool(string name)
        {
            var coreAssembly = Assembly.Load("ModelContextProtocol.Core");
            var implType = coreAssembly.GetTypes().FirstOrDefault(t => t.Name == "McpClientImpl")
                ?? throw new InvalidOperationException("McpClientImpl type not found.");

            var transport = new FakeTransportForTests();
            var options = new McpClientOptions();
            var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

            var dummyClient = (McpClient)Activator.CreateInstance(
                implType, 
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, 
                null, 
                new object[] { transport, "fake-client", options, loggerFactory }, 
                null)!;

            var tool = new Tool
            {
                Name = name,
                Description = "A test tool",
                InputSchema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement
            };

            return new McpClientTool(dummyClient, tool, new JsonSerializerOptions());
        }

        [Fact]
        public async Task BuildMcpToolsKeyboardAsync_ShouldMarkEnabledAndDisabledToolsCorrectly()
        {
            // Arrange
            var userId = 123L;
            _localizerMock.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()])
                .Returns((string key, object[] args) => 
                {
                    if (key == "McpTools_GroupHeader") return $"📁 {args[0]}";
                    return key;
                });

            var user = new TelegramUserInfo 
            { 
                Id = userId, 
                Balance = 1.0M, 
                FirstName = "Test", 
                IsBot = false,
                DisabledTools = "tool1" // tool1 is disabled
            };
            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);

            var toolsList = new List<McpClientTool>
            {
                CreateMockClientTool("tool1"),
                CreateMockClientTool("tool2")
            };
            _mcpManagerMock.Setup(m => m.GetAllToolsAsync()).ReturnsAsync(toolsList);

            // Act
            var keyboard = await _sut.BuildMcpToolsKeyboardAsync(userId);

            // Assert
            Assert.NotNull(keyboard);
            var rows = keyboard.InlineKeyboard.ToList();
            Assert.Equal(3, rows.Count); // 1 group header + 2 tools

            // group header
            Assert.Contains("📁 EXTERNAL", rows[0].First().Text);
            // tool1 is disabled, should have ❌
            Assert.Contains("❌ tool1", rows[1].First().Text);
            // tool2 is enabled, should have ✅
            Assert.Contains("✅ tool2", rows[2].First().Text);
        }

        [Fact]
        public async Task ProcessMessage_ProactiveWarning_ShouldBlockRequestAndTriggerKeyboard_WhenActiveToolsExceedLimit()
        {
            // Arrange
            long chatId = 123;
            long messageId = 456;
            long userId = 789;
            string messageText = "Hello";

            var user = new TelegramUserInfo 
            { 
                Id = userId, 
                LanguageCode = "en", 
                FirstName = "Test", 
                IsBot = false,
                Balance = 1.0M,
                DisabledTools = "" // no disabled tools
            };
            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);

            // Setup 130 tools (limit is 128)
            var toolsList = new List<McpClientTool>();
            for (int i = 1; i <= 130; i++)
            {
                toolsList.Add(CreateMockClientTool($"tool{i}"));
            }
            _mcpManagerMock.Setup(m => m.GetAllToolsAsync()).ReturnsAsync(toolsList);

            // Mock TelegramBotClient SendMessage
            _botClientMock.Setup(b => b.SendRequest(It.IsAny<global::Telegram.Bot.Requests.SendMessageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new global::Telegram.Bot.Types.Message());

            // Act
            await _sut.ProcessMessage(chatId, messageId, null, userId, messageText, CancellationToken.None);

            // Assert
            // 1. Verify warning message was sent
            _botClientMock.Verify(b => b.SendRequest(It.Is<global::Telegram.Bot.Requests.SendMessageRequest>(r => 
                r.ChatId == chatId && 
                r.Text.Contains("Tools limit exceeded")
            ), It.IsAny<CancellationToken>()), Times.Once);

            // 2. Verify AI request was blocked (i.e. SendMessages2ChatAsync was NEVER called)
            _chatServiceMock.Verify(c => c.SendMessages2ChatAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<List<AiMessage>>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public void SplitHtmlText_ShouldCorrectlyBalanceTags()
        {
            // Arrange
            string html = "<b>This is a <i>very</i> long text with <code>code block</code> inside it.</b>";
            int maxLength = 50;

            // Act
            var chunks = _sut.SplitHtmlText(html, maxLength).ToList();

            // Assert
            Assert.NotEmpty(chunks);
            foreach (var chunk in chunks)
            {
                int openB = chunk.Split("<b>").Length - 1;
                int closeB = chunk.Split("</b>").Length - 1;
                Assert.Equal(openB, closeB);

                int openI = chunk.Split("<i>").Length - 1;
                int closeI = chunk.Split("</i>").Length - 1;
                Assert.Equal(openI, closeI);

                int openCode = chunk.Split("<code>").Length - 1;
                int closeCode = chunk.Split("</code>").Length - 1;
                Assert.Equal(openCode, closeCode);
            }
        }

        [Fact]
        public async Task GetReactionMarkup_ShouldNotAppendThoughtsButton_WhenUserIsNotOwner()
        {
            // Arrange
            long chatId = 123L;
            long messageId = 456L;
            long nonOwnerUserId = 999L; // OwnerId is 7342855906L

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            memoryCache.Set($"AiThoughtsToken:{messageId}", "secure-token-value");
            _serviceProviderMock.Setup(x => x.GetService(typeof(IMemoryCache))).Returns(memoryCache);

            // Act
            var result = await _sut.GetReactionMarkup(chatId, messageId, nonOwnerUserId);

            // Assert
            Assert.NotNull(result);
            var buttons = result.InlineKeyboard.SelectMany(row => row).ToList();
            // Should only have the standard reaction buttons, no thoughts button
            Assert.DoesNotContain(buttons, btn => btn.Text.Contains("ThoughtsAndStatsButton"));
        }

        [Fact]
        public async Task GetReactionMarkup_ShouldAppendThoughtsButton_WhenUserIsOwnerAndTokenInCache()
        {
            // Arrange
            long chatId = 123L;
            long messageId = 456L;
            long ownerUserId = 7342855906L; // OwnerId configured in constructor
            string token = "test-secure-token-123";

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            memoryCache.Set($"AiThoughtsToken:{messageId}", token);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IMemoryCache))).Returns(memoryCache);

            // Configure AppSettings BaseApiUrl
            _appSettings.TelegramBotConfiguration.BaseApiUrl = "https://mybot.com/";

            // Act
            var result = await _sut.GetReactionMarkup(chatId, messageId, ownerUserId);

            // Assert
            Assert.NotNull(result);
            var buttons = result.InlineKeyboard.SelectMany(row => row).ToList();
            var thoughtsButton = buttons.FirstOrDefault(btn => btn.Text.Contains("ThoughtsAndStatsButton"));
            
            Assert.NotNull(thoughtsButton);
            Assert.Equal("ThoughtsAndStatsButton", thoughtsButton.Text);
            Assert.Equal($"https://mybot.com/admin/thoughts/{messageId}?token={token}", thoughtsButton.Url);
        }

        [Fact]
        public async Task ProcessMessage_ShouldCacheStatsAndToken_WhenThoughtsAreEmptyButToolsAreUsed()
        {
            // Arrange
            long chatId = 123L;
            long messageId = 456L;
            long userId = 789L;
            string messageText = "Hello";

            _historyRepositoryMock.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<HistoryMessage, bool>>>()))
                .ReturnsAsync((HistoryMessage?)null);

            // Mock ChatServiceResponse with empty thoughts but used tools
            var response = new ChatServiceResponse 
            { 
                Choices = new List<string> { "Hi" }, 
                ProviderName = "MockProvider", 
                ModelName = "MockModel",
                Thoughts = null,
                UsedTools = new List<string> { "mcp_tool_abc" }
            };

            _chatServiceMock.Setup(c => c.SendMessages2ChatAsync(chatId, userId, It.IsAny<List<AiMessage>>(), It.IsAny<string?>()))
                .ReturnsAsync(response);

            var messageJson = "{\"message_id\":456,\"date\":1715500000,\"chat\":{\"id\":123,\"type\":\"private\"}}";
            var mockMessage = System.Text.Json.JsonSerializer.Deserialize<global::Telegram.Bot.Types.Message>(messageJson);

            _botClientMock.Setup(b => b.SendRequest(It.IsAny<global::Telegram.Bot.Requests.Abstractions.IRequest<global::Telegram.Bot.Types.Message>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockMessage!);
            
            _botClientMock.Setup(b => b.SendRequest(It.IsAny<global::Telegram.Bot.Requests.Abstractions.IRequest<global::Telegram.Bot.Types.Message?>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockMessage!);

            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<TelegramUserInfo, bool>>>()))
                .ReturnsAsync(new TelegramUserInfo { Id = userId, LanguageCode = "en", FirstName = "Test", IsBot = false, Balance = 100.0M });

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            _serviceProviderMock.Setup(x => x.GetService(typeof(IMemoryCache))).Returns(memoryCache);

            // Act
            await _sut.ProcessMessage(chatId, messageId, null, userId, messageText, CancellationToken.None);

            // Assert
            var cacheKey = "AiThoughts:456";
            var tokenKey = "AiThoughtsToken:456";

            Assert.True(memoryCache.TryGetValue(cacheKey, out ThoughtsAndStats? cachedStats));
            Assert.NotNull(cachedStats);
            Assert.Equal("", cachedStats.Thoughts);
            Assert.Contains("mcp_tool_abc", cachedStats.UsedTools);

            Assert.True(memoryCache.TryGetValue(tokenKey, out string? cachedToken));
            Assert.False(string.IsNullOrEmpty(cachedToken));
        }

        [Fact]
        public async Task GetReactionMarkup_ShouldReplaceLocalhostWithNipIo_WhenBaseUrlContainsLocalhost()
        {
            // Arrange
            long chatId = 123L;
            long messageId = 456L;
            long ownerUserId = 7342855906L;
            string token = "token123";

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            memoryCache.Set($"AiThoughtsToken:{messageId}", token);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IMemoryCache))).Returns(memoryCache);

            // Configure AppSettings BaseApiUrl to localhost
            _appSettings.TelegramBotConfiguration.BaseApiUrl = "http://localhost:8080/";

            // Act
            var result = await _sut.GetReactionMarkup(chatId, messageId, ownerUserId);

            // Assert
            Assert.NotNull(result);
            var buttons = result.InlineKeyboard.SelectMany(row => row).ToList();
            var thoughtsButton = buttons.FirstOrDefault(btn => btn.Text.Contains("ThoughtsAndStatsButton"));
            
            Assert.NotNull(thoughtsButton);
            Assert.Equal($"http://127.0.0.1.nip.io:8080/admin/thoughts/{messageId}?token={token}", thoughtsButton.Url);
        }

        [Fact]
        public async Task GetReactionMarkup_ShouldReplaceTailscaleIpWithNipIo_WhenBaseUrlIsRawIp()
        {
            // Arrange
            long chatId = 123L;
            long messageId = 456L;
            long ownerUserId = 7342855906L;
            string token = "token123";

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            memoryCache.Set($"AiThoughtsToken:{messageId}", token);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IMemoryCache))).Returns(memoryCache);

            // Configure AppSettings BaseApiUrl to Tailscale IP
            _appSettings.TelegramBotConfiguration.BaseApiUrl = "http://100.82.239.59:8080/";

            // Act
            var result = await _sut.GetReactionMarkup(chatId, messageId, ownerUserId);

            // Assert
            Assert.NotNull(result);
            var buttons = result.InlineKeyboard.SelectMany(row => row).ToList();
            var thoughtsButton = buttons.FirstOrDefault(btn => btn.Text.Contains("ThoughtsAndStatsButton"));
            
            Assert.NotNull(thoughtsButton);
            Assert.Equal($"http://100.82.239.59.nip.io:8080/admin/thoughts/{messageId}?token={token}", thoughtsButton.Url);
        }

        [Fact]
        public async Task GetReactionMarkup_ShouldKeepStandardDomainUntouched_WhenBaseUrlIsDomainName()
        {
            // Arrange
            long chatId = 123L;
            long messageId = 456L;
            long ownerUserId = 7342855906L;
            string token = "token123";

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            memoryCache.Set($"AiThoughtsToken:{messageId}", token);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IMemoryCache))).Returns(memoryCache);

            // Configure AppSettings BaseApiUrl to a standard domain
            _appSettings.TelegramBotConfiguration.BaseApiUrl = "http://bot.example.com/";

            // Act
            var result = await _sut.GetReactionMarkup(chatId, messageId, ownerUserId);

            // Assert
            Assert.NotNull(result);
            var buttons = result.InlineKeyboard.SelectMany(row => row).ToList();
            var thoughtsButton = buttons.FirstOrDefault(btn => btn.Text.Contains("ThoughtsAndStatsButton"));
            
            Assert.NotNull(thoughtsButton);
            Assert.Equal($"http://bot.example.com/admin/thoughts/{messageId}?token={token}", thoughtsButton.Url);
        }

        [Fact]
        public async Task GetReactionMarkup_ShouldUseDashboardBaseUrl_WhenDashboardBaseUrlIsConfiguredAndBaseUrlIsEmpty()
        {
            // Arrange
            long chatId = 123L;
            long messageId = 456L;
            long ownerUserId = 7342855906L;
            string token = "token123";

            var memoryCache = new MemoryCache(new MemoryCacheOptions());
            memoryCache.Set($"AiThoughtsToken:{messageId}", token);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IMemoryCache))).Returns(memoryCache);

            // Configure AppSettings: BaseApiUrl is empty, but DashboardBaseUrl is set
            _appSettings.TelegramBotConfiguration.BaseApiUrl = "";
            _appSettings.TelegramBotConfiguration.DashboardBaseUrl = "http://100.82.239.59:8080/";

            // Act
            var result = await _sut.GetReactionMarkup(chatId, messageId, ownerUserId);

            // Assert
            Assert.NotNull(result);
            var buttons = result.InlineKeyboard.SelectMany(row => row).ToList();
            var thoughtsButton = buttons.FirstOrDefault(btn => btn.Text.Contains("ThoughtsAndStatsButton"));
            
            Assert.NotNull(thoughtsButton);
            Assert.Equal($"http://100.82.239.59.nip.io:8080/admin/thoughts/{messageId}?token={token}", thoughtsButton.Url);
        }
    }

    public class FakeTransportForTests : ModelContextProtocol.Protocol.ITransport
    {
        public string SessionId => "fake-session";
        public System.Threading.Channels.ChannelReader<ModelContextProtocol.Protocol.JsonRpcMessage> MessageReader => 
            System.Threading.Channels.Channel.CreateBounded<ModelContextProtocol.Protocol.JsonRpcMessage>(1).Reader;
        public Task SendMessageAsync(ModelContextProtocol.Protocol.JsonRpcMessage message, System.Threading.CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

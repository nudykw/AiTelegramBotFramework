using Moq;
using ServiceLayer.Services;
using ServiceLayer.Services.MessageProcessor;
using ServiceLayer.Services.Telegram;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Xunit;
using Microsoft.Extensions.Logging;
using ServiceLayer.Services.Localization;
using ServiceLayer.Services.OpenAI;
using Telegram.Bot;
using Microsoft.Extensions.Options;

namespace ServiceLayer.UnitTests.Services.MessageProcessor
{
    public class HistoryChainTests
    {
        private readonly Mock<IServiceProvider> _serviceProviderMock = new();
        private readonly Mock<ILogger<ServiceLayer.Services.MessageProcessor.MessageProcessor>> _loggerMock = new();
        private readonly Mock<IChatService> _chatServiceMock = new();
        private readonly Mock<IChatServiceFactory> _chatServiceFactoryMock = new();
        private readonly Mock<ITelegramBotClient> _botClientMock = new();
        private readonly Mock<IRepository<TelegramUserInfo>> _userInfoRepositoryMock = new();
        private readonly Mock<IDynamicLocalizer> _localizerMock = new();
        private readonly Mock<IRepository<HistoryMessage>> _historyRepositoryMock = new();
        private readonly Mock<IReactionService> _reactionServiceMock = new();
        private readonly Mock<ServiceLayer.Services.Memory.IBotSelfAwarenessService> _botSelfAwarenessServiceMock = new();
        private readonly Mock<ServiceLayer.Services.Mcp.McpServerManager> _mcpManagerMock;
        private readonly ServiceLayer.Services.MessageProcessor.MessageProcessor _sut;

        public HistoryChainTests()
        {
            var appSettings = new AppSettings { 
                TelegramBotConfiguration = new ServiceLayer.Services.Telegram.Configuretions.TelegramBotConfiguration { OwnerId = 1 } 
            };
            var optionsMock = new Mock<IOptions<AppSettings>>();
            optionsMock.Setup(x => x.Value).Returns(appSettings);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IOptions<AppSettings>))).Returns(optionsMock.Object);

            _mcpManagerMock = new Mock<ServiceLayer.Services.Mcp.McpServerManager>(
                _serviceProviderMock.Object,
                new Mock<ILogger<ServiceLayer.Services.Mcp.McpServerManager>>().Object,
                appSettings);
            _mcpManagerMock.Setup(m => m.GetAllToolsAsync()).ReturnsAsync(new List<ModelContextProtocol.Client.McpClientTool>());
            
            _reactionServiceMock.Setup(r => r.GetReactionCounts(It.IsAny<long>(), It.IsAny<long>()))
                .ReturnsAsync(new Dictionary<DataBaseLayer.Enums.MessageReactionType, int>());
            
            _reactionServiceMock.Setup(r => r.GetReactionCountsForMessages(It.IsAny<long>(), It.IsAny<List<long>>()))
                .ReturnsAsync(new Dictionary<long, Dictionary<DataBaseLayer.Enums.MessageReactionType, int>>());

            _botSelfAwarenessServiceMock.Setup(s => s.GetBotSystemSummaryPromptAsync(It.IsAny<long?>()))
                .ReturnsAsync("Mock prompt containing create_scheduled_newsletter and EVERGREEN");

            _sut = new ServiceLayer.Services.MessageProcessor.MessageProcessor(
                _serviceProviderMock.Object, _loggerMock.Object, _historyRepositoryMock.Object, 
                _chatServiceMock.Object, _chatServiceFactoryMock.Object, _botClientMock.Object, 
                _userInfoRepositoryMock.Object, _localizerMock.Object, _reactionServiceMock.Object,
                new Mock<ServiceLayer.Services.MessageProcessor.ISummaryService>().Object,
                new Mock<ServiceLayer.Services.Memory.ISemanticMemoryService>().Object,
                _botSelfAwarenessServiceMock.Object,
                _mcpManagerMock.Object);
        }

        [Fact]
        public async Task GetConversationContext_ShouldReconstructFullChainOf10Messages()
        {
            // Arrange
            long chatId = 12345;
            var historyMap = new Dictionary<long, HistoryMessage>();
            
            for (int i = 0; i < 10; i++)
            {
                long msgId = i + 1;
                var roleId = (i % 2 == 0) ? (int)global::OpenAI.Role.User : (int)global::OpenAI.Role.Assistant;
                var msg = new HistoryMessage
                {
                    ChatId = chatId,
                    MessageId = msgId,
                    ParentMessageId = i == 0 ? null : (long?)i,
                    RoleId = roleId,
                    Text = $"Message {msgId}",
                    FromUserName = roleId == (int)global::OpenAI.Role.User ? "User" : "Assistant",
                    CreationDate = DateTime.UtcNow,
                    ModifiedDate = DateTime.UtcNow
                };
                historyMap[msgId] = msg;
            }

            // Setup repository to return the correct message from the map
            _historyRepositoryMock.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<HistoryMessage, bool>>>(), It.IsAny<CancellationToken>()))
                .Returns<System.Linq.Expressions.Expression<System.Func<HistoryMessage, bool>>, CancellationToken>((expr, ct) => 
                {
                    var func = expr.Compile();
                    var result = historyMap.Values.FirstOrDefault(m => func(m));
                    return Task.FromResult(result);
                });

            var lastMessage = historyMap[10];

            // Act
            var context = await _sut.GetConversationContext(lastMessage, "English");

            // Assert
            // +1 because GetConversationContext always injects the newsletter-tools system prompt at index 0
            Assert.Equal(11, context.Count);
            Assert.Equal(global::OpenAI.Role.System, context[0].Role);
            Assert.Contains("create_scheduled_newsletter", (string)context[0].Content);

            for (int i = 0; i < 10; i++)
            {
                var expectedRole = (i % 2 == 0) ? global::OpenAI.Role.User : global::OpenAI.Role.Assistant;
                Assert.Equal(expectedRole, context[i + 1].Role);
                
                string expectedText = $"Message {i + 1}";
                if (i == 9) expectedText += " (Respond in English)";
                Assert.Equal(expectedText, context[i + 1].Content);
            }
        }
    }
}

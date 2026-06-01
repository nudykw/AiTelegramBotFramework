using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ServiceLayer.Services;
using ServiceLayer.Services.AudioTranscriptor;
using ServiceLayer.Services.MessageProcessor;
using ServiceLayer.Services.Telegram;
using ServiceLayer.Services.Localization;
using Telegram.Bot;
using Telegram.Bot.Types;
using Xunit;
using Microsoft.Extensions.Localization;
using ServiceLayer.Resources;
using ServiceLayer.Utils;
using System.Threading;
using System.Globalization;
using ServiceLayer.Services.Mcp;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using MessageProcessorClass = ServiceLayer.Services.MessageProcessor.MessageProcessor;

namespace ServiceLayer.UnitTests.Services.Telegram
{
    public class UpdateHandlerTests
    {
        private readonly Mock<ITelegramBotClient> _botClientMock = new();
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
        private readonly Mock<IServiceScope> _scopeMock = new();
        private readonly Mock<IServiceProvider> _serviceProviderMock = new();
        private readonly Mock<ILogger<UpdateHandler>> _loggerMock = new();
        private readonly Mock<IDynamicLocalizer> _localizerMock = new();
        private readonly Mock<IUserContext> _userContextMock = new();
        private readonly Mock<IChatServiceFactory> _chatServiceFactoryMock = new();
        
        // Use MockBehavior.Loose for dependencies we don't strictly care about in this functional test
        private readonly Mock<MessageProcessorClass> _messageProcessorMock;
        private readonly Mock<AudioTranscriptorService> _audioTranscriptorMock;
        private readonly Mock<IChatService> _chatServiceMock = new();
        private readonly Mock<IReactionService> _reactionServiceMock = new();
        private readonly Mock<McpServerManager> _mcpServerManagerMock;
        private readonly AppSettings _appSettings;

        public UpdateHandlerTests()
        {
            _appSettings = new AppSettings { 
                TelegramBotConfiguration = new ServiceLayer.Services.Telegram.Configuretions.TelegramBotConfiguration()
            };

            var optionsMock = new Mock<IOptions<AppSettings>>();
            optionsMock.Setup(x => x.Value).Returns(_appSettings);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IOptions<AppSettings>)))
                .Returns(optionsMock.Object);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IChatService)))
                .Returns(_chatServiceMock.Object);

            _mcpServerManagerMock = new Mock<McpServerManager>(
                _serviceProviderMock.Object,
                new Mock<ILogger<McpServerManager>>().Object,
                _appSettings);

            // Initializing complex mocks
            _messageProcessorMock = new Mock<MessageProcessorClass>(
                _serviceProviderMock.Object, 
                new Mock<ILogger<MessageProcessorClass>>().Object,
                new Mock<IRepository<HistoryMessage>>().Object,
                new Mock<IChatService>().Object,
                _chatServiceFactoryMock.Object,
                _botClientMock.Object,
                new Mock<IRepository<TelegramUserInfo>>().Object,
                _localizerMock.Object,
                _reactionServiceMock.Object,
                new Mock<ISummaryService>().Object,
                new Mock<ServiceLayer.Services.Memory.ISemanticMemoryService>().Object,
                new Mock<ServiceLayer.Services.Memory.IBotSelfAwarenessService>().Object,
                _mcpServerManagerMock.Object); 

            _audioTranscriptorMock = new Mock<AudioTranscriptorService>(
                _serviceProviderMock.Object,
                new Mock<ILogger<AudioTranscriptorService>>().Object,
                _chatServiceFactoryMock.Object); // factory

            // Setup infrastructure
            _scopeFactoryMock.Setup(x => x.CreateScope()).Returns(_scopeMock.Object);
            _scopeMock.Setup(x => x.ServiceProvider).Returns(_serviceProviderMock.Object);

            _botClientMock.Setup(x => x.SendRequest(
                It.IsAny<global::Telegram.Bot.Requests.GetMeRequest>(), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new User { Id = 123, Username = "TestBot", FirstName = "Test" });
        }

        [Fact]
        public async Task HandleUpdateAsync_ShouldBeginProcessingInNewScopeImmediately()
        {
            // Arrange
            var update = new Update { Id = 1, Message = new Message { Text = "Hello", Chat = new Chat { Id = 1 } } };
            
            // This is the instance resolved from the new scope
            var internalHandlerMock = new Mock<UpdateHandler>(
                _serviceProviderMock.Object,
                _loggerMock.Object,
                _botClientMock.Object,
                _messageProcessorMock.Object,
                _audioTranscriptorMock.Object,
                _scopeFactoryMock.Object,
                _localizerMock.Object,
                _userContextMock.Object,
                _appSettings,
                _chatServiceMock.Object,
                _mcpServerManagerMock.Object,
                _reactionServiceMock.Object);

            _serviceProviderMock.Setup(x => x.GetService(typeof(UpdateHandler)))
                .Returns(internalHandlerMock.Object);

            var dispatcher = new UpdateHandler(
                _serviceProviderMock.Object,
                _loggerMock.Object,
                _botClientMock.Object,
                _messageProcessorMock.Object,
                _audioTranscriptorMock.Object,
                _scopeFactoryMock.Object,
                _localizerMock.Object,
                _userContextMock.Object,
                _appSettings,
                _chatServiceMock.Object,
                _mcpServerManagerMock.Object,
                _reactionServiceMock.Object);

            // Act
            var task = dispatcher.HandleUpdateAsync(_botClientMock.Object, update, CancellationToken.None);

            // Assert
            // 1. The task returned to the polling loop should be completed immediately
            Assert.True(task.IsCompleted);

            // 2. We wait a bit to allow the background task (Task.Run) to execute
            await Task.Delay(200);

            // 3. Verify that a new scope was created
            _scopeFactoryMock.Verify(x => x.CreateScope(), Times.Once);

            // 4. Verify that the internal processor was resolved from the scope and called
            // Note: Since we renamed it to ProcessUpdateInternalAsync but it's private,
            // we can't verify it directly unless we mock the IUpdateHandler interface behavior 
            // OR check side effects like typing indicators.
            
            // Let's verify that the scope-resolved handler was asked for (by checking GetService)
            _serviceProviderMock.Verify(x => x.GetService(typeof(UpdateHandler)), Times.Once);
        }
        
        [Fact]
        public async Task HandleUpdateAsync_ShouldHandleMultipleUpdatesInParallel()
        {
            // Arrange
            int scopeCount = 0;
            _scopeFactoryMock.Setup(x => x.CreateScope()).Returns(() => {
                Interlocked.Increment(ref scopeCount);
                return _scopeMock.Object;
            });

            // Re-fetch appSettings if needed or use from outer scope
            // (Using the field instead of local re-definition)

            var dispatcher = new UpdateHandler(
                _serviceProviderMock.Object,
                _loggerMock.Object,
                _botClientMock.Object,
                _messageProcessorMock.Object,
                _audioTranscriptorMock.Object,
                _scopeFactoryMock.Object,
                _localizerMock.Object,
                _userContextMock.Object,
                _appSettings,
                _chatServiceMock.Object,
                _mcpServerManagerMock.Object,
                _reactionServiceMock.Object);

            var update1 = new Update { Id = 1, Message = new Message { Id = 1, Chat = new Chat { Id = 1 } } };
            var update2 = new Update { Id = 2, Message = new Message { Id = 2, Chat = new Chat { Id = 1 } } };

            // Act
            var t1 = dispatcher.HandleUpdateAsync(_botClientMock.Object, update1, CancellationToken.None);
            var t2 = dispatcher.HandleUpdateAsync(_botClientMock.Object, update2, CancellationToken.None);

            // Assert
            Assert.True(t1.IsCompleted);
            Assert.True(t2.IsCompleted);

            await Task.Delay(300);

            // Both should have triggered a scope creation
            Assert.Equal(2, scopeCount);
        }
    }
}

using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using ServiceLayer.Services.Telegram;
using ServiceLayer.Services.MessageProcessor;
using ServiceLayer.Services.AudioTranscriptor;
using ServiceLayer.Services;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using ServiceLayer.Services.Localization;
using Telegram.Bot;
using Xunit;
using ServiceLayer.Services.Telegram.Configuretions;
using System.Linq.Expressions;
using ServiceLayer.Utils;

using MessageProcessorClass = ServiceLayer.Services.MessageProcessor.MessageProcessor;

namespace ServiceLayer.UnitTests.Services.Telegram
{
    public class UpdateHandlerBillingTests
    {
        private readonly Mock<IServiceProvider> _serviceProviderMock = new();
        private readonly Mock<ILogger<UpdateHandler>> _loggerMock = new();
        private readonly Mock<ITelegramBotClient> _botClientMock = new();
        private readonly Mock<MessageProcessorClass> _messageProcessorMock;
        private readonly Mock<AudioTranscriptorService> _audioTranscriptorMock;
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
        private readonly Mock<IDynamicLocalizer> _localizerMock = new();
        private readonly Mock<IUserContext> _userContextMock = new();
        private readonly Mock<IChatService> _chatServiceMock = new();
        private readonly Mock<IReactionService> _reactionServiceMock = new();
        private readonly Mock<IRepository<TelegramUserInfo>> _userInfoRepositoryMock = new();
        private readonly Mock<ServiceLayer.Services.Mcp.McpServerManager> _mcpServerManagerMock;
        private Mock<IChatServiceFactory> _chatServiceFactoryMock;
        private AppSettings _appSettings;

        public UpdateHandlerBillingTests()
        {
            _appSettings = new AppSettings
            {
                TelegramBotConfiguration = new TelegramBotConfiguration 
                { 
                    InitialBalance = 0.1M,
                    IgnoredBalanceUserIds = new List<long> { 999L },
                    OwnerId = 7342855906L
                }
            };

            // Setup repository and policy resolution
            _serviceProviderMock.Setup(x => x.GetService(typeof(IRepository<TelegramUserInfo>)))
                .Returns(_userInfoRepositoryMock.Object);

            var optionsMock = new Mock<IOptions<AppSettings>>();
            optionsMock.Setup(x => x.Value).Returns(_appSettings);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IOptions<AppSettings>)))
                .Returns(optionsMock.Object);

            _chatServiceFactoryMock = new Mock<IChatServiceFactory>();

            _mcpServerManagerMock = new Mock<ServiceLayer.Services.Mcp.McpServerManager>(
                _serviceProviderMock.Object,
                new Mock<ILogger<ServiceLayer.Services.Mcp.McpServerManager>>().Object,
                _appSettings);

            // Mocks for constructor
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
                _chatServiceFactoryMock.Object);
        }

        private UpdateHandler CreateSut()
        {
            return new UpdateHandler(
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
        }

        [Fact]
        public async Task CheckBalanceAndReplenish_ShouldReturnTrue_WhenUserHasPositiveBalance()
        {
            // Arrange
            var userId = 123L;
            var user = new TelegramUserInfo { Id = userId, Balance = 0.005M, FirstName = "Test", IsBot = false };
            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);
            
            var sut = CreateSut();

            // Act
            var result = await sut.CheckBalanceAndReplenish(userId, 456L, CancellationToken.None);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task CheckBalanceAndReplenish_ShouldReturnTrue_When24hPassed()
        {
            // Arrange
            var userId = 123L;
            var user = new TelegramUserInfo 
            { 
                Id = userId, 
                Balance = 0M, 
                LastAiInteraction = DateTime.UtcNow.AddHours(-25),
                FirstName = "Test",
                IsBot = false
            };
            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);
            
            var sut = CreateSut();

            // Act
            var result = await sut.CheckBalanceAndReplenish(userId, 456L, CancellationToken.None);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task CheckBalanceAndReplenish_ShouldReturnTrue_WhenBalanceZeroAndRecentlyUsed()
        {
            // Arrange
            var userId = 123L;
            var chatId = 456L;
            var user = new TelegramUserInfo 
            { 
                Id = userId, 
                Balance = 0M, 
                LastAiInteraction = DateTime.UtcNow.AddHours(-1),
                FirstName = "Test",
                IsBot = false
            };
            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);
            
            _localizerMock.Setup(l => l["InsufficientCredits"]).Returns("No money");
            _localizerMock.Setup(l => l["TryAgainLater"]).Returns("Wait 24h");

            var sut = CreateSut();

            // Act
            var result = await sut.CheckBalanceAndReplenish(userId, chatId, CancellationToken.None);

            // Assert
            Assert.True(result);
            _botClientMock.Verify(b => b.SendRequest(
                It.IsAny<global::Telegram.Bot.Requests.SendMessageRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CheckBalanceAndReplenish_ShouldReturnTrue_WhenUserIsIgnored()
        {
            // Arrange
            var ignoredUserId = 999L;
            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TelegramUserInfo { Id = ignoredUserId, Balance = 0M, FirstName = "Ignored", IsBot = false });
            var sut = CreateSut();

            // Act
            var result = await sut.CheckBalanceAndReplenish(ignoredUserId, 456L, CancellationToken.None);

            // Assert
            Assert.True(result);
            _userInfoRepositoryMock.Verify(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CheckBalanceAndReplenish_ShouldReturnTrue_WhenUserIsOwner()
        {
            // Arrange
            var ownerId = 7342855906L;
            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TelegramUserInfo { Id = ownerId, Balance = 0M, FirstName = "Owner", IsBot = false });
            var sut = CreateSut();

            // Act
            var result = await sut.CheckBalanceAndReplenish(ownerId, 456L, CancellationToken.None);

            // Assert
            Assert.True(result);
            _userInfoRepositoryMock.Verify(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CheckBalanceAndReplenish_ShouldReturnTrue_WhenInitialBalanceIsZero()
        {
            // Arrange
            var userId = 123L;
            _appSettings.TelegramBotConfiguration.InitialBalance = 0M; // Make it free
            var sut = CreateSut();

            // Act
            var result = await sut.CheckBalanceAndReplenish(userId, 456L, CancellationToken.None);

            // Assert
            Assert.True(result);
            _userInfoRepositoryMock.Verify(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ServiceLayer.Services;
using ServiceLayer.Services.AudioTranscriptor;
using ServiceLayer.Services.Localization;
using ServiceLayer.Services.Mcp;
using ServiceLayer.Services.MessageProcessor;
using ServiceLayer.Services.Telegram;
using ServiceLayer.Utils;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Microsoft.Extensions.Localization;
using Xunit;
using MessageProcessorClass = ServiceLayer.Services.MessageProcessor.MessageProcessor;

namespace ServiceLayer.UnitTests.Services.Telegram
{
    public class UpdateHandlerSchedulerTests
    {
        private readonly Mock<ITelegramBotClient> _botClientMock = new();
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
        private readonly Mock<IServiceScope> _scopeMock = new();
        private readonly Mock<IServiceProvider> _serviceProviderMock = new();
        private readonly Mock<ILogger<UpdateHandler>> _loggerMock = new();
        private readonly Mock<IDynamicLocalizer> _localizerMock = new();
        private readonly Mock<IUserContext> _userContextMock = new();
        private readonly Mock<IChatServiceFactory> _chatServiceFactoryMock = new();
        private readonly Mock<IRepository<SchedulerAccessRule>> _ruleRepoMock = new();
        private readonly Mock<IRepository<ScheduledNewsletter>> _newsletterRepoMock = new();
        private readonly Mock<NewsletterSchedulerService> _schedulerServiceMock;
        
        private readonly Mock<MessageProcessorClass> _messageProcessorMock;
        private readonly Mock<AudioTranscriptorService> _audioTranscriptorMock;
        private readonly Mock<IChatService> _chatServiceMock = new();
        private readonly Mock<IReactionService> _reactionServiceMock = new();
        private readonly Mock<McpServerManager> _mcpServerManagerMock;
        private readonly AppSettings _appSettings;

        public UpdateHandlerSchedulerTests()
        {
            _appSettings = new AppSettings { 
                TelegramBotConfiguration = new ServiceLayer.Services.Telegram.Configuretions.TelegramBotConfiguration
                {
                    OwnerId = 12345
                },
                Scheduler = new SchedulerSettings { Enabled = true }
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

            _scopeFactoryMock.Setup(x => x.CreateScope()).Returns(_scopeMock.Object);
            _scopeMock.Setup(x => x.ServiceProvider).Returns(_serviceProviderMock.Object);

            _serviceProviderMock.Setup(x => x.GetService(typeof(IRepository<SchedulerAccessRule>)))
                .Returns(_ruleRepoMock.Object);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IRepository<ScheduledNewsletter>)))
                .Returns(_newsletterRepoMock.Object);

            _schedulerServiceMock = new Mock<NewsletterSchedulerService>(
                _serviceProviderMock.Object,
                new Mock<ILogger<NewsletterSchedulerService>>().Object,
                _appSettings);
            _serviceProviderMock.Setup(x => x.GetService(typeof(NewsletterSchedulerService)))
                .Returns(_schedulerServiceMock.Object);

            _botClientMock.Setup(x => x.SendRequest(
                It.IsAny<global::Telegram.Bot.Requests.GetMeRequest>(), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new User { Id = 123, Username = "TestBot", FirstName = "Test" });

            // Setup localizer default
            _localizerMock.Setup(x => x[It.IsAny<string>(), It.IsAny<object[]>()]).Returns((string s, object[] args) => s);
            _localizerMock.Setup(x => x.GetString(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string s, object[] args) => s);
        }

        [Fact]
        public async Task HandleUpdateAsync_WithSchedulerDisabled_ShouldIgnoreCommand()
        {
            // Arrange
            _appSettings.Scheduler.Enabled = false;

            var update = new Update
            {
                Id = 1,
                Message = new Message
                {
                    Text = "/schedule",
                    Chat = new Chat { Id = 100, Type = ChatType.Private },
                    From = new User { Id = 12345, FirstName = "Owner" }
                }
            };

            var handler = new UpdateHandler(
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
            await handler.ProcessUpdateInternalAsync(_botClientMock.Object, update, CancellationToken.None);

            // Assert: BotClient should NOT send any "PermissionDenied" because it fell back to processing it as normal AI message (Ask)
            _botClientMock.Verify(x => x.SendRequest(
                It.Is<global::Telegram.Bot.Requests.SendMessageRequest>(r => r.Text.Contains("PermissionDenied")),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task HandleUpdateAsync_WhenUserIsBlacklisted_ShouldDenyAccess()
        {
            // Arrange
            var blacklistedUser = 99999L;
            _appSettings.Scheduler.Enabled = true;

            // Setup repository to return a blacklist entry
            _ruleRepoMock.Setup(x => x.Get(It.IsAny<System.Linq.Expressions.Expression<Func<SchedulerAccessRule, bool>>>()))
                .ReturnsAsync(new SchedulerAccessRule { UserId = blacklistedUser, IsAllowed = false });

            var update = new Update
            {
                Id = 2,
                Message = new Message
                {
                    Text = "/schedule",
                    Chat = new Chat { Id = 100, Type = ChatType.Private },
                    From = new User { Id = blacklistedUser, FirstName = "Blacklisted" }
                }
            };

            var handler = new UpdateHandler(
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
            await handler.ProcessUpdateInternalAsync(_botClientMock.Object, update, CancellationToken.None);

            // Assert: should send PermissionDenied message
            _botClientMock.Verify(x => x.SendRequest(
                It.Is<global::Telegram.Bot.Requests.SendMessageRequest>(r => r.Text.Contains("PermissionDenied")),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}

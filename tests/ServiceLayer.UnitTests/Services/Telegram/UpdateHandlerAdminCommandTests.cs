using Moq;
using Microsoft.Extensions.Logging;
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
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using ServiceLayer.Constans;
using Microsoft.Extensions.DependencyInjection;
using ServiceLayer.Utils;
using ServiceLayer.Services.Mcp;

using MessageProcessorClass = ServiceLayer.Services.MessageProcessor.MessageProcessor;

namespace ServiceLayer.UnitTests.Services.Telegram
{
    public class UpdateHandlerAdminCommandTests
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
        private readonly Mock<IRepository<TelegramChatInfo>> _chatInfoRepositoryMock = new();
        private readonly Mock<IRepository<BalanceHistory>> _balanceHistoryRepositoryMock = new();
        private readonly Mock<McpServerManager> _mcpServerManagerMock;
        private Mock<IChatServiceFactory> _chatServiceFactoryMock;
        private readonly AppSettings _appSettings;

        public UpdateHandlerAdminCommandTests()
        {
            _appSettings = new AppSettings
            {
                TelegramBotConfiguration = new TelegramBotConfiguration 
                { 
                    InitialBalance = 0.1M,
                    OwnerId = 7342855906L
                }
            };

            _serviceProviderMock.Setup(x => x.GetService(typeof(IRepository<TelegramUserInfo>)))
                .Returns(_userInfoRepositoryMock.Object);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IRepository<TelegramChatInfo>)))
                .Returns(_chatInfoRepositoryMock.Object);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IRepository<BalanceHistory>)))
                .Returns(_balanceHistoryRepositoryMock.Object);
            
            var optionsMock = new Mock<Microsoft.Extensions.Options.IOptions<AppSettings>>();
            optionsMock.Setup(x => x.Value).Returns(_appSettings);
            _serviceProviderMock.Setup(x => x.GetService(typeof(Microsoft.Extensions.Options.IOptions<AppSettings>)))
                .Returns(optionsMock.Object);

            _chatServiceFactoryMock = new Mock<IChatServiceFactory>();

            _mcpServerManagerMock = new Mock<McpServerManager>(
                _serviceProviderMock.Object,
                new Mock<ILogger<McpServerManager>>().Object,
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

            // Mock bot info
            _botClientMock.Setup(x => x.SendRequest(
                It.IsAny<global::Telegram.Bot.Requests.GetMeRequest>(), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new User { Id = 1, Username = "test_bot", FirstName = "TestBot", IsBot = true });
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
        public async Task UsersBalance_ShouldReturnsBalanceList_WhenCalledByOwner()
        {
            // Arrange
            var ownerId = 7342855906L;
            var chatId = 123L;
            var message = new Message
            {
                From = new User { Id = ownerId, FirstName = "Owner", IsBot = false },
                Chat = new Chat { Id = chatId, Type = ChatType.Private },
                Text = "/users_balance"
            };

            var users = new List<TelegramUserInfo>
            {
                new TelegramUserInfo { Id = ownerId, FirstName = "Owner", Balance = 100M, IsBot = false, BalanceModifiedAt = DateTime.UtcNow.AddMinutes(-5) },
                new TelegramUserInfo { Id = 456L, FirstName = "User", Balance = 50M, IsBot = false, BalanceModifiedAt = DateTime.UtcNow.AddHours(-2) }
            };

            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Expression<Func<TelegramUserInfo, bool>> expr, CancellationToken ct) => users.FirstOrDefault(expr.Compile()));
            
            _userInfoRepositoryMock.Setup(r => r.GetAll())
                .Returns(users.AsQueryable());

            _localizerMock.Setup(l => l["UsersBalanceHeader"]).Returns("Balances:");
            _localizerMock.Setup(l => l["TimeAgo_Minutes", It.IsAny<object[]>()]).Returns("5m ago");
            _localizerMock.Setup(l => l["TimeAgo_Hours", It.IsAny<object[]>()]).Returns("2h ago");
            _localizerMock.Setup(l => l["UsersBalanceItem", It.IsAny<object[]>()])
                .Returns((string key, object[] args) => $"{args[0]} ({args[1]}): {args[2]} (last changed: {args[3]})");

            _botClientMock.Setup(b => b.SendRequest(It.IsAny<global::Telegram.Bot.Requests.SendMessageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Message());

            var sut = CreateSut();

            // Act
            // We need to bypass ProcessUpdateInternalAsync or mock GetMe
            // ProcessUpdateInternalAsync is private, we'll use HandleUpdateAsync if possible or just BotOnMessageReceived via reflection if needed
            // But UpdateHandler has BotOnMessageReceived as private too.
            // Let's use HandleUpdateAsync which is public and handles scoping.
            
            _serviceProviderMock.Setup(s => s.GetService(typeof(UpdateHandler))).Returns(sut);

            await sut.ProcessUpdateInternalAsync(_botClientMock.Object, new Update { Message = message }, CancellationToken.None);



            // Assert
            _botClientMock.Verify(b => b.SendRequest(
                It.Is<global::Telegram.Bot.Requests.SendMessageRequest>(r => r.ChatId == chatId && r.Text.Contains("Owner (7342855906): 100 (last changed: 5m ago)")),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UsersBalance_ShouldDenyAccess_WhenCalledByRegularUser()
        {
            // Arrange
            var userId = 111L;
            var chatId = 123L;
            var message = new Message
            {
                From = new User { Id = userId, FirstName = "Regular", IsBot = false },
                Chat = new Chat { Id = chatId, Type = ChatType.Private },
                Text = "/users_balance"
            };

            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TelegramUserInfo { Id = userId, FirstName = "Regular", IsBot = false });

            _localizerMock.Setup(l => l["PermissionDenied"]).Returns("No access");

            var sut = CreateSut();
            // Act
            await sut.ProcessUpdateInternalAsync(_botClientMock.Object, new Update { Message = message }, CancellationToken.None);

            // Assert
            _botClientMock.Verify(b => b.SendRequest(
                It.Is<global::Telegram.Bot.Requests.SendMessageRequest>(r => r.ChatId == chatId && r.Text.Contains("No access")),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SetBalance_Admin_UpdatesUserBalanceAndRecordsHistory()
        {
            // Arrange
            var adminId = 7342855906L;
            var targetUserId = 456L;
            var chatId = 123L;
            var message = new Message
            {
                From = new User { Id = adminId, FirstName = "Admin", IsBot = false },
                Chat = new Chat { Id = chatId, Type = ChatType.Private },
                Text = $"/set_balance {targetUserId} 150.5"
            };

            var targetUser = new TelegramUserInfo { Id = targetUserId, FirstName = "TargetUser", Balance = 100M, IsBot = false };
            var adminUser = new TelegramUserInfo { Id = adminId, FirstName = "Admin", IsBot = false };

            _userInfoRepositoryMock.Setup(r => r.Get(It.Is<Expression<Func<TelegramUserInfo, bool>>>(e => e.Compile()(targetUser)), It.IsAny<CancellationToken>()))
                .ReturnsAsync(targetUser);
            _userInfoRepositoryMock.Setup(r => r.Get(It.Is<Expression<Func<TelegramUserInfo, bool>>>(e => e.Compile()(adminUser)), It.IsAny<CancellationToken>()))
                .ReturnsAsync(adminUser);

            _localizerMock.Setup(l => l["SetBalance_Success", It.IsAny<object[]>()])
                .Returns((string key, object[] args) => $"Success: {args[0]} ({args[1]}) balance set to {args[2]}");

            var sut = CreateSut();
            _serviceProviderMock.Setup(s => s.GetService(typeof(UpdateHandler))).Returns(sut);

            // Act
            await sut.ProcessUpdateInternalAsync(_botClientMock.Object, new Update { Message = message }, CancellationToken.None);

            // Assert
            Assert.Equal(150.5M, targetUser.Balance);
            _userInfoRepositoryMock.Verify(r => r.Update(targetUser), Times.Once);
            _userInfoRepositoryMock.Verify(r => r.SaveChanges(), Times.AtLeastOnce());
            
            _balanceHistoryRepositoryMock.Verify(r => r.Add(It.Is<BalanceHistory>(h => 
                h.UserId == targetUserId && 
                h.Amount == 50.5M && 
                h.ModifiedById == adminId && 
                h.Source == "Admin")), Times.Once);
            _balanceHistoryRepositoryMock.Verify(r => r.SaveChanges(), Times.Once);

            _botClientMock.Verify(b => b.SendRequest(
                It.Is<global::Telegram.Bot.Requests.SendMessageRequest>(r => r.ChatId == chatId && r.Text.Contains("Success: TargetUser (456) balance set to 150.5")),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SetBalance_InvalidFormat_ReturnsUsage()
        {
            // Arrange
            var adminId = 7342855906L;
            var chatId = 123L;
            var message = new Message
            {
                From = new User { Id = adminId, FirstName = "Admin", IsBot = false },
                Chat = new Chat { Id = chatId, Type = ChatType.Private },
                Text = "/set_balance invalid_id 100"
            };

            _userInfoRepositoryMock.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TelegramUserInfo { Id = adminId, FirstName = "Admin", IsBot = false });

            _localizerMock.Setup(l => l["SetBalance_InvalidFormat"]).Returns("Invalid format");

            var sut = CreateSut();
            _serviceProviderMock.Setup(s => s.GetService(typeof(UpdateHandler))).Returns(sut);

            // Act
            await sut.ProcessUpdateInternalAsync(_botClientMock.Object, new Update { Message = message }, CancellationToken.None);

            // Assert
            _botClientMock.Verify(b => b.SendRequest(
                It.Is<global::Telegram.Bot.Requests.SendMessageRequest>(r => r.ChatId == chatId && r.Text != null && r.Text.Contains("Invalid format")),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}

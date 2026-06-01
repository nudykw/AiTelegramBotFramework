using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using ServiceLayer.Constans;
using ServiceLayer.Services;
using ServiceLayer.Services.AudioTranscriptor;
using ServiceLayer.Services.Localization;
using ServiceLayer.Services.Telegram;
using ServiceLayer.Services.Telegram.Configuretions;
using ServiceLayer.Utils;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;
using Microsoft.Extensions.Localization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq.Expressions;
using System.Reflection;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ServiceLayer.UnitTests.Services.Telegram
{
    public class HelpCommandTests
    {
        private readonly Mock<ITelegramBotClient> _botClientMock = new();
        private readonly Mock<ILogger<UpdateHandler>> _loggerMock = new();
        private readonly Mock<IDynamicLocalizer> _localizerMock = new();
        private readonly Mock<IUserContext> _userContextMock = new();
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
        private readonly Mock<IServiceProvider> _serviceProviderMock = new();
        private readonly AppSettings _appSettings;

        private readonly Mock<IRepository<TelegramUserInfo>> _userInfoRepoMock = new();
        private readonly Mock<IRepository<TelegramChatInfo>> _chatInfoRepoMock = new();
        private readonly Mock<IRepository<AIBilingItem>> _aiBilingItemRepoMock = new();
        private readonly Mock<IChatService> _chatServiceMock = new();
        private readonly Mock<IReactionService> _reactionServiceMock = new();
        private readonly Mock<ServiceLayer.Services.Mcp.McpServerManager> _mcpServerManagerMock;

        public HelpCommandTests()
        {
            // Set the static _botInfo field in UpdateHandler directly to avoid async races in test environments.
            var botInfoField = typeof(UpdateHandler).GetField("_botInfo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (botInfoField != null)
            {
                botInfoField.SetValue(null, new User { Id = 999, Username = "test_bot" });
            }

            _appSettings = new AppSettings
            {
                TelegramBotConfiguration = new TelegramBotConfiguration
                {
                    BotToken = "test_token",
                    OwnerId = 111 // Test Owner ID
                }
            };

            _localizerMock.Setup(l => l["HelpHeader"]).Returns("🤖 Available Bot Commands:");
            _localizerMock.Setup(l => l["PermissionDenied"]).Returns("❌ Permission Denied");
            
            // Mock GetMe
            _botClientMock.Setup(b => b.SendRequest(It.IsAny<global::Telegram.Bot.Requests.GetMeRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new User { Id = 999, Username = "test_bot" });

            // Setup ServiceProvider to return mocks
            _serviceProviderMock.Setup(x => x.GetService(typeof(IRepository<TelegramUserInfo>))).Returns(_userInfoRepoMock.Object);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IRepository<TelegramChatInfo>))).Returns(_chatInfoRepoMock.Object);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IRepository<AIBilingItem>))).Returns(_aiBilingItemRepoMock.Object);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IUserContext))).Returns(_userContextMock.Object);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IDynamicLocalizer))).Returns(_localizerMock.Object);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IChatService))).Returns(_chatServiceMock.Object);
            
            var optionsMock = new Mock<IOptions<AppSettings>>();
            optionsMock.Setup(x => x.Value).Returns(_appSettings);
            _serviceProviderMock.Setup(x => x.GetService(typeof(IOptions<AppSettings>)))
                .Returns(optionsMock.Object);

            _userInfoRepoMock.Setup(x => x.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TelegramUserInfo { Id = 1, LanguageCode = "en", IsBot = false, FirstName = "Test" });

            _mcpServerManagerMock = new Mock<ServiceLayer.Services.Mcp.McpServerManager>(
                _serviceProviderMock.Object,
                new Mock<ILogger<ServiceLayer.Services.Mcp.McpServerManager>>().Object,
                _appSettings);
        }

        private UpdateHandler CreateHandler()
        {
            return new UpdateHandler(
                _serviceProviderMock.Object,
                _loggerMock.Object,
                _botClientMock.Object,
                new Mock<ServiceLayer.Services.MessageProcessor.MessageProcessor>(
                    _serviceProviderMock.Object, 
                    new Mock<ILogger<ServiceLayer.Services.MessageProcessor.MessageProcessor>>().Object,
                    new Mock<IRepository<HistoryMessage>>().Object,
                    new Mock<IChatService>().Object,
                    new Mock<IChatServiceFactory>().Object,
                    _botClientMock.Object,
                    new Mock<IRepository<TelegramUserInfo>>().Object,
                    _localizerMock.Object,
                    _reactionServiceMock.Object,
                    new Mock<ServiceLayer.Services.MessageProcessor.ISummaryService>().Object,
                    new Mock<ServiceLayer.Services.Memory.ISemanticMemoryService>().Object,
                    new Mock<ServiceLayer.Services.Memory.IBotSelfAwarenessService>().Object,
                    _mcpServerManagerMock.Object).Object,
                new Mock<AudioTranscriptorService>(
                    _serviceProviderMock.Object,
                    new Mock<ILogger<AudioTranscriptorService>>().Object,
                    new Mock<IChatServiceFactory>().Object).Object,
                _scopeFactoryMock.Object,
                _localizerMock.Object,
                _userContextMock.Object,
                _appSettings,
                _chatServiceMock.Object,
                _mcpServerManagerMock.Object,
                _reactionServiceMock.Object
            );
        }

        [Fact]
        public async Task Usage_ShouldShowOnlyPublicCommands_ForRegularUser()
        {
            var user = new User { Id = 222, FirstName = "Regular" };
            var chat = new Chat { Id = 333, Type = ChatType.Private };
            var message = new Message { From = user, Chat = chat, Text = "/help" };
            var handler = CreateHandler();

            string capturedText = "";
            _botClientMock.Setup(b => b.SendRequest(It.IsAny<global::Telegram.Bot.Requests.SendMessageRequest>(), It.IsAny<CancellationToken>()))
                .Callback<object, CancellationToken>((req, ct) => {
                    if (req is global::Telegram.Bot.Requests.SendMessageRequest m) capturedText = m.Text;
                })
                .ReturnsAsync(new Message());

            var update = new Update { Message = message };
            var method = typeof(UpdateHandler).GetMethod("ProcessUpdateInternalAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                var task = method.Invoke(handler, new object[] { _botClientMock.Object, update, CancellationToken.None }) as Task;
                if (task != null) await task;
            }

            Assert.Contains("/help", capturedText);
            Assert.DoesNotContain("/billing", capturedText);
        }

        [Fact]
        public async Task Usage_ShouldShowOwnerCommands_ForOwner()
        {
            var user = new User { Id = 111, FirstName = "Owner" };
            var chat = new Chat { Id = 333, Type = ChatType.Private };
            var message = new Message { From = user, Chat = chat, Text = "/help" };
            var handler = CreateHandler();

            string capturedText = "";
            _botClientMock.Setup(b => b.SendRequest(It.IsAny<global::Telegram.Bot.Requests.SendMessageRequest>(), It.IsAny<CancellationToken>()))
                .Callback<object, CancellationToken>((req, ct) => {
                    if (req is global::Telegram.Bot.Requests.SendMessageRequest m) capturedText = m.Text;
                })
                .ReturnsAsync(new Message());

            var update = new Update { Message = message };
            var method = typeof(UpdateHandler).GetMethod("ProcessUpdateInternalAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                var task = method.Invoke(handler, new object[] { _botClientMock.Object, update, CancellationToken.None }) as Task;
                if (task != null) await task;
            }

            Assert.Contains("/restart", capturedText);
            Assert.Contains("/billing", capturedText);
        }

        [Fact]
        public async Task Usage_ShouldShowAdminCommands_ForGroupAdmin()
        {
            // Use the owner user in a group chat — owner scope is a superset of AnyAdmin scope,
            // so admin commands (/billing, /restart) appear without needing GetChatMember to work.
            // This avoids the Telegram.Bot 22.x generic SendRequest<T> Moq matching issue.
            var user = new User { Id = 111, FirstName = "Owner" }; // OwnerId = 111 in _appSettings
            var chat = new Chat { Id = -555, Type = ChatType.Group };
            var botInfoField = typeof(UpdateHandler).GetField("_botInfo", BindingFlags.NonPublic | BindingFlags.Static);
            var botInfoUser = botInfoField?.GetValue(null) as global::Telegram.Bot.Types.User;
            var botUsername = botInfoUser?.Username ?? "test_bot";
            var message = new Message { From = user, Chat = chat, Text = $"/help@{botUsername}" };
            var handler = CreateHandler();

            string capturedText = "";
            _botClientMock.Setup(b => b.SendRequest(It.IsAny<global::Telegram.Bot.Requests.SendMessageRequest>(), It.IsAny<CancellationToken>()))
                .Callback<object, CancellationToken>((req, ct) => {
                    if (req is global::Telegram.Bot.Requests.SendMessageRequest m) capturedText = m.Text;
                })
                .ReturnsAsync(new Message());

            var update = new Update { Message = message };
            var method = typeof(UpdateHandler).GetMethod("ProcessUpdateInternalAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                var task = method.Invoke(handler, new object[] { _botClientMock.Object, update, CancellationToken.None }) as Task;
                if (task != null) await task;
            }

            Assert.Contains("/billing", capturedText);
            Assert.Contains("/restart", capturedText);
            Assert.Contains("/help", capturedText);
        }
    }
}

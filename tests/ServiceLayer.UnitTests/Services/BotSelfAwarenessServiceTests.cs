using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using DataBaseLayer.Enums;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceLayer.Constans;
using ServiceLayer.Services;
using ServiceLayer.Services.Memory;
using ServiceLayer.Services.Telegram.Configuretions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Xunit;

namespace ServiceLayer.UnitTests.Services
{
    public class BotSelfAwarenessServiceTests
    {
        private readonly Mock<ITelegramBotClient> _botClientMock;
        private readonly Mock<IRepository<TelegramUserInfo>> _userInfoRepoMock;
        private readonly Mock<IChatServiceFactory> _chatServiceFactoryMock;
        private readonly Mock<ILogger<BotSelfAwarenessService>> _loggerMock;
        private readonly AppSettings _appSettings;

        public BotSelfAwarenessServiceTests()
        {
            _botClientMock = new Mock<ITelegramBotClient>();
            _userInfoRepoMock = new Mock<IRepository<TelegramUserInfo>>();
            _chatServiceFactoryMock = new Mock<IChatServiceFactory>();
            _loggerMock = new Mock<ILogger<BotSelfAwarenessService>>();

            // Sane TelegramBotClient mock setup
            var meUser = new User
            {
                Id = 12345,
                IsBot = true,
                FirstName = "Test Bot",
                Username = "test_bot"
            };
            _botClientMock
                .Setup(c => c.SendRequest(It.IsAny<global::Telegram.Bot.Requests.GetMeRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(meUser);

            // AppSettings setup
            _appSettings = new AppSettings
            {
                TelegramBotConfiguration = new ServiceLayer.Services.Telegram.Configuretions.TelegramBotConfiguration
                {
                    BotToken = "123:test",
                    OwnerId = 99999,
                    IgnoredBalanceUserIds = new List<long> { 88888 },
                    AiSettings = new AiSettings
                    {
                        ChatProviders = new List<ChatProviderConfig>
                        {
                            new()
                            {
                                Name = "OpenAI",
                                ProviderType = AiProvider.OpenAI,
                                ApiKey = "sk-test",
                                ModelName = "gpt-4o-mini"
                            },
                            new()
                            {
                                Name = "Gemini",
                                ProviderType = AiProvider.Gemini,
                                ApiKey = "gemini-test",
                                ModelName = "gemini-1.5-flash"
                            }
                        }
                    }
                }
            };

            // ChatServiceFactory setup
            _chatServiceFactoryMock
                .Setup(f => f.GetAvailableProviders())
                .Returns(_appSettings.TelegramBotConfiguration.AiSettings.ChatProviders);
        }

        [Fact]
        public async Task GetBotMetadataAsync_ShouldResolveActiveProviderAndModel_WhenUserHasPreferredProviderAndModel()
        {
            // Arrange
            long userId = 11111;
            var user = new TelegramUserInfo
            {
                Id = userId,
                IsBot = false,
                FirstName = "John",
                PreferredProvider = ChatStrategy.Gemini,
                SelectedModel = "gemini-1.5-pro",
                Balance = 5.00M
            };

            _userInfoRepoMock
                .Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);

            var service = new BotSelfAwarenessService(
                _botClientMock.Object,
                _userInfoRepoMock.Object,
                _appSettings,
                _chatServiceFactoryMock.Object,
                _loggerMock.Object);

            // Act
            var metadata = await service.GetBotMetadataAsync(userId);

            // Assert
            Assert.Equal("Test Bot", metadata.BotName);
            Assert.Equal("test_bot", metadata.BotUsername);
            Assert.Equal("gemini-1.5-pro", metadata.ActiveModel);
            Assert.Equal("Google Gemini", metadata.ActiveProvider);
            Assert.Equal(2097152, metadata.ContextWindow);
            Assert.Contains("vision", metadata.SupportedFeatures);
            Assert.Contains("audio", metadata.SupportedFeatures);
            Assert.False(metadata.UserBalance.IsUnlimited);
            Assert.Equal(5.00M, metadata.UserBalance.Balance);
        }

        [Fact]
        public async Task GetBotMetadataAsync_ShouldResolveFallback_WhenUserHasAutoProviderAndNoModel()
        {
            // Arrange
            long userId = 22222;
            var user = new TelegramUserInfo
            {
                Id = userId,
                IsBot = false,
                FirstName = "Alice",
                PreferredProvider = ChatStrategy.Auto,
                SelectedModel = null,
                Balance = 0.50M
            };

            _userInfoRepoMock
                .Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);

            var service = new BotSelfAwarenessService(
                _botClientMock.Object,
                _userInfoRepoMock.Object,
                _appSettings,
                _chatServiceFactoryMock.Object,
                _loggerMock.Object);

            // Act
            var metadata = await service.GetBotMetadataAsync(userId);

            // Assert
            Assert.Equal("gpt-4o-mini", metadata.ActiveModel);
            Assert.Equal("OpenAI", metadata.ActiveProvider);
            Assert.Equal(128000, metadata.ContextWindow);
            Assert.Contains("structured_json", metadata.SupportedFeatures);
            Assert.Contains("vision", metadata.SupportedFeatures);
            Assert.DoesNotContain("audio", metadata.SupportedFeatures);
            Assert.Equal(0.50M, metadata.UserBalance.Balance);
        }

        [Fact]
        public async Task GetBotMetadataAsync_ShouldResolveUnlimited_ForOwnerOrIgnoredIds()
        {
            // Arrange
            long ownerId = 99999;
            var ownerUser = new TelegramUserInfo
            {
                Id = ownerId,
                IsBot = false,
                FirstName = "Owner",
                PreferredProvider = ChatStrategy.Auto,
                Balance = 0.00M
            };

            _userInfoRepoMock
                .Setup(r => r.Get(It.Is<Expression<Func<TelegramUserInfo, bool>>>(expr => expr.Compile()(ownerUser)), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ownerUser);

            var service = new BotSelfAwarenessService(
                _botClientMock.Object,
                _userInfoRepoMock.Object,
                _appSettings,
                _chatServiceFactoryMock.Object,
                _loggerMock.Object);

            // Act
            var metadata = await service.GetBotMetadataAsync(ownerId);

            // Assert
            Assert.True(metadata.UserBalance.IsUnlimited);
        }

        [Fact]
        public async Task GetBotSystemSummaryPromptAsync_ShouldInjectCorrectTimeAndDetails()
        {
            // Arrange
            long userId = 33333;
            var user = new TelegramUserInfo
            {
                Id = userId,
                IsBot = false,
                FirstName = "Bob",
                PreferredProvider = ChatStrategy.Auto,
                Balance = 2.50M
            };

            _userInfoRepoMock
                .Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);

            var service = new BotSelfAwarenessService(
                _botClientMock.Object,
                _userInfoRepoMock.Object,
                _appSettings,
                _chatServiceFactoryMock.Object,
                _loggerMock.Object);

            // Act
            var prompt = await service.GetBotSystemSummaryPromptAsync(userId);

            // Assert
            Assert.Contains("You are Test Bot (@test_bot)", prompt);
            Assert.Contains("powered by the gpt-4o-mini model via the OpenAI provider", prompt);
            Assert.Contains("Current System Time:", prompt);
            Assert.Contains("NEVER include absolute dates", prompt);
            Assert.Contains("EVERGREEN", prompt);
            Assert.Contains("create_scheduled_newsletter", prompt);
            Assert.Contains("Balance status: $2.5000", prompt);
        }

        [Fact]
        public async Task GetBotSystemSummaryPromptAsync_ShouldInjectCustomSystemPrompt_WhenSetForUser()
        {
            // Arrange
            long userId = 44444;
            var user = new TelegramUserInfo
            {
                Id = userId,
                IsBot = false,
                FirstName = "Bob",
                PreferredProvider = ChatStrategy.Auto,
                Balance = 2.50M,
                SystemPrompt = "CRITICAL RULES:\n1. Zero sycophancy."
            };

            _userInfoRepoMock
                .Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);

            var service = new BotSelfAwarenessService(
                _botClientMock.Object,
                _userInfoRepoMock.Object,
                _appSettings,
                _chatServiceFactoryMock.Object,
                _loggerMock.Object);

            // Act
            var prompt = await service.GetBotSystemSummaryPromptAsync(userId);

            // Assert
            Assert.Contains("CRITICAL PERSONA & COMMUNICATION RULES (SET BY USER):", prompt);
            Assert.Contains("CRITICAL RULES:\n1. Zero sycophancy.", prompt);
        }
    }
}

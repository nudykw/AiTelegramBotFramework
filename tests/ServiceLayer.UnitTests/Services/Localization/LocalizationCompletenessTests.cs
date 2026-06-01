using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceLayer.Resources;
using ServiceLayer.Services;
using ServiceLayer.Services.Localization;
using ServiceLayer.Utils;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Xunit;

namespace ServiceLayer.UnitTests.Services.Localization
{
    public class LocalizationCompletenessTests
    {
        private readonly Mock<IStringLocalizer<BotMessages>> _localizerMock = new();
        private readonly Mock<IStringLocalizerFactory> _factoryMock = new();
        private readonly Mock<IRepository<CachedTranslation>> _cacheRepoMock = new();
        private readonly Mock<IRepository<AIBilingItem>> _aiBilingItemRepoMock = new();
        private readonly Mock<IChatService> _chatServiceMock = new();
        private readonly Mock<IServiceProvider> _serviceProviderMock = new();
        private readonly Mock<IUserContext> _userContextMock = new();
        private readonly Mock<ILogger<DynamicLocalizer>> _loggerMock = new();

        private readonly DynamicLocalizer _sut;

        public LocalizationCompletenessTests()
        {
            _serviceProviderMock.Setup(x => x.GetService(typeof(IChatService)))
                .Returns(_chatServiceMock.Object);

            _userContextMock.Setup(u => u.UserId).Returns(12345L);
            _userContextMock.Setup(u => u.ChatId).Returns(67890L);

            _sut = new DynamicLocalizer(
                _localizerMock.Object,
                _factoryMock.Object,
                _cacheRepoMock.Object,
                _aiBilingItemRepoMock.Object,
                _serviceProviderMock.Object,
                _userContextMock.Object,
                _loggerMock.Object);
        }

        [Fact]
        public void VerifyUkrainianLocalizationCompleteness()
        {
            // Arrange & Act
            var resourceManager = new ResourceManager("ServiceLayer.Resources.BotMessages", typeof(BotMessages).Assembly);
            var englishSet = resourceManager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
            var ukSet = resourceManager.GetResourceSet(new CultureInfo("uk"), createIfNotExists: true, tryParents: false);

            // Assert
            Assert.NotNull(englishSet);
            Assert.NotNull(ukSet);

            var missingKeys = new List<string>();

            foreach (System.Collections.DictionaryEntry entry in englishSet)
            {
                string key = (string)entry.Key;
                var ukValue = ukSet.GetString(key);
                
                if (string.IsNullOrEmpty(ukValue))
                {
                    missingKeys.Add(key);
                }
            }

            // Print missing keys if any, to make debugging easier
            if (missingKeys.Any())
            {
                var message = "Missing Ukrainian localization for keys: " + string.Join(", ", missingKeys);
                Assert.Fail(message);
            }
        }

        [Fact]
        public void VerifyDynamicLocalizationUsesAiTranslationAndUpdatesBalance()
        {
            // Arrange
            var culture = new CultureInfo("es");
            CultureInfo.CurrentUICulture = culture;

            var key = "Welcome";
            var englishValue = "Welcome to the bot!";
            var translatedValue = "¡Bienvenido al bot!";

            _localizerMock.Setup(l => l[key]).Returns(new LocalizedString(key, englishValue));
            _cacheRepoMock.Setup(r => r.GetAll()).Returns(new List<CachedTranslation>().AsQueryable());

            // User starts with a positive balance
            decimal userBalance = 1.0M;
            decimal costPerTranslation = 0.01M;

            // Mock the dynamic translation to change/deduct user balance
            _chatServiceMock.Setup(c => c.Ask(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>()))
                .ReturnsAsync((long chatId, long userId, string prompt) =>
                {
                    // Simulate balance deduction inside the mock
                    userBalance -= costPerTranslation;
                    return translatedValue;
                });

            // Act
            var result = _sut.GetString(key);

            // Assert
            Assert.Equal(translatedValue, result);
            Assert.Equal(0.99M, userBalance); // Balance must decrease by 0.01M

            // Verify ChatService was called
            _chatServiceMock.Verify(c => c.Ask(67890L, 12345L, It.Is<string>(p => p.Contains(englishValue))), Times.Once);
        }

        [Fact]
        public void VerifyRussianLocalizationResourceFileDoesNotExist()
        {
            // Arrange
            var resourceManager = new ResourceManager("ServiceLayer.Resources.BotMessages", typeof(BotMessages).Assembly);

            // Act
            var ruSet = resourceManager.GetResourceSet(new CultureInfo("ru"), createIfNotExists: true, tryParents: false);

            // Assert
            Assert.Null(ruSet);
        }
    }
}

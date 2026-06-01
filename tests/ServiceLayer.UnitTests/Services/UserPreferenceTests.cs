using Moq;
using Xunit;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using ServiceLayer.Services;
using System.Linq.Expressions;
using OpenAI.Chat;
using ServiceLayer.Constans;
using Microsoft.Extensions.Logging;
using ServiceLayer.Services.Localization;

namespace ServiceLayer.UnitTests.Services
{
    public class UserPreferenceTests
    {
        private readonly Mock<IChatServiceFactory> _factoryMock = new();
        private readonly Mock<ILogger<ResilientChatService>> _loggerMock = new();
        private readonly Mock<IDynamicLocalizer> _localizerMock = new();
        private readonly Mock<IRepository<TelegramUserInfo>> _userRepoMock = new();
        private readonly Mock<IChatService> _underlyingServiceMock = new();

        public UserPreferenceTests()
        {
            _factoryMock.Setup(f => f.GetAvailableProviders()).Returns(new[] 
            { 
                new ChatProviderConfig { Name = "OpenAI", ProviderType = AiProvider.OpenAI, ApiKey = "test-key" } 
            });
            _factoryMock.Setup(f => f.CreateService(It.IsAny<string>())).Returns(_underlyingServiceMock.Object);
        }

        private ResilientChatService CreateService()
        {
            return new ResilientChatService(_factoryMock.Object, _loggerMock.Object, _userRepoMock.Object, _localizerMock.Object);
        }

        [Fact]
        public async Task SetGPTModel_ShouldUpdateOnlySpecificUser()
        {
            // Arrange
            long userId = 123;
            string modelName = "gpt-4-test";
            var user = new TelegramUserInfo { Id = userId, IsBot = false, FirstName = "Test", SelectedModel = "old-model" };

            _userRepoMock.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);
            
            _underlyingServiceMock.Setup(s => s.SetGPTModel(modelName, It.IsAny<long?>()))
                .ReturnsAsync((true, ""));

            var service = CreateService();

            // Act
            var (success, error) = await service.SetGPTModel(modelName, userId);

            // Assert
            Assert.True(success);
            Assert.Equal(modelName, user.SelectedModel);
            _userRepoMock.Verify(r => r.Update(user), Times.Once);
            _userRepoMock.Verify(r => r.SaveChanges(), Times.Once);
        }

        [Fact]
        public async Task GetSelectedModel_ShouldReturnUserSpecificModel()
        {
            // Arrange
            long user1Id = 1;
            long user2Id = 2;
            
            var user1 = new TelegramUserInfo { Id = user1Id, IsBot = false, FirstName = "U1", SelectedModel = "model1" };
            var user2 = new TelegramUserInfo { Id = user2Id, IsBot = false, FirstName = "U2", SelectedModel = "model2" };

            _userRepoMock.Setup(r => r.Get(It.Is<Expression<Func<TelegramUserInfo, bool>>>(e => e.Compile()(user1)), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user1);
            _userRepoMock.Setup(r => r.Get(It.Is<Expression<Func<TelegramUserInfo, bool>>>(e => e.Compile()(user2)), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user2);

            var service = CreateService();

            // Act
            var res1 = await service.GetSelectedModel(user1Id);
            var res2 = await service.GetSelectedModel(user2Id);

            // Assert
            Assert.Equal("model1", res1);
            Assert.Equal("model2", res2);
        }

        [Fact]
        public async Task SendMessages2ChatAsync_ShouldUseUserSelectedModel()
        {
            // Arrange
            long userId = 123;
            string selectedModel = "user-specific-model";
            var user = new TelegramUserInfo { Id = userId, IsBot = false, FirstName = "Test", SelectedModel = selectedModel };

            _userRepoMock.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(user);

            _underlyingServiceMock.Setup(s => s.SendMessages2ChatAsync(It.IsAny<long>(), userId, It.IsAny<List<Message>>(), selectedModel))
                .ReturnsAsync(new ChatServiceResponse 
                { 
                    ModelName = selectedModel,
                    Choices = new List<string> { "Response" },
                    ProviderName = "OpenAI"
                });

            var service = CreateService();

            // Act
            var response = await service.SendMessages2ChatAsync(999, userId, new List<Message>());

            // Assert
            Assert.Equal(selectedModel, response.ModelName);
            _underlyingServiceMock.Verify(s => s.SendMessages2ChatAsync(It.IsAny<long>(), userId, It.IsAny<List<Message>>(), selectedModel), Times.Once);
        }
    }
}

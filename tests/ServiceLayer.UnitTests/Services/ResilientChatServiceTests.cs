using Moq;
using Microsoft.Extensions.Logging;
using ServiceLayer.Services;
using ServiceLayer.Constans;
using ServiceLayer.Services.Localization;
using DataBaseLayer.Enums;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using OpenAI.Models;
using Xunit;
using System.Linq.Expressions;

namespace ServiceLayer.UnitTests.Services;

public class ResilientChatServiceTests
{
    private readonly Mock<IChatServiceFactory> _mockFactory;
    private readonly Mock<ILogger<ResilientChatService>> _mockLogger;
    private readonly Mock<IDynamicLocalizer> _mockLocalizer;
    private readonly Mock<IRepository<TelegramUserInfo>> _mockUserRepository;
    private readonly ResilientChatService _service;

    public ResilientChatServiceTests()
    {
        _mockFactory = new Mock<IChatServiceFactory>();
        _mockLogger = new Mock<ILogger<ResilientChatService>>();
        _mockLocalizer = new Mock<IDynamicLocalizer>();
        _mockUserRepository = new Mock<IRepository<TelegramUserInfo>>();
        _service = new ResilientChatService(_mockFactory.Object, _mockLogger.Object, _mockUserRepository.Object, _mockLocalizer.Object);
    }

    [Fact]
    public async Task ExecuteWithFallback_WhenStrategyIsAuto_ShouldTryAllProviders()
    {
        // Arrange
        var userId = 1L;
        var user = new TelegramUserInfo 
        { 
            Id = userId, 
            PreferredProvider = ChatStrategy.Auto,
            IsBot = false,
            FirstName = "TestUser"
        };
        _mockUserRepository.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var providers = new List<ChatProviderConfig>
        {
            new ChatProviderConfig { Name = "P1", ProviderType = AiProvider.OpenAI, ApiKey = "K1" },
            new ChatProviderConfig { Name = "P2", ProviderType = AiProvider.Gemini, ApiKey = "K2" }
        };
        _mockFactory.Setup(f => f.GetAvailableProviders()).Returns(providers);

        var mockChatService1 = new Mock<IChatService>();
        mockChatService1.Setup(s => s.Ask(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Failed"));

        var mockChatService2 = new Mock<IChatService>();
        mockChatService2.Setup(s => s.Ask(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>()))
            .ReturnsAsync("Success from P2");

        _mockFactory.Setup(f => f.CreateService("P1")).Returns(mockChatService1.Object);
        _mockFactory.Setup(f => f.CreateService("P2")).Returns(mockChatService2.Object);

        // Act
        var result = await _service.Ask(1, userId, "Hi");

        // Assert
        Assert.Equal("Success from P2", result);
        _mockFactory.Verify(f => f.CreateService("P1"), Times.Once);
        _mockFactory.Verify(f => f.CreateService("P2"), Times.Once);
    }

    [Fact]
    public async Task ExecuteWithFallback_WhenStrategyIsStrictOpenAI_ShouldOnlyTryOpenAI()
    {
        // Arrange
        var userId = 1L;
        var user = new TelegramUserInfo 
        { 
            Id = userId, 
            PreferredProvider = ChatStrategy.OpenAI,
            IsBot = false,
            FirstName = "TestUser"
        };
        _mockUserRepository.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var providers = new List<ChatProviderConfig>
        {
            new ChatProviderConfig { Name = "OpenAI-1", ProviderType = AiProvider.OpenAI, ApiKey = "K1" },
            new ChatProviderConfig { Name = "Gemini-1", ProviderType = AiProvider.Gemini, ApiKey = "K2" }
        };
        _mockFactory.Setup(f => f.GetAvailableProviders()).Returns(providers);

        var mockChatService = new Mock<IChatService>();
        mockChatService.Setup(s => s.Ask(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>()))
            .ReturnsAsync("Success from OpenAI");

        _mockFactory.Setup(f => f.CreateService("OpenAI-1")).Returns(mockChatService.Object);

        // Act
        var result = await _service.Ask(1, userId, "Hi");

        // Assert
        Assert.Equal("Success from OpenAI", result);
        _mockFactory.Verify(f => f.CreateService("OpenAI-1"), Times.Once);
        _mockFactory.Verify(f => f.CreateService("Gemini-1"), Times.Never);
    }

    [Fact]
    public async Task ExecuteWithFallback_WhenStrictProviderFails_ShouldThrowExceptionImmediately()
    {
        // Arrange
        var userId = 1L;
        var user = new TelegramUserInfo 
        { 
            Id = userId, 
            PreferredProvider = ChatStrategy.OpenAI,
            IsBot = false,
            FirstName = "TestUser"
        };
        _mockUserRepository.Setup(r => r.Get(It.IsAny<Expression<Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var providers = new List<ChatProviderConfig>
        {
            new ChatProviderConfig { Name = "OpenAI-1", ProviderType = AiProvider.OpenAI, ApiKey = "K1" },
            new ChatProviderConfig { Name = "Gemini-1", ProviderType = AiProvider.Gemini, ApiKey = "K2" }
        };
        _mockFactory.Setup(f => f.GetAvailableProviders()).Returns(providers);

        var mockChatService = new Mock<IChatService>();
        mockChatService.Setup(s => s.Ask(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("OpenAI Failed"));

        _mockFactory.Setup(f => f.CreateService("OpenAI-1")).Returns(mockChatService.Object);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _service.Ask(1, userId, "Hi"));
        _mockFactory.Verify(f => f.CreateService("OpenAI-1"), Times.Once);
        _mockFactory.Verify(f => f.CreateService("Gemini-1"), Times.Never);
    }
}

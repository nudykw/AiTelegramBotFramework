using Microsoft.Extensions.DependencyInjection;
using Moq;
using ServiceLayer.Services;
using ServiceLayer.Constans;
using Xunit;
using System.Collections.Generic;
using System.Linq;

namespace ServiceLayer.UnitTests;

[Trait("Service", "ChatServiceFactory")]
public class ChatServiceFactoryTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;

    public ChatServiceFactoryTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
    }

    [Fact]
    public void ValidateAndLoadProviders_ShouldFilterOutPlaceholderApiKeys()
    {
        // Arrange
        var appSettings = new AppSettings
        {
            TelegramBotConfiguration = new() 
            { 
                BotToken = "test",
                AiSettings = new()
                {
                    ChatProviders = new List<ChatProviderConfig>
                    {
                        new ChatProviderConfig
                        {
                            Name = "ValidProvider",
                            ProviderType = AiProvider.OpenAI,
                            ApiKey = "real-api-key"
                        },
                        new ChatProviderConfig
                        {
                            Name = "PlaceholderProvider",
                            ProviderType = AiProvider.Gemini,
                            ApiKey = "[YOUR_GEMINI_API_KEY]"
                        }
                    }
                }
            }
        };

        // Act
        var factory = new ChatServiceFactory(_serviceProviderMock.Object, appSettings);
        var providers = factory.GetAvailableProviders().ToList();

        // Assert
        Assert.Single(providers);
        Assert.Equal("ValidProvider", providers[0].Name);
    }

    [Fact]
    public void IsPlaceholder_ShouldReturnTrueForValidPlaceholder()
    {
        // Assert
        Assert.True(ChatProviderConfig.IsPlaceholder("[YOUR_KEY]"));
        Assert.True(ChatProviderConfig.IsPlaceholder("[YOUR_SOMETHING_ELSE]"));
    }

    [Fact]
    public void IsPlaceholder_ShouldReturnFalseForRegularKey()
    {
        // Assert
        Assert.False(ChatProviderConfig.IsPlaceholder("sk-12345"));
        Assert.False(ChatProviderConfig.IsPlaceholder("YOUR_KEY")); // Missing brackets
        Assert.False(ChatProviderConfig.IsPlaceholder("[MY_KEY]")); // Missing YOUR_
        Assert.False(ChatProviderConfig.IsPlaceholder(null));
        Assert.False(ChatProviderConfig.IsPlaceholder(""));
    }
}

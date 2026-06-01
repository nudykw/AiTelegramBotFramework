using Moq;
using Moq.Protected;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using ServiceLayer.Services.OpenAI;
using ServiceLayer.Services.Telegram.Configuretions;
using ServiceLayer.Services;
using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using ServiceLayer.Constans;
using System.Text.Json;
using System.Net;
using System.Text;
using System.Reflection;

using ServiceLayer.Services.Localization;
using ServiceLayer.Services.Mcp;
using ModelContextProtocol.Client;
using Xunit;

namespace ServiceLayer.UnitTests.Services.OpenAI;

[Trait("Service", "OpenAIService")]
public class OpenAIServiceTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<ILogger<OpenAIService>> _mockLogger;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IDynamicLocalizer> _mockLocalizer;
    private readonly Mock<IRepository<AIBilingItem>> _mockBillingRepository;
    private readonly Mock<IRepository<TelegramUserInfo>> _mockUserInfoRepository;
    private readonly Mock<HttpMessageHandler> _mockHandler;
    private readonly Mock<McpServerManager> _mockMcpManager;
    private readonly AppSettings _appSettings;

    public OpenAIServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockLogger = new Mock<ILogger<OpenAIService>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockLocalizer = new Mock<IDynamicLocalizer>();
        _mockBillingRepository = new Mock<IRepository<AIBilingItem>>();
        _mockUserInfoRepository = new Mock<IRepository<TelegramUserInfo>>();
        _mockHandler = new Mock<HttpMessageHandler>();
        _appSettings = new AppSettings
        {
            TelegramBotConfiguration = new TelegramBotConfiguration { BotToken = "test-bot-token" }
        };

        _mockMcpManager = new Mock<McpServerManager>(_mockServiceProvider.Object, new Mock<ILogger<McpServerManager>>().Object, _appSettings);

        _mockServiceProvider
            .Setup(x => x.GetService(typeof(IRepository<AIBilingItem>)))
            .Returns(_mockBillingRepository.Object);

        _mockServiceProvider
            .Setup(x => x.GetService(typeof(IRepository<TelegramUserInfo>)))
            .Returns(_mockUserInfoRepository.Object);

        _mockServiceProvider
            .Setup(x => x.GetService(typeof(IOptions<AppSettings>)))
            .Returns(Mock.Of<IOptions<AppSettings>>(o => o.Value == _appSettings));

        var httpClient = new HttpClient(_mockHandler.Object);
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        _mockMcpManager.Setup(m => m.GetAllToolsAsync()).ReturnsAsync(new List<McpClientTool>());
    }

    private OpenAIService CreateService(OpenAIClient? api = null)
    {
        var config = new ChatProviderConfig
        {
            Name = "OpenAI",
            ProviderType = AiProvider.OpenAI,
            ApiKey = "sk-test-key",
            ModelName = "gpt-4o-mini"
        };

        return new OpenAIService(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            config,
            _mockHttpClientFactory.Object,
            _mockLocalizer.Object,
            _mockMcpManager.Object,
            api);
    }

    [Fact]
    public async Task Ask_ShouldReturnCombinedText()
    {
        // Arrange
        var jsonResponse = JsonSerializer.Serialize(new 
        {
            id = "chatcmpl-123",
            @object = "chat.completion",
            created = 1677652288,
            model = "gpt-4o-mini",
            choices = new[]
            {
                new { message = new { role = "assistant", content = "Hello world!" }, finish_reason = "stop", index = 0 }
            },
            usage = new { prompt_tokens = 9, completion_tokens = 12, total_tokens = 21 }
        });

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", 
                ItExpr.IsAny<HttpRequestMessage>(), 
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            });

        // Use OpenAISettings as recommended with property initialization
        var api = new OpenAIClient(new OpenAIAuthentication("sk-test-key"), client: new HttpClient(_mockHandler.Object));
        
        var service = CreateService(api);

        // Act
        var result = await service.Ask(1, 1, "Hi");

        // Assert
        Assert.Contains("Hello world!", result);
        _mockBillingRepository.Verify(r => r.Add(It.IsAny<AIBilingItem>()), Times.Once);
        _mockBillingRepository.Verify(r => r.SaveChanges(), Times.Once);
    }

    [Fact]
    public async Task SendMessages2ChatAsync_ShouldPassCorrectParametersAndSaveBilling()
    {
        // Arrange
        var jsonResponse = JsonSerializer.Serialize(new 
        {
            id = "chatcmpl-123",
            @object = "chat.completion",
            created = 1677652288,
            model = "gpt-4o-mini",
            choices = new[]
            {
                new { message = new { role = "assistant", content = "Response" }, finish_reason = "stop", index = 0 }
            },
            usage = new { prompt_tokens = 10, completion_tokens = 20, total_tokens = 30 }
        });

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", 
                ItExpr.IsAny<HttpRequestMessage>(), 
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            });

        var api = new OpenAIClient(new OpenAIAuthentication("sk-test-key"), client: new HttpClient(_mockHandler.Object));
        
        var service = CreateService(api);

        var messages = new List<Message> { new Message(Role.User, "Test message") };

        // Act
        var response = await service.SendMessages2ChatAsync(123, 456, messages);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("gpt-4o-mini", response.ModelName);
        Assert.Equal(30, response.TotalTokens);
        Assert.Equal("OpenAI", response.ProviderName);

        _mockBillingRepository.Verify(r => r.Add(It.Is<AIBilingItem>(item => 
            item.TelegramChatInfoId == 123 && 
            item.TelegramUserInfoId == 456 && 
            item.PromptTokens == 10 && 
            item.CompletionTokens == 20 && 
            item.TotalTokens == 30 &&
            item.ProviderName == "OpenAI")), Times.Once);
    }

    [Fact]
    public async Task GetAvailibleModels_ShouldReturnFilteredModels()
    {
        // Arrange
        var modelsJson = JsonSerializer.Serialize(new 
        {
            data = new[]
            {
                new { id = "gpt-4", @object = "model", created = 1677652288, owned_by = "openai" },
                new { id = "unknown-model", @object = "model", created = 1677652288, owned_by = "openai" },
                new { id = "gpt-4o-mini", @object = "model", created = 1677652288, owned_by = "openai" }
            }
        });

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", 
                ItExpr.IsAny<HttpRequestMessage>(), 
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(modelsJson, Encoding.UTF8, "application/json")
            });

        var api = new OpenAIClient(new OpenAIAuthentication("sk-test-key"), client: new HttpClient(_mockHandler.Object));
        
        var service = CreateService(api);

        // Act
        var result = await service.GetAvailibleModels(1, validateModels: false);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(result, m => m.Id == "gpt-4");
        Assert.Contains(result, m => m.Id == "gpt-4o-mini");
        Assert.DoesNotContain(result, m => m.Id == "unknown-model");
    }

    [Fact]
    public async Task SaveBilling_ShouldDeductBalanceAndSetLastAiInteraction()
    {
        // Arrange
        var userId = 456L;
        var chatId = 123L;
        var initialBalance = 0.1M;
        var user = new TelegramUserInfo { Id = userId, Balance = initialBalance, FirstName = "Test", IsBot = false };
        
        _mockUserInfoRepository.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var jsonResponse = JsonSerializer.Serialize(new 
        {
            id = "chatcmpl-123",
            @object = "chat.completion",
            created = 1677652288,
            model = "gpt-4o-mini",
            choices = new[]
            {
                new { message = new { role = "assistant", content = "Response" }, finish_reason = "stop", index = 0 }
            },
            usage = new { prompt_tokens = 1000, completion_tokens = 1000, total_tokens = 2000 }
        });

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync", 
                ItExpr.IsAny<HttpRequestMessage>(), 
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
            });

        var api = new OpenAIClient(new OpenAIAuthentication("sk-test-key"), client: new HttpClient(_mockHandler.Object));
        var service = CreateService(api);

        // Act
        await service.SendMessages2ChatAsync(chatId, userId, new List<Message> { new Message(Role.User, "Test") });

        // Assert
        Assert.True(user.Balance < initialBalance, $"Balance {user.Balance} should be less than {initialBalance}");
        Assert.NotNull(user.LastAiInteraction);
        _mockUserInfoRepository.Verify(r => r.Update(user), Times.Once);
        _mockBillingRepository.Verify(r => r.SaveChanges(), Times.Once);
    }

    [Fact]
    public async Task SaveBilling_ShouldNotDeductBalanceForOwner()
    {
        // Arrange
        var ownerId = 7342855906L;
        _appSettings.TelegramBotConfiguration.OwnerId = ownerId;
        var user = new TelegramUserInfo { Id = ownerId, Balance = 1.0M, FirstName = "Owner", IsBot = false };
        
        _mockUserInfoRepository.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var jsonResponse = JsonSerializer.Serialize(new 
        {
            id = "chatcmpl-123",
            choices = new[] { new { message = new { role = "assistant", content = "OK" } } },
            usage = new { prompt_tokens = 100, completion_tokens = 100, total_tokens = 200 }
        });

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(jsonResponse) });

        var api = new OpenAIClient(new OpenAIAuthentication("sk-test-key"), client: new HttpClient(_mockHandler.Object));
        var service = CreateService(api);

        // Act
        await service.SendMessages2ChatAsync(1, ownerId, new List<Message> { new Message(Role.User, "test") });

        // Assert
        Assert.Equal(1.0M, user.Balance); // Should not change
        Assert.NotNull(user.LastAiInteraction); // But should still update interaction time
    }

    [Fact]
    public async Task SaveBilling_ShouldNotDeductBalanceWhenInitialBalanceIsZero()
    {
        // Arrange
        var userId = 456L;
        var chatId = 123L;
        var initialUserBalance = 1.0M;
        _appSettings.TelegramBotConfiguration.InitialBalance = 0M; // Make it free
        
        var user = new TelegramUserInfo { Id = userId, Balance = initialUserBalance, FirstName = "Test", IsBot = false };
        
        _mockUserInfoRepository.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var jsonResponse = JsonSerializer.Serialize(new 
        {
            id = "chatcmpl-123",
            choices = new[] { new { message = new { role = "assistant", content = "OK" } } },
            usage = new { prompt_tokens = 100, completion_tokens = 100, total_tokens = 200 }
        });

        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(jsonResponse) });

        var api = new OpenAIClient(new OpenAIAuthentication("sk-test-key"), client: new HttpClient(_mockHandler.Object));
        var service = CreateService(api);

        // Act
        await service.SendMessages2ChatAsync(chatId, userId, new List<Message> { new Message(Role.User, "test") });

        // Assert
        Assert.Equal(initialUserBalance, user.Balance); // Should NOT change
        Assert.NotNull(user.LastAiInteraction);
    }

    [Fact]
    public async Task SendMessages2ChatAsync_GrokProvider_StripsNameFromNonNullRoles()
    {
        // Arrange
        var grokConfig = new ChatProviderConfig
        {
            Name = "Grok",
            ProviderType = AiProvider.Grok,
            ApiKey = "sk-test-key",
            ModelName = "grok-beta"
        };
        
        var service = new OpenAIService(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            grokConfig,
            _mockHttpClientFactory.Object,
            _mockLocalizer.Object,
            _mockMcpManager.Object,
            new OpenAIClient(new OpenAIAuthentication("sk-test-key"), client: new HttpClient(_mockHandler.Object)));

        var user = new TelegramUserInfo { Id = 456L, Balance = 1.0M, FirstName = "Test", IsBot = false };
        _mockUserInfoRepository.Setup(r => r.Get(It.IsAny<System.Linq.Expressions.Expression<System.Func<TelegramUserInfo, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var jsonResponse = JsonSerializer.Serialize(new 
        {
            id = "chatcmpl-123",
            choices = new[] { new { message = new { role = "assistant", content = "OK" } } },
            usage = new { prompt_tokens = 100, completion_tokens = 100, total_tokens = 200 }
        });

        HttpRequestMessage? sentRequest = null;
        string requestBodyContent = "";
        _mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, ct) => 
            {
                sentRequest = req;
                if (req.Content != null)
                {
                    requestBodyContent = req.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
                }
            })
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK, Content = new StringContent(jsonResponse) });

        var messages = new List<Message> 
        { 
            new Message(Role.System, "System prompt", "System"),
            new Message(Role.User, "User query", "UserNick"),
            new Message(Role.Assistant, "Previous response", "BotNick")
        };

        // Act
        await service.SendMessages2ChatAsync(123, 456, messages);

        // Assert
        Assert.NotNull(sentRequest);
        var requestBody = requestBodyContent;
        
        // Grok sanitization should strip Name from System and Assistant roles, but keep User name!
        Assert.Contains("\"role\":\"system\"", requestBody);
        Assert.DoesNotContain("\"name\":\"System\"", requestBody);
        
        Assert.Contains("\"role\":\"assistant\"", requestBody);
        Assert.DoesNotContain("\"name\":\"BotNick\"", requestBody);
        
        Assert.Contains("\"role\":\"user\"", requestBody);
        Assert.Contains("\"name\":\"UserNick\"", requestBody);
    }
}

using DataBaseLayer.Models;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using ServiceLayer.Services;
using ServiceLayer.Constans;
using ServiceLayer.IntegrationTests.Fixtures;
using ServiceLayer.Services.OpenAI;
using ServiceLayer.Utils;
using System.IO;
using System.Text;
using System.Linq;
using Xunit.Abstractions;

using Xunit;

namespace ServiceLayer.IntegrationTests.Services.OpenAI;

[Trait("Service", "OpenAIService")]
public class OpenAIServiceTests : IClassFixture<TestAppFixture>
{
    private readonly OpenAIService _openAIService;
    private readonly IRepository<AIBilingItem> _billingRepository;
    private readonly long _testChatId = 12345;
    private readonly long _testUserId = 67890;
    private readonly ITestOutputHelper _output;

    public OpenAIServiceTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        
        _openAIService = fixture.ServiceProvider.GetRequiredService<OpenAIService>();
        _billingRepository = fixture.ServiceProvider.GetRequiredService<IRepository<AIBilingItem>>();
    }

    private readonly TestAppFixture _fixture;

    [Fact]
    public async Task GetAvailibleModels_ShouldReturnModelsList()
    {
        // Act
        var models = await _openAIService.GetAvailibleModels(validateModels: false);

        // Assert
        Assert.NotNull(models);
        Assert.NotEmpty(models);
    }

    [SkippableFact]
    public async Task Ask_ShouldReturnResponse_AndSaveBilling()
    {
        SkipIfApiKeyNotConfigured();
        
        // Arrange
        var message = "Hello, this is a test message. Please reply with 'OK'.";

        // Act
        var response = await _openAIService.Ask(_testChatId, _testUserId, message);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);

        // Verify Billing
        var billingItems = _billingRepository.GetAll().ToList();
        var billingItem = billingItems.LastOrDefault(p => p.TelegramChatInfoId == _testChatId);
        Assert.NotNull(billingItem);
        Assert.True(billingItem.TotalTokens > 0);
        Assert.True(billingItem.Cost > 0);
        Assert.NotNull(billingItem.ProviderName);
    }

    [SkippableFact]
    public async Task SendMessages2ChatAsync_ShouldReturnChatResponse_AndSaveBilling()
    {
        SkipIfApiKeyNotConfigured();
        
        // Arrange
        var messages = new List<Message> { new Message(Role.User, "Say 'Hello User'") };

        // Act
        var response = await _openAIService.SendMessages2ChatAsync(_testChatId, _testUserId, messages);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Choices);
        Assert.Contains("Hello User", response.Choices[0], StringComparison.OrdinalIgnoreCase);

        // Verify Billing
        var billingItems = _billingRepository.GetAll().ToList();
        var billingItem = billingItems.LastOrDefault(p => p.TelegramChatInfoId == _testChatId);
        Assert.NotNull(billingItem);
        Assert.Equal("gpt-4o-mini", billingItem.ModelName);
        Assert.NotNull(billingItem.ProviderName);
    }

    // TODO: Temporarily disabled due to flaky OpenAI API network timeouts. Enable back when network is stable.
    [SkippableFact(Skip = "Temporarily disabled due to flaky OpenAI API network timeouts")]
    public async Task GenerateImage_ShouldReturnImageUrl_AndSaveBilling()
    {
        SkipIfApiKeyNotConfigured();
        
        // Arrange
        var prompt = "A small blue robot dancing in the rain, digital art style.";

        // Act
        var result = await _openAIService.GenerateImage(_testChatId, _testUserId, prompt);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Choices);
        Assert.True(result.Choices[0].StartsWith("http") || result.Choices[0].StartsWith("iVBORw"), "Expected image URL or base64-encoded PNG");

        // Verify Billing
        var billingItems = _billingRepository.GetAll().ToList();
        var billingItem = billingItems.LastOrDefault(p => p.TelegramChatInfoId == _testChatId && (p.ModelName.Contains("dall-e") || p.ModelName.Contains("gpt-image")));
        Assert.NotNull(billingItem);
    }

    [SkippableFact]
    public async Task AudioTranscription_ShouldReturnTranscription_AndSaveBilling()
    {
        SkipIfApiKeyNotConfigured();
        
        // Arrange
        using var audioStream = GetDummyAudioStream();
        var audioName = "test.wav";

        // Act
        var transcription = await _openAIService.AudioTranscription(_testChatId, _testUserId, audioStream, audioName);

        // Assert
        Assert.NotNull(transcription);

        // Verify Billing
        var billingItems = _billingRepository.GetAll().ToList();
        var billingItem = billingItems.LastOrDefault(p => p.TelegramChatInfoId == _testChatId && p.ModelName.Contains("whisper"));
        Assert.NotNull(billingItem);
    }

    private Stream GetDummyAudioStream()
    {
        // Creating a very small valid WAV file with 1 second of silence
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + 44100 * 2); // File size
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16); // Subchunk size
            writer.Write((short)1); // Audio format PCM
            writer.Write((short)1); // Mono
            writer.Write(44100); // Sample rate
            writer.Write(44100 * 2); // Byte rate
            writer.Write((short)2); // Block align
            writer.Write((short)16); // Bits per sample
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(44100 * 2); // Data size
            for (int i = 0; i < 44100; i++)
            {
                writer.Write((short)0); // Silence
            }
        }
        stream.Position = 0;
        return stream;
    }

    private void SkipIfApiKeyNotConfigured()
    {
        var apiKey = _fixture.ServiceProvider.GetRequiredService<AppSettings>()
            .TelegramBotConfiguration?.AiSettings?.ChatProviders
            .FirstOrDefault(p => p.ProviderType == AiProvider.OpenAI)?.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) ||
            apiKey == "sk-test-placeholder" ||
            apiKey.StartsWith("sk-test-"))
        {
            throw new SkipException("Skipped: OpenAI API key is not configured or uses placeholder value. " +
                "Copy Configs/appsettings.Test.json.example to Configs/appsettings.Test.json and fill in your real API key.");
        }
    }
}

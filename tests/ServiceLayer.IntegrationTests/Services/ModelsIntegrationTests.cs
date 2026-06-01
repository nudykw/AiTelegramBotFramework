using Microsoft.Extensions.DependencyInjection;
using ServiceLayer.Constans;
using ServiceLayer.Services;
using ServiceLayer.IntegrationTests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace ServiceLayer.IntegrationTests.Services;

[Trait("Service", "ModelVerification")]
public class ModelsIntegrationTests : IClassFixture<TestAppFixture>
{
    private readonly IChatServiceFactory _chatServiceFactory;
    private readonly ITestOutputHelper _output;

    public ModelsIntegrationTests(TestAppFixture fixture, ITestOutputHelper output)
    {
        _chatServiceFactory = fixture.ServiceProvider.GetRequiredService<IChatServiceFactory>();
        _output = output;
    }

    [Fact]
    public async Task GetAllModels_ForConfiguredProviders_ShouldSucceed()
    {
        // Arrange
        var providers = _chatServiceFactory.GetAvailableProviders().ToList();
        _output.WriteLine($"Found {providers.Count} configured providers.");
        var failures = new List<string>();

        foreach (var config in providers)
        {
            _output.WriteLine($"Testing Provider: {config.Name} ({config.ProviderType})");
            
            try
            {
                // Act
                var service = _chatServiceFactory.CreateService(config.Name);
                var models = await service.GetAvailibleModels(validateModels: false);

                // Assert
                Assert.NotNull(models);
                _output.WriteLine($"  Successfully retrieved {models.Count} models.");
                
                // Print a few models to verify
                foreach (var model in models.Take(5))
                {
                    _output.WriteLine($"    - {model.Id}");
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  FAILED for provider '{config.Name}': {ex.Message}");
                failures.Add(config.Name);
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"The following providers failed: {string.Join(", ", failures)}");
        }
    }

    [Fact]
    [Trait("Category", "ModelVerification")]
    public async Task TestCurrentModel_ForConfiguredProviders_ShouldRespond()
    {
        // Arrange
        var providers = _chatServiceFactory.GetAvailableProviders().ToList();
        _output.WriteLine($"Found {providers.Count} configured providers to test communication.");
        var failures = new List<string>();
        const string testPrompt = "Hi, say 'OK' if you can hear me. Respond with only one word.";

        foreach (var config in providers)
        {
            _output.WriteLine($"Testing Provider Communication: {config.Name} ({config.ProviderType}) using model {config.ModelName ?? "default"}");
            
            try
            {
                // Act
                var service = _chatServiceFactory.CreateService(config.Name);
                
                // We use dummy IDs for testing. Since we use InMemory DB in fixture, 
                // this should not cause FK issues in most cases with InMemory provider.
                var response = await service.Ask(12345, 12345, testPrompt);

                // Assert
                Assert.False(string.IsNullOrWhiteSpace(response), $"Provider {config.Name} returned an empty response.");
                _output.WriteLine($"  SUCCESS. Response: {response.Trim()}");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  FAILED for provider '{config.Name}': {ex.Message}");
                failures.Add($"{config.Name}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail($"The following providers failed to respond: {string.Join("; ", failures)}");
        }
    }
}

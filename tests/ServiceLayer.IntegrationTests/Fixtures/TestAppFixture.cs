using DataBaseLayer.Contexts;
using DataBaseLayer.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceLayer.Services;
using ServiceLayer.Services.OpenAI;
using ServiceLayer.Services.GeminiChat.DotNet;
using ServiceLayer.Services.MessageProcessor;
using Telegram.Bot;
using Telegram.Bot.Requests;
using ServiceLayer.Constans;
using Moq;
using ServiceLayer.Services.Localization;
using ServiceLayer.Utils;
using ServiceLayer.Services.Mcp;
using ServiceLayer.Services.Mcp.GoogleDrive;
using Xunit;

namespace ServiceLayer.IntegrationTests.Fixtures;

public class TestAppFixture : IDisposable
{
    public ServiceProvider ServiceProvider { get; private set; }
    
    public TestAppFixture()
    {
        // Check API key before creating services (validation happens in OpenAIService constructor)
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Test.json", optional: true, reloadOnChange: false)
            .Build();
        
        var appSection = configuration.GetSection(AppSettings.Configuration);
        var appSettings = appSection.Get<AppSettings>() ?? new AppSettings();
        appSettings.TelegramBotConfiguration ??= new ServiceLayer.Services.Telegram.Configuretions.TelegramBotConfiguration();
        
        var apiKey = appSettings.TelegramBotConfiguration.AiSettings.ChatProviders
            .FirstOrDefault(p => p.ProviderType == AiProvider.OpenAI)?.ApiKey;
        
        if (string.IsNullOrWhiteSpace(apiKey) ||
            apiKey == "sk-test-placeholder" ||
            apiKey.StartsWith("sk-test-"))
        {
            throw new SkipException("Skipped: OpenAI API key is not configured or uses placeholder value. " +
                "Copy Configs/appsettings.Test.json.example to Configs/appsettings.Test.json and fill in your real API key.");
        }
        
        var services = new ServiceCollection();

        // Build configuration in layers:
        //   1. appsettings.json   — linked from TelegramBotApp (base, optional)
        //   2. appsettings.Test.json — local override with real API keys (optional, gitignored)
        //      Copy appsettings.Test.json.example → appsettings.Test.json and fill in your keys.
        services.AddSingleton<IConfiguration>(configuration);

        // Bind AppSettings; fall back to a safe default when the file is absent or incomplete
        services.Configure<AppSettings>(appSection);

        // Ensure TelegramBotConfiguration is never null so DI consumers don't throw
        services.AddSingleton(appSettings);

        // Add InMemory DB instead of actual SQLite
        services.AddDbContext<StoreContext>(options =>
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));
            
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Add HttpClient for pricing service
        services.AddHttpClient();

        // Register default OpenAI provider config for tests (falls back to a stub when no real key is configured)
        var openAiConfig = appSettings.TelegramBotConfiguration.AiSettings.ChatProviders
            .FirstOrDefault(p => p.ProviderType == AiProvider.OpenAI)
            ?? new ChatProviderConfig
            {
                Name = "OpenAI-Default",
                ProviderType = AiProvider.OpenAI,
                ApiKey = "sk-test-placeholder",
                ModelName = AiModel.Gpt4oMini
            };
        services.AddSingleton(openAiConfig);

        services.AddLocalization();
        services.AddScoped<IUserContext, UserContext>();
        services.AddScoped<IDynamicLocalizer, DynamicLocalizer>();

        services.AddSingleton<IGoogleDriveAuthService, GoogleDriveAuthService>();
        services.AddScoped<IGoogleDriveService, GoogleDriveService>();

        services.AddSingleton<OpenAIService>();
        services.AddSingleton<ChatGeminiService>();
        services.AddSingleton<IChatServiceFactory, ChatServiceFactory>();
        services.AddSingleton<McpServerManager>();

        ServiceProvider = services.BuildServiceProvider();

        // Ensure purely fresh DB scheme created in memory
        var dbContext = ServiceProvider.GetRequiredService<StoreContext>();
        dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        if (ServiceProvider != null)
        {
            ServiceProvider.Dispose();
        }
    }
}

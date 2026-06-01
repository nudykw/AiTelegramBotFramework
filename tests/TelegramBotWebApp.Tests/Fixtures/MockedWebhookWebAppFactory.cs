using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DataBaseLayer.Contexts;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace TelegramBotWebApp.Tests.Fixtures;

/// <summary>
/// Factory variant for Webhook mode that replaces <see cref="IUpdateHandler"/>
/// with a <see cref="Mock{T}"/> — allowing tests to verify the handler is called
/// without touching the real Telegram API, database, or AI services.
/// </summary>
public sealed class MockedWebhookWebAppFactory : WebApplicationFactory<Program>
{
    private const string WebhookBaseUrl = "https://example.com/";
    private readonly string _connectionString = SharedPostgresContainer.GetUniqueConnectionString();

    /// <summary>The mock injected in place of the real UpdateHandler.</summary>
    public Mock<IUpdateHandler> HandlerMock { get; } = CreateHandlerMock();

    private static Mock<IUpdateHandler> CreateHandlerMock()
    {
        var mock = new Mock<IUpdateHandler>(MockBehavior.Loose);

        // Default: accepts any update and does nothing (fire-and-forget safe)
        mock.Setup(h => h.HandleUpdateAsync(
                It.IsAny<ITelegramBotClient>(),
                It.IsAny<Update>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mock.Setup(h => h.HandleErrorAsync(
                It.IsAny<ITelegramBotClient>(),
                It.IsAny<Exception>(),
                It.IsAny<HandleErrorSource>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return mock;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.Sources.Clear();
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppSettings:TelegramBotConfiguration:BotToken"]   = "1234567890:AABBCCDDEEFFaabbccddeeff-TestToken00",
                ["AppSettings:TelegramBotConfiguration:OwnerId"]    = "0",
                ["AppSettings:TelegramBotConfiguration:BaseApiUrl"] = WebhookBaseUrl,
                ["AppSettings:Database:Provider"]                   = "PostgreSql",
                ["AppSettings:Database:ConnectionString"]           = _connectionString,
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace real DB context with shared PostgreSQL
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<StoreContext>));
            if (dbDescriptor is not null) services.Remove(dbDescriptor);
            services.AddDbContext<StoreContext>(options =>
                DataBaseLayer.MigrationConfigurator.Configure(options, DataBaseLayer.Enums.DatabaseProvider.PostgreSql, _connectionString));

            // Remove real HostedServices (no Telegram polling / webhook setup)
            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();
            foreach (var d in hostedServices) services.Remove(d);

            // ── Replace IUpdateHandler with our mock ─────────────────────────
            // The webhook endpoint resolves IUpdateHandler — remove the real forwarding
            // registration and insert the mock directly.
            var handlerDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IUpdateHandler));
            if (handlerDescriptor is not null) services.Remove(handlerDescriptor);

            services.AddScoped<IUpdateHandler>(_ => HandlerMock.Object);
        });

        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}

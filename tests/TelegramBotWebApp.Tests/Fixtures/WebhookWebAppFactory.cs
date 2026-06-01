using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DataBaseLayer.Contexts;

namespace TelegramBotWebApp.Tests.Fixtures;

/// <summary>
/// Factory variant that starts the app in <b>Webhook mode</b>.
/// The <c>BaseApiUrl</c> has a trailing slash to verify TrimEnd normalisation.
/// </summary>
public sealed class WebhookWebAppFactory : WebApplicationFactory<Program>
{
    private const string WebhookBaseUrl = "https://example.com/"; // trailing slash is intentional
    private readonly string _connectionString = SharedPostgresContainer.GetUniqueConnectionString();

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
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<StoreContext>));
            if (dbDescriptor is not null) services.Remove(dbDescriptor);

            services.AddDbContext<StoreContext>(options =>
                DataBaseLayer.MigrationConfigurator.Configure(options, DataBaseLayer.Enums.DatabaseProvider.PostgreSql, _connectionString));

            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();
            foreach (var d in hostedServices) services.Remove(d);
        });

        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}

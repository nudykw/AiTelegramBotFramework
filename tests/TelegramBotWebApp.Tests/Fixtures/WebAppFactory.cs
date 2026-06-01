using DataBaseLayer.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DataBaseLayer.Contexts;
using ServiceLayer.Services.Telegram.Configuretions;

namespace TelegramBotWebApp.Tests.Fixtures;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that spins up the full
/// <see cref="TelegramBotWebApp"/> pipeline in-process with:
/// <list type="bullet">
///   <item>In-memory SQLite database (no external dependencies)</item>
///   <item>Telegram bot client replaced by a no-op (no real API calls)</item>
///   <item>Configurable <see cref="TelegramBotConfiguration.BaseApiUrl"/> for
///         Polling / Webhook mode switching in tests</item>
/// </list>
/// </summary>
public class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = SharedPostgresContainer.GetUniqueConnectionString();
    private readonly string _uploadsDirName = Path.Combine(AppContext.BaseDirectory, $"test-mcp-uploads-{Guid.NewGuid():N}");

    /// <summary>
    /// When set, the app starts in Webhook mode.
    /// When null or empty, the app starts in Polling mode.
    /// </summary>
    public string? BaseApiUrl { get; init; }

    /// <summary>Bot token used in config (does not need to be real).</summary>
    public string BotToken { get; init; } = "1234567890:AABBCCDDEEFFaabbccddeeff-TestToken00";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Find the project root to locate appsettings.json
            var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            var configPath = Path.Combine(projectRoot, "Configs", "appsettings.json");
            
            if (File.Exists(configPath))
            {
                config.AddJsonFile(configPath, optional: false);
            }

            // Override only specific keys, but keep existing sources like appsettings.json
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppSettings:TelegramBotConfiguration:BotToken"]   = BotToken,
                ["AppSettings:TelegramBotConfiguration:OwnerId"]    = "0",
                ["AppSettings:TelegramBotConfiguration:BaseApiUrl"] = BaseApiUrl ?? "",
                ["AppSettings:Database:Provider"]                   = "PostgreSql",
                ["AppSettings:Database:ConnectionString"]           = _connectionString,
                ["AppSettings:Mcp:UploadsDir"]                      = _uploadsDirName,
                ["MCP_RUNTIME_CONTAINER"]                           = "",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the real StoreContext registration added by AddBotServices()
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<StoreContext>));
            if (dbDescriptor is not null)
                services.Remove(dbDescriptor);

            // Use the shared PostgreSQL container connection
            services.AddDbContext<StoreContext>(options =>
                DataBaseLayer.MigrationConfigurator.Configure(options, DatabaseProvider.PostgreSql, _connectionString));

            // Remove real IHostedService registrations so the bot doesn't try
            // to connect to Telegram during tests
            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();
            foreach (var d in hostedServices)
                services.Remove(d);
        });

        // Use test environment so the app doesn't require production secrets
        builder.UseEnvironment("Testing");

        Environment.SetEnvironmentVariable("MCP_UPLOADS_DIR", _uploadsDirName);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                if (Directory.Exists(_uploadsDirName))
                {
                    Directory.Delete(_uploadsDirName, true);
                }
            }
            catch { }
        }
    }
}

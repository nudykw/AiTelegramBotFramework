using DataBaseLayer;
using DataBaseLayer.Contexts;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServiceLayer.Services;
using ServiceLayer.Services.AudioTranscriptor;
using ServiceLayer.Services.OpenAI;
using ServiceLayer.Services.MessageProcessor;
using ServiceLayer.Services.Telegram;
using ServiceLayer.Services.Localization;
using ServiceLayer.Services.Mcp;
using ServiceLayer.Utils;
using ServiceLayer.Extensions;
using Telegram.Bot;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        // Load shared base config (AI keys, BotToken, SQLite default):
        //   Dev:  Configs/ is at the repo root, 4 levels up from bin/Debug/net*/
        //   Prod: Configs/ is copied next to the binary
        string? configsDir = null;
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null)
        {
            var candidate = Path.Combine(currentDir.FullName, "Configs");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "appsettings.json")))
            {
                configsDir = candidate;
                break;
            }
            currentDir = currentDir.Parent;
        }
        configsDir ??= Path.Combine(AppContext.BaseDirectory, "Configs");

        cfg.AddJsonFile(Path.Combine(configsDir, "appsettings.json"), optional: true, reloadOnChange: true);
        // appsettings.json in the project dir overrides (debug logging, local DB path)
        // already added by CreateDefaultBuilder — no need to add again
    })
    .ConfigureServices((context, services) =>
    {
        services.AddCommonBotServices(context.Configuration);
        
        // App-specific services
        services.AddScoped<UpdateHandler>();
        services.AddScoped<ReceiverService>();
        services.AddHostedService<PollingService>();
    })
    .Build();

using (var scope = host.Services.CreateScope())
{
    var serviceProvider = scope.ServiceProvider;
    MigrationConfigurator.ApplyMigrations(serviceProvider);
}

await host.RunAsync();
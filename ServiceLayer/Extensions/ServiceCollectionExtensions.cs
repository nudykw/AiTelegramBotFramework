using DataBaseLayer.Contexts;
using DataBaseLayer.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceLayer.Services;
using ServiceLayer.Services.AudioTranscriptor;
using ServiceLayer.Services.Localization;
using ServiceLayer.Services.Mcp;
using ServiceLayer.Services.MessageProcessor;
using ServiceLayer.Services.OpenAI;
using ServiceLayer.Services.Telegram;
using ServiceLayer.Services.Memory;
using ServiceLayer.Utils;
using Telegram.Bot;

namespace ServiceLayer.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommonBotServices(this IServiceCollection services, IConfiguration configuration)
    {
        // ── App configuration ────────────────────────────────────────────────
        var appSection = configuration.GetSection(AppSettings.Configuration);
        services.Configure<AppSettings>(appSection);
        
        // Provide AppSettings as a directly-resolvable singleton via IOptions
        services.AddSingleton<AppSettings>(sp =>
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppSettings>>().Value);

        // Provide TelegramBotConfiguration as a singleton derived from AppSettings
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            return settings.TelegramBotConfiguration
                ?? new ServiceLayer.Services.Telegram.Configuretions.TelegramBotConfiguration();
        });

        // ── Telegram Bot client ──────────────────────────────────────────────
        services.AddHttpClient();
        services.AddHttpClient("telegram_bot_client")
            .AddTypedClient<ITelegramBotClient>((httpClient, sp) =>
            {
                var cfg = sp.GetRequiredService<AppSettings>();
                var token = cfg.TelegramBotConfiguration?.BotToken ?? string.Empty;
                var options = new TelegramBotClientOptions(string.IsNullOrEmpty(token) ? "0:test" : token);
                return new TelegramBotClient(options, httpClient);
            });

        // ── Chat / AI services ───────────────────────────────────────────────
        services.AddScoped<IChatServiceFactory, ChatServiceFactory>();
        services.AddScoped<IChatService, ResilientChatService>();
        services.AddScoped<IReactionService, ReactionService>();
        services.AddMemoryCache();
        services.AddScoped<ISummaryService, SummaryService>();
        services.AddScoped<MessageProcessor>();
        services.AddScoped<AudioTranscriptorService>();
        
        // ── Long-Term Semantic Memory (RAG) ──────────────────────────────────
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
        services.AddSingleton<ISemanticMemoryService, SemanticMemoryService>();
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            return new Qdrant.Client.QdrantClient(new Uri(settings.MemorySettings.VectorDbUrl));
        });

        // ── MCP (Model Context Protocol) ─────────────────────────────────────
        services.AddSingleton<McpServerManager>();

        // Native MCP tools — one registration per logical tool
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.SchedulerMcpTools(
            ServiceLayer.Services.Mcp.SchedulerMcpTools.ToolCreate,
            sp.GetRequiredService<IRepository<DataBaseLayer.Models.ScheduledNewsletter>>(),
            sp.GetRequiredService<ServiceLayer.Services.Telegram.NewsletterSchedulerService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.SchedulerMcpTools>>()));
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.SchedulerMcpTools(
            ServiceLayer.Services.Mcp.SchedulerMcpTools.ToolList,
            sp.GetRequiredService<IRepository<DataBaseLayer.Models.ScheduledNewsletter>>(),
            sp.GetRequiredService<ServiceLayer.Services.Telegram.NewsletterSchedulerService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.SchedulerMcpTools>>()));
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.SchedulerMcpTools(
            ServiceLayer.Services.Mcp.SchedulerMcpTools.ToolDelete,
            sp.GetRequiredService<IRepository<DataBaseLayer.Models.ScheduledNewsletter>>(),
            sp.GetRequiredService<ServiceLayer.Services.Telegram.NewsletterSchedulerService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.SchedulerMcpTools>>()));

        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.DatabaseQueryMcpTools(
            ServiceLayer.Services.Mcp.DatabaseQueryMcpTools.ToolGetSchema,
            sp.GetRequiredService<StoreContext>(),
            sp.GetRequiredService<ITelegramBotClient>(),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.DatabaseQueryMcpTools>>()));
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.DatabaseQueryMcpTools(
            ServiceLayer.Services.Mcp.DatabaseQueryMcpTools.ToolExecuteQuery,
            sp.GetRequiredService<StoreContext>(),
            sp.GetRequiredService<ITelegramBotClient>(),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.DatabaseQueryMcpTools>>()));

        // ── Bot Self-Awareness & Metadata MCP Tool ───────────────────────────
        services.AddScoped<IBotSelfAwarenessService, BotSelfAwarenessService>();
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.BotMetadataMcpTool(
            sp.GetRequiredService<IBotSelfAwarenessService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.BotMetadataMcpTool>>()));

        // ── Telegram Context MCP Tools ───────────────────────────────────────
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.TelegramContextMcpTools(
            ServiceLayer.Services.Mcp.TelegramContextMcpTools.ToolGetCurrentUserInfo,
            sp.GetRequiredService<ITelegramBotClient>(),
            sp.GetRequiredService<TgUserRepo>(),
            sp.GetRequiredService<TgChatRepo>(),
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.TelegramContextMcpTools>>()));

        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.TelegramContextMcpTools(
            ServiceLayer.Services.Mcp.TelegramContextMcpTools.ToolGetCurrentGroupInfo,
            sp.GetRequiredService<ITelegramBotClient>(),
            sp.GetRequiredService<TgUserRepo>(),
            sp.GetRequiredService<TgChatRepo>(),
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.TelegramContextMcpTools>>()));

        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.TelegramContextMcpTools(
            ServiceLayer.Services.Mcp.TelegramContextMcpTools.ToolGetUserContact,
            sp.GetRequiredService<ITelegramBotClient>(),
            sp.GetRequiredService<TgUserRepo>(),
            sp.GetRequiredService<TgChatRepo>(),
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.TelegramContextMcpTools>>()));

        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.TelegramContextMcpTools(
            ServiceLayer.Services.Mcp.TelegramContextMcpTools.ToolGetUserLocation,
            sp.GetRequiredService<ITelegramBotClient>(),
            sp.GetRequiredService<TgUserRepo>(),
            sp.GetRequiredService<TgChatRepo>(),
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.TelegramContextMcpTools>>()));

        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.TelegramContextMcpTools(
            ServiceLayer.Services.Mcp.TelegramContextMcpTools.ToolGetAvailableInformationDirectory,
            sp.GetRequiredService<ITelegramBotClient>(),
            sp.GetRequiredService<TgUserRepo>(),
            sp.GetRequiredService<TgChatRepo>(),
            sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.TelegramContextMcpTools>>()));

        // ── Google Drive MCP Service & Tools ─────────────────────────────────
        services.AddSingleton<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveAuthService, ServiceLayer.Services.Mcp.GoogleDrive.GoogleDriveAuthService>();
        services.AddScoped<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveService, ServiceLayer.Services.Mcp.GoogleDrive.GoogleDriveService>();
        
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.GoogleDriveMcpTools(
            ServiceLayer.Services.Mcp.GoogleDriveMcpTools.ToolList,
            sp.GetRequiredService<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.GoogleDriveMcpTools>>()));
            
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.GoogleDriveMcpTools(
            ServiceLayer.Services.Mcp.GoogleDriveMcpTools.ToolRead,
            sp.GetRequiredService<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.GoogleDriveMcpTools>>()));
            
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.GoogleDriveMcpTools(
            ServiceLayer.Services.Mcp.GoogleDriveMcpTools.ToolWrite,
            sp.GetRequiredService<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.GoogleDriveMcpTools>>()));
            
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.GoogleDriveMcpTools(
            ServiceLayer.Services.Mcp.GoogleDriveMcpTools.ToolCreate,
            sp.GetRequiredService<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.GoogleDriveMcpTools>>()));
            
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.GoogleDriveMcpTools(
            ServiceLayer.Services.Mcp.GoogleDriveMcpTools.ToolUpdateDescription,
            sp.GetRequiredService<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.GoogleDriveMcpTools>>()));
            
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.GoogleDriveMcpTools(
            ServiceLayer.Services.Mcp.GoogleDriveMcpTools.ToolDelete,
            sp.GetRequiredService<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.GoogleDriveMcpTools>>()));
            
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.GoogleDriveMcpTools(
            ServiceLayer.Services.Mcp.GoogleDriveMcpTools.ToolRestore,
            sp.GetRequiredService<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.GoogleDriveMcpTools>>()));

        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.GoogleDriveMcpTools(
            ServiceLayer.Services.Mcp.GoogleDriveMcpTools.ToolListVersions,
            sp.GetRequiredService<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.GoogleDriveMcpTools>>()));

        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.GoogleDriveMcpTools(
            ServiceLayer.Services.Mcp.GoogleDriveMcpTools.ToolReadVersion,
            sp.GetRequiredService<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.GoogleDriveMcpTools>>()));

        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.GoogleDriveMcpTools(
            ServiceLayer.Services.Mcp.GoogleDriveMcpTools.ToolRestoreVersion,
            sp.GetRequiredService<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveService>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.GoogleDriveMcpTools>>()));

        // ── Native SSH MCP Tools ─────────────────────────────────────────────
        services.AddScoped<INativeMcpTool>(sp => new ServiceLayer.Services.Mcp.SshMcpTools(
            ServiceLayer.Services.Mcp.SshMcpTools.ToolExecute,
            sp.GetRequiredService<ServiceLayer.Services.Mcp.GoogleDrive.IGoogleDriveService>(),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ServiceLayer.Services.Mcp.SshMcpTools>>()));

        // ── Localization ─────────────────────────────────────────────────────
        services.AddLocalization();
        services.AddScoped<IUserContext, UserContext>();
        services.AddScoped<IDynamicLocalizer, DynamicLocalizer>();

        // ── Database ─────────────────────────────────────────────────────────
        services.AddDbContext<StoreContext>((sp, options) =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            DataBaseLayer.MigrationConfigurator.Configure(options, settings.Database.Provider, settings.Database.ConnectionString);
        });

        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        // ── Newsletter Scheduler ─────────────────────────────────────────────
        services.AddSingleton<ServiceLayer.Services.Telegram.NewsletterSchedulerService>();

        return services;
    }
}

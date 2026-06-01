using DataBaseLayer;
using DataBaseLayer.Contexts;
using DataBaseLayer.Repositories;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ServiceLayer.Services;
using ServiceLayer.Services.AudioTranscriptor;
using ServiceLayer.Services.Localization;
using ServiceLayer.Services.MessageProcessor;
using ServiceLayer.Services.OpenAI;
using ServiceLayer.Services.Telegram;
using ServiceLayer.Services.Mcp;
using ServiceLayer.Extensions;
using ServiceLayer.Utils;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.MemoryStorage;

namespace TelegramBotWebApp.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all application services: configuration, Telegram bot client,
    /// database, domain services, and localization.
    /// </summary>
    public static WebApplicationBuilder AddBotServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config  = builder.Configuration;

        // ── Shared Services ──────────────────────────────────────────────────
        services.AddCommonBotServices(config);

        // ── Bot handler services (app-specific) ─────────────────────────────
        // IUpdateHandler: used by the webhook endpoint (easily mockable in tests)
        // UpdateHandler:  used by ReceiverService for Polling mode
        services.AddScoped<UpdateHandler>();
        services.AddScoped<IUpdateHandler>(sp => sp.GetRequiredService<UpdateHandler>());
        services.AddScoped<ReceiverService>();

        // ── OpenTelemetry → Aspire Dashboard ───────────────────────────────────
        // Reads OTEL_EXPORTER_OTLP_ENDPOINT directly from the process environment
        // (env vars are available from startup, before the full config pipeline runs).
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");

        var otelBuilder = services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(
                serviceName:        "GptChatTelegramBot",
                serviceVersion:     typeof(ServiceCollectionExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                        o.Protocol = OtlpExportProtocol.Grpc;
                    });
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    metrics.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(otlpEndpoint);
                        o.Protocol = OtlpExportProtocol.Grpc;
                    });
            });

        // Forward structured logs to Aspire Dashboard
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
                logging.AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(otlpEndpoint);
                    o.Protocol = OtlpExportProtocol.Grpc;
                });
            });
        }

        // ── Hangfire (Background Job Scheduler) ──────────────────────────────
        var settings = builder.Configuration.GetSection(AppSettings.Configuration).Get<AppSettings>();
        if (settings?.Scheduler?.Enabled == true)
        {
            builder.Services.AddHangfire(config =>
            {
                config.SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                      .UseSimpleAssemblyNameTypeSerializer()
                      .UseRecommendedSerializerSettings();

                // Dynamic/Adaptive Hybrid storage configuration
                try
                {
                    switch (settings.Database.Provider)
                    {
                        case DataBaseLayer.Enums.DatabaseProvider.PostgreSql:
                            config.UsePostgreSqlStorage(c => c.UseNpgsqlConnection(settings.Database.ConnectionString));
                            break;
                        default:
                            config.UseMemoryStorage();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // Safe fallback to MemoryStorage if actual database storage initialization fails
                    Console.WriteLine($"[Hangfire] Failed to configure database storage for provider {settings.Database.Provider}. Falling back to MemoryStorage. Error: {ex.Message}");
                    config.UseMemoryStorage();
                }
            });

            // Register Hangfire server
            builder.Services.AddHangfireServer();
        }

        return builder;
    }
}

using DataBaseLayer;
using Prometheus;
using Scalar.AspNetCore;
using ServiceLayer.Services.Telegram;
using ServiceLayer.Services.Mcp;
using TelegramBotWebApp.Endpoints;
using TelegramBotWebApp.Extensions;
using TelegramBotWebApp.Services;
using Hangfire;

// ── Builder ──────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// Load configuration (layered, each layer overrides the previous):
//   1. Configs/appsettings.json      — shared base: AI keys, BotToken, SQLite default
//   2. Configs/appsettings.web.json  — web overrides: AllowedHosts, log levels
//   3. Environment variables         — Docker overrides: DB provider, connection string
//
// In development: Configs/ is four levels up from bin/Debug/net*/
// In Docker:      Configs/ is copied next to the published binary
string? configsBase = null;
var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
while (currentDir != null)
{
    var candidate = Path.Combine(currentDir.FullName, "Configs");
    if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "appsettings.json")))
    {
        configsBase = candidate;
        break;
    }
    currentDir = currentDir.Parent;
}
configsBase ??= Path.Combine(AppContext.BaseDirectory, "Configs");

builder.Configuration
    .AddJsonFile(Path.Combine(configsBase, "appsettings.json"),     optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(configsBase, "appsettings.web.json"), optional: true, reloadOnChange: true)
    .AddEnvironmentVariables(); // env vars always win (used in Docker)

// ── Bot Services ─────────────────────────────────────────────────────────────
// Resolve TelegramBotConfiguration early so we can decide which mode to register
builder.AddBotServices();

// Read the config to decide the bot mode before Build()
// In testing environment the factory replaces config, so section may be absent — default to Polling
var tempConfig = builder.Configuration
    .GetSection(ServiceLayer.Services.AppSettings.Configuration)
    .Get<ServiceLayer.Services.AppSettings>();

var isWebhookMode = !string.IsNullOrWhiteSpace(
    tempConfig?.TelegramBotConfiguration?.BaseApiUrl);

if (isWebhookMode)
{
    // Webhook: register the setup service that calls SetWebhook on startup
    builder.Services.AddHostedService<WebhookSetupService>();
}
else
{
    // Polling: register PollingService as a background IHostedService
    builder.Services.AddHostedService<PollingService>();
}

// ── ASP.NET Core services ────────────────────────────────────────────────────

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()));

// ── App ──────────────────────────────────────────────────────────────────────

var app = builder.Build();

// Apply DB migrations on startup
using (var scope = app.Services.CreateScope())
{
    MigrationConfigurator.ApplyMigrations(scope.ServiceProvider);
}

// Initialize MCP Servers
using (var scope = app.Services.CreateScope())
{
    var mcpManager = scope.ServiceProvider.GetRequiredService<McpServerManager>();
    _ = mcpManager.ReloadServersAsync(); // Background initialization
}

// Initialize Hangfire Scheduled Newsletters
var startupSettings = app.Services.GetRequiredService<ServiceLayer.Services.AppSettings>();
if (startupSettings.Scheduler.Enabled)
{
    using (var scope = app.Services.CreateScope())
    {
        var newsletterScheduler = scope.ServiceProvider.GetRequiredService<ServiceLayer.Services.Telegram.NewsletterSchedulerService>();
        _ = newsletterScheduler.InitializeJobsAsync(); // Background initialization
    }
}

var telegramConfig = app.Services
    .GetRequiredService<ServiceLayer.Services.Telegram.Configuretions.TelegramBotConfiguration>();

app.Logger.LogInformation(
    "Bot mode: {Mode}",
    telegramConfig.IsWebhookMode()
        ? $"WEBHOOK → {telegramConfig.GetWebhookUrl()}"
        : "POLLING");

// ── Middleware ────────────────────────────────────────────────────────────────

// Serve wwwroot/ static assets (logo.png, favicon.ico)
app.UseStaticFiles();

app.UseCors();

var settingsForScheduler = app.Services.GetRequiredService<ServiceLayer.Services.AppSettings>();
if (settingsForScheduler.Scheduler.Enabled)
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new AllowAllDashboardAuthorizationFilter() }
    });
}

// Prometheus: track HTTP request metrics automatically
app.UseHttpMetrics();

// ── Endpoints ────────────────────────────────────────────────────────────────

app.MapDashboardEndpoints(); // GET /
app.MapHealthEndpoints();   // GET /health
app.MapInfoEndpoints();     // GET /api/info
app.MapMcpEndpoints();      // GET/POST /admin/mcp

if (telegramConfig.IsWebhookMode())
{
    app.MapWebhookEndpoints(); // POST /aibot
}

// ── Swagger / OpenAPI (controlled by SWAGGER_ENABLED in .env) ────────────────
// Default: enabled in Development, disabled in Production.
// Override in .env: SWAGGER_ENABLED=true | false
var swaggerEnabledRaw = app.Configuration["SWAGGER_ENABLED"];
var swaggerEnabled = string.IsNullOrWhiteSpace(swaggerEnabledRaw)
    ? app.Environment.IsDevelopment()
    : swaggerEnabledRaw.Equals("true", StringComparison.OrdinalIgnoreCase);

if (swaggerEnabled)
{
    app.MapOpenApi(); // GET /openapi/v1.json

    app.MapScalarApiReference(options =>
    {
        options.Title = "GPTChatBot API";
        options.Theme = ScalarTheme.DeepSpace;
    }); // GET /scalar/v1

    app.Logger.LogInformation("Swagger UI enabled at /scalar/v1");
}
else
{
    app.Logger.LogInformation("Swagger UI disabled (SWAGGER_ENABLED=false)");
}

// Prometheus metrics: GET /metrics
app.MapMetrics();

// ── Service URL banner ────────────────────────────────────────────────────────
// Printed once on startup so operators can see exactly where each service lives.
app.Lifetime.ApplicationStarted.Register(() =>
{
    var botPort         = app.Configuration["BOT_PORT"] ?? "8080";
    var composeProfiles = app.Configuration["COMPOSE_PROFILES"] ?? "";
    var webhookUrl      = telegramConfig.IsWebhookMode() ? telegramConfig.GetWebhookUrl() : null;

    app.Logger.LogInformation("──────────────────────────────────────────────────────");
    app.Logger.LogInformation("🤖  Bot WebApp  →  http://localhost:{Port}", botPort);
    app.Logger.LogInformation("    /health     →  http://localhost:{Port}/health",    botPort);
    app.Logger.LogInformation("    /api/info   →  http://localhost:{Port}/api/info",  botPort);
    app.Logger.LogInformation("    /metrics    →  http://localhost:{Port}/metrics",   botPort);

    if (webhookUrl is not null)
        app.Logger.LogInformation("    /aibot      →  {WebhookUrl} (Webhook)", webhookUrl);

    if (swaggerEnabled)
        app.Logger.LogInformation("    /scalar     →  http://localhost:{Port}/scalar/v1 (Swagger)", botPort);

    if (composeProfiles.Contains("cloudbeaver", StringComparison.OrdinalIgnoreCase))
    {
        var cbPort = app.Configuration["CLOUDBEAVER_PORT"] ?? "8978";
        app.Logger.LogInformation("🗄️   CloudBeaver →  http://localhost:{Port}", cbPort);
    }

    if (composeProfiles.Contains("aspire", StringComparison.OrdinalIgnoreCase))
    {
        var aspirePort = app.Configuration["ASPIRE_PORT"] ?? "18888";
        app.Logger.LogInformation("🔭  Aspire Dashboard →  http://localhost:{Port}", aspirePort);
    }

    app.Logger.LogInformation("──────────────────────────────────────────────────────");
});

// ── Run ───────────────────────────────────────────────────────────────────────

await app.RunAsync();

// ── Hangfire Authorization Filter ─────────────────────────────────────────────
public class AllowAllDashboardAuthorizationFilter : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        // Allow all connections (external IPs) to view the Hangfire Dashboard.
        // For public-facing environments, secure this with appropriate credentials/IP restrictions.
        return true;
    }
}

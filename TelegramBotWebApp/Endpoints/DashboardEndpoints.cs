using Microsoft.AspNetCore.Http.Extensions;
using ServiceLayer.Services.Telegram.Configuretions;
using TelegramBotWebApp.Extensions;
using Microsoft.Extensions.Caching.Memory;

namespace TelegramBotWebApp.Endpoints;

public static class DashboardEndpoints
{
    /// <summary>Maps GET / — returns an HTML dashboard with links to all active services.</summary>
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        app.MapGet("/", (HttpContext ctx, IConfiguration cfg, TelegramBotConfiguration telegramCfg, ServiceLayer.Services.AppSettings appSettings) =>
        {
            var request        = ctx.Request;
            var scheme         = request.Scheme;
            var host           = request.Host.Host;        // just hostname, no port
            var fullHost       = request.Host.ToUriComponent(); // host:port or just host

            var baseUrl        = $"{scheme}://{fullHost}"; // same origin — always auto

            // Profiles & feature flags
            var profiles       = cfg["COMPOSE_PROFILES"] ?? "";
            var swaggerRaw     = cfg["SWAGGER_ENABLED"];
            var swaggerEnabled = string.IsNullOrWhiteSpace(swaggerRaw)
                ? app.Environment.IsDevelopment()
                : swaggerRaw.Equals("true", StringComparison.OrdinalIgnoreCase);

            var hasCloudBeaver = profiles.Contains("cloudbeaver", StringComparison.OrdinalIgnoreCase);
            var hasAspire      = profiles.Contains("aspire",       StringComparison.OrdinalIgnoreCase);
            var hasNginx       = profiles.Contains("nginx",        StringComparison.OrdinalIgnoreCase);
            var isWebhook      = telegramCfg.IsWebhookMode();

            // External service URLs
            // CloudBeaver:
            //   1. CLOUDBEAVER_PUBLIC_URL set → custom subdomain (Cloudflare etc.)
            //   2. nginx profile active       → same origin /cloudbeaver/ path
            //   3. otherwise                  → direct host:CLOUDBEAVER_PORT (LAN)
            string? cloudBeaverUrl = null;
            if (hasCloudBeaver)
            {
                var cbPublic = cfg["CLOUDBEAVER_PUBLIC_URL"];
                if (!string.IsNullOrWhiteSpace(cbPublic))
                {
                    cloudBeaverUrl = cbPublic.TrimEnd('/');
                }
                else if (hasNginx)
                {
                    cloudBeaverUrl = $"{baseUrl}/cloudbeaver/";
                }
                else
                {
                    var cbPort = cfg["CLOUDBEAVER_PORT"] ?? "8978";
                    cloudBeaverUrl = $"{scheme}://{host}:{cbPort}/";
                }
            }

            // Aspire: use explicit public URL if set, otherwise host:port (LAN only)
            string? aspireUrl = null;
            if (hasAspire)
            {
                var aspirePublic = cfg["ASPIRE_PUBLIC_URL"];
                if (!string.IsNullOrWhiteSpace(aspirePublic))
                {
                    aspireUrl = aspirePublic.TrimEnd('/');
                }
                else
                {
                    var aspirePort = cfg["ASPIRE_PORT"] ?? "18888";
                    aspireUrl = $"{scheme}://{host}:{aspirePort}";
                }
            }

            // Build service cards
            var cards = new List<ServiceCard>
            {
                new("❤️", "Health Check",     $"{baseUrl}/health",   "System",         "Always available — returns 200 OK when the bot is running.",            "#22c55e"),
                new("ℹ️", "API Info",         $"{baseUrl}/api/info", "System",         "Bot mode, version, masked token, and current timestamp.",               "#3b82f6"),
                new("📊", "Metrics",          $"{baseUrl}/metrics",  "Observability",  "Prometheus-compatible metrics endpoint for scraping.",                   "#a855f7"),
                new("🛠️", "MCP Tools",       $"{baseUrl}/admin/mcp", "AI Admin",       "Manage local MCP servers (npx tools).",                                 "#10b981"),
            };

            if (appSettings.Scheduler.Enabled)
                cards.Add(new("⏰", "Hangfire Dashboard", $"{baseUrl}/hangfire", "Jobs", "Background scheduled tasks, newsletters, and active cron jobs.", "#f43f5e"));

            if (swaggerEnabled)
                cards.Add(new("📖", "Swagger UI",   $"{baseUrl}/scalar/v1", "Developer", "Interactive OpenAPI documentation (Scalar theme).",                  "#f59e0b"));

            if (isWebhook)
                cards.Add(new("🔗", "Webhook",      telegramCfg.GetWebhookUrl()!, "Telegram", "Active Telegram webhook endpoint (POST).",                      "#06b6d4"));

            if (cloudBeaverUrl is not null)
                cards.Add(new("🗄️", "CloudBeaver",  cloudBeaverUrl, "Database",       "Web-based database manager — browse tables, run queries.",              "#f97316"));

            if (aspireUrl is not null)
                cards.Add(new("🔭", "Aspire Dashboard", aspireUrl,  "Observability",  "Metrics, distributed traces and structured logs from all services.",     "#8b5cf6"));

            var dbProvider = cfg["AppSettings:Database:Provider"] ?? cfg["DB_PROVIDER"] ?? "SQLite";
            var botMode    = isWebhook ? "Webhook" : "Polling";

            var html = BuildHtml(cards, dbProvider, botMode, profiles);

            return Results.Content(html, "text/html; charset=utf-8");
        })
        .WithName("Dashboard")
        .WithSummary("Service dashboard")
        .WithDescription("HTML page listing all active service endpoints.")
        .WithTags("System")
        .ExcludeFromDescription() // exclude from OpenAPI so it doesn't clutter the schema
        .AllowAnonymous();

        app.MapGet("/admin/thoughts/{messageId:long}", (
            long messageId,
            string? token,
            Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
            ServiceLayer.Services.Localization.IDynamicLocalizer localizer,
            ILoggerFactory loggerFactory) =>
        {
            if (string.IsNullOrEmpty(token))
            {
                return Results.Problem("Missing secure token.", statusCode: StatusCodes.Status403Forbidden);
            }

            var tokenKey = $"AiThoughtsToken:{messageId}";
            if (!cache.TryGetValue(tokenKey, out string? cachedToken) || cachedToken != token)
            {
                return Results.Problem("Invalid or expired secure token.", statusCode: StatusCodes.Status403Forbidden);
            }

            var cacheKey = $"AiThoughts:{messageId}";
            if (!cache.TryGetValue(cacheKey, out ServiceLayer.Models.ThoughtsAndStats? thoughtsAndStats) || thoughtsAndStats == null)
            {
                return Results.Problem("Reasoning data not found or expired from cache.", statusCode: StatusCodes.Status404NotFound);
            }

            var html = BuildThoughtsHtml(thoughtsAndStats, localizer);
            return Results.Content(html, "text/html; charset=utf-8");
        })
        .WithName("AiThoughtsAndStats")
        .WithSummary("View detailed AI reasoning and query statistics")
        .ExcludeFromDescription()
        .AllowAnonymous();
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private static string BuildHtml(List<ServiceCard> cards, string dbProvider, string botMode, string profiles)
    {
        var cardHtml = string.Join("\n", cards.Select(c => $"""
            <a href="{c.Url}" target="_blank" class="card" style="--accent:{c.Color}">
              <div class="card-icon">{c.Icon}</div>
              <div class="card-body">
                <div class="card-tag">{c.Tag}</div>
                <div class="card-title">{c.Title}</div>
                <div class="card-desc">{c.Description}</div>
                <div class="card-url">{c.Url}</div>
              </div>
              <div class="card-arrow">→</div>
            </a>
        """));

        var profileBadges = string.IsNullOrWhiteSpace(profiles)
            ? "<span class='badge'>SQLite</span>"
            : string.Join("", profiles.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => $"<span class='badge'>{p.Trim()}</span>"));

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
  <title>GptChatBot — Dashboard</title>
  <meta name="description" content="Service dashboard for GptChatTelegramBot — links to all active services."/>
  <!-- Favicon -->
  <link rel="icon" type="image/x-icon" href="/favicon.ico"/>
  <link rel="apple-touch-icon" href="/logo.png"/>
  <!-- Open Graph / social preview -->
  <meta property="og:type"        content="website"/>
  <meta property="og:title"       content="GptChatBot — Dashboard"/>
  <meta property="og:description" content="All active services at a glance."/>
  <meta property="og:image"       content="/logo.png"/>
  <meta name="twitter:card"       content="summary_large_image"/>
  <meta name="twitter:image"      content="/logo.png"/>
  <link rel="preconnect" href="https://fonts.googleapis.com"/>
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin/>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet"/>
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

    :root {
      --bg:        #0a0a0f;
      --surface:   #13131a;
      --border:    rgba(255,255,255,.07);
      --text:      #e2e2f0;
      --muted:     #6b6b85;
      --radius:    16px;
    }

    body {
      font-family: 'Inter', system-ui, sans-serif;
      background: var(--bg);
      color: var(--text);
      min-height: 100vh;
      display: flex;
      flex-direction: column;
      align-items: center;
      padding: 2.5rem 1.25rem 4rem;
    }

    /* ── Ambient glow ── */
    body::before {
      content: '';
      position: fixed;
      top: -20%;
      left: 50%;
      transform: translateX(-50%);
      width: 700px;
      height: 400px;
      background: radial-gradient(ellipse, rgba(99,102,241,.18) 0%, transparent 70%);
      pointer-events: none;
      z-index: 0;
    }

    .page { position: relative; z-index: 1; width: 100%; max-width: 860px; }

    /* ── Header ── */
    header {
      text-align: center;
      margin-bottom: 2.5rem;
    }

    .logo {
      display: block;
      width: 50%;
      height: auto;
      border-radius: 18px;
      margin: 0 auto 1.75rem;
      object-fit: cover;
      box-shadow: 0 12px 48px rgba(99,102,241,.35);
      border: 1px solid rgba(255,255,255,.08);
    }

    @keyframes float {
      0%, 100% { transform: translateY(0); }
      50%       { transform: translateY(-6px); }
    }

    h1 {
      font-size: 1.75rem;
      font-weight: 700;
      letter-spacing: -.02em;
      background: linear-gradient(135deg, #fff 40%, #a5b4fc);
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
      background-clip: text;
    }

    .subtitle {
      color: var(--muted);
      font-size: .9rem;
      margin-top: .4rem;
    }

    /* ── Meta strip ── */
    .meta {
      display: flex;
      flex-wrap: wrap;
      gap: .5rem;
      justify-content: center;
      margin-bottom: 2rem;
    }

    .meta-item {
      display: flex;
      align-items: center;
      gap: .4rem;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 999px;
      padding: .3rem .85rem;
      font-size: .8rem;
      color: var(--muted);
    }

    .meta-item strong { color: var(--text); }

    .badge {
      display: inline-block;
      background: rgba(99,102,241,.15);
      border: 1px solid rgba(99,102,241,.3);
      color: #a5b4fc;
      border-radius: 999px;
      padding: .15rem .65rem;
      font-size: .75rem;
      font-weight: 500;
      text-transform: capitalize;
    }

    /* ── Grid ── */
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
      gap: 1rem;
    }

    /* ── Card ── */
    .card {
      display: flex;
      align-items: flex-start;
      gap: 1rem;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 1.25rem 1.4rem;
      text-decoration: none;
      color: var(--text);
      position: relative;
      overflow: hidden;
      transition: transform .2s ease, border-color .2s ease, box-shadow .2s ease;
    }

    .card::before {
      content: '';
      position: absolute;
      inset: 0;
      background: linear-gradient(135deg, var(--accent, #6366f1) 0%, transparent 60%);
      opacity: 0;
      transition: opacity .25s ease;
    }

    .card:hover {
      transform: translateY(-3px);
      border-color: color-mix(in srgb, var(--accent, #6366f1) 60%, transparent);
      box-shadow: 0 8px 32px color-mix(in srgb, var(--accent, #6366f1) 20%, transparent);
    }

    .card:hover::before { opacity: .06; }

    .card-icon {
      font-size: 1.6rem;
      flex-shrink: 0;
      margin-top: .1rem;
      position: relative;
      z-index: 1;
    }

    .card-body {
      flex: 1;
      min-width: 0;
      position: relative;
      z-index: 1;
    }

    .card-tag {
      font-size: .7rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: .08em;
      color: var(--accent, #6366f1);
      margin-bottom: .25rem;
    }

    .card-title {
      font-size: 1rem;
      font-weight: 600;
      margin-bottom: .25rem;
    }

    .card-desc {
      font-size: .8rem;
      color: var(--muted);
      line-height: 1.5;
      margin-bottom: .5rem;
    }

    .card-url {
      font-family: 'SF Mono', 'Fira Code', ui-monospace, monospace;
      font-size: .72rem;
      color: color-mix(in srgb, var(--accent, #6366f1) 80%, #fff);
      word-break: break-all;
      opacity: .8;
    }

    .card-arrow {
      position: relative;
      z-index: 1;
      font-size: 1.1rem;
      color: var(--muted);
      flex-shrink: 0;
      margin-top: .15rem;
      transition: transform .2s ease, color .2s ease;
    }

    .card:hover .card-arrow {
      transform: translateX(4px);
      color: var(--accent, #6366f1);
    }

    /* ── Footer ── */
    footer {
      margin-top: 3rem;
      text-align: center;
      font-size: .78rem;
      color: var(--muted);
    }

    footer a { color: inherit; text-decoration: underline; opacity: .7; }

    @media (max-width: 480px) {
      .grid { grid-template-columns: 1fr; }
      h1 { font-size: 1.4rem; }
    }
  </style>
</head>
<body>
  <div class="page">
    <header>
      <img src="/logo.png" alt="GptChatBot logo" class="logo"/>
      <h1>GptChatBot — Dashboard</h1>
      <p class="subtitle">All active services at a glance</p>
    </header>

    <div class="meta">
      <div class="meta-item">🗄️ DB &nbsp;<strong>{{dbProvider}}</strong></div>
      <div class="meta-item">⚙️ Mode &nbsp;<strong>{{botMode}}</strong></div>
      <div class="meta-item">🧩 Profiles &nbsp;{{profileBadges}}</div>
    </div>

    <div class="grid">
      {{cardHtml}}
    </div>

    <footer>
      <p>Generated at {{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}} UTC &nbsp;·&nbsp;
         <a href="https://github.com/nudykw/GptChatTelegramBot" target="_blank">GitHub</a></p>
      {{TelegramBotWebApp.Services.GitInfoProvider.RenderFooterLine()}}
    </footer>
  </div>
</body>
</html>
""";
    }

    // ── Model ─────────────────────────────────────────────────────────────────

    private sealed record ServiceCard(
        string Icon,
        string Title,
        string Url,
        string Tag,
        string Description,
        string Color);

    private static double PromptPercentage(ServiceLayer.Models.ThoughtsAndStats stats)
    {
        if (stats.TotalTokens == null || stats.TotalTokens == 0) return 0;
        return (double)(stats.PromptTokens ?? 0) / stats.TotalTokens.Value * 100;
    }
    
    private static double CompletionPercentage(ServiceLayer.Models.ThoughtsAndStats stats)
    {
        if (stats.TotalTokens == null || stats.TotalTokens == 0) return 0;
        var comp = (stats.CompletionTokens ?? 0) - (stats.ReasoningTokens ?? 0);
        return (double)Math.Max(0, comp) / stats.TotalTokens.Value * 100;
    }
    
    private static double ReasoningPercentage(ServiceLayer.Models.ThoughtsAndStats stats)
    {
        if (stats.TotalTokens == null || stats.TotalTokens == 0) return 0;
        return (double)(stats.ReasoningTokens ?? 0) / stats.TotalTokens.Value * 100;
    }

    private static string BuildThoughtsHtml(ServiceLayer.Models.ThoughtsAndStats stats, ServiceLayer.Services.Localization.IDynamicLocalizer localizer)
    {
        var copyForAiButtonText = localizer["CopyForAiButtonText"];
        var copySuccessToastText = localizer["CopySuccessToastText"];
        var promptTemplate = localizer["CopyDiagnosticPrompt"]
            .Replace("`", "\\`")
            .Replace("$", "\\$");
        
        var provider = stats.ProviderName.ToLowerInvariant();
        string themeColor = "#6366f1"; // default indigo
        string themeGlow = "rgba(99,102,241,0.15)";
        
        if (provider.Contains("openai"))
        {
            themeColor = "#10b981"; // emerald
            themeGlow = "rgba(16,185,129,0.15)";
        }
        else if (provider.Contains("gemini") || provider.Contains("google"))
        {
            themeColor = "#a855f7"; // amethyst/purple
            themeGlow = "rgba(168,85,247,0.15)";
        }
        else if (provider.Contains("deepseek"))
        {
            themeColor = "#06b6d4"; // cyan
            themeGlow = "rgba(6,182,212,0.15)";
        }
        else if (provider.Contains("grok") || provider.Contains("xai"))
        {
            themeColor = "#f59e0b"; // amber/gold
            themeGlow = "rgba(245,158,11,0.15)";
        }

        var thoughtsEncoded = !string.IsNullOrWhiteSpace(stats.Thoughts)
            ? System.Net.WebUtility.HtmlEncode(stats.Thoughts)
            : "No reasoning thoughts were generated for this response (model responded directly).";
        var systemPromptEncoded = System.Net.WebUtility.HtmlEncode(stats.SystemPrompt ?? "No system prompt was provided.");
        
        var mcpToolsHtml = "";
        if (stats.ToolCalls != null && stats.ToolCalls.Any())
        {
            var rows = stats.ToolCalls.Select(t => $@"
                <details class='tool-call-card'>
                    <summary class='tool-call-summary'>
                        <span class='tool-name-container'>
                            <span class='tool-name'>🔧 {System.Net.WebUtility.HtmlEncode(t.ToolName)}</span>
                        </span>
                        <span class='tool-time'>{t.Timestamp:HH:mm:ss}</span>
                    </summary>
                    <div class='tool-call-details'>
                        <details class='inner-details' open>
                            <summary class='inner-summary'>📥 Request Arguments</summary>
                            <pre class='code-block'><code>{System.Net.WebUtility.HtmlEncode(DecodeEncodedUnicodeCharacters(t.Arguments))}</code></pre>
                        </details>
                        <details class='inner-details' style='margin-top: 0.75rem;'>
                            <summary class='inner-summary'>📤 Response Output</summary>
                            <pre class='code-block response'><code>{System.Net.WebUtility.HtmlEncode(DecodeEncodedUnicodeCharacters(t.Response))}</code></pre>
                        </details>
                    </div>
                </details>
            ");
            mcpToolsHtml = string.Join("", rows);
        }
        else
        {
            mcpToolsHtml = "<div class='no-tools'>No detailed MCP tool executions recorded for this response.</div>";
        }

        var formattedCost = stats.Cost.HasValue ? stats.Cost.Value.ToString("F6") : "0.000000";
        var formattedBalanceBefore = stats.UserBalanceBefore.HasValue ? stats.UserBalanceBefore.Value.ToString("F4") : "N/A";
        var formattedBalanceAfter = stats.UserBalanceAfter.HasValue ? stats.UserBalanceAfter.Value.ToString("F4") : "N/A";
        var formattedLatency = stats.LatencySeconds.HasValue ? stats.LatencySeconds.Value.ToString("F2") : "N/A";
        var formattedSpeed = stats.TokensPerSecond.HasValue ? stats.TokensPerSecond.Value.ToString("F1") : "N/A";

        // Conditional rendering strings for reasoning tokens
        var hasReasoning = stats.ReasoningTokens.HasValue && stats.ReasoningTokens.Value > 0;
        
        var reasoningStatCard = hasReasoning
            ? $"""
               <div class="stat-card">
                 <div class="stat-label">Reasoning</div>
                 <div class="stat-value highlight" style="color: var(--theme-color)">{stats.ReasoningTokens!.Value}</div>
               </div>
               """
            : "";

        var reasoningGauge = hasReasoning
            ? $"""
               <div class="gauge-reasoning" style="width: {ReasoningPercentage(stats)}%"></div>
               """
            : "";

        var reasoningLegend = hasReasoning
            ? $"""
               <div class="legend-item">
                 <div class="legend-color" style="background: var(--theme-color)"></div>
                 <span>Reasoning</span>
               </div>
               """
            : "";

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8"/>
  <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
  <title>GptChatBot — AI Thoughts & Statistics</title>
  <link rel="icon" type="image/x-icon" href="/favicon.ico"/>
  <link rel="preconnect" href="https://fonts.googleapis.com"/>
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin/>
  <link href="https://fonts.googleapis.com/css2?family=Fira+Code:wght@400;500&family=Inter:wght@400;500;600;700&display=swap" rel="stylesheet"/>
  <style>
    *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }

    :root {
      --bg:            #0a0a0f;
      --surface:       #13131a;
      --border:        rgba(255,255,255,.07);
      --text:          #e2e2f0;
      --muted:         #6b6b85;
      --radius:        16px;
      --theme-color:   {{themeColor}};
      --theme-glow:    {{themeGlow}};
    }

    body {
      font-family: 'Inter', system-ui, sans-serif;
      background: var(--bg);
      color: var(--text);
      min-height: 100vh;
      display: flex;
      flex-direction: column;
      align-items: center;
      padding: 2.5rem 1.25rem 4rem;
    }

    /* Ambient glow matching provider */
    body::before {
      content: '';
      position: fixed;
      top: -20%;
      left: 50%;
      transform: translateX(-50%);
      width: 700px;
      height: 400px;
      background: radial-gradient(ellipse, var(--theme-glow) 0%, transparent 70%);
      pointer-events: none;
      z-index: 0;
    }

    .page { position: relative; z-index: 1; width: 100%; max-width: 800px; }

    /* Header */
    header {
      text-align: center;
      margin-bottom: 2.5rem;
    }

    .logo-container {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: 999px;
      padding: 0.5rem 1.2rem;
      gap: 0.6rem;
      margin-bottom: 1.25rem;
      box-shadow: 0 8px 32px rgba(0,0,0,0.3);
    }

    .provider-badge {
      background: var(--theme-glow);
      border: 1px solid var(--theme-color);
      color: var(--theme-color);
      border-radius: 999px;
      padding: 0.15rem 0.65rem;
      font-size: 0.75rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    h1 {
      font-size: 1.85rem;
      font-weight: 700;
      letter-spacing: -.02em;
      background: linear-gradient(135deg, #fff 40%, var(--theme-color));
      -webkit-background-clip: text;
      -webkit-text-fill-color: transparent;
      background-clip: text;
    }

    .subtitle {
      color: var(--muted);
      font-size: .9rem;
      margin-top: .4rem;
    }

    /* Structured Collapsible Details Card */
    details {
      background: var(--surface);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      margin-bottom: 1.25rem;
      padding: 1.25rem 1.5rem;
      transition: border-color 0.25s ease, box-shadow 0.25s ease;
      overflow: hidden;
    }

    details[open] {
      border-color: var(--theme-color);
      box-shadow: 0 8px 32px color-mix(in srgb, var(--theme-color) 12%, transparent);
    }

    summary {
      font-size: 1.05rem;
      font-weight: 600;
      cursor: pointer;
      list-style: none;
      display: flex;
      justify-content: space-between;
      align-items: center;
      outline: none;
      user-select: none;
    }

    summary::-webkit-details-marker { display: none; }

    .summary-title {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }

    .summary-icon {
      font-size: 1.3rem;
    }

    summary::after {
      content: '→';
      font-size: 1.1rem;
      color: var(--muted);
      transition: transform 0.25s ease, color 0.25s ease;
    }

    details[open] summary::after {
      transform: rotate(90deg);
      color: var(--theme-color);
    }

    /* Card Content Container */
    .card-content {
      margin-top: 1.25rem;
      border-top: 1px solid var(--border);
      padding-top: 1.25rem;
      animation: fadeIn 0.3s ease-out;
    }

    @keyframes fadeIn {
      from { opacity: 0; transform: translateY(4px); }
      to { opacity: 1; transform: translateY(0); }
    }

    /* Monospace thought content styling */
    .thought-box {
      font-family: 'Fira Code', 'SF Mono', monospace;
      font-size: 0.9rem;
      line-height: 1.6;
      white-space: pre-wrap;
      word-break: break-word;
      color: #cbd5e1;
      background: rgba(0,0,0,0.2);
      border-radius: 8px;
      padding: 1rem;
      border: 1px solid rgba(255,255,255,0.03);
      max-height: 500px;
      overflow-y: auto;
    }

    .thought-box::-webkit-scrollbar {
      width: 6px;
    }
    .thought-box::-webkit-scrollbar-track {
      background: transparent;
    }
    .thought-box::-webkit-scrollbar-thumb {
      background: var(--border);
      border-radius: 999px;
    }
    .thought-box::-webkit-scrollbar-thumb:hover {
      background: var(--theme-color);
    }

    /* Stats Grid & Labels */
    .stats-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 1rem;
    }

    .stat-card {
      background: rgba(255,255,255,0.02);
      border: 1px solid var(--border);
      border-radius: 10px;
      padding: 1rem;
    }

    .stat-label {
      font-size: 0.75rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      color: var(--muted);
      margin-bottom: 0.35rem;
    }

    .stat-value {
      font-size: 1.25rem;
      font-weight: 700;
    }

    .stat-value.highlight {
      color: var(--theme-color);
    }

    /* Token Visual Gauge */
    .gauge-container {
      margin-top: 1rem;
      background: rgba(255,255,255,0.02);
      border-radius: 999px;
      height: 10px;
      width: 100%;
      overflow: hidden;
      display: flex;
      border: 1px solid var(--border);
    }

    .gauge-prompt { background: #3b82f6; }
    .gauge-completion { background: #f59e0b; }
    .gauge-reasoning { background: var(--theme-color); }

    .gauge-legend {
      display: flex;
      flex-wrap: wrap;
      gap: 1rem;
      margin-top: 0.75rem;
      font-size: 0.8rem;
    }

    .legend-item {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      color: var(--muted);
    }

    .legend-color {
      width: 10px;
      height: 10px;
      border-radius: 3px;
    }

    /* System prompt formatting */
    .prompt-text {
      font-size: 0.85rem;
      line-height: 1.5;
      white-space: pre-wrap;
      color: #94a3b8;
      max-height: 300px;
      overflow-y: auto;
    }

    /* Tool tags */
    .tool-badge {
      display: inline-flex;
      align-items: center;
      background: rgba(255,255,255,0.03);
      border: 1px solid var(--border);
      border-radius: 8px;
      padding: 0.4rem 0.8rem;
      font-family: 'Fira Code', monospace;
      font-size: 0.82rem;
      margin-right: 0.5rem;
      margin-bottom: 0.5rem;
    }

    .no-tools, .no-prompt {
      font-size: 0.85rem;
      color: var(--muted);
      font-style: italic;
    }

    /* Premium Copy AI Button */
    .btn-copy-ai {
      display: block;
      width: 100%;
      background: linear-gradient(135deg, var(--theme-color) 0%, color-mix(in srgb, var(--theme-color) 70%, #000) 100%);
      color: #ffffff;
      border: none;
      border-radius: var(--radius);
      padding: 0.9rem 1.5rem;
      font-size: 0.95rem;
      font-weight: 600;
      cursor: pointer;
      transition: transform 0.2s ease, filter 0.2s ease, box-shadow 0.2s ease;
      box-shadow: 0 4px 20px rgba(0,0,0,0.4);
      margin-bottom: 1.5rem;
      text-align: center;
      letter-spacing: 0.01em;
    }

    .btn-copy-ai:hover {
      filter: brightness(1.15);
      box-shadow: 0 6px 24px var(--theme-glow);
      transform: translateY(-1px);
    }

    .btn-copy-ai:active {
      transform: translateY(1px);
      filter: brightness(0.95);
    }

    /* Collapsible Tool Call Timeline */
    .tool-call-card {
      background: rgba(255,255,255,0.01) !important;
      border: 1px solid var(--border) !important;
      border-radius: 12px !important;
      margin-bottom: 0.75rem !important;
      padding: 0.75rem 1rem !important;
      transition: all 0.2s ease !important;
    }

    .tool-call-card[open] {
      border-color: rgba(255,255,255,0.15) !important;
      background: rgba(255,255,255,0.02) !important;
    }

    .tool-call-summary {
      font-size: 0.92rem !important;
      font-weight: 500 !important;
      display: flex !important;
      justify-content: space-between !important;
      align-items: center !important;
    }

    .tool-name-container {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }

    .tool-name {
      font-family: 'Fira Code', monospace;
      color: var(--theme-color);
      font-weight: 600;
    }

    .tool-time {
      font-size: 0.8rem;
      color: var(--muted);
    }

    .tool-call-details {
      margin-top: 0.75rem;
      border-top: 1px dashed var(--border);
      padding-top: 0.75rem;
      animation: fadeIn 0.25s ease-out;
    }

    .inner-details {
      background: transparent !important;
      border: none !important;
      border-radius: 0 !important;
      margin-bottom: 0.5rem !important;
      padding: 0 !important;
    }

    .inner-summary {
      font-size: 0.82rem !important;
      font-weight: 600 !important;
      color: var(--muted) !important;
      text-transform: uppercase !important;
      letter-spacing: 0.05em !important;
      margin-bottom: 0.35rem !important;
      outline: none !important;
      user-select: none !important;
    }

    .code-block {
      font-family: 'Fira Code', 'SF Mono', monospace;
      font-size: 0.82rem;
      line-height: 1.5;
      white-space: pre-wrap;
      word-break: break-all;
      color: #94a3b8;
      background: rgba(0,0,0,0.3);
      border-radius: 6px;
      padding: 0.75rem;
      border: 1px solid rgba(255,255,255,0.02);
      max-height: 250px;
      overflow-y: auto;
    }

    .code-block.response {
      color: #cbd5e1;
      background: rgba(0,0,0,0.4);
    }

    /* Toast Notification */
    #toast-notification {
      position: fixed;
      bottom: 2rem;
      left: 50%;
      transform: translateX(-50%) translateY(100px);
      background: #1e1b4b;
      color: #e0e7ff;
      border: 1px solid #4338ca;
      padding: 0.75rem 1.5rem;
      border-radius: 999px;
      font-size: 0.88rem;
      font-weight: 500;
      z-index: 1000;
      box-shadow: 0 10px 30px rgba(0,0,0,0.5);
      transition: transform 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275);
      pointer-events: none;
    }

    #toast-notification.show {
      transform: translateX(-50%) translateY(0);
    }

    /* Footer */
    footer {
      margin-top: 3rem;
      text-align: center;
      font-size: .78rem;
      color: var(--muted);
    }

    @media (max-width: 480px) {
      h1 { font-size: 1.45rem; }
      details { padding: 1rem 1.1rem; }
    }
  </style>
</head>
<body>
  <div class="page">
    <header>
      <div class="logo-container">
        <span>🤖 GptChatBot</span>
        <span class="provider-badge">{{stats.ProviderName}}</span>
      </div>
      <h1>Thoughts & Stats Panel</h1>
      <p class="subtitle">Detailed performance metrics and unedited reasoning process</p>
    </header>

    <!-- Copy diagnostics for Antigravity -->
    <button class="btn btn-copy-ai" onclick="copyDiagnosticPrompt()">{{copyForAiButtonText}}</button>

    <!-- 1. AI Thoughts Block -->
    <details>
      <summary>
        <div class="summary-title">
          <span class="summary-icon">🧠</span>
          <span>AI Thoughts</span>
        </div>
      </summary>
      <div class="card-content">
        <pre class="thought-box">{{thoughtsEncoded}}</pre>
      </div>
    </details>

    <!-- 2. Token Breakdown Block -->
    <details>
      <summary>
        <div class="summary-title">
          <span class="summary-icon">🪙</span>
          <span>Token Metrics</span>
        </div>
      </summary>
      <div class="card-content">
        <div class="stats-grid">
          <div class="stat-card">
            <div class="stat-label">Total Tokens</div>
            <div class="stat-value highlight">{{stats.TotalTokens ?? 0}}</div>
          </div>
          <div class="stat-card">
            <div class="stat-label">Prompt (Input)</div>
            <div class="stat-value">{{stats.PromptTokens ?? 0}}</div>
          </div>
          <div class="stat-card">
            <div class="stat-label">Completion (Output)</div>
            <div class="stat-value">{{stats.CompletionTokens ?? 0}}</div>
          </div>
          {{reasoningStatCard}}
        </div>
        
        <div class="gauge-container">
          <div class="gauge-prompt" style="width: {{PromptPercentage(stats)}}%"></div>
          <div class="gauge-completion" style="width: {{CompletionPercentage(stats)}}%"></div>
          {{reasoningGauge}}
        </div>
        
        <div class="gauge-legend">
          <div class="legend-item">
            <div class="legend-color" style="background: #3b82f6"></div>
            <span>Prompt (Input)</span>
          </div>
          <div class="legend-item">
            <div class="legend-color" style="background: #f59e0b"></div>
            <span>Completion (Output)</span>
          </div>
          {{reasoningLegend}}
        </div>
      </div>
    </details>

    <!-- 3. Billing & Costs Block -->
    <details>
      <summary>
        <div class="summary-title">
          <span class="summary-icon">💰</span>
          <span>Cost & Billing</span>
        </div>
      </summary>
      <div class="card-content">
        <div class="stats-grid">
          <div class="stat-card">
            <div class="stat-label">Request Cost</div>
            <div class="stat-value highlight">${{formattedCost}}</div>
          </div>
          <div class="stat-card">
            <div class="stat-label">User</div>
            <div class="stat-value">{{stats.UserFullName ?? "Unknown"}} (ID: {{stats.UserId}})</div>
          </div>
          <div class="stat-card">
            <div class="stat-label">Balance (Before)</div>
            <div class="stat-value">${{formattedBalanceBefore}}</div>
          </div>
          <div class="stat-card">
            <div class="stat-label">Balance (After)</div>
            <div class="stat-value">${{formattedBalanceAfter}}</div>
          </div>
        </div>
      </div>
    </details>

    <!-- 4. Performance & Speeds Block -->
    <details>
      <summary>
        <div class="summary-title">
          <span class="summary-icon">⚡</span>
          <span>Speed & Performance</span>
        </div>
      </summary>
      <div class="card-content">
        <div class="stats-grid">
          <div class="stat-card">
            <div class="stat-label">Generation Time</div>
            <div class="stat-value highlight">{{formattedLatency}} sec</div>
          </div>
          <div class="stat-card">
            <div class="stat-label">Generation Speed</div>
            <div class="stat-value">{{formattedSpeed}} t/s</div>
          </div>
          <div class="stat-card">
            <div class="stat-label">Active Model</div>
            <div class="stat-value" style="font-size: 0.95rem; font-family: monospace;">{{stats.ModelName}}</div>
          </div>
        </div>
      </div>
    </details>

    <!-- 5. MCP Tools Log Block -->
    <details>
      <summary>
        <div class="summary-title">
          <span class="summary-icon">🧩</span>
          <span>MCP Tool Executions</span>
        </div>
      </summary>
      <div class="card-content">
        {{mcpToolsHtml}}
      </div>
    </details>

    <!-- 6. Request Context (System Prompt) Block -->
    <details>
      <summary>
        <div class="summary-title">
          <span class="summary-icon">⚙️</span>
          <span>Request Context (System Prompt)</span>
        </div>
      </summary>
      <div class="card-content">
        <div class="stat-label" style="margin-bottom: 0.6rem;">Active model system prompt:</div>
        <pre class="prompt-text">{{systemPromptEncoded}}</pre>
      </div>
    </details>

    <footer>
      <p>GptChatTelegramBot Owner Dashboard · All blocks collapsed by default</p>
      {{TelegramBotWebApp.Services.GitInfoProvider.RenderFooterLine()}}
    </footer>
  </div>

  <div id="toast-notification">{{copySuccessToastText}}</div>

  <script>
    function showToast(message) {
      const toast = document.getElementById("toast-notification");
      toast.textContent = message;
      toast.classList.add("show");
      setTimeout(() => {
        toast.classList.remove("show");
      }, 3000);
    }

    function copyDiagnosticPrompt() {
      const template = `{{promptTemplate}}`;
      const curlCommand = 'curl -s "' + window.location.href + '"';
      const fullPrompt = template
          .replace('{0}', curlCommand)
          .replace('{1}', window.location.href);

      const successHandler = () => {
          showToast('{{copySuccessToastText}}');
      };

      const errorHandler = (err) => {
          console.error('Copy failed: ', err);
          alert('Copy failed. Please copy the URL manually.');
      };

      if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(fullPrompt).then(successHandler).catch(errorHandler);
      } else {
          try {
              const textArea = document.createElement("textarea");
              textArea.value = fullPrompt;
              textArea.style.top = "0";
              textArea.style.left = "0";
              textArea.style.position = "fixed";
              document.body.appendChild(textArea);
              textArea.focus();
              textArea.select();
              const successful = document.execCommand('copy');
              document.body.removeChild(textArea);
              if (successful) successHandler();
              else errorHandler('execCommand failed');
          } catch (err) {
              errorHandler(err);
          }
      }
    }
  </script>
</body>
</html>
""";
    }

    private static string DecodeEncodedUnicodeCharacters(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        try
        {
            return System.Text.RegularExpressions.Regex.Replace(input, @"\\u([0-9a-fA-F]{4})", match => 
            {
                return ((char)int.Parse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)).ToString();
            });
        }
        catch
        {
            return input;
        }
    }
}

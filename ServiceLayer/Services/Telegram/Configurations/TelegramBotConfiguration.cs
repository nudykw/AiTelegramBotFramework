using Telegram.Bot.Types.Enums;
using ServiceLayer.Models;

namespace ServiceLayer.Services.Telegram.Configuretions;

public class TelegramBotConfiguration
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public static readonly string Configuration = "TelegramBotConfiguration";

    /// <summary>
    /// Telegram bot API token.
    /// </summary>
    public string BotToken { get; set; } = "";

    /// <summary>
    /// Default message parsing mode (Markdown, HTML, etc.).
    /// </summary>
    public ParseMode DefaultParseMode { get; set; } = ParseMode.Markdown;

    /// <summary>
    /// Bot owner ID for access to administrative commands.
    /// </summary>
    public long? OwnerId { get; set; }

    /// <summary>
    /// Initial balance for new users and top-up amount.
    /// </summary>
    public decimal InitialBalance { get; set; } = 0.1M;

    /// <summary>
    /// List of user IDs whose balance should not be deducted.
    /// </summary>
    public List<long> IgnoredBalanceUserIds { get; set; } = new();
    
    /// <summary>
    /// Specialized AI task configurations.
    /// </summary>
    public AiSettings AiSettings { get; set; } = new AiSettings();

    /// <summary>
    /// AI model cache expiry in hours.
    /// </summary>
    public int ModelCacheExpiryHours { get; set; } = 48;

    /// <summary>
    /// List of available voices for OpenAI TTS.
    /// </summary>
    public List<string> AvailableVoices { get; set; } = new();

    /// <summary>
    /// Public base URL of the web application (e.g. "https://example.com").
    /// When set, the bot uses Webhook mode: Telegram will POST updates to {BaseApiUrl}/aibot.
    /// When null or empty, the bot falls back to Polling mode.
    /// Trailing slash is normalised automatically.
    /// </summary>
    public string? BaseApiUrl { get; set; }

    /// <summary>
    /// Public base URL specifically for the admin/thoughts dashboard.
    /// If set, the inline thoughts button will use this URL.
    /// If null or empty, it falls back to BaseApiUrl or dynamic localhost URL.
    /// </summary>
    public string? DashboardBaseUrl { get; set; }
}

public class AiSettings
{
    /// <summary>
    /// Vision configuration containing model and provider names.
    /// </summary>
    public FullModelName? Vision { get; set; } = new FullModelName { ModelName = "gpt-4o", ProviderName = "OpenAI" };

    /// <summary>
    /// Drawing configuration containing model and provider names.
    /// </summary>
    public FullModelName? Drawing { get; set; } = new FullModelName { ModelName = "gpt-image-2", ProviderName = "OpenAI" };

    /// <summary>
    /// Classification configuration for intent analysis and internal tasks.
    /// </summary>
    public FullModelName? Classification { get; set; } = new FullModelName { ModelName = "gpt-4o", ProviderName = "OpenAI" };
    
    /// <summary>
    /// Summarization configuration for context minification.
    /// </summary>
    public FullModelName? Summarizer { get; set; } = new FullModelName { ModelName = "gpt-4o-mini", ProviderName = "OpenAI" };

    /// <summary>
    /// List of configurations for AI providers (OpenAI, Gemini, etc.).
    /// </summary>
    public List<ChatProviderConfig> ChatProviders { get; set; } = new();
}

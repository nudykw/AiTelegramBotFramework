using ServiceLayer.Services.Telegram.Configuretions;

namespace ServiceLayer.Services;

public class AppSettings
{
    /// <summary>
    /// Main section name in appsettings.json.
    /// </summary>
    public static readonly string Configuration = "AppSettings";

    /// <summary>
    /// Telegram bot configuration.
    /// </summary>
    public TelegramBotConfiguration TelegramBotConfiguration { get; set; } = null!;

    /// <summary>
    /// Database configuration.
    /// </summary>
    public DatabaseSettings Database { get; set; } = new();

    /// <summary>
    /// MCP configuration.
    /// </summary>
    public McpSettings McpSettings { get; set; } = new();

    /// <summary>
    /// Long-term memory (RAG) configuration.
    /// </summary>
    public MemorySettings MemorySettings { get; set; } = new();

    /// <summary>
    /// Background Scheduler (Hangfire) configuration.
    /// </summary>
    public SchedulerSettings Scheduler { get; set; } = new();

    /// <summary>
    /// Google Drive MCP configuration.
    /// </summary>
    public ServiceLayer.Services.Mcp.GoogleDrive.GoogleDriveSettings GoogleDriveSettings { get; set; } = new();
}

public class SchedulerSettings
{
    public bool Enabled { get; set; } = false;
}

public class MemorySettings
{
    public bool Enabled { get; set; } = false;
    public string VectorDbUrl { get; set; } = "http://localhost:16334";
    public string ProviderName { get; set; } = "OpenAI";
    public string ModelName { get; set; } = "text-embedding-3-small";
    public int TopK { get; set; } = 5;
    public double SimilarityThreshold { get; set; } = 0.7;
    public int MinMessageLength { get; set; } = 10;
}

public class McpSettings
{
    public List<DefaultMcpTool> DefaultTools { get; set; } = new();
}

public class DefaultMcpTool
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = "npx";
    public string Args { get; set; } = "[]";
    public string EnvVars { get; set; } = "{}";
}

public class DatabaseSettings
{
    public DataBaseLayer.Enums.DatabaseProvider Provider { get; set; } = DataBaseLayer.Enums.DatabaseProvider.PostgreSql;
    public string ConnectionString { get; set; } = string.Empty;
}

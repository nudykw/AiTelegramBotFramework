using ServiceLayer.Utils;

namespace ServiceLayer.Constans;

/// <summary>
/// Static bot commands.
/// </summary>
public sealed class BotCommand : StaticStringEnumBase<BotCommand>, IStaticStringEnum<BotCommand>
{
    private BotCommand(string value, string description, BotCommandScope requiredScope = BotCommandScope.Default | BotCommandScope.AllPrivateChats | BotCommandScope.AllGroupChats) : base(value)
    {
        Description = description;
        RequiredScope = requiredScope;
    }

    /// <summary>
    /// Command description for the Telegram menu.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Required scope/permissions for accessing the command.
    /// </summary>
    public BotCommandScope RequiredScope { get; }

    public static readonly BotCommand Billing = new("/billing", "Show usage statistics", BotCommandScope.AnyAdmin | BotCommandScope.Owner);
    public static readonly BotCommand Model = new("/model", "Select AI model");
    public static readonly BotCommand Provider = new("/provider", "Select AI provider");
    public static readonly BotCommand Draw = new("/draw", "Generate image by description. You can also just send a text prompt asking to draw something, and the image will be generated.");
    public static readonly BotCommand Lang = new("/lang", "Change language (e.g. /lang German)");
    public static readonly BotCommand Help = new("/help", "Show help info");
    public static readonly BotCommand Restart = new("/restart", "Restart the bot", BotCommandScope.Owner | BotCommandScope.AnyAdmin);
    public static readonly BotCommand UsersBalance = new("/users_balance", "Show all users' balances", BotCommandScope.AnyAdmin);
    public static readonly BotCommand SetBalance = new("/set_balance", "Set user balance by Id", BotCommandScope.AnyAdmin);
    public static readonly BotCommand RefreshModels = new("/refresh_models", "Refresh AI models cache", BotCommandScope.AnyAdmin);
    public static readonly BotCommand McpTools = new("/tools", "Manage active MCP tools");
    public static readonly BotCommand Voice = new("/voice", "Select voice for audio responses");
    public static readonly BotCommand Schedule = new("/schedule", "Manage scheduled newsletters");
    public static readonly BotCommand Shedule = new("/shedule", "Manage scheduled newsletters (alias)");
    public static readonly BotCommand Prompt = new("/prompt", "Set a custom system prompt/persona");

    /// <summary>
    /// Commands are typically case-insensitive.
    /// </summary>
    public static bool DefaultIgnoreCase => true;

    /// <summary>
    /// Implicit conversion from string (for ease of message parsing).
    /// </summary>
    public static implicit operator BotCommand?(string? value) => FromString(value);
}

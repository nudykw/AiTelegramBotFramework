using System.ComponentModel;
using ServiceLayer.Utils;

namespace ServiceLayer.Constans;

/// <summary>
/// AI providers.
/// </summary>
[TypeConverter(typeof(StaticStringEnumTypeConverter<AiProvider>))]
public sealed class AiProvider : StaticStringEnumBase<AiProvider>, IStaticStringEnum<AiProvider>
{
    private AiProvider(string value, string displayName, string? defaultDrawingModel = null) : base(value)
    {
        DisplayName = displayName;
        DefaultDrawingModel = defaultDrawingModel;
    }

    public string DisplayName { get; }

    /// <summary>
    /// Default drawing model for this provider.
    /// </summary>
    public string? DefaultDrawingModel { get; }

    public static readonly AiProvider OpenAI = new("openai", "OpenAI", AiModel.GptImage2);
    public static readonly AiProvider Gemini = new("gemini", "Google Gemini", AiModel.Imagen3);
    public static readonly AiProvider DeepSeek = new("deepseek", "DeepSeek");
    public static readonly AiProvider Grok = new("grok", "xAI Grok");

    /// <summary>
    /// Providers are typically case-insensitive in configs.
    /// </summary>
    public static bool DefaultIgnoreCase => true;

    /// <summary>
    /// Implicit conversion from string (for parsing from config or DB).
    /// </summary>
    public static implicit operator AiProvider?(string? value) => FromString(value);
}

using System.ComponentModel;
using ServiceLayer.Utils;

namespace ServiceLayer.Constans;

/// <summary>
/// Standard AI model names.
/// </summary>
[TypeConverter(typeof(StaticStringEnumTypeConverter<AiModel>))]
public sealed class AiModel : StaticStringEnumBase<AiModel>, IStaticStringEnum<AiModel>
{
    private AiModel(string value) : base(value) { }

    // OpenAI Models
    public static readonly AiModel Gpt4o = new("gpt-4o");
    public static readonly AiModel Gpt4oMini = new("gpt-4o-mini");
    public static readonly AiModel O1 = new("o1");
    public static readonly AiModel O1Pro = new("o1-pro");
    public static readonly AiModel O3Mini = new("o3-mini");
    public static readonly AiModel Gpt5 = new("gpt-5");
    public static readonly AiModel Gpt5Mini = new("gpt-5-mini");
    public static readonly AiModel Gpt54 = new("gpt-5.4");
    public static readonly AiModel Gpt54Mini = new("gpt-5.4-mini");
    public static readonly AiModel Gpt4Turbo = new("gpt-4-turbo");
    public static readonly AiModel Gpt35Turbo = new("gpt-3.5-turbo");

    // Gemini Models
    public static readonly AiModel GeminiPro = new("gemini-pro"); // Legacy/Alias
    public static readonly AiModel GeminiFlash = new("gemini-1.5-flash");
    public static readonly AiModel Gemini15Pro = new("gemini-1.5-pro");
    public static readonly AiModel Gemini20Flash = new("gemini-2.0-flash");
    public static readonly AiModel Gemini25Flash = new("gemini-2.5-flash");
    public static readonly AiModel Gemini25Pro = new("gemini-2.5-pro");
    public static readonly AiModel Gemini3Flash = new("gemini-3-flash-preview");
    public static readonly AiModel Gemini3Pro = new("gemini-3-pro-preview");
    public static readonly AiModel Gemini31Pro = new("gemini-3.1-pro-preview");
    public static readonly AiModel Imagen3 = new("imagen-3.0-generate-002");

    // OpenAI Image Models
    public static readonly AiModel DallE2 = new("dall-e-2"); // Legacy (Shutdown May 12, 2026)
    public static readonly AiModel DallE3 = new("dall-e-3"); // Legacy (Shutdown May 12, 2026)
    public static readonly AiModel GptImage2 = new("gpt-image-2");
    public static readonly AiModel GptImage1Mini = new("gpt-image-1-mini");

    // DeepSeek Models
    public static readonly AiModel DeepSeekChat = new("deepseek-chat");
    public static readonly AiModel DeepSeekReasoner = new("deepseek-reasoner");

    // Grok Models
    public static readonly AiModel Grok2 = new("grok-2");
    public static readonly AiModel Grok3 = new("grok-3");
    public static readonly AiModel Grok3Mini = new("grok-3-mini");
    public static readonly AiModel Grok4 = new("grok-4-0709");
    public static readonly AiModel Grok4Fast = new("grok-4-fast-non-reasoning");
    public static readonly AiModel Grok4Reasoner = new("grok-4-fast-reasoning");
    public static readonly AiModel Grok420 = new("grok-4.20-0309-non-reasoning");
    public static readonly AiModel GrokImagine = new("grok-imagine-image");
    public static readonly AiModel GrokBeta = new("grok-beta");

    public static implicit operator AiModel?(string? value) => FromString(value);
}

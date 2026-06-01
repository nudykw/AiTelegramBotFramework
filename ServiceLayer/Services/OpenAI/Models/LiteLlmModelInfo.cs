using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceLayer.Services.OpenAI.Models;

public class LiteLlmModelInfo
{
    [JsonPropertyName("input_cost_per_token")]
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? InputCostPerToken { get; set; }

    [JsonPropertyName("output_cost_per_token")]
    [JsonConverter(typeof(NullableDoubleConverter))]
    public double? OutputCostPerToken { get; set; }

    [JsonPropertyName("litellm_provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("max_tokens")]
    [JsonConverter(typeof(NullableIntConverter))]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("max_input_tokens")]
    [JsonConverter(typeof(NullableIntConverter))]
    public int? MaxInputTokens { get; set; }

    [JsonPropertyName("supports_function_calling")]
    [JsonConverter(typeof(NullableBoolConverter))]
    public bool? SupportsFunctionCalling { get; set; }
}

public class NullableIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt32(out var value))
            {
                return value;
            }
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (int.TryParse(str, out var value))
            {
                return value;
            }
            return null;
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

public class NullableDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetDouble(out var value))
            {
                return value;
            }
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
            return null;
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteNumberValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

public class NullableBoolConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.True)
        {
            return true;
        }

        if (reader.TokenType == JsonTokenType.False)
        {
            return false;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (bool.TryParse(str, out var value))
            {
                return value;
            }
            if (str == "1" || string.Equals(str, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (str == "0" || string.Equals(str, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteBooleanValue(value.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

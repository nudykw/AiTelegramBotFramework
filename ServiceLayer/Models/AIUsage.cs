using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceLayer.Models
{
    internal class AIUsage
    {
        [JsonInclude]
        [JsonPropertyName("prompt_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        internal int? PromptTokens { get; set; }

        [JsonInclude]
        [JsonPropertyName("completion_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        internal int? CompletionTokens { get; set; }

        [JsonInclude]
        [JsonPropertyName("total_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        internal int? TotalTokens { get; set; }
        
        [JsonInclude]
        [JsonPropertyName("reasoning_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        internal int? ReasoningTokens { get; set; }

        internal AIUsage()
        {
        }

        internal AIUsage(int? promptTokens, int? completionTokens, int? totalTokens, int? reasoningTokens = null)
        {
            PromptTokens = promptTokens;
            CompletionTokens = completionTokens;
            TotalTokens = totalTokens;
            ReasoningTokens = reasoningTokens;
        }

        public static implicit operator AIUsage(global::OpenAI.Usage usage)
        {
            if (usage == null)
            {
                throw new ArgumentNullException("Usage");
            }

            // OpenAI library might have ReasoningTokens or similar in newer versions
            // but for now we'll stick to what we know exists and add more if possible.
            return new AIUsage(usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens);
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}

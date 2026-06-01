using System.Collections.Generic;
using ServiceLayer.Models;

namespace ServiceLayer.Services;

public class ChatServiceResponse
{
    public required List<string> Choices { get; set; }
    public int? TotalTokens { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? ReasoningTokens { get; set; }
    public decimal? Cost { get; set; }
    public double? LatencySeconds { get; set; }
    public double? TokensPerSecond { get; set; }
    public required string ProviderName { get; set; }
    public required string ModelName { get; set; }
    public List<string> UsedTools { get; set; } = new();
    public List<ToolCallInfo> ToolCalls { get; set; } = new();
    public int TruncatedToolsCount { get; set; }
    public string? Thoughts { get; set; }
}


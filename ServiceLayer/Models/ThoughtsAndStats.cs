using System.Collections.Generic;

namespace ServiceLayer.Models;

public class ThoughtsAndStats
{
    public required string Thoughts { get; set; }
    public required string ModelName { get; set; }
    public required string ProviderName { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public int? ReasoningTokens { get; set; }
    public decimal? Cost { get; set; }
    public double? LatencySeconds { get; set; }
    public double? TokensPerSecond { get; set; }
    public List<string> UsedTools { get; set; } = new();
    public List<ToolCallInfo> ToolCalls { get; set; } = new();
    public string? SystemPrompt { get; set; }
    public decimal? UserBalanceBefore { get; set; }
    public decimal? UserBalanceAfter { get; set; }
    public string? UserFullName { get; set; }
    public long UserId { get; set; }
}

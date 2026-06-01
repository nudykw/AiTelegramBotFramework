using System;

namespace ServiceLayer.Models;

public class ToolCallInfo
{
    public required string ToolName { get; set; }
    public required string Arguments { get; set; }
    public required string Response { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

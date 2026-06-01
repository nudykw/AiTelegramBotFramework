using System.ComponentModel.DataAnnotations;

namespace DataBaseLayer.Models;

public class McpToolRecord
{
    [Key]
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Command { get; set; } = "npx";

    public string Args { get; set; } = "[]"; // JSON array, e.g. ["-y", "@modelcontextprotocol/server-web-search"]

    public string EnvVars { get; set; } = "{}"; // JSON dictionary for API keys etc.

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

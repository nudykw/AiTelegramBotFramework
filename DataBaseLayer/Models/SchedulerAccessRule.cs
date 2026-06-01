using System;

namespace DataBaseLayer.Models;

public class SchedulerAccessRule
{
    public int Id { get; set; }
    public long UserId { get; set; }
    public bool IsAllowed { get; set; } // true = whitelist, false = blacklist
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
}

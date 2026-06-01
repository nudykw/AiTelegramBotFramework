using System;

namespace DataBaseLayer.Models;

public class ScheduledNewsletter
{
    public int Id { get; set; }
    public long UserId { get; set; }
    public long ChatId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

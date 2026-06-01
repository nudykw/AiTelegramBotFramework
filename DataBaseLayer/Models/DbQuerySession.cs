using System;

namespace DataBaseLayer.Models
{
    public class DbQuerySession
    {
        public int Id { get; set; }
        public long AdminUserId { get; set; }
        public string SqlQuery { get; set; } = string.Empty;
        public int PageSize { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

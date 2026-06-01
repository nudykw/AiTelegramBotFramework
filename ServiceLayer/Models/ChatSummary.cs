namespace ServiceLayer.Models
{
    public class ChatSummary
    {
        public string Content { get; set; } = string.Empty;
        public long LastSummarizedMessageId { get; set; }
        public int TotalMessagesSummarized { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

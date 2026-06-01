using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataBaseLayer.Models
{
    [PrimaryKey(nameof(ChatId), nameof(MessageId))]
    public class HistoryMessage
    {
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long ChatId { get; set; }
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long MessageId { get; set; }
        public long? ParentMessageId { get; set; }
        public int RoleId { get; set; }

        public required DateTime CreationDate { get; set; }
        public required DateTime ModifiedDate { get; set; }
        public required string Text { get; set; }
        [MaxLength(256)]
        public string? FromUserName { get; set; }
        [MaxLength(128)]
        public string? ProviderName { get; set; }
        [MaxLength(128)]
        public string? ModelName { get; set; }
    }
}

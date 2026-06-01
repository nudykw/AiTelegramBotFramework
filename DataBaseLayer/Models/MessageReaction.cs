using DataBaseLayer.Enums;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataBaseLayer.Models
{
    [PrimaryKey(nameof(Id))]
    [Index(nameof(ChatId), nameof(MessageId), nameof(UserId), IsUnique = true)]
    public class MessageReaction
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public required long ChatId { get; set; }

        public required long MessageId { get; set; }

        public required long UserId { get; set; }

        public required MessageReactionType Reaction { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    }
}

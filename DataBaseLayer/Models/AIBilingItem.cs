using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataBaseLayer.Models
{
    [PrimaryKey(nameof(Id))]
    public class AIBilingItem
    {
        public long Id { get; set; }
        [ForeignKey("TelegramChatInfo")]
        public required long TelegramChatInfoId { get; set; }
        [ForeignKey("TelegramUserInfo")]
        public required long TelegramUserInfoId { get; set; }
        public required DateTime CreationDate { get; set; }
        public required DateTime ModifiedDate { get; set; }
        [MaxLength(128)]
        public required string ModelName { get; set; }
        [MaxLength(128)]
        public string? ProviderName { get; set; }
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
        public int? TotalTokens { get; set; }
        public decimal? Cost { get; set; }

        public TelegramChatInfo TelegramChatInfo { get; set; } = default!;
        public TelegramUserInfo TelegramUserInfo { get; set; } = default!;

    }
}

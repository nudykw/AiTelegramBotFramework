using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DataBaseLayer.Enums;

namespace DataBaseLayer.Models
{
    [PrimaryKey(nameof(Id))]

    public class TelegramUserInfo
    {
        /// <summary>
        /// UserId
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long Id { get; set; }
        public required bool IsBot { get; set; }
        [MaxLength(512)]
        public required string FirstName { get; set; }
        [MaxLength(128)]
        public string? LastName { get; set; }
        [MaxLength(128)]
        public string? Username { get; set; }
        public bool? IsPremium { get; set; }
        public ChatStrategy PreferredProvider { get; set; }
        [MaxLength(10)]
        public string? LanguageCode { get; set; }
        /// <summary>
        /// User's selected voice ID for OpenAI TTS (e.g., "alloy", "echo").
        /// </summary>
        [MaxLength(32)]
        public string VoiceId { get; set; } = "alloy";

        /// <summary>
        /// Personally selected GPT model for the user.
        /// </summary>
        [MaxLength(128)]
        public string? SelectedModel { get; set; }

        /// <summary>
        /// Personally selected system prompt or persona rules.
        /// </summary>
        [MaxLength(4000)]
        public string? SystemPrompt { get; set; }

        /// <summary>
        /// Comma-separated list of tool names that the user has manually disabled.
        /// </summary>
        [MaxLength(1000)]
        public string? DisabledTools { get; set; }

        public decimal Balance { get; set; }
        public DateTime? BalanceModifiedAt { get; set; }
        public DateTime? LastAiInteraction { get; set; }
    }
}

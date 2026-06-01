using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DataBaseLayer.Models
{
    [PrimaryKey(nameof(Id))]
    public class TelegramChatInfo
    {
        /// <summary>
        /// ChatId
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public long Id { get; set; }
        [MaxLength(32)]
        public required string ChatType { get; set; }
        [MaxLength(512)]
        public string? Description { get; set; }
        [MaxLength(512)]
        public string? Title { get; set; }
        [MaxLength(512)]
        public string? Username { get; set; }
        [MaxLength(512)]
        public string? InviteLink { get; set; }
    }
}

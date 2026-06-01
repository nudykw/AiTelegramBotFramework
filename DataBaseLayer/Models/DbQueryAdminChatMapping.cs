using Microsoft.EntityFrameworkCore;

namespace DataBaseLayer.Models
{
    [PrimaryKey(nameof(AdminUserId), nameof(ChatId))]
    public class DbQueryAdminChatMapping
    {
        public long AdminUserId { get; set; }
        public long ChatId { get; set; }
    }
}

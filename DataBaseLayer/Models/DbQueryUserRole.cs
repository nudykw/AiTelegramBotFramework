using Microsoft.EntityFrameworkCore;

namespace DataBaseLayer.Models
{
    [PrimaryKey(nameof(UserId), nameof(RoleId))]
    public class DbQueryUserRole
    {
        public long UserId { get; set; }
        public int RoleId { get; set; }
    }
}

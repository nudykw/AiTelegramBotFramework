namespace DataBaseLayer.Models
{
    public class DbQueryPermission
    {
        public int Id { get; set; }
        public int RoleId { get; set; }
        public required string TableName { get; set; }
    }
}

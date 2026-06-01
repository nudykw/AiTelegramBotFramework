namespace ServiceLayer.Utils
{
    public interface IUserContext
    {
        long UserId { get; set; }
        long ChatId { get; set; }
    }

    public class UserContext : IUserContext
    {
        public long UserId { get; set; }
        public long ChatId { get; set; }
    }
}

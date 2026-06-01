using DataBaseLayer.Models;
using Microsoft.EntityFrameworkCore;

namespace DataBaseLayer.Contexts
{
    public class StoreContext : DbContext
    {
        public StoreContext() { }
        public StoreContext(DbContextOptions<StoreContext> options) : base(options) { }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Configuration is now handled in MigrationConfigurator or Program.cs via DI
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<HistoryMessage>()
            .HasKey(e => new { e.ChatId, e.MessageId });

            modelBuilder.Entity<MessageReaction>()
                .HasIndex(p => new { p.ChatId, p.MessageId, p.UserId })
                .IsUnique();

            modelBuilder.Entity<DbQueryUserRole>()
                .HasKey(e => new { e.UserId, e.RoleId });

            modelBuilder.Entity<DbQueryAdminChatMapping>()
                .HasKey(e => new { e.AdminUserId, e.ChatId });

            base.OnModelCreating(modelBuilder);
        }

        #region models
        public DbSet<HistoryMessage> Messages { get; set; }
        public DbSet<AIBilingItem> AIBilingItem { get; set; }
        public DbSet<TelegramChatInfo> TelegramChatInfos { get; set; }
        public DbSet<TelegramUserInfo> TelegramUserInfos { get; set; }
        public DbSet<CachedTranslation> CachedTranslations { get; set; }
        public DbSet<BalanceHistory> BalanceHistories { get; set; }
        public DbSet<CachedAIModel> CachedAIModels { get; set; }
        public DbSet<McpToolRecord> McpToolRecords { get; set; }
        public DbSet<MessageReaction> MessageReactions { get; set; }
        public DbSet<ScheduledNewsletter> ScheduledNewsletters { get; set; }
        public DbSet<SchedulerAccessRule> SchedulerAccessRules { get; set; }
        public DbSet<DbQueryRole> DbQueryRoles { get; set; }
        public DbSet<DbQueryPermission> DbQueryPermissions { get; set; }
        public DbSet<DbQueryUserRole> DbQueryUserRoles { get; set; }
        public DbSet<DbQueryAdminChatMapping> DbQueryAdminChatMappings { get; set; }
        public DbSet<DbQuerySession> DbQuerySessions { get; set; }
        #endregion
    }
}

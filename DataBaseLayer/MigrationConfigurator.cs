using DataBaseLayer.Contexts;
using DataBaseLayer.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;
using DataBaseLayer.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DataBaseLayer;

public static class MigrationConfigurator
{
    public static void Configure(DbContextOptionsBuilder optionsBuilder, DatabaseProvider provider, string connectionString)
    {
        if (provider != DatabaseProvider.PostgreSql)
        {
            throw new ArgumentException("Only PostgreSQL is supported.", nameof(provider));
        }

        optionsBuilder.UseNpgsql(connectionString, x => x.MigrationsAssembly("DataBaseLayer"))
                      .ReplaceService<IMigrationsAssembly, DataBaseLayer.Internal.ProviderSpecificMigrationsAssembly>()
                      .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }

    public static void ApplyMigrations(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<StoreContext>();
        dbContext.Database.Migrate();


        // Seed DbQueryRoles
        if (!dbContext.DbQueryRoles.Any())
        {
            dbContext.DbQueryRoles.AddRange(
                new Models.DbQueryRole { Name = "bot_owner" },
                new Models.DbQueryRole { Name = "bot_admin" }
            );
            dbContext.SaveChanges();
        }

        var ownerRole = dbContext.DbQueryRoles.FirstOrDefault(r => r.Name == "bot_owner");
        var adminRole = dbContext.DbQueryRoles.FirstOrDefault(r => r.Name == "bot_admin");

        if (adminRole != null && !dbContext.DbQueryPermissions.Any())
        {
            var defaultPermissions = new[] { "TelegramUserInfo", "AIBilingItem", "BalanceHistory", "ScheduledNewsletter", "MessageReaction" };
            foreach (var table in defaultPermissions)
            {
                dbContext.DbQueryPermissions.Add(new Models.DbQueryPermission
                {
                    RoleId = adminRole.Id,
                    TableName = table
                });
            }
            dbContext.SaveChanges();
        }

        // Auto-bind owner
        var configuration = scope.ServiceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
        var ownerIdVal = configuration?.GetSection("AppSettings:TelegramBotConfiguration:OwnerId")?.Value;
        if (long.TryParse(ownerIdVal, out long ownerId) && ownerId != 0 && ownerRole != null)
        {
            var hasOwnerRole = dbContext.DbQueryUserRoles.Any(r => r.UserId == ownerId && r.RoleId == ownerRole.Id);
            if (!hasOwnerRole)
            {
                dbContext.DbQueryUserRoles.Add(new Models.DbQueryUserRole
                {
                    UserId = ownerId,
                    RoleId = ownerRole.Id
                });
                dbContext.SaveChanges();
            }
        }
    }
}
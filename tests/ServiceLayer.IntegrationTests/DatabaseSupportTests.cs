using DataBaseLayer;
using DataBaseLayer.Contexts;
using DataBaseLayer.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace ServiceLayer.IntegrationTests;

public class DatabaseSupportTests
{
    [Fact]
    public async Task Postgres_Migrations_CanBeApplied()
    {
        // Arrange
        var container = new PostgreSqlBuilder("postgres:15-alpine")
            .Build();
        await container.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<StoreContext>(options =>
            MigrationConfigurator.Configure(options, DatabaseProvider.PostgreSql, container.GetConnectionString()));

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        try
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<StoreContext>();
            await context.Database.MigrateAsync();
            
            var canConnect = await context.Database.CanConnectAsync();
            Assert.True(canConnect);
        }
        finally
        {
            await container.StopAsync();
        }
    }
}

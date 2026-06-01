using DataBaseLayer.Contexts;
using DataBaseLayer.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DataBaseLayer;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StoreContext>
{
    public StoreContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<StoreContext>();
        
        var provider = DatabaseProvider.PostgreSql;
        var connectionString = "Host=localhost;Database=design;Username=postgres;Password=password";

        if (args.Length > 1)
        {
            connectionString = args[1];
        }

        MigrationConfigurator.Configure(optionsBuilder, provider, connectionString);

        return new StoreContext(optionsBuilder.Options);
    }
}

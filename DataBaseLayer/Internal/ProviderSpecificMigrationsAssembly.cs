using System.Reflection;
using DataBaseLayer.Contexts;
using DataBaseLayer.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DataBaseLayer.Internal;

#pragma warning disable EF1001 // Internal EF Core API usage.
public class ProviderSpecificMigrationsAssembly : MigrationsAssembly
{
    private readonly DbContext _context;

    public ProviderSpecificMigrationsAssembly(
        ICurrentDbContext currentContext,
        IDbContextOptions options,
        IMigrationsIdGenerator idGenerator,
        IDiagnosticsLogger<DbLoggerCategory.Migrations> logger)
        : base(currentContext, options, idGenerator, logger)
    {
        _context = currentContext.Context;
    }

    public override ModelSnapshot? ModelSnapshot
    {
        get
        {
            var provider = _context.Database.ProviderName;
            var expectedNamespace = provider switch
            {
                "Npgsql.EntityFrameworkCore.PostgreSQL" => "DataBaseLayer.Migrations.Postgres",
                _ => null
            };

            if (expectedNamespace == null) return base.ModelSnapshot;

            var snapshotType = Assembly.GetTypes()
                .FirstOrDefault(t => t.Namespace == expectedNamespace && typeof(ModelSnapshot).IsAssignableFrom(t));

            if (snapshotType == null) return base.ModelSnapshot;

            return (ModelSnapshot)Activator.CreateInstance(snapshotType)!;
        }
    }

    public override IReadOnlyDictionary<string, TypeInfo> Migrations
    {
        get
        {
            var allMigrations = base.Migrations;
            var provider = _context.Database.ProviderName;

            // Map provider names to namespaces
            var expectedNamespace = provider switch
            {
                "Npgsql.EntityFrameworkCore.PostgreSQL" => "DataBaseLayer.Migrations.Postgres",
                _ => null
            };

            if (expectedNamespace == null) return allMigrations;

            return allMigrations
                .Where(m => m.Value.Namespace == expectedNamespace)
                .ToDictionary(m => m.Key, m => m.Value)
                .AsReadOnly();
        }
    }
}
#pragma warning restore EF1001

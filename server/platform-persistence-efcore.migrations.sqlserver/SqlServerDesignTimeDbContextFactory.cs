using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ICP.Platform.Persistence.EfCore.SqlServerMigrations;

public sealed class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<EfPlatformDbContext>
{
    public EfPlatformDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EfPlatformDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=NewP_DesignTime;Integrated Security=True;TrustServerCertificate=True;";

        EfCoreProviderOptions.Configure(
            optionsBuilder,
            PersistenceProvider.SqlServer,
            connectionString,
            typeof(SqlServerDesignTimeDbContextFactory).Assembly.GetName().Name);

        return new EfPlatformDbContext(
            optionsBuilder.Options,
            PlatformPersistenceModelCatalog.AdaptSqlServerMigrationBaseline(
                PlatformCoreLegacyModelSource.GetDefinitions()));
    }
}

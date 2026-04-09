using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ICP.Platform.Persistence.EfCore.MySqlMigrations;

public sealed class MySqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<EfPlatformDbContext>
{
    public EfPlatformDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EfPlatformDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MySql")
            ?? "Server=localhost;Port=3306;Database=icp_platform;User=icp;Password=REDACTED_FOR_OPEN_SOURCE;";

        EfCoreProviderOptions.Configure(
            optionsBuilder,
            PersistenceProvider.MySql,
            connectionString,
            typeof(MySqlDesignTimeDbContextFactory).Assembly.GetName().Name);

        return new EfPlatformDbContext(optionsBuilder.Options, PlatformPersistenceModelCatalog.GetDefinitions());
    }
}

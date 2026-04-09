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
            ?? "Server=localhost,1433;Database=ICP_Platform;User Id=sa;Password=REDACTED_FOR_OPEN_SOURCE;TrustServerCertificate=True;";

        EfCoreProviderOptions.Configure(
            optionsBuilder,
            PersistenceProvider.SqlServer,
            connectionString,
            typeof(SqlServerDesignTimeDbContextFactory).Assembly.GetName().Name);

        return new EfPlatformDbContext(optionsBuilder.Options, PlatformPersistenceModelCatalog.GetDefinitions());
    }
}

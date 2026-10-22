using ICP.Platform.Persistence.Composition;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ICP.Platform.Persistence.EfCore.OracleMigrations;

public sealed class OracleDesignTimeDbContextFactory : IDesignTimeDbContextFactory<EfPlatformDbContext>
{
    public EfPlatformDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EfPlatformDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Oracle")
            ?? "User Id=system;Password=Icp_oracle_123;Data Source=localhost:1521/FREEPDB1;";

        EfCoreProviderOptions.Configure(
            optionsBuilder,
            PersistenceProvider.Oracle,
            connectionString,
            typeof(OracleDesignTimeDbContextFactory).Assembly.GetName().Name);

        return new EfPlatformDbContext(
            optionsBuilder.Options,
            PlatformPersistenceModelCatalog.AdaptPlatformCore(NonSqlPlatformBaselineModelSource.GetDefinitions()));
    }
}

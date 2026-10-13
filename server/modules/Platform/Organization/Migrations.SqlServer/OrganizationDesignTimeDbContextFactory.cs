using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ICP.Platform.Modules.Organization.Migrations.SqlServer;

public sealed class OrganizationDesignTimeDbContextFactory : IDesignTimeDbContextFactory<EfPlatformDbContext>
{
    public EfPlatformDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EfPlatformDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=NewP_Organization_DesignTime;Integrated Security=True;TrustServerCertificate=True;";

        EfCoreProviderOptions.Configure(
            optionsBuilder,
            PersistenceProvider.SqlServer,
            connectionString,
            typeof(OrganizationDesignTimeDbContextFactory).Assembly.GetName().Name);

        var definitions = new OrganizationModelMetadataProvider().GetDefinitions()
            .Select(static definition => definition with
            {
                OwnerModuleId = "Organization",
                SchemaName = "dbo"
            })
            .ToArray();
        return new EfPlatformDbContext(optionsBuilder.Options, definitions);
    }
}

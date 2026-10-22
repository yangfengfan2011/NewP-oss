using ICP.Platform.Modules.Audit.Persistence.Descriptors;
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ICP.Platform.Modules.Audit.Migrations.SqlServer;

public sealed class AuditDesignTimeDbContextFactory : IDesignTimeDbContextFactory<EfPlatformDbContext>
{
    // 只投影 Audit owner 的当前模型，保证首个 migration 从零创建本模块表。
    public EfPlatformDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EfPlatformDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=NewP_Audit_DesignTime;Integrated Security=True;TrustServerCertificate=True;";

        EfCoreProviderOptions.Configure(
            optionsBuilder,
            PersistenceProvider.SqlServer,
            connectionString,
            typeof(AuditDesignTimeDbContextFactory).Assembly.GetName().Name);

        var definitions = new AuditModelMetadataProvider().GetDefinitions()
            .Select(static definition => definition with
            {
                OwnerModuleId = "Audit",
                SchemaName = "dbo"
            })
            .ToArray();
        return new EfPlatformDbContext(optionsBuilder.Options, definitions);
    }
}

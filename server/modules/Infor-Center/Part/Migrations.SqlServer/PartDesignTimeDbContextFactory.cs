using ICP.Platform.Persistence;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ICP.Platform.Modules.Part.Migrations.SqlServer;

public sealed class PartDesignTimeDbContextFactory : IDesignTimeDbContextFactory<EfPlatformDbContext>
{
    /// <summary>迁移只消费本模块 authoring；无运行时建库或初始化副作用。</summary>
    public EfPlatformDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>();
        EfCoreProviderOptions.Configure(options, PersistenceProvider.SqlServer,
            "Server=(localdb)\\MSSQLLocalDB;Database=NewP_Part_DesignTime;Integrated Security=True;TrustServerCertificate=True;",
            typeof(PartDesignTimeDbContextFactory).Assembly.GetName().Name);
        return new(options.Options, new PartModelMetadataProvider().GetDefinitions());
    }
}

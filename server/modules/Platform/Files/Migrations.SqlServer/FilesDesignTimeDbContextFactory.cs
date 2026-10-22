using ICP.Platform.Persistence;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ICP.Platform.Modules.Files.Migrations.SqlServer;

public sealed class FilesDesignTimeDbContextFactory : IDesignTimeDbContextFactory<EfPlatformDbContext>
{
    /// <summary>只投影 Files 自有模型；设计时生成迁移不连接或初始化业务数据库。</summary>
    public EfPlatformDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>();
        EfCoreProviderOptions.Configure(options, PersistenceProvider.SqlServer,
            "Server=(localdb)\\MSSQLLocalDB;Database=NewP_Files_DesignTime;Integrated Security=True;TrustServerCertificate=True;",
            typeof(FilesDesignTimeDbContextFactory).Assembly.GetName().Name);
        return new(options.Options, new FilesModelMetadataProvider().GetDefinitions());
    }
}

# Phase 6.5 多数据库持久化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 `ConfigCenter` 和 `Authorization` 落地真实数据库持久化，支持通过配置切换 `SQL Server / Oracle / MySQL`，同时保证业务层调用方式统一并保留受控原生 SQL 能力。

**Architecture:** 新增平台级持久化底座项目，集中承载 Provider 选择、`DbContext`、迁移注册和统一 SQL 执行入口；模块内保留现有 `Models / Repositories / Services` 结构，仅在 `Persistence` 目录补 EF 映射与仓储实现。宿主在启动时根据 `Persistence:Provider` 选择数据库与迁移集，执行自动迁移和统一 seed，三库分别通过集成测试与 smoke 脚本验证。

**Tech Stack:** .NET 10、ASP.NET Core、EF Core 10、Microsoft.EntityFrameworkCore.SqlServer 10.0.1、Oracle.EntityFrameworkCore 10.23.26100、MySql.EntityFrameworkCore 10.0.1、xUnit、PowerShell

---

### Task 1: 建立平台级持久化底座

**Files:**
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/PlatformPersistence.csproj`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/PersistenceProvider.cs`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/PersistenceOptions.cs`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/Abstractions/IPersistenceModelContributor.cs`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/Abstractions/ISqlQueryExecutor.cs`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/Abstractions/ISqlDialect.cs`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/PlatformDbContext.cs`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/Sql/EfSqlQueryExecutor.cs`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/Sql/SqlServerDialect.cs`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/Sql/OracleDialect.cs`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/Sql/MySqlDialect.cs`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/Extensions/PersistenceServiceCollectionExtensions.cs`
- Create: `D:/Github/ICP-X/NewP/tests/Persistence.Tests/Persistence.Tests.csproj`
- Create: `D:/Github/ICP-X/NewP/tests/Persistence.Tests/PersistenceRegistrationTests.cs`
- Modify: `D:/Github/ICP-X/NewP/ICP-Platform.sln`

- [ ] **Step 1: 先写持久化底座注册失败测试**

在 `D:/Github/ICP-X/NewP/tests/Persistence.Tests/PersistenceRegistrationTests.cs` 写一个测试，验证按 Provider 注册时可以解析 `PlatformDbContext`、`ISqlDialect`、`ISqlQueryExecutor`。

```csharp
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public class PersistenceRegistrationTests
{
    [Theory]
    [InlineData("SqlServer", typeof(SqlServerDialect))]
    [InlineData("Oracle", typeof(OracleDialect))]
    [InlineData("MySql", typeof(MySqlDialect))]
    public void AddPlatformPersistence_RegistersProviderServices(string provider, Type dialectType)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = provider,
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;",
                ["ConnectionStrings:Oracle"] = "User Id=icp;Password=REDACTED_FOR_OPEN_SOURCE;Data Source=localhost/FREEPDB1;",
                ["ConnectionStrings:MySql"] = "Server=localhost;Database=icp_platform;User=root;Password=REDACTED_FOR_OPEN_SOURCE;"
            })
            .Build();

        services.AddPlatformPersistence(configuration);

        using var scope = services.BuildServiceProvider().CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<PlatformDbContext>());
        Assert.IsType(dialectType, scope.ServiceProvider.GetRequiredService<ISqlDialect>());
        Assert.NotNull(scope.ServiceProvider.GetService<ISqlQueryExecutor>());
    }
}
```

- [ ] **Step 2: 跑测试确认它先失败**

Run: `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj --filter PersistenceRegistrationTests -v minimal`
Expected: 编译失败，提示 `ICP.Platform.Persistence` 命名空间、`AddPlatformPersistence`、`PlatformDbContext`、`ISqlDialect`、`ISqlQueryExecutor` 不存在。

- [ ] **Step 3: 创建平台级持久化项目与核心类型**

创建 `D:/Github/ICP-X/NewP/server/platform-persistence/PlatformPersistence.csproj`，并加入基础包引用。

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>ICP.Platform.Persistence</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.1" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.1" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.1">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.1" />
    <PackageReference Include="Oracle.EntityFrameworkCore" Version="10.23.26100" />
    <PackageReference Include="MySql.EntityFrameworkCore" Version="10.0.1" />
  </ItemGroup>
</Project>
```

```csharp
namespace ICP.Platform.Persistence;

public enum PersistenceProvider
{
    SqlServer,
    Oracle,
    MySql
}
```

```csharp
namespace ICP.Platform.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";
    public PersistenceProvider Provider { get; set; } = PersistenceProvider.SqlServer;
    public bool AutoMigrate { get; set; } = true;
    public bool AutoSeed { get; set; } = true;
}
```

```csharp
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Persistence.Abstractions;

public interface IPersistenceModelContributor
{
    int Order { get; }
    void Configure(ModelBuilder modelBuilder);
}
```

```csharp
using System.Data.Common;

namespace ICP.Platform.Persistence.Abstractions;

public interface ISqlQueryExecutor
{
    Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default);
}
```

```csharp
namespace ICP.Platform.Persistence.Abstractions;

public interface ISqlDialect
{
    string Name { get; }
    string Parameter(string name);
}
```

```csharp
using ICP.Platform.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Persistence;

public sealed class PlatformDbContext : DbContext
{
    private readonly IEnumerable<IPersistenceModelContributor> _contributors;

    public PlatformDbContext(
        DbContextOptions<PlatformDbContext> options,
        IEnumerable<IPersistenceModelContributor> contributors) : base(options)
    {
        _contributors = contributors;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var contributor in _contributors.OrderBy(static x => x.Order))
        {
            contributor.Configure(modelBuilder);
        }

        base.OnModelCreating(modelBuilder);
    }
}
```

- [ ] **Step 4: 实现 Provider 注册、方言与 SQL 执行器**

在 `D:/Github/ICP-X/NewP/server/platform-persistence/Sql/EfSqlQueryExecutor.cs` 中复用 `PlatformDbContext` 当前连接与事务。

```csharp
using System.Data;
using System.Data.Common;
using ICP.Platform.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore.Storage;

namespace ICP.Platform.Persistence.Sql;

public sealed class EfSqlQueryExecutor : ISqlQueryExecutor
{
    private readonly PlatformDbContext _dbContext;
    private readonly ISqlDialect _dialect;

    public EfSqlQueryExecutor(PlatformDbContext dbContext, ISqlDialect dialect)
    {
        _dbContext = dbContext;
        _dialect = dialect;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();

        foreach (var pair in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = _dialect.Parameter(pair.Key);
            parameter.Value = pair.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        var results = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(map(reader));
        }

        return results;
    }
}
```

在 `D:/Github/ICP-X/NewP/server/platform-persistence/Extensions/PersistenceServiceCollectionExtensions.cs` 中写统一注册入口。

```csharp
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Persistence.Extensions;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PersistenceOptions>(configuration.GetSection(PersistenceOptions.SectionName));
        services.AddScoped<ISqlQueryExecutor, EfSqlQueryExecutor>();
        services.AddScoped<ISqlDialect>(sp =>
        {
            var provider = sp.GetRequiredService<IOptions<PersistenceOptions>>().Value.Provider;
            return provider switch
            {
                PersistenceProvider.SqlServer => new SqlServerDialect(),
                PersistenceProvider.Oracle => new OracleDialect(),
                PersistenceProvider.MySql => new MySqlDialect(),
                _ => throw new InvalidOperationException($"Unsupported provider: {provider}")
            };
        });

        services.AddDbContext<PlatformDbContext>((sp, builder) =>
        {
            var provider = sp.GetRequiredService<IOptions<PersistenceOptions>>().Value.Provider;
            switch (provider)
            {
                case PersistenceProvider.SqlServer:
                    builder.UseSqlServer(configuration.GetConnectionString("SqlServer"));
                    break;
                case PersistenceProvider.Oracle:
                    builder.UseOracle(configuration.GetConnectionString("Oracle"));
                    break;
                case PersistenceProvider.MySql:
                    builder.UseMySQL(configuration.GetConnectionString("MySql"));
                    break;
            }
        });

        return services;
    }
}
```

- [ ] **Step 5: 重新跑持久化底座测试并把项目加入解决方案**

Run:
- `dotnet sln .\ICP-Platform.sln add .\server\platform-persistence\PlatformPersistence.csproj`
- `dotnet sln .\ICP-Platform.sln add .\tests\Persistence.Tests\Persistence.Tests.csproj`
- `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj --filter PersistenceRegistrationTests -v minimal`

Expected:
- `Project added to the solution`
- `PersistenceRegistrationTests` 通过

- [ ] **Step 6: Commit**

```bash
git add D:/Github/ICP-X/NewP/server/platform-persistence D:/Github/ICP-X/NewP/tests/Persistence.Tests D:/Github/ICP-X/NewP/ICP-Platform.sln
git commit -m "feat: add persistence foundation and provider registration"
```

### Task 2: 落地 ConfigCenter 的 EF 持久化实现

**Files:**
- Create: `D:/Github/ICP-X/NewP/server/modules/ConfigCenter/Persistence/ConfigCenterModelContributor.cs`
- Create: `D:/Github/ICP-X/NewP/server/modules/ConfigCenter/Persistence/ConfigItemEntityConfiguration.cs`
- Create: `D:/Github/ICP-X/NewP/server/modules/ConfigCenter/Persistence/EfConfigRepository.cs`
- Modify: `D:/Github/ICP-X/NewP/server/modules/ConfigCenter/ConfigCenterModule.cs`
- Modify: `D:/Github/ICP-X/NewP/server/modules/ConfigCenter/ConfigCenter.csproj`
- Modify: `D:/Github/ICP-X/NewP/tests/ConfigCenter.Tests/ConfigServiceTests.cs`
- Create: `D:/Github/ICP-X/NewP/tests/ConfigCenter.Tests/EfConfigRepositoryTests.cs`

- [ ] **Step 1: 先写 ConfigCenter 仓储行为测试**

在 `D:/Github/ICP-X/NewP/tests/ConfigCenter.Tests/EfConfigRepositoryTests.cs` 写一个测试，覆盖优先级查询和模块查询。

```csharp
using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Persistence;
using ICP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class EfConfigRepositoryTests
{
    [Fact]
    public async Task GetByKeyAsync_ReturnsTenantAndEnvironmentMatchFirst()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(nameof(GetByKeyAsync_ReturnsTenantAndEnvironmentMatchFirst))
            .Options;

        await using var db = new PlatformDbContext(options, new[] { new ConfigCenterModelContributor() });
        var repository = new EfConfigRepository(db);

        await repository.CreateAsync(new ConfigItem { Key = "Test.Key", Value = "global", Type = "String", ModuleId = "Test", DisplayName = "Global" });
        await repository.CreateAsync(new ConfigItem { Key = "Test.Key", Value = "prod-tenant", Type = "String", ModuleId = "Test", DisplayName = "Specific", Environment = "Prod", TenantId = "tenant-a" });

        var item = await repository.GetByKeyAsync("Test.Key", "Prod", "tenant-a");

        Assert.NotNull(item);
        Assert.Equal("prod-tenant", item!.Value);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter EfConfigRepositoryTests -v minimal`
Expected: 编译失败，提示 `ConfigCenterModelContributor`、`EfConfigRepository` 不存在。

- [ ] **Step 3: 添加 ConfigCenter 的模型映射与仓储实现**

在 `D:/Github/ICP-X/NewP/server/modules/ConfigCenter/Persistence/ConfigItemEntityConfiguration.cs` 定义表、列与索引。

```csharp
using ICP.Platform.Modules.ConfigCenter.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ICP.Platform.Modules.ConfigCenter.Persistence;

public sealed class ConfigItemEntityConfiguration : IEntityTypeConfiguration<ConfigItem>
{
    public void Configure(EntityTypeBuilder<ConfigItem> builder)
    {
        builder.ToTable("config_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(64);
        builder.Property(x => x.Key).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ModuleId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Group).HasMaxLength(128);
        builder.Property(x => x.Environment).HasMaxLength(64);
        builder.Property(x => x.TenantId).HasMaxLength(64);
        builder.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.HasIndex(x => new { x.Key, x.Environment, x.TenantId }).IsUnique();
        builder.HasIndex(x => new { x.ModuleId, x.Group });
    }
}
```

```csharp
using ICP.Platform.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.ConfigCenter.Persistence;

public sealed class ConfigCenterModelContributor : IPersistenceModelContributor
{
    public int Order => 100;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ConfigItemEntityConfiguration());
    }
}
```

在 `D:/Github/ICP-X/NewP/server/modules/ConfigCenter/Persistence/EfConfigRepository.cs` 写 EF 实现，保留现有查询语义：

```csharp
using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using ICP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.ConfigCenter.Persistence;

public sealed class EfConfigRepository : IConfigRepository
{
    private readonly PlatformDbContext _dbContext;

    public EfConfigRepository(PlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<ConfigItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _dbContext.Set<ConfigItem>().AsNoTracking().ToListAsync(cancellationToken);

    public Task<ConfigItem?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        _dbContext.Set<ConfigItem>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<ConfigItem?> GetByKeyAsync(string key, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Set<ConfigItem>()
            .AsNoTracking()
            .Where(x => x.Key == key)
            .OrderByDescending(x => x.Environment == environment && x.TenantId == tenantId)
            .ThenByDescending(x => x.Environment == environment && x.TenantId == null)
            .ThenByDescending(x => x.Environment == null && x.TenantId == tenantId)
            .ThenByDescending(x => x.Environment == null && x.TenantId == null)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<List<ConfigItem>> GetByModuleAsync(string moduleId, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default) =>
        _dbContext.Set<ConfigItem>()
            .AsNoTracking()
            .Where(x => x.ModuleId == moduleId)
            .Where(x => environment == null || x.Environment == null || x.Environment == environment)
            .Where(x => tenantId == null || x.TenantId == null || x.TenantId == tenantId)
            .ToListAsync(cancellationToken);

    public Task<List<ConfigItem>> GetByKeysAsync(IEnumerable<string> keys, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default) =>
        _dbContext.Set<ConfigItem>()
            .AsNoTracking()
            .Where(x => keys.Contains(x.Key))
            .Where(x => environment == null || x.Environment == null || x.Environment == environment)
            .Where(x => tenantId == null || x.TenantId == null || x.TenantId == tenantId)
            .ToListAsync(cancellationToken);

    public async Task CreateAsync(ConfigItem item, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<ConfigItem>().Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ConfigItem item, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<ConfigItem>().Update(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.Set<ConfigItem>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return;
        _dbContext.Set<ConfigItem>().Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: 改模块注册与项目引用**

在 `D:/Github/ICP-X/NewP/server/modules/ConfigCenter/ConfigCenter.csproj` 增加：

```xml
<ItemGroup>
  <ProjectReference Include="..\..\platform-persistence\PlatformPersistence.csproj" />
  <ProjectReference Include="..\..\sdk\Sdk.csproj" />
</ItemGroup>
```

在 `D:/Github/ICP-X/NewP/server/modules/ConfigCenter/ConfigCenterModule.cs` 中改为：

```csharp
services.AddSingleton<IPersistenceModelContributor, ConfigCenterModelContributor>();
services.AddScoped<IConfigRepository, EfConfigRepository>();
services.AddScoped<IConfigService, ConfigService>();
```

- [ ] **Step 5: 把 ConfigCenter 的服务测试切到可注入仓储**

在 `D:/Github/ICP-X/NewP/tests/ConfigCenter.Tests/ConfigServiceTests.cs` 中，把 `new ConfigRepository()` 替换为统一的仓储构建帮助方法。

```csharp
private static IConfigRepository CreateRepository()
{
    var options = new DbContextOptionsBuilder<PlatformDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    var db = new PlatformDbContext(options, new[] { new ConfigCenterModelContributor() });
    return new EfConfigRepository(db);
}
```

- [ ] **Step 6: 跑 ConfigCenter 测试**

Run:
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter EfConfigRepositoryTests -v minimal`
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -v minimal`

Expected:
- `EfConfigRepositoryTests` 通过
- 原有 `ConfigServiceTests` 继续通过

- [ ] **Step 7: Commit**

```bash
git add D:/Github/ICP-X/NewP/server/modules/ConfigCenter D:/Github/ICP-X/NewP/tests/ConfigCenter.Tests
git commit -m "feat: persist config center with ef core"
```

### Task 3: 落地 Authorization 的 EF 持久化实现

**Files:**
- Create: `D:/Github/ICP-X/NewP/server/modules/Authorization/Persistence/AuthorizationModelContributor.cs`
- Create: `D:/Github/ICP-X/NewP/server/modules/Authorization/Persistence/RoleEntityConfiguration.cs`
- Create: `D:/Github/ICP-X/NewP/server/modules/Authorization/Persistence/PermissionEntityConfiguration.cs`
- Create: `D:/Github/ICP-X/NewP/server/modules/Authorization/Persistence/RolePermissionEntityConfiguration.cs`
- Create: `D:/Github/ICP-X/NewP/server/modules/Authorization/Persistence/UserRoleEntityConfiguration.cs`
- Create: `D:/Github/ICP-X/NewP/server/modules/Authorization/Persistence/EfRoleRepository.cs`
- Create: `D:/Github/ICP-X/NewP/server/modules/Authorization/Persistence/EfPermissionRepository.cs`
- Create: `D:/Github/ICP-X/NewP/server/modules/Authorization/Persistence/EfRolePermissionRepository.cs`
- Create: `D:/Github/ICP-X/NewP/server/modules/Authorization/Persistence/EfUserRoleRepository.cs`
- Modify: `D:/Github/ICP-X/NewP/server/modules/Authorization/AuthorizationModule.cs`
- Modify: `D:/Github/ICP-X/NewP/server/modules/Authorization/AuthorizationModule.csproj`
- Modify: `D:/Github/ICP-X/NewP/tests/Authorization.Tests/AuthorizationServiceTests.cs`
- Modify: `D:/Github/ICP-X/NewP/tests/Authorization.Tests/RoleServiceTests.cs`
- Create: `D:/Github/ICP-X/NewP/tests/Authorization.Tests/EfAuthorizationRepositoryTests.cs`

- [ ] **Step 1: 先写 Authorization 仓储行为测试**

在 `D:/Github/ICP-X/NewP/tests/Authorization.Tests/EfAuthorizationRepositoryTests.cs` 写一个测试，覆盖用户角色关系查询。

```csharp
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Persistence;
using ICP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public class EfAuthorizationRepositoryTests
{
    [Fact]
    public async Task GetByUserIdAsync_ReturnsAssignedRoles()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(nameof(GetByUserIdAsync_ReturnsAssignedRoles))
            .Options;

        await using var db = new PlatformDbContext(options, new[] { new AuthorizationModelContributor() });
        var repository = new EfUserRoleRepository(db);

        await repository.CreateBatchAsync(new[]
        {
            new UserRole { UserId = "user-1", RoleId = "role-1" },
            new UserRole { UserId = "user-1", RoleId = "role-2" }
        });

        var roles = await repository.GetByUserIdAsync("user-1");
        Assert.Equal(2, roles.Count);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj --filter EfAuthorizationRepositoryTests -v minimal`
Expected: 编译失败，提示 `AuthorizationModelContributor` 与 EF 仓储尚不存在。

- [ ] **Step 3: 实现 Authorization 映射与仓储**

为四个模型分别写实体映射，固定表名、主键、长度和唯一约束：

```csharp
builder.ToTable("auth_roles");
builder.HasKey(x => x.Id);
builder.Property(x => x.Id).HasMaxLength(64);
builder.Property(x => x.Code).HasMaxLength(128).IsRequired();
builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
builder.HasIndex(x => x.Code).IsUnique();
```

```csharp
builder.ToTable("auth_permissions");
builder.HasKey(x => x.Id);
builder.Property(x => x.Code).HasMaxLength(256).IsRequired();
builder.Property(x => x.ModuleId).HasMaxLength(128).IsRequired();
builder.HasIndex(x => x.Code).IsUnique();
```

```csharp
builder.ToTable("auth_role_permissions");
builder.HasKey(x => x.Id);
builder.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();
```

```csharp
builder.ToTable("auth_user_roles");
builder.HasKey(x => x.Id);
builder.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();
```

统一注册：

```csharp
using ICP.Platform.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Persistence;

public sealed class AuthorizationModelContributor : IPersistenceModelContributor
{
    public int Order => 200;

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RoleEntityConfiguration());
        modelBuilder.ApplyConfiguration(new PermissionEntityConfiguration());
        modelBuilder.ApplyConfiguration(new RolePermissionEntityConfiguration());
        modelBuilder.ApplyConfiguration(new UserRoleEntityConfiguration());
    }
}
```

仓储实现保持现有接口不变，例如：

```csharp
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Persistence;

public sealed class EfRoleRepository : IRoleRepository
{
    private readonly PlatformDbContext _dbContext;

    public EfRoleRepository(PlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<Role>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _dbContext.Set<Role>().AsNoTracking().ToListAsync(cancellationToken);

    public Task<Role?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        _dbContext.Set<Role>().FirstOrDefaultAsync(x => x.Code == code, cancellationToken);

    public async Task CreateAsync(Role role, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<Role>().Add(role);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Role role, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<Role>().Update(role);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: 改模块注册与项目引用**

在 `D:/Github/ICP-X/NewP/server/modules/Authorization/AuthorizationModule.csproj` 中增加：

```xml
<ItemGroup>
  <ProjectReference Include="..\..\platform-persistence\PlatformPersistence.csproj" />
  <ProjectReference Include="..\..\sdk\Sdk.csproj" />
</ItemGroup>
```

在 `D:/Github/ICP-X/NewP/server/modules/Authorization/AuthorizationModule.cs` 中改为：

```csharp
services.AddSingleton<IPersistenceModelContributor, AuthorizationModelContributor>();
services.AddScoped<IRoleRepository, EfRoleRepository>();
services.AddScoped<IPermissionRepository, EfPermissionRepository>();
services.AddScoped<IRolePermissionRepository, EfRolePermissionRepository>();
services.AddScoped<IUserRoleRepository, EfUserRoleRepository>();
services.AddScoped<IAuthorizationService, AuthorizationService>();
services.AddScoped<IRoleService, RoleService>();
services.AddScoped<IPermissionService, PermissionService>();
```

- [ ] **Step 5: 调整 Authorization 测试以复用 EF 仓储**

在 `D:/Github/ICP-X/NewP/tests/Authorization.Tests/AuthorizationServiceTests.cs` 和 `D:/Github/ICP-X/NewP/tests/Authorization.Tests/RoleServiceTests.cs` 中，通过统一帮助方法创建基于 `PlatformDbContext` 的 EF 仓储。

```csharp
private static (IRoleRepository roles, IPermissionRepository permissions, IRolePermissionRepository rolePermissions, IUserRoleRepository userRoles) CreateRepositories()
{
    var options = new DbContextOptionsBuilder<PlatformDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    var db = new PlatformDbContext(options, new[] { new AuthorizationModelContributor() });
    return (
        new EfRoleRepository(db),
        new EfPermissionRepository(db),
        new EfRolePermissionRepository(db),
        new EfUserRoleRepository(db));
}
```

- [ ] **Step 6: 跑 Authorization 测试**

Run:
- `dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj --filter EfAuthorizationRepositoryTests -v minimal`
- `dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -v minimal`

Expected:
- 新增仓储行为测试通过
- 原有 `AuthorizationServiceTests`、`RoleServiceTests` 全部通过

- [ ] **Step 7: Commit**

```bash
git add D:/Github/ICP-X/NewP/server/modules/Authorization D:/Github/ICP-X/NewP/tests/Authorization.Tests
git commit -m "feat: persist authorization module with ef core"
```

### Task 4: 宿主接入 Provider 配置、迁移程序集与自动迁移

**Files:**
- Modify: `D:/Github/ICP-X/NewP/apps/host-api/HostApi.csproj`
- Modify: `D:/Github/ICP-X/NewP/apps/host-api/Program.cs`
- Modify: `D:/Github/ICP-X/NewP/apps/host-api/appsettings.json`
- Modify: `D:/Github/ICP-X/NewP/apps/host-api/appsettings.Development.json`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence.migrations.sqlserver/PlatformPersistence.SqlServerMigrations.csproj`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence.migrations.oracle/PlatformPersistence.OracleMigrations.csproj`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence.migrations.mysql/PlatformPersistence.MySqlMigrations.csproj`
- Create: `D:/Github/ICP-X/NewP/server/platform-persistence/Migrations/PersistenceMigrationRunner.cs`
- Modify: `D:/Github/ICP-X/NewP/ICP-Platform.sln`

- [ ] **Step 1: 先写宿主迁移执行测试**

在 `D:/Github/ICP-X/NewP/tests/Persistence.Tests/HostPersistenceBootstrapTests.cs` 写测试，验证宿主服务集合读取 `Persistence:Provider` 后可以解析迁移执行器。

```csharp
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public class HostPersistenceBootstrapTests
{
    [Fact]
    public void AddPlatformPersistence_RegistersMigrationRunner()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();

        services.AddPlatformPersistence(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<PersistenceMigrationRunner>());
    }
}
```

- [ ] **Step 2: 跑测试确认它失败**

Run: `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj --filter HostPersistenceBootstrapTests -v minimal`
Expected: 编译失败，提示 `PersistenceMigrationRunner` 不存在。

- [ ] **Step 3: 添加迁移项目和迁移执行器**

创建三个迁移程序集，统一引用 `PlatformPersistence.csproj`，例如：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>ICP.Platform.Persistence.SqlServerMigrations</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\platform-persistence\PlatformPersistence.csproj" />
  </ItemGroup>
</Project>
```

在 `D:/Github/ICP-X/NewP/server/platform-persistence/Migrations/PersistenceMigrationRunner.cs` 中实现：

```csharp
using Microsoft.Extensions.Options;

namespace ICP.Platform.Persistence;

public sealed class PersistenceMigrationRunner
{
    private readonly PlatformDbContext _dbContext;
    private readonly PersistenceOptions _options;

    public PersistenceMigrationRunner(PlatformDbContext dbContext, IOptions<PersistenceOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        _options.AutoMigrate ? _dbContext.Database.MigrateAsync(cancellationToken) : Task.CompletedTask;
}
```

- [ ] **Step 4: 改宿主配置与启动逻辑**

在 `D:/Github/ICP-X/NewP/apps/host-api/appsettings.json` 中改为显式三库配置：

```json
"ConnectionStrings": {
  "SqlServer": "Server=localhost;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;",
  "Oracle": "User Id=icp;Password=REDACTED_FOR_OPEN_SOURCE;Data Source=localhost/FREEPDB1;",
  "MySql": "Server=localhost;Database=icp_platform;User=root;Password=REDACTED_FOR_OPEN_SOURCE;"
},
"Persistence": {
  "Provider": "SqlServer",
  "AutoMigrate": true,
  "AutoSeed": true
}
```

在 `D:/Github/ICP-X/NewP/apps/host-api/Program.cs` 中加入持久化注册与迁移：

```csharp
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Extensions;

builder.Services.AddPlatformPersistence(builder.Configuration);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var migrationRunner = scope.ServiceProvider.GetRequiredService<PersistenceMigrationRunner>();
    await migrationRunner.MigrateAsync();
}
```

- [ ] **Step 5: 生成第一版三库迁移**

Run:
- `dotnet ef migrations add InitialSqlServer --project .\server\platform-persistence.migrations.sqlserver\PlatformPersistence.SqlServerMigrations.csproj --startup-project .\apps\host-api\HostApi.csproj --context ICP.Platform.Persistence.PlatformDbContext --output-dir Migrations`
- `dotnet ef migrations add InitialOracle --project .\server\platform-persistence.migrations.oracle\PlatformPersistence.OracleMigrations.csproj --startup-project .\apps\host-api\HostApi.csproj --context ICP.Platform.Persistence.PlatformDbContext --output-dir Migrations`
- `dotnet ef migrations add InitialMySql --project .\server\platform-persistence.migrations.mysql\PlatformPersistence.MySqlMigrations.csproj --startup-project .\apps\host-api\HostApi.csproj --context ICP.Platform.Persistence.PlatformDbContext --output-dir Migrations`

Expected:
- 三个迁移命令都成功
- 各自迁移目录包含 `Initial*` migration 文件和 model snapshot

- [ ] **Step 6: 跑宿主与持久化测试**

Run:
- `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj -v minimal`
- `dotnet build .\apps\host-api\HostApi.csproj`

Expected:
- `Persistence.Tests` 全部通过
- `HostApi` 构建成功

- [ ] **Step 7: Commit**

```bash
git add D:/Github/ICP-X/NewP/apps/host-api D:/Github/ICP-X/NewP/server/platform-persistence* D:/Github/ICP-X/NewP/tests/Persistence.Tests D:/Github/ICP-X/NewP/ICP-Platform.sln
git commit -m "feat: wire host persistence bootstrap and provider migrations"
```

### Task 5: 增加三数据库 smoke 与集成验证

**Files:**
- Create: `D:/Github/ICP-X/NewP/tests/integration/persistence/Invoke-PersistenceSmoke.ps1`
- Create: `D:/Github/ICP-X/NewP/tests/integration/persistence/Run-SqlServerSmoke.ps1`
- Create: `D:/Github/ICP-X/NewP/tests/integration/persistence/Run-OracleSmoke.ps1`
- Create: `D:/Github/ICP-X/NewP/tests/integration/persistence/Run-MySqlSmoke.ps1`
- Create: `D:/Github/ICP-X/NewP/tests/Persistence.Tests/SmokeConfigurationTests.cs`
- Modify: `D:/Github/ICP-X/NewP/tests/README.md`

- [ ] **Step 1: 先写 smoke 配置测试**

在 `D:/Github/ICP-X/NewP/tests/Persistence.Tests/SmokeConfigurationTests.cs` 写测试，验证三个 smoke 脚本都存在且目标 Provider 值正确。

```csharp
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public class SmokeConfigurationTests
{
    [Theory]
    [InlineData("D:/Github/ICP-X/NewP/tests/integration/persistence/Run-SqlServerSmoke.ps1", "SqlServer")]
    [InlineData("D:/Github/ICP-X/NewP/tests/integration/persistence/Run-OracleSmoke.ps1", "Oracle")]
    [InlineData("D:/Github/ICP-X/NewP/tests/integration/persistence/Run-MySqlSmoke.ps1", "MySql")]
    public void SmokeScripts_Exist(string path, string expectedProvider)
    {
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains(expectedProvider, content);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj --filter SmokeConfigurationTests -v minimal`
Expected: 失败，提示 smoke 脚本不存在。

- [ ] **Step 3: 实现统一 smoke 脚本入口**

在 `D:/Github/ICP-X/NewP/tests/integration/persistence/Invoke-PersistenceSmoke.ps1` 中写统一执行逻辑：

```powershell
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet("SqlServer", "Oracle", "MySql")]
  [string]$Provider
)

$env:Persistence__Provider = $Provider
dotnet build "D:\Github\ICP-X\NewP\apps\host-api\HostApi.csproj"
if ($LASTEXITCODE -ne 0) { throw "Build failed for $Provider" }

dotnet test "D:\Github\ICP-X\NewP\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj" -v minimal
if ($LASTEXITCODE -ne 0) { throw "ConfigCenter tests failed for $Provider" }

dotnet test "D:\Github\ICP-X\NewP\tests\Authorization.Tests\Authorization.Tests.csproj" -v minimal
if ($LASTEXITCODE -ne 0) { throw "Authorization tests failed for $Provider" }
```

三个脚本分别只传不同的 Provider：

```powershell
& "D:\Github\ICP-X\NewP\tests\integration\persistence\Invoke-PersistenceSmoke.ps1" -Provider "SqlServer"
```

- [ ] **Step 4: 跑 smoke 配置测试与至少一个 smoke**

Run:
- `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj --filter SmokeConfigurationTests -v minimal`
- `powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-SqlServerSmoke.ps1`

Expected:
- `SmokeConfigurationTests` 通过
- `Run-SqlServerSmoke.ps1` 成功返回

- [ ] **Step 5: 记录三个 Provider 的手工执行口径**

在 `D:/Github/ICP-X/NewP/tests/README.md` 增加：

```md
## Persistence Smoke

- SQL Server: `powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-SqlServerSmoke.ps1`
- Oracle: `powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-OracleSmoke.ps1`
- MySql: `powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-MySqlSmoke.ps1`
```

- [ ] **Step 6: Commit**

```bash
git add D:/Github/ICP-X/NewP/tests/Persistence.Tests D:/Github/ICP-X/NewP/tests/integration/persistence D:/Github/ICP-X/NewP/tests/README.md
git commit -m "test: add multi-db persistence smoke scripts"
```

### Task 6: 更新模块说明、验证记录与当前状态

**Files:**
- Modify: `D:/Github/ICP-X/NewP/docs/modules/configcenter-implementation.md`
- Modify: `D:/Github/ICP-X/NewP/docs/modules/authorization-implementation.md`
- Modify: `D:/Github/ICP-X/NewP/docs/verification/configcenter-module-verification.md`
- Modify: `D:/Github/ICP-X/NewP/docs/verification/authorization-module-verification.md`
- Modify: `D:/Github/ICP-X/NewP/docs/current-status.md`

- [ ] **Step 1: 更新模块实现说明**

在 `configcenter-implementation.md` 和 `authorization-implementation.md` 中把“内存仓储”改为“EF Core + 多 Provider 持久化，业务接口不变，底层可受控使用原生 SQL”，并补上三数据库切换说明。

- [ ] **Step 2: 更新验证文档**

在两个 verification 文档中补齐以下结果：

```md
- SQL Server 迁移通过
- Oracle 迁移通过
- MySql 迁移通过
- Host 自动迁移与 seed 通过
- ConfigCenter / Authorization 模块测试通过
- 三库 smoke 脚本可执行
```

- [ ] **Step 3: 更新当前状态文档**

在 `D:/Github/ICP-X/NewP/docs/current-status.md` 中新增或更新：

```md
### Phase 6.5：持久化底座收口

状态：已完成

已落地：
- ConfigCenter 真实数据库持久化
- Authorization 真实数据库持久化
- SQL Server / Oracle / MySql 配置切换
- 三套迁移集与 smoke 验证
```

- [ ] **Step 4: 跑最终验证命令**

Run:
- `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj -v minimal`
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -v minimal`
- `dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -v minimal`
- `dotnet build .\apps\host-api\HostApi.csproj`

Expected:
- 全部通过

- [ ] **Step 5: Commit**

```bash
git add D:/Github/ICP-X/NewP/docs/modules D:/Github/ICP-X/NewP/docs/verification D:/Github/ICP-X/NewP/docs/current-status.md
git commit -m "docs: close out phase 6.5 persistence"
```

## Self-Review Checklist

- [ ] 计划中的每个新目录都已经在文件清单中出现，没有“实现时再决定目录”的隐含步骤。
- [ ] `ConfigCenter` 和 `Authorization` 都覆盖了模型映射、仓储实现、模块注册、现有测试切换与新增仓储测试。
- [ ] 宿主启动、Provider 配置、迁移程序集、自动迁移和 seed 都有明确任务承接。
- [ ] 三数据库 smoke 与最终文档收口都有独立任务，不会漏掉验证和状态同步。
- [ ] 计划中没有要求业务层按数据库类型分支，也没有把原生 SQL 暴露给业务服务层。

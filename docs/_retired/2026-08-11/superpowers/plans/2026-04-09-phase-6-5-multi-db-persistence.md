# Phase 6.5 多数据库持久化（混合架构） Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 `ConfigCenter` 和 `Authorization` 落地真实数据库持久化，同时把数据库结构语义从具体 ORM 实现中抽离出来，形成“代码内核 + 强约束模型语义 + EF Core 第一批适配器”的多数据库持久化底座。

**Architecture:** 本轮先建立 `platform-modeling` 与 `platform-persistence` 两个基础层，分别承载模型语义和 Provider 无关持久化内核；再由 `platform-persistence-efcore` 把模型语义投影到 `DbContext`、迁移和三数据库 Provider 上；最后让 `ConfigCenter` 与 `Authorization` 通过模型定义 + EF 仓储完成首批真实落库与验证闭环。

**Tech Stack:** .NET 10、ASP.NET Core、EF Core 10、Microsoft.EntityFrameworkCore.SqlServer 10.0.1、Oracle.EntityFrameworkCore 10.23.26100、MySql.EntityFrameworkCore 10.0.1、xUnit、PowerShell

**Metadata Workflow:** 当前阶段不做给人使用的元数据设计器，模型语义定义默认由 AI 生成或修改，人负责审核，校验器与测试负责兜底；未来如果建设设计器，设计器也必须输出同一套模型语义契约。

---

### Task 1: 建立模型语义底座

**Files:**
- Create: `server/platform-modeling/PlatformModeling.csproj`
- Create: `server/platform-modeling/ModelKind.cs`
- Create: `server/platform-modeling/FieldKind.cs`
- Create: `server/platform-modeling/FieldValueType.cs`
- Create: `server/platform-modeling/Definitions/ModelDefinition.cs`
- Create: `server/platform-modeling/Definitions/FieldDefinition.cs`
- Create: `server/platform-modeling/Definitions/IndexDefinition.cs`
- Create: `server/platform-modeling/Definitions/IModelDefinitionProvider.cs`
- Create: `server/platform-modeling/Validation/ModelDefinitionValidator.cs`
- Create: `tests/Modeling.Tests/Modeling.Tests.csproj`
- Create: `tests/Modeling.Tests/ModelDefinitionValidatorTests.cs`
- Modify: `ICP-Platform.sln`

- [ ] **Step 1: 先写模型定义校验失败测试**

```csharp
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Validation;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelDefinitionValidatorTests
{
    [Fact]
    public void Validate_ThrowsWhenFieldNamesDuplicate()
    {
        var definition = new ModelDefinition(
            "ConfigItem",
            "config_items",
            ModelKind.Entity,
            new[]
            {
                new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.Text, "string", true, true, false),
                new FieldDefinition("Id", "config_id", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false)
            },
            Array.Empty<IndexDefinition>());

        var validator = new ModelDefinitionValidator();

        var ex = Assert.Throws<InvalidOperationException>(() => validator.Validate(definition));
        Assert.Contains("duplicate field", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: 跑测试确认它先失败**

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter ModelDefinitionValidatorTests -v minimal`

Expected: 编译失败，提示 `ICP.Platform.Modeling`、`ModelDefinition`、`ModelDefinitionValidator` 等类型不存在。

- [ ] **Step 3: 创建模型语义项目与核心类型**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>ICP.Platform.Modeling</RootNamespace>
  </PropertyGroup>
</Project>
```

```csharp
namespace ICP.Platform.Modeling;

public enum ModelKind
{
    Entity,
    Link
}
```

```csharp
namespace ICP.Platform.Modeling;

public enum FieldKind
{
    Scalar,
    Reference,
    Audit
}
```

```csharp
namespace ICP.Platform.Modeling;

public enum FieldValueType
{
    Text,
    Description,
    Boolean,
    DateTime,
    ObjectId,
    Enum,
    User,
    Group
}
```

```csharp
namespace ICP.Platform.Modeling.Definitions;

public sealed record FieldDefinition(
    string Name,
    string StoreName,
    FieldKind Kind,
    FieldValueType ValueType,
    string StoreType,
    bool Required,
    bool IsPrimaryKey,
    bool IsAuditField);
```

```csharp
namespace ICP.Platform.Modeling.Definitions;

public sealed record IndexDefinition(
    string Name,
    IReadOnlyList<string> Fields,
    bool IsUnique);
```

```csharp
namespace ICP.Platform.Modeling.Definitions;

public sealed record ModelDefinition(
    string Name,
    string TableName,
    ModelKind Kind,
    IReadOnlyList<FieldDefinition> Fields,
    IReadOnlyList<IndexDefinition> Indexes);
```

```csharp
namespace ICP.Platform.Modeling.Definitions;

public interface IModelDefinitionProvider
{
    IReadOnlyList<ModelDefinition> GetDefinitions();
}
```

- [ ] **Step 4: 实现模型定义校验器**

```csharp
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modeling.Validation;

public sealed class ModelDefinitionValidator
{
    public void Validate(ModelDefinition definition)
    {
        var duplicateField = definition.Fields
            .GroupBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static x => x.Count() > 1);

        if (duplicateField is not null)
        {
            throw new InvalidOperationException($"Model '{definition.Name}' has duplicate field '{duplicateField.Key}'.");
        }

        if (!definition.Fields.Any(static x => x.IsPrimaryKey))
        {
            throw new InvalidOperationException($"Model '{definition.Name}' must define at least one primary key field.");
        }
    }
}
```

- [ ] **Step 5: 加入解决方案并回跑测试**

Run:
- `dotnet sln .\ICP-Platform.sln add .\server\platform-modeling\PlatformModeling.csproj`
- `dotnet sln .\ICP-Platform.sln add .\tests\Modeling.Tests\Modeling.Tests.csproj`
- `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter ModelDefinitionValidatorTests -v minimal`

Expected:
- `Project added to the solution`
- `ModelDefinitionValidatorTests` 通过

- [ ] **Step 6: Commit**

```bash
git add server/platform-modeling tests/Modeling.Tests ICP-Platform.sln
git commit -m "feat: add platform modeling foundation"
```

- [ ] **Step 7: 补充 AI 生成元数据的工作约束**

在 `server/platform-modeling/README.md` 或等效文档中补充以下约束：

```md
## Metadata Authoring Workflow

- 当前阶段不提供人工设计器
- 模型语义定义默认由 AI 生成或修改
- 所有生成结果必须进入版本库
- 所有生成结果必须通过 ModelDefinitionValidator 和相关模块测试
- 后续若建设设计器，设计器输出仍必须对齐当前模型语义契约
```

Run: `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -v minimal`
Expected: `Modeling.Tests` 继续通过

- [ ] **Step 8: Commit**

```bash
git add server/platform-modeling
git commit -m "docs: define ai-assisted metadata authoring workflow"
```

### Task 2: 建立 Provider 无关的持久化内核

**Files:**
- Create: `server/platform-persistence/PlatformPersistence.csproj`
- Create: `server/platform-persistence/PersistenceProvider.cs`
- Create: `server/platform-persistence/PersistenceAdapterKind.cs`
- Create: `server/platform-persistence/PersistenceOptions.cs`
- Create: `server/platform-persistence/Abstractions/ISqlDialect.cs`
- Create: `server/platform-persistence/Abstractions/ISqlQueryExecutor.cs`
- Create: `server/platform-persistence/Abstractions/IPersistenceMigrationRunner.cs`
- Create: `server/platform-persistence/Extensions/PersistenceOptionValidation.cs`
- Create: `tests/Persistence.Tests/Persistence.Tests.csproj`
- Create: `tests/Persistence.Tests/PersistenceOptionValidationTests.cs`
- Modify: `ICP-Platform.sln`

- [ ] **Step 1: 先写配置校验测试**

```csharp
using ICP.Platform.Persistence;
using ICP.Platform.Persistence.Extensions;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public class PersistenceOptionValidationTests
{
    [Fact]
    public void Validate_ThrowsWhenAdapterMissing()
    {
        var options = new PersistenceOptions
        {
            Provider = PersistenceProvider.SqlServer,
            Adapter = null
        };

        var ex = Assert.Throws<InvalidOperationException>(() => PersistenceOptionValidation.Validate(options));
        Assert.Contains("Adapter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj --filter PersistenceOptionValidationTests -v minimal`

Expected: 编译失败，提示 `PersistenceOptions`、`PersistenceOptionValidation` 等类型不存在。

- [ ] **Step 3: 创建持久化内核项目与配置类型**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>ICP.Platform.Persistence</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\platform-modeling\PlatformModeling.csproj" />
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

public enum PersistenceAdapterKind
{
    EfCore
}
```

```csharp
namespace ICP.Platform.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public PersistenceProvider Provider { get; set; } = PersistenceProvider.SqlServer;
    public PersistenceAdapterKind? Adapter { get; set; } = PersistenceAdapterKind.EfCore;
    public bool AutoMigrate { get; set; } = true;
    public bool AutoSeed { get; set; } = true;
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
namespace ICP.Platform.Persistence.Abstractions;

public interface IPersistenceMigrationRunner
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: 实现配置校验入口**

```csharp
namespace ICP.Platform.Persistence.Extensions;

public static class PersistenceOptionValidation
{
    public static void Validate(PersistenceOptions options)
    {
        if (options.Adapter is null)
        {
            throw new InvalidOperationException("Persistence Adapter must be configured.");
        }
    }
}
```

- [ ] **Step 5: 加入解决方案并回跑测试**

Run:
- `dotnet sln .\ICP-Platform.sln add .\server\platform-persistence\PlatformPersistence.csproj`
- `dotnet sln .\ICP-Platform.sln add .\tests\Persistence.Tests\Persistence.Tests.csproj`
- `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj --filter PersistenceOptionValidationTests -v minimal`

Expected:
- `Project added to the solution`
- `PersistenceOptionValidationTests` 通过

- [ ] **Step 6: Commit**

```bash
git add server/platform-persistence tests/Persistence.Tests ICP-Platform.sln
git commit -m "feat: add provider-agnostic persistence kernel"
```

### Task 3: 建立 EF Core 第一批适配器

**Files:**
- Create: `server/platform-persistence-efcore/PlatformPersistence.EfCore.csproj`
- Create: `server/platform-persistence-efcore/EfPlatformDbContext.cs`
- Create: `server/platform-persistence-efcore/Projection/EfModelProjector.cs`
- Create: `server/platform-persistence-efcore/Sql/SqlServerDialect.cs`
- Create: `server/platform-persistence-efcore/Sql/OracleDialect.cs`
- Create: `server/platform-persistence-efcore/Sql/MySqlDialect.cs`
- Create: `server/platform-persistence-efcore/Sql/EfSqlQueryExecutor.cs`
- Create: `server/platform-persistence-efcore/PersistenceMigrationRunner.cs`
- Create: `server/platform-persistence-efcore/Extensions/EfCorePersistenceServiceCollectionExtensions.cs`
- Create: `tests/Persistence.Tests/EfModelProjectionTests.cs`
- Modify: `ICP-Platform.sln`

- [ ] **Step 1: 先写模型投影测试**

```csharp
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public class EfModelProjectionTests
{
    [Fact]
    public void OnModelCreating_ProjectsTableAndPrimaryKey()
    {
        var definitions = new[]
        {
            new ModelDefinition(
                "ConfigItem",
                "config_items",
                ModelKind.Entity,
                new[]
                {
                    new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.Text, "string", true, true, false),
                    new FieldDefinition("Key", "config_key", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false)
                },
                Array.Empty<IndexDefinition>())
        };

        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(nameof(OnModelCreating_ProjectsTableAndPrimaryKey))
            .Options;

        using var db = new EfPlatformDbContext(options, definitions);
        var entityType = db.Model.FindEntityType("ConfigItem");

        Assert.NotNull(entityType);
        Assert.Equal("config_items", entityType!.GetTableName());
        Assert.NotNull(entityType.FindPrimaryKey());
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj --filter EfModelProjectionTests -v minimal`

Expected: 编译失败，提示 `ICP.Platform.Persistence.EfCore`、`EfPlatformDbContext` 等类型不存在。

- [ ] **Step 3: 创建 EF Core 适配器项目**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>ICP.Platform.Persistence.EfCore</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\platform-modeling\PlatformModeling.csproj" />
    <ProjectReference Include="..\platform-persistence\PlatformPersistence.csproj" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.1" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.1" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.1" />
    <PackageReference Include="Oracle.EntityFrameworkCore" Version="10.23.26100" />
    <PackageReference Include="MySql.EntityFrameworkCore" Version="10.0.1" />
  </ItemGroup>
</Project>
```

```csharp
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore.Projection;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Persistence.EfCore;

public sealed class EfPlatformDbContext : DbContext
{
    private readonly IReadOnlyList<ModelDefinition> _definitions;

    public EfPlatformDbContext(DbContextOptions<EfPlatformDbContext> options, IReadOnlyList<ModelDefinition> definitions)
        : base(options)
    {
        _definitions = definitions;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var projector = new EfModelProjector();
        projector.Project(modelBuilder, _definitions);
        base.OnModelCreating(modelBuilder);
    }
}
```

```csharp
using ICP.Platform.Modeling.Definitions;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Persistence.EfCore.Projection;

public sealed class EfModelProjector
{
    public void Project(ModelBuilder modelBuilder, IReadOnlyList<ModelDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            var entity = modelBuilder.SharedTypeEntity(definition.Name, typeof(Dictionary<string, object>));
            entity.ToTable(definition.TableName);

            foreach (var field in definition.Fields)
            {
                entity.IndexerProperty<string>(field.Name).HasColumnName(field.StoreName);
            }

            var primaryKeys = definition.Fields.Where(static x => x.IsPrimaryKey).Select(static x => x.Name).ToArray();
            entity.HasKey(primaryKeys);
        }
    }
}
```

- [ ] **Step 4: 补齐 SQL 方言、查询执行器与迁移执行器**

```csharp
using ICP.Platform.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Persistence.EfCore;

public sealed class PersistenceMigrationRunner : IPersistenceMigrationRunner
{
    private readonly EfPlatformDbContext _dbContext;
    private readonly PersistenceOptions _options;

    public PersistenceMigrationRunner(EfPlatformDbContext dbContext, IOptions<PersistenceOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        _options.AutoMigrate ? _dbContext.Database.MigrateAsync(cancellationToken) : Task.CompletedTask;
}
```

```csharp
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ICP.Platform.Persistence.EfCore.Extensions;

public static class EfCorePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddEfCorePersistence(
        this IServiceCollection services,
        IConfiguration configuration,
        IReadOnlyList<ModelDefinition> definitions)
    {
        services.Configure<PersistenceOptions>(configuration.GetSection(PersistenceOptions.SectionName));
        services.AddSingleton(definitions);

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

        services.AddDbContext<EfPlatformDbContext>((sp, builder) =>
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

        services.AddScoped<IPersistenceMigrationRunner, PersistenceMigrationRunner>();
        services.AddScoped<ISqlQueryExecutor, EfSqlQueryExecutor>();
        return services;
    }
}
```

- [ ] **Step 5: 加入解决方案并回跑投影测试**

Run:
- `dotnet sln .\ICP-Platform.sln add .\server\platform-persistence-efcore\PlatformPersistence.EfCore.csproj`
- `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj --filter EfModelProjectionTests -v minimal`

Expected:
- `Project added to the solution`
- `EfModelProjectionTests` 通过

- [ ] **Step 6: Commit**

```bash
git add server/platform-persistence-efcore tests/Persistence.Tests ICP-Platform.sln
git commit -m "feat: add ef core persistence adapter"
```

### Task 4: 用模型语义定义 ConfigCenter 与 Authorization

**Files:**
- Create: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelDefinitions.cs`
- Create: `server/modules/Authorization/Persistence/Descriptors/AuthorizationModelDefinitions.cs`
- Create: `tests/ConfigCenter.Tests/ConfigCenterModelDefinitionTests.cs`
- Create: `tests/Authorization.Tests/AuthorizationModelDefinitionTests.cs`
- Modify: `server/modules/ConfigCenter/ConfigCenter.csproj`
- Modify: `server/modules/Authorization/AuthorizationModule.csproj`

- [ ] **Step 1: 先写模块模型定义测试**

```csharp
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class ConfigCenterModelDefinitionTests
{
    [Fact]
    public void ConfigItem_DefinitionContainsPrimaryKeyAndUniqueIndex()
    {
        var definition = ConfigCenterModelDefinitions.ConfigItem;

        Assert.Contains(definition.Fields, static x => x.Name == "Id" && x.IsPrimaryKey);
        Assert.Contains(definition.Indexes, static x => x.Name == "UX_ConfigItem_Key_Environment_Tenant" && x.IsUnique);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run:
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter ConfigCenterModelDefinitionTests -v minimal`
- `dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj --filter AuthorizationModelDefinitionTests -v minimal`

Expected: 编译失败，提示两个模块的模型定义类型不存在。

- [ ] **Step 3: 编写 ConfigCenter 模型语义定义**

```csharp
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;

public static class ConfigCenterModelDefinitions
{
    public static readonly ModelDefinition ConfigItem = new(
        "ConfigItem",
        "config_items",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("Key", "config_key", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Value", "config_value", FieldKind.Scalar, FieldValueType.Description, "string", false, false, false),
            new FieldDefinition("Type", "config_type", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("ModuleId", "module_id", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Environment", "environment", FieldKind.Scalar, FieldValueType.Text, "string", false, false, false),
            new FieldDefinition("TenantId", "tenant_id", FieldKind.Scalar, FieldValueType.Text, "string", false, false, false)
        },
        new[]
        {
            new IndexDefinition("UX_ConfigItem_Key_Environment_Tenant", new[] { "Key", "Environment", "TenantId" }, true),
            new IndexDefinition("IX_ConfigItem_Module", new[] { "ModuleId" }, false)
        });
}
```

- [ ] **Step 4: 编写 Authorization 模型语义定义**

```csharp
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Modules.Authorization.Persistence.Descriptors;

public static class AuthorizationModelDefinitions
{
    public static readonly ModelDefinition Role = new(
        "Role",
        "auth_roles",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("Code", "code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("Name", "name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false)
        },
        new[]
        {
            new IndexDefinition("UX_Role_Code", new[] { "Code" }, true)
        });

    public static readonly ModelDefinition UserRole = new(
        "UserRole",
        "auth_user_roles",
        ModelKind.Link,
        new[]
        {
            new FieldDefinition("Id", "id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("UserId", "user_id", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("RoleId", "role_id", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false)
        },
        new[]
        {
            new IndexDefinition("UX_UserRole_User_Role", new[] { "UserId", "RoleId" }, true)
        });
}
```

- [ ] **Step 5: 给模块项目增加基础引用并回跑测试**

```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\platform-modeling\PlatformModeling.csproj" />
  <ProjectReference Include="..\..\..\platform-persistence\PlatformPersistence.csproj" />
</ItemGroup>
```

Run:
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter ConfigCenterModelDefinitionTests -v minimal`
- `dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj --filter AuthorizationModelDefinitionTests -v minimal`

Expected:
- 新增模型定义测试通过

- [ ] **Step 6: Commit**

```bash
git add server/modules/ConfigCenter server/modules/Authorization tests/ConfigCenter.Tests tests/Authorization.Tests
git commit -m "feat: add module model definitions for persistence"
```

### Task 4.5: 安装本地多数据库容器

**Checkpoint:** 完成 `Task 4` 后、进入 `Task 5` 和 `Task 6` 前，先准备本地数据库环境。

**Docs:**
- `docs/guides/local-multi-db-container-setup.md`

- [ ] **Step 1: 安装并验证 Docker Desktop**

Run:
- `docker version`

Expected:
- Docker Client / Server 信息都能正常返回

- [ ] **Step 2: 按推荐顺序启动三库容器**

顺序：
- `MySQL`
- `SQL Server`
- `Oracle`

Expected:
- `docker ps` 可看到 `icp-mysql`、`icp-sqlserver`、`icp-oracle`

- [ ] **Step 3: 记录本地连接串并验证端口**

至少确认：
- `localhost:3306`
- `localhost:1433`
- `localhost:1521`

Expected:
- 三个端口均可用
- 可将连接串写入后续 `appsettings` 或环境变量

- [ ] **Step 4: Commit**

```bash
git add docs/guides/local-multi-db-container-setup.md docs/superpowers/plans/2026-04-09-phase-6-5-multi-db-persistence.md
git commit -m "docs: add local multi-db container setup guide"
```

### Task 5: 让模块通过 EF 适配器落地真实仓储

**Files:**
- Create: `server/modules/ConfigCenter/Persistence/Ef/EfConfigRepository.cs`
- Create: `server/modules/Authorization/Persistence/Ef/EfRoleRepository.cs`
- Create: `server/modules/Authorization/Persistence/Ef/EfPermissionRepository.cs`
- Create: `server/modules/Authorization/Persistence/Ef/EfRolePermissionRepository.cs`
- Create: `server/modules/Authorization/Persistence/Ef/EfUserRoleRepository.cs`
- Modify: `server/modules/ConfigCenter/ConfigCenterModule.cs`
- Modify: `server/modules/Authorization/AuthorizationModule.cs`
- Create: `tests/ConfigCenter.Tests/EfConfigRepositoryTests.cs`
- Create: `tests/Authorization.Tests/EfAuthorizationRepositoryTests.cs`

- [ ] **Step 1: 先写 EF 仓储行为测试**

```csharp
using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Persistence.Ef;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class EfConfigRepositoryTests
{
    [Fact]
    public async Task GetByKeyAsync_ReturnsTenantAndEnvironmentMatchFirst()
    {
        var repository = CreateConfigRepository();

        await repository.CreateAsync(new ConfigItem { Key = "Test.Key", Value = "global", Type = "String", ModuleId = "Test", DisplayName = "Global" });
        await repository.CreateAsync(new ConfigItem { Key = "Test.Key", Value = "tenant-prod", Type = "String", ModuleId = "Test", DisplayName = "Specific", Environment = "Prod", TenantId = "tenant-a" });

        var item = await repository.GetByKeyAsync("Test.Key", "Prod", "tenant-a");

        Assert.NotNull(item);
        Assert.Equal("tenant-prod", item!.Value);
    }

    private static EfConfigRepository CreateConfigRepository()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var dbContext = new EfPlatformDbContext(options, new[] { ConfigCenterModelDefinitions.ConfigItem });
        return new EfConfigRepository(dbContext);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run:
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter EfConfigRepositoryTests -v minimal`
- `dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj --filter EfAuthorizationRepositoryTests -v minimal`

Expected: 编译失败，提示 EF 仓储实现与测试工厂不存在。

- [ ] **Step 3: 编写 ConfigCenter EF 仓储**

```csharp
using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.ConfigCenter.Persistence.Ef;

public sealed class EfConfigRepository : IConfigRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfConfigRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<ConfigItem>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _dbContext.Set<ConfigItem>().AsNoTracking().ToListAsync(cancellationToken);

    public Task<ConfigItem?> GetByKeyAsync(string key, string? environment = null, string? tenantId = null, CancellationToken cancellationToken = default) =>
        _dbContext.Set<ConfigItem>()
            .AsNoTracking()
            .Where(x => x.Key == key)
            .OrderByDescending(x => x.Environment == environment && x.TenantId == tenantId)
            .ThenByDescending(x => x.Environment == environment && x.TenantId == null)
            .ThenByDescending(x => x.Environment == null && x.TenantId == tenantId)
            .ThenByDescending(x => x.Environment == null && x.TenantId == null)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task CreateAsync(ConfigItem item, CancellationToken cancellationToken = default)
    {
        _dbContext.Set<ConfigItem>().Add(item);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: 编写 Authorization EF 仓储并替换模块注册**

```csharp
services.AddScoped<IRoleRepository, EfRoleRepository>();
services.AddScoped<IPermissionRepository, EfPermissionRepository>();
services.AddScoped<IRolePermissionRepository, EfRolePermissionRepository>();
services.AddScoped<IUserRoleRepository, EfUserRoleRepository>();
```

```csharp
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Modules.Authorization.Persistence.Ef;

public sealed class EfUserRoleRepository : IUserRoleRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfUserRoleRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<UserRole>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
        _dbContext.Set<UserRole>()
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
}
```

- [ ] **Step 5: 回跑模块测试**

Run:
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -v minimal`
- `dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -v minimal`

Expected:
- 新增 EF 仓储测试通过
- 现有服务测试继续通过

- [ ] **Step 6: Commit**

```bash
git add server/modules/ConfigCenter server/modules/Authorization tests/ConfigCenter.Tests tests/Authorization.Tests
git commit -m "feat: adapt configcenter and authorization to ef persistence"
```

### Task 6: 宿主接入、迁移程序集与三库验证

**Files:**
- Modify: `apps/host-api/Program.cs`
- Modify: `apps/host-api/appsettings.json`
- Modify: `apps/host-api/appsettings.Development.json`
- Create: `server/platform-persistence-efcore.migrations.sqlserver/PlatformPersistence.EfCore.SqlServerMigrations.csproj`
- Create: `server/platform-persistence-efcore.migrations.oracle/PlatformPersistence.EfCore.OracleMigrations.csproj`
- Create: `server/platform-persistence-efcore.migrations.mysql/PlatformPersistence.EfCore.MySqlMigrations.csproj`
- Create: `tests/Persistence.Tests/HostPersistenceBootstrapTests.cs`
- Create: `tests/integration/persistence/Invoke-PersistenceSmoke.ps1`
- Create: `tests/integration/persistence/Run-SqlServerSmoke.ps1`
- Create: `tests/integration/persistence/Run-OracleSmoke.ps1`
- Create: `tests/integration/persistence/Run-MySqlSmoke.ps1`
- Modify: `tests/README.md`
- Modify: `docs/current-status.md`

- [ ] **Step 1: 先写宿主引导测试**

```csharp
using ICP.Platform.Persistence.Abstractions;
using ICP.Platform.Persistence.EfCore.Extensions;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public class HostPersistenceBootstrapTests
{
    [Fact]
    public void Host_ResolvesMigrationRunner()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Adapter"] = "EfCore",
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();

        ConfigurePersistence(services, configuration);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IPersistenceMigrationRunner>());
    }

    private static void ConfigurePersistence(IServiceCollection services, IConfiguration configuration)
    {
        services.AddEfCorePersistence(
            configuration,
            new[]
            {
                ConfigCenterModelDefinitions.ConfigItem,
                AuthorizationModelDefinitions.Role,
                AuthorizationModelDefinitions.UserRole
            });
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj --filter HostPersistenceBootstrapTests -v minimal`

Expected: 编译失败，提示宿主引导或迁移执行器尚未接入。

- [ ] **Step 3: 接入宿主配置与迁移程序集**

```json
"ConnectionStrings": {
  "SqlServer": "Server=localhost;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;",
  "Oracle": "User Id=icp;Password=REDACTED_FOR_OPEN_SOURCE;Data Source=localhost/FREEPDB1;",
  "MySql": "Server=localhost;Database=icp_platform;User=root;Password=REDACTED_FOR_OPEN_SOURCE;"
},
"Persistence": {
  "Adapter": "EfCore",
  "Provider": "SqlServer",
  "AutoMigrate": true,
  "AutoSeed": true
}
```

```csharp
builder.Services.AddEfCorePersistence(
    builder.Configuration,
    new[]
    {
        ConfigCenterModelDefinitions.ConfigItem,
        AuthorizationModelDefinitions.Role,
        AuthorizationModelDefinitions.UserRole
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var migrationRunner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
    await migrationRunner.MigrateAsync();
}
```

- [ ] **Step 4: 生成三套迁移程序集并补 smoke 脚本**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\platform-persistence-efcore\PlatformPersistence.EfCore.csproj" />
  </ItemGroup>
</Project>
```

```powershell
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet("SqlServer", "Oracle", "MySql")]
  [string]$Provider
)

$env:Persistence__Adapter = "EfCore"
$env:Persistence__Provider = $Provider

dotnet build "apps\host-api\HostApi.csproj"
if ($LASTEXITCODE -ne 0) { throw "Build failed for $Provider" }

dotnet test "tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj" -v minimal
if ($LASTEXITCODE -ne 0) { throw "ConfigCenter tests failed for $Provider" }

dotnet test "tests\Authorization.Tests\Authorization.Tests.csproj" -v minimal
if ($LASTEXITCODE -ne 0) { throw "Authorization tests failed for $Provider" }
```

- [ ] **Step 5: 跑最终验证**

Run:
- `dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -v minimal`
- `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj -v minimal`
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -v minimal`
- `dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -v minimal`
- `dotnet build .\apps\host-api\HostApi.csproj`
- `powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-SqlServerSmoke.ps1`

Expected:
- 所有测试通过
- Host 构建通过
- 至少一个 smoke 成功返回

- [ ] **Step 6: 更新文档并 Commit**

```md
### Phase 6.5：多数据库持久化（混合架构）

状态：进行中/已完成

已落地：
- 模型语义底座
- Provider 无关持久化内核
- EF Core 第一批适配器
- ConfigCenter / Authorization 首批真实数据库持久化
- SQL Server / Oracle / MySql 迁移与 smoke 闭环
```

```bash
git add apps/host-api server/platform-persistence-efcore* tests docs/current-status.md
git commit -m "feat: wire host bootstrap and multi-db verification"
```

## Self-Review Checklist

- [ ] 设计中的三层边界都已经在计划中有任务承接：模型语义、持久化内核、EF 适配器。
- [ ] 计划没有把 EF Core 重新写回“唯一真相来源”，而是把它放在适配器位置。
- [ ] `ConfigCenter` 与 `Authorization` 都同时覆盖了模型定义、仓储适配、宿主接入和验证。
- [ ] 计划仍然保留了三数据库切换、迁移与 smoke 闭环，没有因为调整路线而弱化交付目标。
- [ ] 计划没有要求业务层按数据库类型分支，也没有把原生 SQL 暴露到业务服务层。

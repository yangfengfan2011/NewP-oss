# 开发环境数据库结构同步工具 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付一个仅面向开发环境的 `dev-schema-sync` 可执行工具，基于全量 `model` 文件与当前 SQL Server 数据库结构做 diff，输出计划/SQL 预览，并在二次确认后执行结构同步。

**Architecture:** 方案采用独立控制台工具，放在 `server/tools/dev-schema-sync/`，复用现有 modeling 校验与投影链路以及 Host API 的配置优先级。工具内部拆分为配置引导、model 扫描加载、数据库结构读取、schema diff、风险分类、计划输出与执行器几层，避免把 schema 读取、DDL 生成和 CLI 交互混在一起。

**Tech Stack:** .NET 10、C#、Microsoft.Extensions.Configuration、现有 `server/platform-modeling` 元数据/投影组件、SQL Server 元数据查询、xUnit

---

### Task 1: 建立工具与测试工程骨架

**Files:**
- Create: `server/tools/dev-schema-sync/DevSchemaSync.csproj`
- Create: `server/tools/dev-schema-sync/Program.cs`
- Create: `server/tools/dev-schema-sync/Properties/launchSettings.json`
- Create: `tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj`
- Create: `tests/DevSchemaSync.Tests/Smoke/ToolBootstrapTests.cs`
- Modify: `ICP-Platform.sln`

- [ ] **Step 1: 创建工具工程文件**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>ICP.Platform.Tools.DevSchemaSync</RootNamespace>
    <AssemblyName>dev-schema-sync</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\platform-modeling\PlatformModeling.csproj" />
    <ProjectReference Include="..\..\platform-persistence-efcore\PlatformPersistence.EfCore.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.CommandLine" Version="10.0.0" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: 创建最小可运行入口**

```csharp
using ICP.Platform.Tools.DevSchemaSync;

return await DevSchemaSyncProgram.RunAsync(args);
```

- [ ] **Step 3: 为工具启动补失败测试**

```csharp
using ICP.Platform.Tools.DevSchemaSync;

namespace DevSchemaSync.Tests.Smoke;

public sealed class ToolBootstrapTests
{
    [Fact]
    public async Task RunAsync_WithJsonFlag_ReturnsSuccessForHelpStylePlanPath()
    {
        var exitCode = await DevSchemaSyncProgram.RunAsync(["--json"]);

        Assert.Equal(0, exitCode);
    }
}
```

- [ ] **Step 4: 运行定向测试确认当前缺少工具实现**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~ToolBootstrapTests" -v minimal`
Expected: FAIL，提示 `DevSchemaSyncProgram` 或入口类型不存在

- [ ] **Step 5: 把工具与测试工程加入解决方案**

Run: `dotnet sln ICP-Platform.sln add server/tools/dev-schema-sync/DevSchemaSync.csproj tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj`
Expected: 输出两个 project 已加入 solution

- [ ] **Step 6: 运行定向测试确认最小入口通过**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~ToolBootstrapTests" -v minimal`
Expected: PASS

- [ ] **Step 7: 提交骨架**

```bash
git add ICP-Platform.sln server/tools/dev-schema-sync tests/DevSchemaSync.Tests
git commit -m "功能：建立开发库结构同步工具骨架"
```

### Task 2: 复用 Host API 配置优先级并建立运行上下文

**Files:**
- Create: `server/tools/dev-schema-sync/Configuration/DevSchemaSyncConfigurationBootstrap.cs`
- Create: `server/tools/dev-schema-sync/Configuration/DevSchemaSyncExecutionContext.cs`
- Create: `server/tools/dev-schema-sync/Configuration/RepositoryRootLocator.cs`
- Create: `tests/DevSchemaSync.Tests/Configuration/DevSchemaSyncConfigurationBootstrapTests.cs`
- Modify: `server/tools/dev-schema-sync/Program.cs`
- Test: `tests/DevSchemaSync.Tests/Configuration/DevSchemaSyncConfigurationBootstrapTests.cs`

- [ ] **Step 1: 为配置优先级与 provider 限制写失败测试**

```csharp
using Microsoft.Extensions.Configuration;

namespace DevSchemaSync.Tests.Configuration;

public sealed class DevSchemaSyncConfigurationBootstrapTests
{
    [Fact]
    public void FromConfiguration_UsesSqlServerProviderFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "SqlServer",
                ["ConnectionStrings:SqlServer"] = "Server=(localdb)\\MSSQLLocalDB;Database=ICP_Test;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();

        var context = DevSchemaSyncExecutionContext.FromConfiguration(configuration);

        Assert.Equal("SqlServer", context.Provider);
        Assert.Contains("Database=ICP_Test", context.ConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildConfiguration_LoadsHostApiDevelopmentOverridesFromRepositoryRoot()
    {
        var repositoryRoot = Path.GetFullPath(".");
        var configuration = DevSchemaSyncConfigurationBootstrap.BuildConfiguration(repositoryRoot, environmentName: "Development");

        Assert.Equal("SqlServer", configuration["Persistence:Provider"]);
        Assert.False(string.IsNullOrWhiteSpace(configuration.GetConnectionString("SqlServer")));
    }
}
```

- [ ] **Step 2: 运行测试确认执行上下文类型尚不存在**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~DevSchemaSyncConfigurationBootstrapTests" -v minimal`
Expected: FAIL，提示 `DevSchemaSyncExecutionContext` 不存在

- [ ] **Step 3: 实现配置引导与执行上下文**

```csharp
using Microsoft.Extensions.Configuration;

namespace ICP.Platform.Tools.DevSchemaSync.Configuration;

public static class DevSchemaSyncConfigurationBootstrap
{
    public static IConfigurationRoot BuildConfiguration(string repositoryRoot, string environmentName = "Development")
    {
        var hostApiRoot = Path.Combine(repositoryRoot, "apps", "host-api");

        return new ConfigurationBuilder()
            .SetBasePath(hostApiRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();
    }
}

public static class RepositoryRootLocator
{
    public static string Find()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "apps"))
                && Directory.Exists(Path.Combine(current.FullName, "server"))
                && Directory.Exists(Path.Combine(current.FullName, "modules")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}

public sealed record DevSchemaSyncExecutionContext(
    string Provider,
    string ConnectionString,
    string RepositoryRoot,
    string HostApiRoot)
{
    public static DevSchemaSyncExecutionContext FromConfiguration(IConfiguration configuration, string repositoryRoot = "")
    {
        var provider = configuration["Persistence:Provider"]
            ?? throw new InvalidOperationException("缺少 Persistence:Provider 配置。");

        if (!string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"当前 provider '{provider}' 暂不支持，仅支持 SqlServer。");
        }

        var connectionString = configuration.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:SqlServer 配置。");

        return new DevSchemaSyncExecutionContext("SqlServer", connectionString, repositoryRoot, Path.Combine(repositoryRoot, "apps", "host-api"));
    }
}
```

- [ ] **Step 4: 让 `Program` 先输出基础运行摘要**

```csharp
var repositoryRoot = RepositoryRootLocator.Find();
var configuration = DevSchemaSyncConfigurationBootstrap.BuildConfiguration(repositoryRoot);
var context = DevSchemaSyncExecutionContext.FromConfiguration(configuration, repositoryRoot);

Console.WriteLine($"Provider: {context.Provider}");
Console.WriteLine($"HostApiRoot: {context.HostApiRoot}");
```

- [ ] **Step 5: 运行定向测试确认通过**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~DevSchemaSyncConfigurationBootstrapTests" -v minimal`
Expected: PASS

- [ ] **Step 6: 手动运行工具验证基础摘要**

Run: `dotnet run --project server/tools/dev-schema-sync -- --json`
Expected: 输出 `Provider: SqlServer` 和 `HostApiRoot:`，退出码为 `0`

- [ ] **Step 7: 提交配置引导**

```bash
git add server/tools/dev-schema-sync/Configuration server/tools/dev-schema-sync/Program.cs tests/DevSchemaSync.Tests/Configuration
git commit -m "功能：接入开发库结构同步工具配置引导"
```

### Task 3: 提供单文件业务根对象 model 适配与全量扫描

**Files:**
- Create: `server/tools/dev-schema-sync/Models/SingleRootModelDocumentLoader.cs`
- Create: `server/tools/dev-schema-sync/Models/ModelDiscoveryService.cs`
- Create: `server/tools/dev-schema-sync/Models/DiscoveredModelDocument.cs`
- Create: `tests/DevSchemaSync.Tests/Models/SingleRootModelDocumentLoaderTests.cs`
- Create: `tests/DevSchemaSync.Tests/Models/ModelDiscoveryServiceTests.cs`

- [ ] **Step 1: 为单文件业务根对象加载写失败测试**

```csharp
namespace DevSchemaSync.Tests.Models;

public sealed class SingleRootModelDocumentLoaderTests
{
    [Fact]
    public void Load_ConfigItemModel_MapsSingleBusinessRootToModelDocument()
    {
        var path = Path.GetFullPath("modules/ConfigCenter/metadata/models/ConfigItem.model.json");
        var loader = new SingleRootModelDocumentLoader();

        var document = loader.Load(path);

        Assert.Equal("1.0", document.SchemaVersion);
        Assert.Equal("ConfigCenter", document.ModuleId);
        Assert.Equal("ConfigItem", document.Model.Name);
    }
}
```

- [ ] **Step 2: 运行测试确认当前通用 loader 不能直接满足布局**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~SingleRootModelDocumentLoaderTests" -v minimal`
Expected: FAIL

- [ ] **Step 3: 实现单文件业务根对象加载器**

```csharp
using System.Text.Json;
using ICP.Platform.Modeling.Metadata;

namespace ICP.Platform.Tools.DevSchemaSync.Models;

public sealed class SingleRootModelDocumentLoader
{
    public ModelMetadataDocument Load(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;

        var schemaVersion = root.GetProperty("schemaVersion").GetString() ?? "1.0";
        var type = root.GetProperty("type").GetString();
        if (!string.Equals(type, "model", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"模型文件类型不匹配：{path}");
        }

        var businessRoot = root.EnumerateObject()
            .Where(property => !property.NameEquals("schemaVersion") && !property.NameEquals("type"))
            .ToList();

        if (businessRoot.Count != 1)
        {
            throw new InvalidOperationException($"模型文件必须且只能包含一个业务根对象：{path}");
        }

        var modelName = businessRoot[0].Name;
        var moduleId = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(path)))!);
        var model = businessRoot[0].Value.Deserialize<ModelMetadataModel>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException($"模型文件反序列化失败：{path}");

        return new ModelMetadataDocument
        {
            SchemaVersion = schemaVersion,
            ModuleId = moduleId,
            Model = new ModelMetadataModel
            {
                Name = modelName,
                Kind = model.Kind,
                BuiltinFields = model.BuiltinFields,
                Fields = model.Fields,
                LinkedTypes = model.LinkedTypes,
                Indexes = model.Indexes
            }
        };
    }
}
```

- [ ] **Step 4: 为全量扫描写失败测试**

```csharp
namespace DevSchemaSync.Tests.Models;

public sealed class ModelDiscoveryServiceTests
{
    [Fact]
    public void DiscoverAll_FindsConfigCenterModelUnderModulesRoot()
    {
        var repositoryRoot = Path.GetFullPath(".");
        var service = new ModelDiscoveryService(new SingleRootModelDocumentLoader());

        var models = service.DiscoverAll(repositoryRoot);

        Assert.Contains(models, item => item.ModuleId == "ConfigCenter" && item.ModelName == "ConfigItem");
    }
}
```

- [ ] **Step 5: 实现全量扫描服务**

```csharp
namespace ICP.Platform.Tools.DevSchemaSync.Models;

public sealed record DiscoveredModelDocument(
    string ModuleId,
    string ModelName,
    string FilePath,
    ModelMetadataDocument Document);

public sealed class ModelDiscoveryService
{
    private readonly SingleRootModelDocumentLoader _loader;

    public ModelDiscoveryService(SingleRootModelDocumentLoader loader)
    {
        _loader = loader;
    }

    public IReadOnlyList<DiscoveredModelDocument> DiscoverAll(string repositoryRoot)
    {
        var files = Directory.GetFiles(Path.Combine(repositoryRoot, "modules"), "*.model.json", SearchOption.AllDirectories);
        return files
            .Select(path =>
            {
                var document = _loader.Load(path);
                return new DiscoveredModelDocument(document.ModuleId, document.Model.Name, path, document);
            })
            .OrderBy(item => item.ModuleId, StringComparer.Ordinal)
            .ThenBy(item => item.ModelName, StringComparer.Ordinal)
            .ToList();
    }
}
```

- [ ] **Step 6: 运行模型加载定向测试**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~DevSchemaSync.Tests.Models" -v minimal`
Expected: PASS

- [ ] **Step 7: 提交 model 适配**

```bash
git add server/tools/dev-schema-sync/Models tests/DevSchemaSync.Tests/Models
git commit -m "功能：接入开发库结构同步模型扫描与加载"
```

### Task 4: 投影目标 schema 并支持 `historyName`

**Files:**
- Create: `server/tools/dev-schema-sync/Schema/TargetSchemaBuilder.cs`
- Create: `server/tools/dev-schema-sync/Schema/TargetSchema.cs`
- Create: `server/tools/dev-schema-sync/Schema/TargetTableSchema.cs`
- Create: `server/tools/dev-schema-sync/Schema/TargetColumnSchema.cs`
- Create: `server/tools/dev-schema-sync/Schema/TargetIndexSchema.cs`
- Modify: `server/platform-modeling/metadata/ModelMetadataField.cs`
- Modify: `server/platform-modeling/metadata/ModelMetadataModel.cs`
- Modify: `server/tools/dev-schema-sync/Models/SingleRootModelDocumentLoader.cs`
- Create: `tests/DevSchemaSync.Tests/Schema/TargetSchemaBuilderTests.cs`
- Test: `tests/Modeling.Tests/Modeling.Tests.csproj`

- [ ] **Step 1: 为 `historyName` 反序列化补失败测试**

```csharp
namespace DevSchemaSync.Tests.Schema;

public sealed class TargetSchemaBuilderTests
{
    [Fact]
    public void Build_IncludesHistoryNameForModelAndField()
    {
        const string json = """
        {
          "schemaVersion": "1.0",
          "type": "model",
          "ConfigItem": {
            "kind": "Entity",
            "historyName": "config_items",
            "fields": [
              {
                "name": "DisplayName",
                "historyName": "display_name",
                "storage": { "type": "string" }
              }
            ]
          }
        }
        """;

        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        var document = new SingleRootModelDocumentLoader().Load(path);
        var schema = new TargetSchemaBuilder(Path.GetFullPath(".")).Build([new DiscoveredModelDocument(document.ModuleId, document.Model.Name, path, document)]);

        var table = Assert.Single(schema.Tables);
        Assert.Equal("config_items", table.HistoryName);
        Assert.Equal("display_name", Assert.Single(table.Columns).HistoryName);
        Assert.Contains(table.Columns, column => column.Name == "EID");
        Assert.Contains(table.Indexes, index => index.Name == "UX_ConfigItem_EID");
    }
}
```

- [ ] **Step 2: 运行测试确认当前元数据对象缺少 `historyName`**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~TargetSchemaBuilderTests" -v minimal`
Expected: FAIL

- [ ] **Step 3: 扩展 modeling 元数据对象**

```csharp
public sealed class ModelMetadataField
{
    public string Name { get; init; } = string.Empty;
    public string? HistoryName { get; init; }
    public ModelMetadataStorageSection? Storage { get; init; }
}
```

```csharp
public sealed class ModelMetadataModel
{
    public string Name { get; init; } = string.Empty;
    public string? HistoryName { get; init; }
    public string Kind { get; init; } = "Entity";
}
```

- [ ] **Step 4: 同步更新单文件 loader，避免 `historyName` 在装配文档时被丢失**

```csharp
return new ModelMetadataDocument
{
    SchemaVersion = schemaVersion,
    ModuleId = moduleId,
    Model = new ModelMetadataModel
    {
        Name = modelName,
        HistoryName = model.HistoryName,
        Kind = model.Kind,
        BuiltinFields = model.BuiltinFields,
        Fields = model.Fields,
        LinkedTypes = model.LinkedTypes,
        Indexes = model.Indexes
    }
};
```

- [ ] **Step 5: 实现目标 schema 构建器并显式复用现有校验/投影链**

```csharp
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Tools.DevSchemaSync.Schema;

public sealed record TargetIndexSchema(
    string Name,
    IReadOnlyList<string> Columns,
    bool IsUnique);

public sealed record TargetColumnSchema(
    string Name,
    string StoreName,
    string? HistoryName,
    string StoreType,
    bool Required);

public sealed record TargetTableSchema(
    string ModuleId,
    string Name,
    string TableName,
    string? HistoryName,
    IReadOnlyList<TargetColumnSchema> Columns,
    IReadOnlyList<TargetIndexSchema> Indexes);

public sealed record TargetSchema(IReadOnlyList<TargetTableSchema> Tables);

public sealed class TargetSchemaBuilder
{
    private readonly string _repositoryRoot;
    private readonly JsonStorageTypeMapLoader _typeMapLoader = new();
    private readonly ModelMetadataValidator _validator = new();
    private readonly ModelMetadataProjector _projector = new();

    public TargetSchemaBuilder(string repositoryRoot)
    {
        _repositoryRoot = repositoryRoot;
    }

    public TargetSchema Build(IReadOnlyList<DiscoveredModelDocument> models)
    {
        var storageTypeMapPath = Path.Combine(_repositoryRoot, "server", "platform-modeling", "metadata", "storage-types.json");
        var typeMap = _typeMapLoader.LoadFromFile(storageTypeMapPath);

        var tables = models.Select(model =>
        {
            _validator.Validate(model.Document, typeMap);
            var definition = _projector.Project(model.Document, typeMap, "SqlServer");

            return new TargetTableSchema(
                model.ModuleId,
                definition.Name,
                definition.TableName,
                model.Document.Model.HistoryName,
                definition.Fields.Select(field => new TargetColumnSchema(
                    field.Name,
                    field.StoreName,
                    model.Document.Model.Fields.FirstOrDefault(item => string.Equals(item.Name, field.Name, StringComparison.Ordinal))?.HistoryName,
                    field.StoreType,
                    field.Required)).ToList(),
                definition.Indexes.Select(index => new TargetIndexSchema(index.Name, index.Fields, index.IsUnique)).ToList());
        }).ToList();

        return new TargetSchema(tables);
    }
}
```

- [ ] **Step 6: 运行工具与建模测试**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~TargetSchemaBuilderTests" -v minimal`
Expected: PASS

Run: `dotnet test tests/Modeling.Tests/Modeling.Tests.csproj -v minimal`
Expected: PASS

- [ ] **Step 7: 提交 schema 投影**

```bash
git add server/platform-modeling/metadata/ModelMetadataField.cs server/platform-modeling/metadata/ModelMetadataModel.cs server/tools/dev-schema-sync/Models/SingleRootModelDocumentLoader.cs server/tools/dev-schema-sync/Schema tests/DevSchemaSync.Tests/Schema
git commit -m "功能：接入开发库结构同步目标模型投影"
```

### Task 5: 读取 SQL Server 当前结构并输出受管对象摘要

**Files:**
- Create: `server/tools/dev-schema-sync/Database/SqlServerSchemaReader.cs`
- Create: `server/tools/dev-schema-sync/Database/DatabaseTableSchema.cs`
- Create: `server/tools/dev-schema-sync/Database/DatabaseColumnSchema.cs`
- Create: `tests/DevSchemaSync.Tests/Database/SqlServerSchemaReaderTests.cs`
- Modify: `server/tools/dev-schema-sync/Program.cs`

- [ ] **Step 1: 为数据库结构摘要写失败测试**

```csharp
namespace DevSchemaSync.Tests.Database;

public sealed class SqlServerSchemaReaderTests
{
    [Fact]
    public async Task ReadAsync_ReturnsKnownTablesFromConnection()
    {
        var reader = new SqlServerSchemaReader();
        var schema = await reader.ReadAsync("Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;");

        Assert.NotNull(schema.Tables);
    }
}
```

- [ ] **Step 2: 运行测试确认 reader 尚不存在**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~SqlServerSchemaReaderTests" -v minimal`
Expected: FAIL

- [ ] **Step 3: 实现 SQL Server 结构读取器**

```csharp
using Microsoft.Data.SqlClient;

namespace ICP.Platform.Tools.DevSchemaSync.Database;

public sealed record DatabaseColumnSchema(
    string Name,
    string StoreType,
    bool IsNullable);

public sealed record DatabaseTableSchema(
    string Name,
    IReadOnlyList<DatabaseColumnSchema> Columns);
```

- [ ] **Step 4: 在主程序先输出受管对象与当前库摘要**

```csharp
Console.WriteLine($"Models: {models.Count}");
Console.WriteLine($"Database Tables: {databaseSchema.Tables.Count}");
```

- [ ] **Step 5: 运行 reader 定向测试**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~SqlServerSchemaReaderTests" -v minimal`
Expected: PASS

- [ ] **Step 6: 手动运行工具确认摘要输出**

Run: `dotnet run --project server/tools/dev-schema-sync --`
Expected: 输出 `Models:` 和 `Database Tables:`

- [ ] **Step 7: 提交数据库读取层**

```bash
git add server/tools/dev-schema-sync/Database server/tools/dev-schema-sync/Program.cs tests/DevSchemaSync.Tests/Database
git commit -m "功能：接入开发库结构同步数据库结构读取"
```

### Task 6: 实现 diff engine、未托管对象摘要与 JSON/文本计划输出

**Files:**
- Create: `server/tools/dev-schema-sync/Planning/SchemaPlan.cs`
- Create: `server/tools/dev-schema-sync/Planning/SchemaOperation.cs`
- Create: `server/tools/dev-schema-sync/Planning/SchemaDiffEngine.cs`
- Create: `server/tools/dev-schema-sync/Planning/RiskClassifier.cs`
- Create: `server/tools/dev-schema-sync/Planning/PlanConsoleWriter.cs`
- Create: `tests/DevSchemaSync.Tests/Planning/SchemaDiffEngineTests.cs`
- Create: `tests/DevSchemaSync.Tests/Planning/RiskClassifierTests.cs`
- Create: `tests/DevSchemaSync.Tests/Planning/PlanConsoleWriterTests.cs`
- Modify: `server/tools/dev-schema-sync/Program.cs`

- [ ] **Step 1: 为未托管对象摘要与 rename 计划写失败测试**

```csharp
namespace DevSchemaSync.Tests.Planning;

public sealed class SchemaDiffEngineTests
{
    [Fact]
    public void BuildPlan_ReportsUnmanagedTablesWithoutDropTableOperation()
    {
        var plan = new SchemaDiffEngine(new RiskClassifier()).BuildPlan(
            targetTables: [],
            databaseTables: [new DatabaseTableSchema("legacy_table", [])]);

        Assert.Empty(plan.Operations.Where(op => op.Kind == "DropTable"));
        Assert.Contains(plan.UnmanagedTables, item => item == "legacy_table");
    }

    [Fact]
    public void BuildPlan_UsesHistoryNameToProduceRenameColumnOperation()
    {
        var target = new TargetSchema([
            new TargetTableSchema(
                "ConfigCenter",
                "ConfigItem",
                "ConfigItem",
                "config_items",
                [
                    new TargetColumnSchema("ENAME", "ENAME", "display_name", "nvarchar", true)
                ],
                [])
        ]);

        var database = [
            new DatabaseTableSchema("config_items", [
                new DatabaseColumnSchema("display_name", "nvarchar", false)
            ])
        ];

        var plan = new SchemaDiffEngine(new RiskClassifier()).BuildPlan(target.Tables, database);

        Assert.Contains(plan.Operations, op => op.Kind == "RenameColumn" && op.RiskLevel == "Confirm");
    }
}
```

- [ ] **Step 2: 运行测试确认 diff 计划对象尚不存在**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~SchemaDiffEngineTests" -v minimal`
Expected: FAIL

- [ ] **Step 3: 为风险分类写失败测试**

```csharp
namespace DevSchemaSync.Tests.Planning;

public sealed class RiskClassifierTests
{
    [Theory]
    [InlineData("DropColumn", "Confirm")]
    [InlineData("RenameColumn", "Confirm")]
    [InlineData("AlterColumnType", "Confirm")]
    [InlineData("AlterNullability", "Confirm")]
    [InlineData("CreateTable", "Safe")]
    public void Classify_ReturnsExpectedRisk(string operationKind, string expectedRisk)
    {
        var classifier = new RiskClassifier();

        var risk = classifier.Classify(operationKind);

        Assert.Equal(expectedRisk, risk);
    }
}
```

- [ ] **Step 4: 实现计划对象、风险分类器与 diff engine**

```csharp
namespace ICP.Platform.Tools.DevSchemaSync.Planning;

public sealed record SchemaOperation(
    string Kind,
    string RiskLevel,
    string TableName,
    string? ColumnName,
    string Description,
    IReadOnlyList<string> SqlPreview);

public sealed record SchemaPlan(
    IReadOnlyList<SchemaOperation> Operations,
    IReadOnlyList<string> UnmanagedTables);

public sealed class RiskClassifier
{
    public string Classify(string operationKind) => operationKind switch
    {
        "DropColumn" => "Confirm",
        "RenameTable" => "Confirm",
        "RenameColumn" => "Confirm",
        "AlterColumnType" => "Confirm",
        "AlterNullability" => "Confirm",
        "RecreateIndex" => "Confirm",
        _ => "Safe"
    };
}

public sealed class SchemaDiffEngine
{
    private readonly RiskClassifier _riskClassifier;

    public SchemaDiffEngine(RiskClassifier riskClassifier)
    {
        _riskClassifier = riskClassifier;
    }
}
```

- [ ] **Step 5: 实现文本与 JSON 输出**

```csharp
if (arguments.Json)
{
    Console.WriteLine(JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
}
else
{
    PlanConsoleWriter.Write(plan, includeSql: arguments.IncludeSql);
}
```

- [ ] **Step 6: 运行计划层定向测试**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~DevSchemaSync.Tests.Planning" -v minimal`
Expected: PASS

- [ ] **Step 7: 手动运行工具确认文本与 JSON 输出**

Run: `dotnet run --project server/tools/dev-schema-sync -- --json`
Expected: 输出包含 `operations` 和 `unmanagedTables`

Run: `dotnet run --project server/tools/dev-schema-sync --`
Expected: 输出 Summary、Managed Objects、Planned Operations、Unmanaged Objects Summary

- [ ] **Step 8: 提交计划层**

```bash
git add server/tools/dev-schema-sync/Planning server/tools/dev-schema-sync/Program.cs tests/DevSchemaSync.Tests/Planning
git commit -m "功能：生成开发库结构同步计划与摘要输出"
```

### Task 7: 实现 SQL 预览、风险分类、确认交互与执行器

**Files:**
- Create: `server/tools/dev-schema-sync/Execution/SchemaSqlGenerator.cs`
- Create: `server/tools/dev-schema-sync/Execution/SchemaApplyExecutor.cs`
- Create: `server/tools/dev-schema-sync/Execution/ConfirmationPrompts.cs`
- Create: `tests/DevSchemaSync.Tests/Execution/SchemaApplyExecutorTests.cs`
- Modify: `server/tools/dev-schema-sync/Program.cs`

- [ ] **Step 1: 为高风险确认与 `--apply` 流程写失败测试**

```csharp
namespace DevSchemaSync.Tests.Execution;

public sealed class SchemaApplyExecutorTests
{
    [Fact]
    public async Task ApplyAsync_DoesNotExecuteWhenRiskConfirmationRejected()
    {
        var executor = new SchemaApplyExecutor(
            confirmAll: () => true,
            confirmRisk: () => false);

        var result = await executor.ApplyAsync(new SchemaPlan(
            [new SchemaOperation("DropColumn", "Confirm", "ConfigItem", "Description", "drop column", ["ALTER TABLE ... DROP COLUMN ..."])],
            []));

        Assert.False(result.Applied);
    }
}
```

- [ ] **Step 2: 运行执行层测试确认 executor 尚不存在**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~SchemaApplyExecutorTests" -v minimal`
Expected: FAIL

- [ ] **Step 3: 实现 SQL 生成与执行器**

```csharp
namespace ICP.Platform.Tools.DevSchemaSync.Execution;

public sealed record ApplyResult(bool Applied, int ExecutedOperations);

public sealed class SchemaApplyExecutor
{
    private readonly Func<bool> _confirmAll;
    private readonly Func<bool> _confirmRisk;

    public SchemaApplyExecutor(Func<bool> confirmAll, Func<bool> confirmRisk)
    {
        _confirmAll = confirmAll;
        _confirmRisk = confirmRisk;
    }

    public Task<ApplyResult> ApplyAsync(SchemaPlan plan)
    {
        if (!_confirmAll())
        {
            return Task.FromResult(new ApplyResult(false, 0));
        }

        if (plan.Operations.Any(op => string.Equals(op.RiskLevel, "Confirm", StringComparison.OrdinalIgnoreCase)) && !_confirmRisk())
        {
            return Task.FromResult(new ApplyResult(false, 0));
        }

        return Task.FromResult(new ApplyResult(true, plan.Operations.Count));
    }
}
```

- [ ] **Step 4: 把 `--sql` 与 `--apply` 串到主流程**

```csharp
if (!arguments.Apply)
{
    return 0;
}

var applyResult = await executor.ApplyAsync(plan);
return applyResult.Applied ? 0 : 2;
```

- [ ] **Step 5: 运行执行层定向测试**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj --filter "FullyQualifiedName~DevSchemaSync.Tests.Execution" -v minimal`
Expected: PASS

- [ ] **Step 6: 手动验证确认交互**

Run: `dotnet run --project server/tools/dev-schema-sync -- --sql --apply`
Expected: 先输出计划和 SQL；提示第一次确认；若存在高风险，再提示第二次确认

- [ ] **Step 7: 提交执行层**

```bash
git add server/tools/dev-schema-sync/Execution server/tools/dev-schema-sync/Program.cs tests/DevSchemaSync.Tests/Execution
git commit -m "功能：接入开发库结构同步执行与风险确认"
```

### Task 8: 完整验证、文档回写与一致性收口

**Files:**
- Modify: `docs/designs/dev-schema-sync-tool-design.md`
- Create: `docs/verification/dev-schema-sync-tool-verification.md`

- [ ] **Step 1: 创建验证记录文档**

```md
# 开发环境数据库结构同步工具验证记录

## 1. 验证范围

- 工具骨架与配置引导
- model 全量扫描
- SQL Server schema 读取
- 计划输出与 JSON 输出
- SQL 预览
- `--apply` 与高风险确认
- 不生成 migration / 不修改 `ModelSnapshot` / 不写 `__EFMigrationsHistory`
```

- [ ] **Step 2: 运行后端构建**

Run: `dotnet build ICP-Platform.sln -v minimal`
Expected: PASS

- [ ] **Step 3: 运行工具相关测试**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj -v minimal`
Expected: PASS

Run: `dotnet test tests/Modeling.Tests/Modeling.Tests.csproj -v minimal`
Expected: PASS

- [ ] **Step 4: 运行工具 smoke**

Run: `dotnet run --project server/tools/dev-schema-sync -- --sql`
Expected: 输出 Summary、Planned Operations、SQL Preview 或空计划摘要

- [ ] **Step 5: 验证正式链路隔离**

Run: `git diff -- server/platform-persistence-efcore.migrations.sqlserver server/platform-persistence-efcore.migrations.mysql server/platform-persistence-efcore.migrations.oracle`
Expected: 无输出

Run: `git diff -- apps/host-api`
Expected: 无输出

- [ ] **Step 6: 回写验证与已知限制**

```md
## 2. 结果

- 第一版仅支持 SQL Server
- 仅接管 `model` 管理对象
- 数据库多出表只进入 `Unmanaged Objects Summary`
- 工具不生成 migration、不修改 `ModelSnapshot`、不写 `__EFMigrationsHistory`
```

- [ ] **Step 7: 提交验证与文档收口**

```bash
git add docs/designs/dev-schema-sync-tool-design.md docs/verification/dev-schema-sync-tool-verification.md
git commit -m "验证：补充开发库结构同步工具验证记录"
```

## Self-Review

- 规格覆盖检查：本计划覆盖了工具骨架、配置优先级、单文件业务根对象适配、`historyName`、SQL Server schema 读取、计划输出、JSON 输出、SQL 预览、执行确认、正式链路隔离验证。
- 占位检查：未使用 TBD/TODO；每个实现步骤都给出了目标文件、示例代码和验证命令。
- 一致性检查：计划始终坚持“只读 `model`、只支持 SQL Server、第一版不自动删表、不修改 migration 链”的边界，没有重新引入手写 `ModelDefinition` 或 provider 选择入口。

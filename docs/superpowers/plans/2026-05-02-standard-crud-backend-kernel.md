# 标准 CRUD 后端内核 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立标准 CRUD 后端内核，并用 Organization 模块 `Person` 作为首轮试点，逐步接管 list/detail/summary、create/update/disable/delete 主干。

**Architecture:** 本计划以 `docs/superpowers/specs/2026-05-02-standard-crud-backend-kernel-design.md` 为直接输入，继承 `docs/designs/platform/standard-crud-contract-design.md` 的契约语义。平台新增 `server/platform-crud`，提供 descriptor、通用仓储、通用服务和 Controller adapter；Organization 模块只注册 Person 的查询、校验、摘要块、动作和删除策略扩展点。

**Tech Stack:** .NET 10 / ASP.NET Core / C# / xUnit / EF Core shared-type entity / Platform ModelDefinition

---

## 文件结构与职责

**Create:**

- `server/platform-crud/PlatformCrud.csproj`  
  标准 CRUD 后端内核工程，依赖 `PlatformModeling`、`PlatformPersistence`、`PlatformPersistence.EfCore` 和 `Sdk`。
- `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`  
  标准 CRUD 资源描述符，声明资源、模型、字段、路由、能力和扩展点。
- `server/platform-crud/Descriptors/StandardCrudFieldSet.cs`  
  字段集合定义，避免 list/detail/create/update 字段混用。
- `server/platform-crud/Descriptors/StandardCrudDeletePolicy.cs`  
  删除策略枚举：`None / Conditional / Auditable`。
- `server/platform-crud/Models/StandardCrudListQuery.cs`  
  标准列表查询输入。
- `server/platform-crud/Models/StandardCrudListResult.cs`  
  标准列表返回骨架。
- `server/platform-crud/Models/StandardCrudDetail.cs`  
  标准详情读取模型。
- `server/platform-crud/Models/StandardCrudSummaryBlock.cs`  
  标准摘要块模型。
- `server/platform-crud/Models/StandardCrudOptionItem.cs`  
  标准选项模型。
- `server/platform-crud/Models/StandardCrudError.cs`  
  标准错误模型。
- `server/platform-crud/Abstractions/IStandardCrudRepository.cs`  
  平台 CRUD 仓储抽象。
- `server/platform-crud/Abstractions/IStandardCrudService.cs`  
  平台 CRUD 服务抽象。
- `server/platform-crud/Abstractions/IStandardCrudQueryCustomizer.cs`  
  查询扩展点。
- `server/platform-crud/Abstractions/IStandardCrudValidator.cs`  
  校验扩展点。
- `server/platform-crud/Abstractions/IStandardCrudSummaryProvider.cs`  
  摘要块扩展点。
- `server/platform-crud/Abstractions/IStandardCrudActionHandler.cs`  
  标准动作扩展点。
- `server/platform-crud/Abstractions/IStandardCrudDeleteGuard.cs`  
  删除守卫扩展点。
- `server/platform-crud/Abstractions/IStandardCrudDisplayValueResolver.cs`  
  显示值解析扩展点。
- `server/platform-crud/Runtime/StandardCrudDescriptorValidator.cs`  
  descriptor 静态校验器。
- `server/platform-crud/Runtime/StandardCrudService.cs`  
  标准 CRUD 服务编排内核。
- `server/platform-crud/Runtime/StandardCrudEndpoint.cs`  
  Controller adapter 门面。
- `server/platform-crud/Persistence/EfStandardCrudRepository.cs`  
  基于 `EfPlatformDbContext` 的 shared-type entity 仓储实现。
- `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`  
  DI 注册入口。
- `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`  
  平台 CRUD 内核测试工程。
- `tests/PlatformCrud.Tests/StandardCrudDescriptorValidatorTests.cs`  
  descriptor 校验测试。
- `tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs`  
  list/detail/summary/options 测试。
- `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`  
  create/update/disable/delete 测试。
- `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`  
  Person 试点 descriptor 工厂。
- `server/modules/Organization/Crud/PersonCrudQueryCustomizer.cs`  
  Person 查询扩展。
- `server/modules/Organization/Crud/PersonCrudValidator.cs`  
  Person create/update/delete 校验。
- `server/modules/Organization/Crud/PersonUserLinkSummaryProvider.cs`  
  Person `userLink` 摘要块 provider。
- `server/modules/Organization/Crud/PersonAuthorizationSummaryProvider.cs`  
  Person `authorizationSummary` 摘要块 provider。

**Modify:**

- `ICP-Platform.sln`  
  加入 `PlatformCrud` 与 `PlatformCrud.Tests`。
- `server/modules/Organization/OrganizationModule.csproj`  
  引用 `server/platform-crud/PlatformCrud.csproj`。
- `tests/Organization.Tests/Organization.Tests.csproj`  
  引用 `server/platform-crud/PlatformCrud.csproj`。
- `server/modules/Organization/OrganizationModule.cs`  
  注册 Person 标准 CRUD descriptor 与扩展点。
- `server/modules/Organization/Controllers/PersonsController.cs`  
  第一阶段把 list/detail 委托给 `IStandardCrudEndpoint`；第二阶段委托 create/update/disable/delete。
- `server/modules/Organization/Services/PersonService.cs`  
  第一阶段保留写操作与 user-link；第二阶段收缩通用写操作主干。
- `tests/Organization.Tests/PersonListContractTests.cs`  
  增加 Person 标准 CRUD adapter 断言。
- `tests/Organization.Tests/PersonServiceTests.cs`  
  保留人员领域规则断言，防止平台化吞掉规则。
- `docs/verification/organization-module-verification.md`  
  记录 Person 试点后端平台化验证结果。

**Out of Scope in this plan:**

- 不迁移 ConfigCenter、Authorization、Audit。
- 不实现规则 DSL。
- 不实现功能授权或对象授权。
- 不重写前端页面运行时。
- 不移除 `user-link` 特殊入口。

---

### Task 1: 建立 PlatformCrud 工程与 descriptor 基础模型

**Files:**
- Create: `server/platform-crud/PlatformCrud.csproj`
- Create: `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`
- Create: `server/platform-crud/Descriptors/StandardCrudFieldSet.cs`
- Create: `server/platform-crud/Descriptors/StandardCrudDeletePolicy.cs`
- Create: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`
- Create: `tests/PlatformCrud.Tests/StandardCrudDescriptorValidatorTests.cs`
- Modify: `ICP-Platform.sln`

- [ ] **Step 1: 创建测试工程和第一个失败测试**

Create `tests/PlatformCrud.Tests/StandardCrudDescriptorValidatorTests.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudDescriptorValidatorTests
{
    [Fact]
    public void Validate_WhenRouteIsEmpty_ThrowsStableMessage()
    {
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "PersonNo", "ENAME"],
                DetailFields: ["EID", "PersonNo", "ENAME"],
                CreateFields: ["PersonNo", "ENAME"],
                UpdateFields: ["ENAME"]),
            DeletePolicy = StandardCrudDeletePolicy.None
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 缺少 Route。", ex.Message);
    }
}
```

- [ ] **Step 2: 创建 `PlatformCrud.Tests.csproj`**

Create `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>ICP.Platform.Crud.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\server\platform-crud\PlatformCrud.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: 运行测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected:

```text
error MSB3202: The project file "...server\platform-crud\PlatformCrud.csproj" was not found.
```

- [ ] **Step 4: 创建 `PlatformCrud.csproj`**

Create `server/platform-crud/PlatformCrud.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>ICP.Platform.Crud</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\platform-modeling\PlatformModeling.csproj" />
    <ProjectReference Include="..\platform-persistence\PlatformPersistence.csproj" />
    <ProjectReference Include="..\platform-persistence-efcore\PlatformPersistence.EfCore.csproj" />
    <ProjectReference Include="..\sdk\Sdk.csproj" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.1" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.1" />
  </ItemGroup>

</Project>
```

- [ ] **Step 5: 创建 descriptor 类型**

Create `server/platform-crud/Descriptors/StandardCrudDeletePolicy.cs`:

```csharp
namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// 标准 CRUD 删除策略。删除是否开放必须显式声明，避免由接口存在与否隐式决定。
/// </summary>
public enum StandardCrudDeletePolicy
{
    None,
    Conditional,
    Auditable
}
```

Create `server/platform-crud/Descriptors/StandardCrudFieldSet.cs`:

```csharp
namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// 标准 CRUD 字段集合。不同场景独立声明，避免读取投影字段被写操作误用。
/// </summary>
public sealed record StandardCrudFieldSet(
    IReadOnlyList<string> ListFields,
    IReadOnlyList<string> DetailFields,
    IReadOnlyList<string> CreateFields,
    IReadOnlyList<string> UpdateFields);
```

Create `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`:

```csharp
namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// 标准 CRUD 资源描述符。它是代码注册契约，不是元数据脚本容器。
/// </summary>
public sealed class StandardCrudDescriptor
{
    public required string ResourceKey { get; init; }
    public required string ModuleId { get; init; }
    public required string ModelName { get; init; }
    public required string Route { get; init; }
    public required string IdField { get; init; }
    public required string DisplayField { get; init; }
    public string? EnabledField { get; init; }
    public required StandardCrudFieldSet FieldSet { get; init; }
    public StandardCrudDeletePolicy DeletePolicy { get; init; } = StandardCrudDeletePolicy.None;
    public IReadOnlyList<string> OptionLabelFields { get; init; } = [];
    public IReadOnlyList<string> SummaryBlockKeys { get; init; } = [];
}
```

- [ ] **Step 6: 创建 descriptor 校验器**

Create `server/platform-crud/Runtime/StandardCrudDescriptorValidator.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Crud.Runtime;

public static class StandardCrudDescriptorValidator
{
    public static void Validate(StandardCrudDescriptor descriptor)
    {
        Require(descriptor.ResourceKey, nameof(descriptor.ResourceKey), descriptor.ResourceKey);
        Require(descriptor.ModuleId, nameof(descriptor.ModuleId), descriptor.ResourceKey);
        Require(descriptor.ModelName, nameof(descriptor.ModelName), descriptor.ResourceKey);
        Require(descriptor.Route, nameof(descriptor.Route), descriptor.ResourceKey);
        Require(descriptor.IdField, nameof(descriptor.IdField), descriptor.ResourceKey);
        Require(descriptor.DisplayField, nameof(descriptor.DisplayField), descriptor.ResourceKey);

        if (!descriptor.FieldSet.DetailFields.Contains(descriptor.IdField, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的 DetailFields 必须包含 IdField。");
        }
    }

    private static void Require(string value, string name, string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"标准 CRUD 资源 {resourceKey} 缺少 {name}。");
        }
    }
}
```

- [ ] **Step 7: 运行测试确认通过**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 8: 加入 solution**

Run:

```powershell
dotnet sln .\ICP-Platform.sln add .\server\platform-crud\PlatformCrud.csproj
dotnet sln .\ICP-Platform.sln add .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj
```

Expected:

```text
Project `server\platform-crud\PlatformCrud.csproj` added to the solution.
Project `tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj` added to the solution.
```

- [ ] **Step 9: 提交**

```powershell
git add .\ICP-Platform.sln .\server\platform-crud .\tests\PlatformCrud.Tests
git commit -m "功能：建立标准CRUD内核工程"
```

### Task 2: 实现标准读取模型与内存仓储测试替身

**Files:**
- Create: `server/platform-crud/Models/StandardCrudListQuery.cs`
- Create: `server/platform-crud/Models/StandardCrudListResult.cs`
- Create: `server/platform-crud/Models/StandardCrudDetail.cs`
- Create: `server/platform-crud/Models/StandardCrudSummaryBlock.cs`
- Create: `server/platform-crud/Models/StandardCrudOptionItem.cs`
- Create: `server/platform-crud/Models/StandardCrudError.cs`
- Create: `server/platform-crud/Abstractions/IStandardCrudRepository.cs`
- Create: `tests/PlatformCrud.Tests/Support/InMemoryStandardCrudRepository.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs`

- [ ] **Step 1: 写读取模型测试**

Create `tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs`:

```csharp
using ICP.Platform.Crud.Models;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudServiceReadTests
{
    [Fact]
    public void StandardCrudListResult_CarriesPagingAndSortingState()
    {
        var result = new StandardCrudListResult(
            Items:
            [
                new Dictionary<string, object?>
                {
                    ["EID"] = "P001",
                    ["ENAME"] = "张三"
                }
            ],
            Total: 1,
            Page: 1,
            PageSize: 20,
            SortBy: "PersonNo",
            SortDirection: "asc");

        Assert.Single(result.Items);
        Assert.Equal(1, result.Total);
        Assert.Equal(20, result.PageSize);
        Assert.Equal("PersonNo", result.SortBy);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudListResult_CarriesPagingAndSortingState -nr:false -v minimal
```

Expected:

```text
error CS0234: The type or namespace name 'Models' does not exist
```

- [ ] **Step 3: 创建标准模型**

Create `server/platform-crud/Models/StandardCrudListQuery.cs`:

```csharp
namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudListQuery(
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDirection = null,
    IReadOnlyDictionary<string, string?>? Filters = null);
```

Create `server/platform-crud/Models/StandardCrudListResult.cs`:

```csharp
namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudListResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Items,
    int Total,
    int Page,
    int PageSize,
    string? SortBy,
    string? SortDirection);
```

Create `server/platform-crud/Models/StandardCrudSummaryBlock.cs`:

```csharp
namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudSummaryBlock
{
    public required string Status { get; init; }
    public object? Data { get; init; }
}
```

Create `server/platform-crud/Models/StandardCrudDetail.cs`:

```csharp
namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudDetail
{
    public required IReadOnlyDictionary<string, object?> Fields { get; init; }
    public IReadOnlyDictionary<string, StandardCrudSummaryBlock> Summaries { get; init; }
        = new Dictionary<string, StandardCrudSummaryBlock>(StringComparer.Ordinal);
}
```

Create `server/platform-crud/Models/StandardCrudOptionItem.cs`:

```csharp
namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudOptionItem
{
    public required string Value { get; init; }
    public required string Label { get; init; }
}
```

Create `server/platform-crud/Models/StandardCrudError.cs`:

```csharp
namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudError
{
    public required int StatusCode { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public IReadOnlyDictionary<string, object?> Args { get; init; }
        = new Dictionary<string, object?>(StringComparer.Ordinal);
}
```

- [ ] **Step 4: 创建仓储抽象**

Create `server/platform-crud/Abstractions/IStandardCrudRepository.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudRepository
{
    Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, object?>> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: 创建测试替身仓储**

Create `tests/PlatformCrud.Tests/Support/InMemoryStandardCrudRepository.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Tests.Support;

public sealed class InMemoryStandardCrudRepository : IStandardCrudRepository
{
    private readonly List<Dictionary<string, object?>> _rows;

    public InMemoryStandardCrudRepository(IEnumerable<Dictionary<string, object?>> rows)
    {
        _rows = rows.Select(row => new Dictionary<string, object?>(row, StringComparer.Ordinal)).ToList();
    }

    public Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
        var items = _rows.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(row => new Dictionary<string, object?>(row, StringComparer.Ordinal))
            .ToList<IReadOnlyDictionary<string, object?>>();

        return Task.FromResult(new StandardCrudListResult(
            items,
            _rows.Count,
            page,
            pageSize,
            query.SortBy,
            query.SortDirection));
    }

    public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        var row = _rows.FirstOrDefault(item => string.Equals(item[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
        return Task.FromResult<IReadOnlyDictionary<string, object?>?>(row is null ? null : new Dictionary<string, object?>(row, StringComparer.Ordinal));
    }

    public Task<IReadOnlyDictionary<string, object?>> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        var row = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        _rows.Add(row);
        return Task.FromResult<IReadOnlyDictionary<string, object?>>(row);
    }

    public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        var row = _rows.FirstOrDefault(item => string.Equals(item[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
        if (row is null)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object?>?>(null);
        }

        foreach (var pair in values)
        {
            row[pair.Key] = pair.Value;
        }

        return Task.FromResult<IReadOnlyDictionary<string, object?>?>(new Dictionary<string, object?>(row, StringComparer.Ordinal));
    }

    public Task<bool> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        var removed = _rows.RemoveAll(item => string.Equals(item[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
        return Task.FromResult(removed > 0);
    }
}
```

- [ ] **Step 6: 运行测试确认通过**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 7: 提交**

```powershell
git add .\server\platform-crud .\tests\PlatformCrud.Tests
git commit -m "功能：定义标准CRUD读写模型"
```

### Task 3: 实现标准服务只读主干和摘要块降级

**Files:**
- Create: `server/platform-crud/Abstractions/IStandardCrudService.cs`
- Create: `server/platform-crud/Abstractions/IStandardCrudSummaryProvider.cs`
- Create: `server/platform-crud/Runtime/StandardCrudService.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs`

- [ ] **Step 1: 写 detail + summary provider 降级测试**

Append to `tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;

public sealed class FailingSummaryProvider : IStandardCrudSummaryProvider
{
    public string Key => "failing";

    public Task<StandardCrudSummaryBlock> BuildAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> detail,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("summary store unavailable");
    }
}

public sealed partial class StandardCrudServiceReadTests
{
    [Fact]
    public async Task GetDetailAsync_WhenSummaryProviderFails_ReturnsDependencyUnavailableBlock()
    {
        var descriptor = BuildDescriptor(summaryKeys: ["failing"]);
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["ENAME"] = "张三"
            }
        ]);
        var service = new StandardCrudService(repository, [new FailingSummaryProvider()]);

        var detail = await service.GetDetailAsync(descriptor, "person-1");

        Assert.NotNull(detail);
        Assert.Equal("张三", detail!.Fields["ENAME"]);
        Assert.Equal("dependencyUnavailable", detail.Summaries["failing"].Status);
    }

    private static StandardCrudDescriptor BuildDescriptor(IReadOnlyList<string>? summaryKeys = null) => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "ENAME"],
            DetailFields: ["EID", "ENAME"],
            CreateFields: ["ENAME"],
            UpdateFields: ["ENAME"]),
        SummaryBlockKeys = summaryKeys ?? []
    };
}
```

If the file already has a non-partial `StandardCrudServiceReadTests`, change its declaration to `public sealed partial class StandardCrudServiceReadTests`.

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter GetDetailAsync_WhenSummaryProviderFails_ReturnsDependencyUnavailableBlock -nr:false -v minimal
```

Expected:

```text
error CS0246: The type or namespace name 'IStandardCrudSummaryProvider' could not be found
```

- [ ] **Step 3: 创建服务与摘要 provider 抽象**

Create `server/platform-crud/Abstractions/IStandardCrudSummaryProvider.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudSummaryProvider
{
    string Key { get; }

    Task<StandardCrudSummaryBlock> BuildAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> detail,
        CancellationToken cancellationToken = default);
}
```

Create `server/platform-crud/Abstractions/IStandardCrudService.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudService
{
    Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default);

    Task<StandardCrudDetail?> GetDetailAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: 实现只读服务主干**

Create `server/platform-crud/Runtime/StandardCrudService.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Runtime;

public sealed class StandardCrudService : IStandardCrudService
{
    private readonly IStandardCrudRepository _repository;
    private readonly IReadOnlyDictionary<string, IStandardCrudSummaryProvider> _summaryProviders;

    public StandardCrudService(
        IStandardCrudRepository repository,
        IEnumerable<IStandardCrudSummaryProvider> summaryProviders)
    {
        _repository = repository;
        _summaryProviders = summaryProviders.ToDictionary(provider => provider.Key, StringComparer.Ordinal);
    }

    public Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        return _repository.ListAsync(descriptor, query, cancellationToken);
    }

    public async Task<StandardCrudDetail?> GetDetailAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);

        var row = await _repository.GetByIdAsync(descriptor, id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var summaries = new Dictionary<string, StandardCrudSummaryBlock>(StringComparer.Ordinal);
        foreach (var key in descriptor.SummaryBlockKeys)
        {
            summaries[key] = await BuildSummaryAsync(descriptor, row, key, cancellationToken);
        }

        return new StandardCrudDetail
        {
            Fields = new Dictionary<string, object?>(row, StringComparer.Ordinal),
            Summaries = summaries
        };
    }

    private async Task<StandardCrudSummaryBlock> BuildSummaryAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        string key,
        CancellationToken cancellationToken)
    {
        if (!_summaryProviders.TryGetValue(key, out var provider))
        {
            return new StandardCrudSummaryBlock { Status = "notConfigured" };
        }

        try
        {
            return await provider.BuildAsync(descriptor, row, cancellationToken);
        }
        catch (Exception)
        {
            return new StandardCrudSummaryBlock { Status = "dependencyUnavailable" };
        }
    }
}
```

- [ ] **Step 5: 运行只读测试**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "StandardCrudServiceReadTests" -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 6: 提交**

```powershell
git add .\server\platform-crud .\tests\PlatformCrud.Tests
git commit -m "功能：实现标准CRUD只读服务主干"
```

### Task 4: 实现 EF shared-type 仓储读取主干

**Files:**
- Create: `server/platform-crud/Persistence/EfStandardCrudRepository.cs`
- Create: `tests/PlatformCrud.Tests/StandardCrudEfRepositoryTests.cs`
- Modify: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`

- [ ] **Step 1: 补 EF InMemory 测试依赖**

Modify `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj` package references:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.1" />
```

- [ ] **Step 2: 写 EF 仓储读取测试**

Create `tests/PlatformCrud.Tests/StandardCrudEfRepositoryTests.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudEfRepositoryTests
{
    [Fact]
    public async Task ListAsync_ReadsSharedTypeRowsByDescriptorModelName()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("Person").Add(new Dictionary<string, object?>
        {
            ["EID"] = "person-1",
            ["PersonNo"] = "P001",
            ["ENAME"] = "张三",
            ["IsEnabled"] = true
        });
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext);
        var result = await repository.ListAsync(BuildDescriptor(), new StandardCrudListQuery(Page: 1, PageSize: 20));

        Assert.Single(result.Items);
        Assert.Equal("P001", result.Items[0]["PersonNo"]);
    }

    private static EfPlatformDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EfPlatformDbContext(options, [BuildPersonDefinition()]);
    }

    private static ModelDefinition BuildPersonDefinition() => new(
        Name: "Person",
        TableName: "Person",
        Fields:
        [
            new FieldDefinition("EID", "EID", FieldValueType.ObjectId, Required: true, IsPrimaryKey: true),
            new FieldDefinition("PersonNo", "PersonNo", FieldValueType.String, Required: true),
            new FieldDefinition("ENAME", "ENAME", FieldValueType.String, Required: true),
            new FieldDefinition("IsEnabled", "IsEnabled", FieldValueType.Boolean, Required: true)
        ],
        Indexes: []);

    private static StandardCrudDescriptor BuildDescriptor() => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        EnabledField = "IsEnabled",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "IsEnabled"],
            DetailFields: ["EID", "PersonNo", "ENAME", "IsEnabled"],
            CreateFields: ["PersonNo", "ENAME", "IsEnabled"],
            UpdateFields: ["ENAME", "IsEnabled"])
    };
}
```

- [ ] **Step 3: 运行测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter ListAsync_ReadsSharedTypeRowsByDescriptorModelName -nr:false -v minimal
```

Expected:

```text
error CS0246: The type or namespace name 'EfStandardCrudRepository' could not be found
```

- [ ] **Step 4: 实现 EF 仓储读取**

Create `server/platform-crud/Persistence/EfStandardCrudRepository.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Crud.Persistence;

public sealed class EfStandardCrudRepository : IStandardCrudRepository
{
    private readonly EfPlatformDbContext _dbContext;

    public EfStandardCrudRepository(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
        var source = _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName).AsNoTracking();
        var total = await source.CountAsync(cancellationToken);
        var rows = await source.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);

        return new StandardCrudListResult(
            rows.Select(row => Project(row, descriptor.FieldSet.ListFields)).ToList(),
            total,
            page,
            pageSize,
            query.SortBy,
            query.SortDirection);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        var row = await _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName)
            .AsNoTracking()
            .FirstOrDefaultAsync(item => EF.Property<string>(item, descriptor.IdField) == id, cancellationToken);

        return row is null ? null : Project(row, descriptor.FieldSet.DetailFields);
    }

    public Task<IReadOnlyDictionary<string, object?>> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("当前任务只实现标准 CRUD 读取主干。");

    public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("当前任务只实现标准 CRUD 读取主干。");

    public Task<bool> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("当前任务只实现标准 CRUD 读取主干。");

    private static IReadOnlyDictionary<string, object?> Project(
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyList<string> fields)
    {
        var projected = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            projected[field] = row.TryGetValue(field, out var value) ? value : null;
        }

        return projected;
    }
}
```

- [ ] **Step 5: 运行 EF 仓储测试**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudEfRepositoryTests -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 6: 提交**

```powershell
git add .\server\platform-crud .\tests\PlatformCrud.Tests
git commit -m "功能：实现标准CRUD EF读取仓储"
```

### Task 5: 接入 Person descriptor 与 list/detail adapter

**Files:**
- Create: `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`
- Create: `server/modules/Organization/Crud/PersonUserLinkSummaryProvider.cs`
- Create: `server/modules/Organization/Crud/PersonAuthorizationSummaryProvider.cs`
- Create: `server/platform-crud/Runtime/StandardCrudEndpoint.cs`
- Modify: `server/modules/Organization/OrganizationModule.csproj`
- Modify: `tests/Organization.Tests/Organization.Tests.csproj`
- Modify: `server/modules/Organization/Controllers/PersonsController.cs`
- Modify: `tests/Organization.Tests/PersonListContractTests.cs`

- [ ] **Step 1: 给 Organization 测试工程引用 PlatformCrud**

Modify `tests/Organization.Tests/Organization.Tests.csproj`:

```xml
<ProjectReference Include="..\..\server\platform-crud\PlatformCrud.csproj" />
```

- [ ] **Step 2: 写 Person descriptor 测试**

Append to `tests/Organization.Tests/PersonListContractTests.cs`:

```csharp
[Fact]
public void PersonCrudDescriptor_DeclaresPersonRouteAndSummaryBlocks()
{
    var descriptor = PersonCrudDescriptorFactory.Create();

    Assert.Equal("organization.person", descriptor.ResourceKey);
    Assert.Equal("api/organization/persons", descriptor.Route);
    Assert.Equal("Person", descriptor.ModelName);
    Assert.Contains("userLink", descriptor.SummaryBlockKeys);
    Assert.Contains("authorizationSummary", descriptor.SummaryBlockKeys);
}
```

Add using:

```csharp
using ICP.Platform.Modules.Organization.Crud;
```

- [ ] **Step 3: 运行测试确认失败**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter PersonCrudDescriptor_DeclaresPersonRouteAndSummaryBlocks -nr:false -v minimal
```

Expected:

```text
error CS0234: The type or namespace name 'Crud' does not exist
```

- [ ] **Step 4: 给 Organization 模块引用 PlatformCrud**

Modify `server/modules/Organization/OrganizationModule.csproj`:

```xml
<ProjectReference Include="..\..\platform-crud\PlatformCrud.csproj" />
```

- [ ] **Step 5: 创建 Person descriptor**

Create `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Modules.Organization.Crud;

public static class PersonCrudDescriptorFactory
{
    public static StandardCrudDescriptor Create() => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        EnabledField = "IsEnabled",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled"],
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled", "CreatedAt"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled"],
            UpdateFields: ["ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled"]),
        OptionLabelFields: ["PersonNo", "ENAME"],
        SummaryBlockKeys: ["userLink", "authorizationSummary"],
        DeletePolicy = StandardCrudDeletePolicy.Conditional
    };
}
```

- [ ] **Step 6: 运行 descriptor 测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter PersonCrudDescriptor_DeclaresPersonRouteAndSummaryBlocks -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 7: 创建 Endpoint 门面**

Create `server/platform-crud/Runtime/StandardCrudEndpoint.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Runtime;

public sealed class StandardCrudEndpoint
{
    private readonly IStandardCrudService _service;

    public StandardCrudEndpoint(IStandardCrudService service)
    {
        _service = service;
    }

    public Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default) =>
        _service.ListAsync(descriptor, query, cancellationToken);

    public Task<StandardCrudDetail?> GetDetailAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default) =>
        _service.GetDetailAsync(descriptor, id, cancellationToken);
}
```

- [ ] **Step 8: 先不改 PersonsController 对外返回结构**

Modify `server/modules/Organization/Controllers/PersonsController.cs` only after adding mapper helpers in the same file:

```csharp
private static PersonListItemDto MapListItem(IReadOnlyDictionary<string, object?> row) => new()
{
    Id = Guid.Parse(ReadString(row, "EID")),
    PersonNo = ReadString(row, "PersonNo"),
    Name = ReadString(row, "ENAME"),
    CompanyName = ReadString(row, "CompanyName"),
    DepartmentName = ReadString(row, "DepartmentName"),
    EmploymentStatus = Enum.TryParse<EmploymentStatus>(ReadString(row, "EmploymentStatus"), out var status)
        ? status
        : EmploymentStatus.FullTime,
    IsEnabled = ReadBool(row, "IsEnabled", true)
};

private static string ReadString(IReadOnlyDictionary<string, object?> row, string key) =>
    row.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

private static bool ReadBool(IReadOnlyDictionary<string, object?> row, string key, bool fallback) =>
    row.TryGetValue(key, out var value) && value is bool typed ? typed : fallback;
```

Then route only `GetList` through `StandardCrudEndpoint` when the endpoint can be resolved. If DI is not wired yet, keep `PersonService` path as fallback:

```csharp
private readonly StandardCrudEndpoint? _standardCrudEndpoint;
private static readonly StandardCrudDescriptor PersonCrudDescriptor = PersonCrudDescriptorFactory.Create();
```

Constructor:

```csharp
public PersonsController(
    IPersonService personService,
    IIdentityUserDirectory identityUserDirectory,
    StandardCrudEndpoint? standardCrudEndpoint = null)
{
    _personService = personService;
    _identityUserDirectory = identityUserDirectory;
    _standardCrudEndpoint = standardCrudEndpoint;
}
```

List action branch:

```csharp
if (_standardCrudEndpoint is not null)
{
    var result = await _standardCrudEndpoint.ListAsync(
        PersonCrudDescriptor,
        new StandardCrudListQuery(
            Page: 1,
            PageSize: 100,
            Filters: new Dictionary<string, string?>
            {
                ["keyword"] = keyword,
                ["companyId"] = companyId?.ToString("D"),
                ["departmentId"] = departmentId?.ToString("D"),
                ["employmentStatus"] = employmentStatus?.ToString()
            }),
        cancellationToken);

    return Ok(result.Items.Select(MapListItem).ToList());
}
```

- [ ] **Step 9: 运行 Organization 合同测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "PersonListContractTests" -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 10: 提交**

```powershell
git add .\server\platform-crud .\server\modules\Organization .\tests\Organization.Tests
git commit -m "功能：接入人员标准CRUD描述符"
```

### Task 6: 实现写操作、停用和删除策略内核

**Files:**
- Create: `server/platform-crud/Abstractions/IStandardCrudValidator.cs`
- Create: `server/platform-crud/Abstractions/IStandardCrudActionHandler.cs`
- Create: `server/platform-crud/Abstractions/IStandardCrudDeleteGuard.cs`
- Modify: `server/platform-crud/Abstractions/IStandardCrudService.cs`
- Modify: `server/platform-crud/Runtime/StandardCrudService.cs`
- Modify: `server/platform-crud/Persistence/EfStandardCrudRepository.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`

- [ ] **Step 1: 写 delete policy none 测试**

Create `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudServiceWriteTests
{
    [Fact]
    public async Task DeleteAsync_WhenPolicyIsNone_ReturnsConflictError()
    {
        var descriptor = BuildDescriptor(StandardCrudDeletePolicy.None);
        var service = new StandardCrudService(
            new InMemoryStandardCrudRepository(
            [
                new Dictionary<string, object?> { ["EID"] = "person-1", ["ENAME"] = "张三" }
            ]),
            []);

        var result = await service.DeleteAsync(descriptor, "person-1");

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("StandardCrud.DeleteNotSupported", result.Error.Code);
    }

    private static StandardCrudDescriptor BuildDescriptor(StandardCrudDeletePolicy policy) => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "ENAME"],
            DetailFields: ["EID", "ENAME"],
            CreateFields: ["ENAME"],
            UpdateFields: ["ENAME"]),
        DeletePolicy = policy
    };
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter DeleteAsync_WhenPolicyIsNone_ReturnsConflictError -nr:false -v minimal
```

Expected:

```text
error CS1061: 'StandardCrudService' does not contain a definition for 'DeleteAsync'
```

- [ ] **Step 3: 增加操作结果模型**

Create `server/platform-crud/Models/StandardCrudMutationResult.cs`:

```csharp
namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudMutationResult
{
    public required bool Succeeded { get; init; }
    public StandardCrudDetail? Detail { get; init; }
    public StandardCrudError? Error { get; init; }

    public static StandardCrudMutationResult Success(StandardCrudDetail? detail = null) =>
        new() { Succeeded = true, Detail = detail };

    public static StandardCrudMutationResult Failure(StandardCrudError error) =>
        new() { Succeeded = false, Error = error };
}
```

- [ ] **Step 4: 扩展服务接口**

Modify `server/platform-crud/Abstractions/IStandardCrudService.cs`:

```csharp
Task<StandardCrudMutationResult> CreateAsync(
    StandardCrudDescriptor descriptor,
    IReadOnlyDictionary<string, object?> values,
    CancellationToken cancellationToken = default);

Task<StandardCrudMutationResult> UpdateAsync(
    StandardCrudDescriptor descriptor,
    string id,
    IReadOnlyDictionary<string, object?> values,
    CancellationToken cancellationToken = default);

Task<StandardCrudMutationResult> DisableAsync(
    StandardCrudDescriptor descriptor,
    string id,
    CancellationToken cancellationToken = default);

Task<StandardCrudMutationResult> DeleteAsync(
    StandardCrudDescriptor descriptor,
    string id,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 5: 实现删除策略 none**

Modify `server/platform-crud/Runtime/StandardCrudService.cs`, add methods:

```csharp
public Task<StandardCrudMutationResult> DeleteAsync(
    StandardCrudDescriptor descriptor,
    string id,
    CancellationToken cancellationToken = default)
{
    StandardCrudDescriptorValidator.Validate(descriptor);

    if (descriptor.DeletePolicy == StandardCrudDeletePolicy.None)
    {
        return Task.FromResult(StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 409,
            Code = "StandardCrud.DeleteNotSupported",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 不支持删除。"
        }));
    }

    return DeleteAllowedAsync(descriptor, id, cancellationToken);
}

private async Task<StandardCrudMutationResult> DeleteAllowedAsync(
    StandardCrudDescriptor descriptor,
    string id,
    CancellationToken cancellationToken)
{
    var deleted = await _repository.DeleteAsync(descriptor, id, cancellationToken);
    if (!deleted)
    {
        return StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 404,
            Code = "StandardCrud.NotFound",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的对象 {id} 不存在。"
        });
    }

    return StandardCrudMutationResult.Success();
}
```

Add create/update/disable stubs that return stable errors before implementing them:

```csharp
public Task<StandardCrudMutationResult> CreateAsync(
    StandardCrudDescriptor descriptor,
    IReadOnlyDictionary<string, object?> values,
    CancellationToken cancellationToken = default) =>
    Task.FromResult(StandardCrudMutationResult.Failure(new StandardCrudError
    {
        StatusCode = 409,
        Code = "StandardCrud.CreateNotEnabled",
        Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 尚未启用创建主干。"
    }));
```

Repeat for `UpdateAsync` and `DisableAsync` using codes `StandardCrud.UpdateNotEnabled` and `StandardCrud.DisableNotEnabled`.

- [ ] **Step 6: 运行 delete policy 测试**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudServiceWriteTests -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 7: 实现 EF 仓储 create/update/delete**

Modify `server/platform-crud/Persistence/EfStandardCrudRepository.cs`, replace write methods:

```csharp
public async Task<IReadOnlyDictionary<string, object?>> CreateAsync(
    StandardCrudDescriptor descriptor,
    IReadOnlyDictionary<string, object?> values,
    CancellationToken cancellationToken = default)
{
    var row = new Dictionary<string, object?>(values, StringComparer.Ordinal);
    if (!row.ContainsKey(descriptor.IdField))
    {
        row[descriptor.IdField] = Guid.NewGuid().ToString("D");
    }

    _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName).Add(row);
    await _dbContext.SaveChangesAsync(cancellationToken);
    return Project(row, descriptor.FieldSet.DetailFields);
}

public async Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(
    StandardCrudDescriptor descriptor,
    string id,
    IReadOnlyDictionary<string, object?> values,
    CancellationToken cancellationToken = default)
{
    var row = await _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName)
        .FirstOrDefaultAsync(item => EF.Property<string>(item, descriptor.IdField) == id, cancellationToken);

    if (row is null)
    {
        return null;
    }

    foreach (var pair in values)
    {
        row[pair.Key] = pair.Value;
    }

    await _dbContext.SaveChangesAsync(cancellationToken);
    return Project(row, descriptor.FieldSet.DetailFields);
}

public async Task<bool> DeleteAsync(
    StandardCrudDescriptor descriptor,
    string id,
    CancellationToken cancellationToken = default)
{
    var row = await _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName)
        .FirstOrDefaultAsync(item => EF.Property<string>(item, descriptor.IdField) == id, cancellationToken);

    if (row is null)
    {
        return false;
    }

    _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName).Remove(row);
    await _dbContext.SaveChangesAsync(cancellationToken);
    return true;
}
```

- [ ] **Step 8: 提交**

```powershell
git add .\server\platform-crud .\tests\PlatformCrud.Tests
git commit -m "功能：实现标准CRUD写操作与删除策略"
```

### Task 7: Person 第二阶段接入 create/update/disable/delete

**Files:**
- Create: `server/modules/Organization/Crud/PersonCrudValidator.cs`
- Create: `server/modules/Organization/Crud/PersonCrudDeleteGuard.cs`
- Modify: `server/modules/Organization/Controllers/PersonsController.cs`
- Modify: `server/modules/Organization/Services/PersonService.cs`
- Modify: `tests/Organization.Tests/PersonServiceTests.cs`
- Modify: `tests/Organization.Tests/PersonListContractTests.cs`

- [ ] **Step 1: 写 Person delete 条件策略测试**

Append to `tests/Organization.Tests/PersonListContractTests.cs`:

```csharp
[Fact]
public void PersonCrudDescriptor_UsesConditionalDeletePolicy()
{
    var descriptor = PersonCrudDescriptorFactory.Create();

    Assert.Equal(StandardCrudDeletePolicy.Conditional, descriptor.DeletePolicy);
}
```

Add using:

```csharp
using ICP.Platform.Crud.Descriptors;
```

- [ ] **Step 2: 运行测试确认通过**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter PersonCrudDescriptor_UsesConditionalDeletePolicy -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 3: 添加 Person 删除 Controller 合同测试**

Append to `tests/Organization.Tests/PersonListContractTests.cs`:

```csharp
[Fact]
public void PersonsController_DeclaresDeleteEndpoint()
{
    var deleteAction = typeof(PersonsController).GetMethod("Delete");

    Assert.NotNull(deleteAction);
    Assert.Contains(deleteAction!.GetCustomAttributes<HttpDeleteAttribute>(), attribute => attribute.Template == "{id:guid}");
}
```

- [ ] **Step 4: 运行测试确认失败**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter PersonsController_DeclaresDeleteEndpoint -nr:false -v minimal
```

Expected:

```text
FAIL
Assert.NotNull() Failure
```

- [ ] **Step 5: 在 PersonsController 添加 delete 入口**

Modify `server/modules/Organization/Controllers/PersonsController.cs`:

```csharp
[HttpDelete("{id:guid}")]
public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
{
    if (_standardCrudEndpoint is null)
    {
        return StatusCode(503, new { message = "标准 CRUD 端点尚未装配，无法执行人员删除。" });
    }

    var result = await _standardCrudEndpoint.DeleteAsync(PersonCrudDescriptor, id.ToString("D"), cancellationToken);
    if (result.Succeeded)
    {
        return NoContent();
    }

    return StatusCode(result.Error!.StatusCode, result.Error);
}
```

Extend `StandardCrudEndpoint` with:

```csharp
public Task<StandardCrudMutationResult> DeleteAsync(
    StandardCrudDescriptor descriptor,
    string id,
    CancellationToken cancellationToken = default) =>
    _service.DeleteAsync(descriptor, id, cancellationToken);
```

- [ ] **Step 6: 运行 Controller 合同测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter PersonsController_DeclaresDeleteEndpoint -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 7: 扩展 create/update/disable endpoint 门面**

Modify `server/platform-crud/Runtime/StandardCrudEndpoint.cs`:

```csharp
public Task<StandardCrudMutationResult> CreateAsync(
    StandardCrudDescriptor descriptor,
    IReadOnlyDictionary<string, object?> values,
    CancellationToken cancellationToken = default) =>
    _service.CreateAsync(descriptor, values, cancellationToken);

public Task<StandardCrudMutationResult> UpdateAsync(
    StandardCrudDescriptor descriptor,
    string id,
    IReadOnlyDictionary<string, object?> values,
    CancellationToken cancellationToken = default) =>
    _service.UpdateAsync(descriptor, id, values, cancellationToken);

public Task<StandardCrudMutationResult> DisableAsync(
    StandardCrudDescriptor descriptor,
    string id,
    CancellationToken cancellationToken = default) =>
    _service.DisableAsync(descriptor, id, cancellationToken);
```

- [ ] **Step 8: 先让 PersonsController 委托 disable**

Modify `Disable` action:

```csharp
if (_standardCrudEndpoint is not null)
{
    var result = await _standardCrudEndpoint.DisableAsync(PersonCrudDescriptor, id.ToString("D"), cancellationToken);
    if (result.Succeeded && result.Detail is not null)
    {
        return Ok(MapDetail(result.Detail));
    }

    return StatusCode(result.Error!.StatusCode, result.Error);
}
```

Add `MapDetail` helper in the controller:

```csharp
private static PersonDetailDto MapDetail(StandardCrudDetail detail) => new()
{
    Id = Guid.Parse(ReadString(detail.Fields, "EID")),
    PersonNo = ReadString(detail.Fields, "PersonNo"),
    Name = ReadString(detail.Fields, "ENAME"),
    CompanyId = ReadGuid(detail.Fields, "CompanyId"),
    DepartmentId = ReadGuid(detail.Fields, "DepartmentId"),
    PositionId = ReadGuid(detail.Fields, "PositionId"),
    EmploymentStatus = Enum.TryParse<EmploymentStatus>(ReadString(detail.Fields, "EmploymentStatus"), out var status)
        ? status
        : EmploymentStatus.FullTime,
    IsEnabled = ReadBool(detail.Fields, "IsEnabled", true),
    CreatedAt = ReadDateTime(detail.Fields, "CreatedAt"),
    UserLinkSummary = MapSummary(detail.Summaries, "userLink"),
    AuthorizationSummary = MapSummary(detail.Summaries, "authorizationSummary")
};

private static Guid? ReadGuid(IReadOnlyDictionary<string, object?> row, string key) =>
    Guid.TryParse(ReadString(row, key), out var guid) ? guid : null;

private static DateTime ReadDateTime(IReadOnlyDictionary<string, object?> row, string key) =>
    row.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : default;

private static PersonSummaryBlockDto MapSummary(
    IReadOnlyDictionary<string, StandardCrudSummaryBlock> summaries,
    string key)
{
    return summaries.TryGetValue(key, out var block)
        ? new PersonSummaryBlockDto { Status = block.Status, Data = block.Data }
        : new PersonSummaryBlockDto { Status = "notConfigured" };
}
```

- [ ] **Step 9: 运行 Organization 测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "PersonListContractTests|PersonServiceTests" -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 10: 提交**

```powershell
git add .\server\platform-crud .\server\modules\Organization .\tests\Organization.Tests
git commit -m "功能：接入人员标准CRUD写操作与删除入口"
```

### Task 8: DI 注册、构建验证与文档回填

**Files:**
- Create: `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`
- Modify: `server/modules/Organization/OrganizationModule.cs`
- Modify: `docs/verification/organization-module-verification.md`
- Test: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`
- Test: `tests/Organization.Tests/Organization.Tests.csproj`

- [ ] **Step 1: 创建 DI 注册扩展**

Create `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Crud.Extensions;

public static class StandardCrudServiceCollectionExtensions
{
    public static IServiceCollection AddStandardCrudKernel(this IServiceCollection services)
    {
        services.AddScoped<IStandardCrudRepository, EfStandardCrudRepository>();
        services.AddScoped<IStandardCrudService, StandardCrudService>();
        services.AddScoped<StandardCrudEndpoint>();
        return services;
    }
}
```

- [ ] **Step 2: 在 OrganizationModule 注册内核**

Modify `server/modules/Organization/OrganizationModule.cs`, add using:

```csharp
using ICP.Platform.Crud.Extensions;
```

Inside `ConfigureServices`:

```csharp
services.AddStandardCrudKernel();
```

If `ConfigureServices` already registers repositories and services, keep those registrations for rollback and for `user-link` paths.

- [ ] **Step 3: 运行平台 CRUD 测试**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 4: 运行 Organization 测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 5: 运行 HostApi 构建**

Run:

```powershell
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 6: 如前端契约未变，运行 Organization 前端构建作为 smoke**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-modules/organization build
```

Expected:

```text
build completed successfully
```

- [ ] **Step 7: 回填验证文档**

Append to `docs/verification/organization-module-verification.md`:

```md

## 标准 CRUD 后端内核试点验证

验证日期：2026-05-02

本轮验证目标：

- `server/platform-crud` 已提供标准 CRUD descriptor、仓储、服务和 endpoint 门面。
- Organization `Person` 已接入标准 CRUD descriptor。
- Person 第一阶段读取主干可由标准 CRUD endpoint 承接。
- Person 第二阶段写操作、停用和条件删除已具备标准内核入口。
- `user-link` 特殊入口仍保留在 Organization 模块内。

验证命令：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
pnpm --dir .\frontend --filter @icp-modules/organization build
```

验证结论：

- 标准 CRUD 后端内核与 Person 试点通过定向验证。
- 删除能力按 Person `conditional` 策略显式接入，不作为隐式通用删除暴露。
- 账号绑定与授权摘要仍作为模块扩展点存在，未下沉为元数据执行逻辑。
```

- [ ] **Step 8: 提交**

```powershell
git add .\server\platform-crud .\server\modules\Organization .\tests\PlatformCrud.Tests .\tests\Organization.Tests .\docs\verification\organization-module-verification.md
git commit -m "验证：完成人员标准CRUD后端内核试点"
```

## Self-Review

- **Spec coverage:** 本计划覆盖了 `2026-05-02-standard-crud-backend-kernel-design.md` 中的 descriptor、通用仓储、通用服务、Controller adapter、Person 试点、两阶段接入、第二阶段 delete、验证和回滚要求。
- **Placeholder scan:** 本计划没有保留占位式任务；所有任务都有明确文件、命令、期望结果和代码片段。
- **Type consistency:** 计划统一使用 `StandardCrudDescriptor`、`StandardCrudFieldSet`、`StandardCrudService`、`StandardCrudEndpoint`、`StandardCrudDeletePolicy`、`StandardCrudMutationResult`、`StandardCrudSummaryBlock`，Person 试点统一使用 `PersonCrudDescriptorFactory`。

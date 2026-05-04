# 标准 CRUD 后端内核 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立标准 CRUD 后端内核，并用 Organization 模块 `Person` 作为首轮试点，接管 list/detail/options/summary、create/update/disable/delete 主干。

**Architecture:** 本计划以 `docs/superpowers/specs/2026-05-02-standard-crud-backend-kernel-design.md` 为直接输入，并继承 `docs/designs/platform/standard-crud-contract-design.md` 的契约语义。平台新增 `server/platform-crud`，提供 descriptor、扩展点、EF shared-type 仓储、标准服务和 endpoint 门面；Organization 模块只注册 Person 的查询、显示值、摘要块、校验、动作和删除守卫。

**Tech Stack:** .NET 10 / ASP.NET Core / C# / xUnit / EF Core shared-type entity / Platform `ModelDefinition`

---

## 修订说明

本计划是根据 `docs/designs/platform/standard-crud-backend-kernel-plan-consistency-review.md` 修订后的 V2，已吸收以下阻断项：

- 第一阶段补齐 `options`。
- 补齐 `QueryCustomizer`、filter/sort 和 Person keyword 查询。
- 补齐 `DisplayValueResolver`，确保 Person list 可返回 `CompanyName / DepartmentName`。
- 补齐 `SummaryProvider` 实现和注册。
- 使用当前 `ModelDefinition / FieldDefinition` 构造签名。
- 第二阶段先实现 create/update 字段白名单、disable 状态切换，再切 Controller。
- `conditional delete` 必须通过 `IStandardCrudDeleteGuard`，无 guard 时 fail closed。
- 所有标准 update/disable 类更新行为都由 CRUD 内核自动维护 `UpdatedAt`；资源声明该字段可写时，内核覆盖为当前时间，不接受客户端时间作为真源。

## 文件结构与职责

**Create:**

- `server/platform-crud/PlatformCrud.csproj`  
  标准 CRUD 后端内核工程。
- `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`  
  标准 CRUD 资源描述符，持有资源、模型、字段、删除策略和扩展点 key。
- `server/platform-crud/Descriptors/StandardCrudFieldSet.cs`  
  `list/detail/create/update` 字段集合。
- `server/platform-crud/Descriptors/StandardCrudDeletePolicy.cs`  
  `None / Conditional / Auditable` 删除策略。
- `server/platform-crud/Models/*.cs`  
  list/detail/options/summary/mutation/error/query/filter 模型。
- `server/platform-crud/Abstractions/*.cs`  
  仓储、服务、查询扩展、显示值、摘要块、校验、动作、删除守卫接口。
- `server/platform-crud/Runtime/StandardCrudService.cs`  
  标准 CRUD 编排内核。
- `server/platform-crud/Runtime/StandardCrudEndpoint.cs`  
  Controller adapter 门面。
- `server/platform-crud/Persistence/EfStandardCrudRepository.cs`  
  基于 `EfPlatformDbContext` 的 shared-type entity 仓储。
- `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`  
  DI 注册入口。
- `tests/PlatformCrud.Tests/*`  
  平台 CRUD 内核测试。
- `server/modules/Organization/Crud/*`  
  Person descriptor、查询、显示值、摘要、校验、删除守卫扩展点。

**Modify:**

- `ICP-Platform.sln`
- `server/modules/Organization/OrganizationModule.csproj`
- `tests/Organization.Tests/Organization.Tests.csproj`
- `server/modules/Organization/OrganizationModule.cs`
- `server/modules/Organization/Controllers/PersonsController.cs`
- `tests/Organization.Tests/PersonListContractTests.cs`
- `tests/Organization.Tests/PersonServiceTests.cs`
- `docs/verification/organization-module-verification.md`

**Out of Scope:**

- 不迁移 ConfigCenter、Authorization、Audit。
- 不实现规则 DSL、功能授权、对象授权。
- 不移除 `user-link` 特殊入口。
- 不重写前端页面运行时。

---

### Task 1: 建立 PlatformCrud 工程与当前模型签名测试基线

**Files:**
- Create: `server/platform-crud/PlatformCrud.csproj`
- Create: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`
- Create: `tests/PlatformCrud.Tests/Support/PlatformCrudModelDefinitions.cs`
- Modify: `ICP-Platform.sln`

- [x] **Step 1: 创建 `PlatformCrud.csproj`**

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

- [x] **Step 2: 创建 `PlatformCrud.Tests.csproj`**

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
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.1" />
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

- [x] **Step 3: 创建当前 `ModelDefinition` 签名测试辅助**

Create `tests/PlatformCrud.Tests/Support/PlatformCrudModelDefinitions.cs`:

```csharp
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;

namespace ICP.Platform.Crud.Tests.Support;

public static class PlatformCrudModelDefinitions
{
    public static ModelDefinition Person() => new(
        Name: "Person",
        TableName: "Person",
        Kind: ModelKind.Entity,
        Fields:
        [
            new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("PersonNo", "PersonNo", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("ENAME", "ENAME", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
            new FieldDefinition("CompanyId", "CompanyId", FieldKind.Reference, FieldValueType.ObjectId, "string", false, false, false),
            new FieldDefinition("DepartmentId", "DepartmentId", FieldKind.Reference, FieldValueType.ObjectId, "string", false, false, false),
            new FieldDefinition("EmploymentStatus", "EmploymentStatus", FieldKind.Scalar, FieldValueType.Enum, "string", false, false, false),
            new FieldDefinition("IsEnabled", "IsEnabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
            new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true)
        ],
        Indexes: []);
}
```

- [x] **Step 4: 加入 solution 并验证工程能构建**

Run:

```powershell
dotnet sln .\ICP-Platform.sln add .\server\platform-crud\PlatformCrud.csproj
dotnet sln .\ICP-Platform.sln add .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 5: 提交**

```powershell
git add .\ICP-Platform.sln .\server\platform-crud .\tests\PlatformCrud.Tests
git commit -m "功能：建立标准CRUD内核工程"
```

### Task 2: 定义 descriptor、读写模型和扩展点契约

**Files:**
- Create: `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`
- Create: `server/platform-crud/Descriptors/StandardCrudFieldSet.cs`
- Create: `server/platform-crud/Descriptors/StandardCrudDeletePolicy.cs`
- Create: `server/platform-crud/Models/StandardCrudListQuery.cs`
- Create: `server/platform-crud/Models/StandardCrudQueryCondition.cs`
- Create: `server/platform-crud/Models/StandardCrudListResult.cs`
- Create: `server/platform-crud/Models/StandardCrudDetail.cs`
- Create: `server/platform-crud/Models/StandardCrudSummaryBlock.cs`
- Create: `server/platform-crud/Models/StandardCrudOptionItem.cs`
- Create: `server/platform-crud/Models/StandardCrudError.cs`
- Create: `server/platform-crud/Models/StandardCrudMutationResult.cs`
- Create: `server/platform-crud/Abstractions/IStandardCrudQueryCustomizer.cs`
- Create: `server/platform-crud/Abstractions/IStandardCrudDisplayValueResolver.cs`
- Create: `server/platform-crud/Abstractions/IStandardCrudSummaryProvider.cs`
- Create: `server/platform-crud/Abstractions/IStandardCrudValidator.cs`
- Create: `server/platform-crud/Abstractions/IStandardCrudActionHandler.cs`
- Create: `server/platform-crud/Abstractions/IStandardCrudDeleteGuard.cs`
- Create: `tests/PlatformCrud.Tests/StandardCrudDescriptorTests.cs`

- [x] **Step 1: 写 descriptor 校验测试**

Create `tests/PlatformCrud.Tests/StandardCrudDescriptorTests.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Runtime;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudDescriptorTests
{
    [Fact]
    public void Validate_WhenDetailFieldsMissId_ThrowsStableMessage()
    {
        var descriptor = new StandardCrudDescriptor
        {
            ResourceKey = "organization.person",
            ModuleId = "Organization",
            ModelName = "Person",
            Route = "api/organization/persons",
            IdField = "EID",
            DisplayField = "ENAME",
            FieldSet = new StandardCrudFieldSet(
                ListFields: ["EID", "ENAME"],
                DetailFields: ["ENAME"],
                CreateFields: ["ENAME"],
                UpdateFields: ["ENAME"]),
            DeletePolicy = StandardCrudDeletePolicy.None
        };

        var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

        Assert.Equal("标准 CRUD 资源 organization.person 的 DetailFields 必须包含 IdField。", ex.Message);
    }
}
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudDescriptorTests -nr:false -v minimal
```

Expected:

```text
error CS0234: The type or namespace name 'Descriptors' does not exist
```

- [x] **Step 3: 创建 descriptor 类型**

Create `server/platform-crud/Descriptors/StandardCrudDeletePolicy.cs`:

```csharp
namespace ICP.Platform.Crud.Descriptors;

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

public sealed record StandardCrudFieldSet(
    IReadOnlyList<string> ListFields,
    IReadOnlyList<string> DetailFields,
    IReadOnlyList<string> CreateFields,
    IReadOnlyList<string> UpdateFields);
```

Create `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`:

```csharp
namespace ICP.Platform.Crud.Descriptors;

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
    public IReadOnlyList<string> DisplayValueFields { get; init; } = [];
}
```

- [x] **Step 4: 创建标准模型**

Create `server/platform-crud/Models/StandardCrudQueryCondition.cs`:

```csharp
namespace ICP.Platform.Crud.Models;

public sealed record StandardCrudQueryCondition(
    string Field,
    string Operator,
    object? Value);
```

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
    public string? ReasonKey { get; init; }
    public IReadOnlyList<StandardCrudFieldError> FieldErrors { get; init; } = [];
    public IReadOnlyDictionary<string, object?> Args { get; init; }
        = new Dictionary<string, object?>(StringComparer.Ordinal);
}

public sealed record StandardCrudFieldError(
    string Field,
    string Code,
    string Message);
```

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

- [x] **Step 5: 创建扩展点接口**

Create `server/platform-crud/Abstractions/IStandardCrudQueryCustomizer.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudQueryCustomizer
{
    string ResourceKey { get; }

    IReadOnlyList<StandardCrudQueryCondition> BuildConditions(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query);
}
```

Create `server/platform-crud/Abstractions/IStandardCrudDisplayValueResolver.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudDisplayValueResolver
{
    string ResourceKey { get; }

    Task<IReadOnlyDictionary<string, object?>> ResolveAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        CancellationToken cancellationToken = default);
}
```

Create `server/platform-crud/Abstractions/IStandardCrudSummaryProvider.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudSummaryProvider
{
    string ResourceKey { get; }
    string Key { get; }

    Task<StandardCrudSummaryBlock> BuildAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> detail,
        CancellationToken cancellationToken = default);
}
```

Create `server/platform-crud/Abstractions/IStandardCrudValidator.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudValidator
{
    string ResourceKey { get; }

    Task<StandardCrudError?> ValidateCreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);

    Task<StandardCrudError?> ValidateUpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);
}
```

Create `server/platform-crud/Abstractions/IStandardCrudActionHandler.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudActionHandler
{
    string ResourceKey { get; }
    string ActionKey { get; }

    Task<StandardCrudMutationResult> ExecuteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);
}
```

Create `server/platform-crud/Abstractions/IStandardCrudDeleteGuard.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudDeleteGuard
{
    string ResourceKey { get; }

    Task<StandardCrudError?> CanDeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);
}
```

- [x] **Step 6: 创建 descriptor 校验器**

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

        if (descriptor.DeletePolicy == StandardCrudDeletePolicy.Conditional && descriptor.ResourceKey.Length == 0)
        {
            throw new InvalidOperationException("标准 CRUD 条件删除资源必须声明 ResourceKey。");
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

- [x] **Step 7: 运行测试并提交**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

Commit:

```powershell
git add .\server\platform-crud .\tests\PlatformCrud.Tests
git commit -m "功能：定义标准CRUD契约与扩展点"
```

### Task 3: 实现仓储抽象、EF 读取、filter/sort 与 options

**Files:**
- Create: `server/platform-crud/Abstractions/IStandardCrudRepository.cs`
- Create: `server/platform-crud/Persistence/EfStandardCrudRepository.cs`
- Create: `tests/PlatformCrud.Tests/StandardCrudEfRepositoryTests.cs`

- [x] **Step 1: 写 EF list filter/sort/options 测试**

Create `tests/PlatformCrud.Tests/StandardCrudEfRepositoryTests.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudEfRepositoryTests
{
    [Fact]
    public async Task ListAsync_AppliesConditionsAndSorting()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("Person").AddRange(
            Row("person-1", "P001", "张三", true),
            Row("person-2", "P002", "李四", false));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext);
        var result = await repository.ListAsync(
            Descriptor(),
            new StandardCrudListQuery(Page: 1, PageSize: 20, SortBy: "PersonNo", SortDirection: "desc"),
            [new StandardCrudQueryCondition("IsEnabled", "=", true)]);

        Assert.Single(result.Items);
        Assert.Equal("P001", result.Items[0]["PersonNo"]);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task OptionsAsync_UsesConfiguredLabelFields()
    {
        await using var dbContext = BuildDbContext();
        dbContext.Set<Dictionary<string, object?>>("Person").Add(Row("person-1", "P001", "张三", true));
        await dbContext.SaveChangesAsync();

        var repository = new EfStandardCrudRepository(dbContext);
        var options = await repository.OptionsAsync(Descriptor(), new StandardCrudListQuery());

        var option = Assert.Single(options);
        Assert.Equal("person-1", option.Value);
        Assert.Equal("P001 张三", option.Label);
    }

    private static EfPlatformDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new EfPlatformDbContext(options, [PlatformCrudModelDefinitions.Person()]);
    }

    private static Dictionary<string, object?> Row(string id, string personNo, string name, bool isEnabled) => new(StringComparer.Ordinal)
    {
        ["EID"] = id,
        ["PersonNo"] = personNo,
        ["ENAME"] = name,
        ["CompanyId"] = "company-1",
        ["DepartmentId"] = "department-1",
        ["EmploymentStatus"] = "FullTime",
        ["IsEnabled"] = isEnabled,
        ["CreatedAt"] = DateTime.UtcNow
    };

    private static StandardCrudDescriptor Descriptor() => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        EnabledField = "IsEnabled",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "EmploymentStatus", "IsEnabled"],
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "EmploymentStatus", "IsEnabled", "CreatedAt"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId", "DepartmentId", "EmploymentStatus", "IsEnabled"],
            UpdateFields: ["ENAME", "CompanyId", "DepartmentId", "EmploymentStatus", "IsEnabled"]),
        OptionLabelFields: ["PersonNo", "ENAME"]
    };
}
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudEfRepositoryTests -nr:false -v minimal
```

Expected:

```text
error CS0246: The type or namespace name 'EfStandardCrudRepository' could not be found
```

- [x] **Step 3: 创建仓储抽象**

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
        IReadOnlyList<StandardCrudQueryCondition> conditions,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
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

- [x] **Step 4: 实现 EF 仓储读取、过滤、排序、options**

Create `server/platform-crud/Persistence/EfStandardCrudRepository.cs` with these required members:

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
        IReadOnlyList<StandardCrudQueryCondition> conditions,
        CancellationToken cancellationToken = default)
    {
        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
        IQueryable<Dictionary<string, object?>> source = _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName).AsNoTracking();

        source = ApplyConditions(source, conditions);
        source = ApplySort(source, descriptor, query);

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

    public async Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName)
            .AsNoTracking()
            .Take(query.PageSize <= 0 ? 50 : query.PageSize)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new StandardCrudOptionItem
        {
            Value = ReadString(row, descriptor.IdField),
            Label = string.Join(" ", descriptor.OptionLabelFields.Select(field => ReadString(row, field)).Where(value => value.Length > 0))
        }).ToList();
    }

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

    private static IQueryable<Dictionary<string, object?>> ApplyConditions(
        IQueryable<Dictionary<string, object?>> source,
        IReadOnlyList<StandardCrudQueryCondition> conditions)
    {
        foreach (var condition in conditions)
        {
            if (condition.Operator == "=" && condition.Value is not null)
            {
                var field = condition.Field;
                var value = condition.Value.ToString();
                source = source.Where(row => EF.Property<string>(row, field) == value);
            }

            if (condition.Operator == "contains" && condition.Value is not null)
            {
                var field = condition.Field;
                var value = condition.Value.ToString() ?? string.Empty;
                source = source.Where(row => EF.Property<string>(row, field).Contains(value));
            }
        }

        return source;
    }

    private static IQueryable<Dictionary<string, object?>> ApplySort(
        IQueryable<Dictionary<string, object?>> source,
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query)
    {
        var sortBy = string.IsNullOrWhiteSpace(query.SortBy) ? descriptor.DisplayField : query.SortBy;
        return string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase)
            ? source.OrderByDescending(row => EF.Property<string>(row, sortBy))
            : source.OrderBy(row => EF.Property<string>(row, sortBy));
    }

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

    private static string ReadString(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
}
```

- [x] **Step 5: 运行测试并提交**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudEfRepositoryTests -nr:false -v minimal
```

Expected:

```text
Passed!
```

Commit:

```powershell
git add .\server\platform-crud .\tests\PlatformCrud.Tests
git commit -m "功能：实现标准CRUD仓储读取与选项"
```

### Task 4: 实现标准服务读取编排、扩展点调用和摘要降级

**Files:**
- Create: `server/platform-crud/Abstractions/IStandardCrudService.cs`
- Create: `server/platform-crud/Runtime/StandardCrudService.cs`
- Create: `server/platform-crud/Runtime/StandardCrudEndpoint.cs`
- Create: `tests/PlatformCrud.Tests/Support/InMemoryStandardCrudRepository.cs`
- Create: `tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs`

- [x] **Step 1: 写服务读取编排测试**

Create `tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs` with tests covering query customizer, display resolver, summary fallback and options:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudServiceReadTests
{
    [Fact]
    public async Task ListAsync_AppliesQueryCustomizerAndDisplayValueResolver()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["CompanyId"] = "company-1" },
            new Dictionary<string, object?> { ["EID"] = "person-2", ["PersonNo"] = "P002", ["ENAME"] = "李四", ["CompanyId"] = "company-2" }
        ]);
        var service = new StandardCrudService(
            repository,
            [new KeywordQueryCustomizer()],
            [new CompanyDisplayResolver()],
            [],
            [],
            [],
            []);

        var result = await service.ListAsync(Descriptor(), new StandardCrudListQuery(Filters: new Dictionary<string, string?> { ["keyword"] = "P001" }));

        var row = Assert.Single(result.Items);
        Assert.Equal("公司一", row["CompanyName"]);
    }

    [Fact]
    public async Task GetDetailAsync_WhenSummaryProviderThrows_ReturnsDependencyUnavailable()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }
        ]);
        var service = new StandardCrudService(repository, [], [], [new FailingSummaryProvider()], [], [], []);

        var detail = await service.GetDetailAsync(Descriptor(summaryKeys: ["userLink"]), "person-1");

        Assert.NotNull(detail);
        Assert.Equal("dependencyUnavailable", detail!.Summaries["userLink"].Status);
    }

    [Fact]
    public async Task OptionsAsync_ReturnsRepositoryOptions()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }
        ]);
        var service = new StandardCrudService(repository, [], [], [], [], [], []);

        var options = await service.OptionsAsync(Descriptor(), new StandardCrudListQuery());

        var option = Assert.Single(options);
        Assert.Equal("person-1", option.Value);
        Assert.Equal("P001 张三", option.Label);
    }

    private static StandardCrudDescriptor Descriptor(IReadOnlyList<string>? summaryKeys = null) => new()
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "PersonNo", "ENAME", "CompanyId"],
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId"],
            UpdateFields: ["ENAME", "CompanyId"]),
        OptionLabelFields: ["PersonNo", "ENAME"],
        SummaryBlockKeys: summaryKeys ?? []
    };

    private sealed class KeywordQueryCustomizer : IStandardCrudQueryCustomizer
    {
        public string ResourceKey => "organization.person";

        public IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query)
        {
            return query.Filters?.TryGetValue("keyword", out var keyword) == true && !string.IsNullOrWhiteSpace(keyword)
                ? [new StandardCrudQueryCondition("PersonNo", "contains", keyword)]
                : [];
        }
    }

    private sealed class CompanyDisplayResolver : IStandardCrudDisplayValueResolver
    {
        public string ResourceKey => "organization.person";

        public Task<IReadOnlyDictionary<string, object?>> ResolveAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>
            {
                ["CompanyName"] = row.TryGetValue("CompanyId", out var value) && Equals(value, "company-1") ? "公司一" : string.Empty
            });
        }
    }

    private sealed class FailingSummaryProvider : IStandardCrudSummaryProvider
    {
        public string ResourceKey => "organization.person";
        public string Key => "userLink";

        public Task<StandardCrudSummaryBlock> BuildAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> detail, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("summary dependency unavailable");
        }
    }
}
```

- [x] **Step 2: 创建测试仓储**

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

    public Task<StandardCrudListResult> ListAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, IReadOnlyList<StandardCrudQueryCondition> conditions, CancellationToken cancellationToken = default)
    {
        IEnumerable<Dictionary<string, object?>> filtered = _rows;
        foreach (var condition in conditions)
        {
            filtered = filtered.Where(row => row.TryGetValue(condition.Field, out var value) && value?.ToString()?.Contains(condition.Value?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true);
        }

        var items = filtered.Select(row => Project(row, descriptor.FieldSet.ListFields)).ToList();
        return Task.FromResult(new StandardCrudListResult(items, items.Count, query.Page <= 0 ? 1 : query.Page, query.PageSize <= 0 ? 20 : query.PageSize, query.SortBy, query.SortDirection));
    }

    public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
    {
        var row = _rows.FirstOrDefault(item => string.Equals(item[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
        return Task.FromResult<IReadOnlyDictionary<string, object?>?>(row is null ? null : Project(row, descriptor.FieldSet.DetailFields));
    }

    public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StandardCrudOptionItem> options = _rows.Select(row => new StandardCrudOptionItem
        {
            Value = row[descriptor.IdField]?.ToString() ?? string.Empty,
            Label = string.Join(" ", descriptor.OptionLabelFields.Select(field => row.TryGetValue(field, out var value) ? value?.ToString() : null).Where(value => !string.IsNullOrWhiteSpace(value)))
        }).ToList();

        return Task.FromResult(options);
    }

    public Task<IReadOnlyDictionary<string, object?>> CreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        var row = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        if (!row.ContainsKey(descriptor.IdField))
        {
            row[descriptor.IdField] = Guid.NewGuid().ToString("D");
        }

        _rows.Add(row);
        return Task.FromResult(Project(row, descriptor.FieldSet.DetailFields));
    }

    public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
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

        return Task.FromResult<IReadOnlyDictionary<string, object?>?>(Project(row, descriptor.FieldSet.DetailFields));
    }

    public Task<bool> DeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_rows.RemoveAll(row => string.Equals(row[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal)) > 0);
    }

    private static IReadOnlyDictionary<string, object?> Project(IReadOnlyDictionary<string, object?> row, IReadOnlyList<string> fields)
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

- [x] **Step 3: 运行测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudServiceReadTests -nr:false -v minimal
```

Expected:

```text
error CS0246: The type or namespace name 'StandardCrudService' could not be found
```

- [x] **Step 4: 创建服务接口和实现**

Create `server/platform-crud/Abstractions/IStandardCrudService.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Abstractions;

public interface IStandardCrudService
{
    Task<StandardCrudListResult> ListAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default);
    Task<StandardCrudDetail?> GetDetailAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default);
    Task<StandardCrudMutationResult> CreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default);
    Task<StandardCrudMutationResult> UpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default);
    Task<StandardCrudMutationResult> DisableAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default);
    Task<StandardCrudMutationResult> DeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default);
}
```

Create `server/platform-crud/Runtime/StandardCrudService.cs` with these required behaviors:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Runtime;

public sealed class StandardCrudService : IStandardCrudService
{
    private readonly IStandardCrudRepository _repository;
    private readonly IReadOnlyDictionary<string, IStandardCrudQueryCustomizer> _queryCustomizers;
    private readonly IReadOnlyDictionary<string, IStandardCrudDisplayValueResolver> _displayResolvers;
    private readonly IReadOnlyDictionary<(string ResourceKey, string Key), IStandardCrudSummaryProvider> _summaryProviders;
    private readonly IReadOnlyDictionary<string, IStandardCrudValidator> _validators;
    private readonly IReadOnlyDictionary<(string ResourceKey, string ActionKey), IStandardCrudActionHandler> _actionHandlers;
    private readonly IReadOnlyDictionary<string, IStandardCrudDeleteGuard> _deleteGuards;

    public StandardCrudService(
        IStandardCrudRepository repository,
        IEnumerable<IStandardCrudQueryCustomizer> queryCustomizers,
        IEnumerable<IStandardCrudDisplayValueResolver> displayResolvers,
        IEnumerable<IStandardCrudSummaryProvider> summaryProviders,
        IEnumerable<IStandardCrudValidator> validators,
        IEnumerable<IStandardCrudActionHandler> actionHandlers,
        IEnumerable<IStandardCrudDeleteGuard> deleteGuards)
    {
        _repository = repository;
        _queryCustomizers = queryCustomizers.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _displayResolvers = displayResolvers.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _summaryProviders = summaryProviders.ToDictionary(item => (item.ResourceKey, item.Key));
        _validators = validators.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
        _actionHandlers = actionHandlers.ToDictionary(item => (item.ResourceKey, item.ActionKey));
        _deleteGuards = deleteGuards.ToDictionary(item => item.ResourceKey, StringComparer.Ordinal);
    }

    public async Task<StandardCrudListResult> ListAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var conditions = BuildConditions(descriptor, query);
        var result = await _repository.ListAsync(descriptor, query, conditions, cancellationToken);
        var items = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var row in result.Items)
        {
            items.Add(await ApplyDisplayValuesAsync(descriptor, row, cancellationToken));
        }

        return result with { Items = items };
    }

    public async Task<StandardCrudDetail?> GetDetailAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        var row = await _repository.GetByIdAsync(descriptor, id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var fields = await ApplyDisplayValuesAsync(descriptor, row, cancellationToken);
        var summaries = new Dictionary<string, StandardCrudSummaryBlock>(StringComparer.Ordinal);
        foreach (var key in descriptor.SummaryBlockKeys)
        {
            summaries[key] = await BuildSummaryAsync(descriptor, fields, key, cancellationToken);
        }

        return new StandardCrudDetail { Fields = fields, Summaries = summaries };
    }

    public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default)
    {
        StandardCrudDescriptorValidator.Validate(descriptor);
        return _repository.OptionsAsync(descriptor, query, cancellationToken);
    }

    public Task<StandardCrudMutationResult> CreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 409,
            Code = "StandardCrud.CreateNotEnabled",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 尚未启用创建主干。",
            ReasonKey = "standardCrud.createNotEnabled"
        }));
    }

    public Task<StandardCrudMutationResult> UpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 409,
            Code = "StandardCrud.UpdateNotEnabled",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 尚未启用更新主干。",
            ReasonKey = "standardCrud.updateNotEnabled"
        }));
    }

    public Task<StandardCrudMutationResult> DisableAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 409,
            Code = "StandardCrud.DisableNotEnabled",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 尚未启用停用主干。",
            ReasonKey = "standardCrud.disableNotEnabled"
        }));
    }

    public Task<StandardCrudMutationResult> DeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 409,
            Code = "StandardCrud.DeleteNotEnabled",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 尚未启用删除主干。",
            ReasonKey = "standardCrud.deleteNotEnabled"
        }));
    }

    private IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query)
    {
        return _queryCustomizers.TryGetValue(descriptor.ResourceKey, out var customizer)
            ? customizer.BuildConditions(descriptor, query)
            : [];
    }

    private async Task<IReadOnlyDictionary<string, object?>> ApplyDisplayValuesAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>(row, StringComparer.Ordinal);
        if (_displayResolvers.TryGetValue(descriptor.ResourceKey, out var resolver))
        {
            foreach (var pair in await resolver.ResolveAsync(descriptor, row, cancellationToken))
            {
                values[pair.Key] = pair.Value;
            }
        }

        return values;
    }

    private async Task<StandardCrudSummaryBlock> BuildSummaryAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> row, string key, CancellationToken cancellationToken)
    {
        if (!_summaryProviders.TryGetValue((descriptor.ResourceKey, key), out var provider))
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

    public Task<StandardCrudListResult> ListAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default) =>
        _service.ListAsync(descriptor, query, cancellationToken);

    public Task<StandardCrudDetail?> GetDetailAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default) =>
        _service.GetDetailAsync(descriptor, id, cancellationToken);

    public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(StandardCrudDescriptor descriptor, StandardCrudListQuery query, CancellationToken cancellationToken = default) =>
        _service.OptionsAsync(descriptor, query, cancellationToken);

    public Task<StandardCrudMutationResult> CreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default) =>
        _service.CreateAsync(descriptor, values, cancellationToken);

    public Task<StandardCrudMutationResult> UpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default) =>
        _service.UpdateAsync(descriptor, id, values, cancellationToken);

    public Task<StandardCrudMutationResult> DisableAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default) =>
        _service.DisableAsync(descriptor, id, cancellationToken);

    public Task<StandardCrudMutationResult> DeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default) =>
        _service.DeleteAsync(descriptor, id, cancellationToken);
}
```

- [x] **Step 5: 运行测试并提交**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudServiceReadTests -nr:false -v minimal
```

Expected:

```text
Passed!
```

Commit:

```powershell
git add .\server\platform-crud .\tests\PlatformCrud.Tests
git commit -m "功能：实现标准CRUD读取编排"
```

### Task 5: 接入 Person descriptor、查询、显示值和摘要 provider

**Files:**
- Create: `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`
- Create: `server/modules/Organization/Crud/PersonCrudQueryCustomizer.cs`
- Create: `server/modules/Organization/Crud/PersonCrudDisplayValueResolver.cs`
- Create: `server/modules/Organization/Crud/PersonUserLinkSummaryProvider.cs`
- Create: `server/modules/Organization/Crud/PersonAuthorizationSummaryProvider.cs`
- Modify: `server/modules/Organization/OrganizationModule.csproj`
- Modify: `tests/Organization.Tests/Organization.Tests.csproj`
- Modify: `tests/Organization.Tests/PersonListContractTests.cs`

- [x] **Step 1: 引用 PlatformCrud**

Modify `server/modules/Organization/OrganizationModule.csproj`:

```xml
<ProjectReference Include="..\..\platform-crud\PlatformCrud.csproj" />
```

Modify `tests/Organization.Tests/Organization.Tests.csproj`:

```xml
<ProjectReference Include="..\..\server\platform-crud\PlatformCrud.csproj" />
```

- [x] **Step 2: 写 Person descriptor 和扩展点合同测试**

Append to `tests/Organization.Tests/PersonListContractTests.cs`:

```csharp
[Fact]
public void PersonCrudDescriptor_DeclaresQueryDisplaySummaryAndConditionalDelete()
{
    var descriptor = PersonCrudDescriptorFactory.Create();

    Assert.Equal("organization.person", descriptor.ResourceKey);
    Assert.Equal("Person", descriptor.ModelName);
    Assert.Equal("api/organization/persons", descriptor.Route);
    Assert.Equal(StandardCrudDeletePolicy.Conditional, descriptor.DeletePolicy);
    Assert.Contains("CompanyName", descriptor.DisplayValueFields);
    Assert.Contains("DepartmentName", descriptor.DisplayValueFields);
    Assert.Contains("userLink", descriptor.SummaryBlockKeys);
    Assert.Contains("authorizationSummary", descriptor.SummaryBlockKeys);
}

[Fact]
public void PersonCrudQueryCustomizer_MapsKeywordToPersonNoAndName()
{
    var customizer = new PersonCrudQueryCustomizer();
    var conditions = customizer.BuildConditions(
        PersonCrudDescriptorFactory.Create(),
        new StandardCrudListQuery(Filters: new Dictionary<string, string?> { ["keyword"] = "张" }));

    Assert.Contains(conditions, condition => condition.Field == "PersonNo" && condition.Operator == "contains");
    Assert.Contains(conditions, condition => condition.Field == "ENAME" && condition.Operator == "contains");
}
```

Add usings:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Crud;
```

- [x] **Step 3: 创建 Person descriptor 和 query customizer**

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
        DisplayValueFields: ["CompanyName", "DepartmentName", "PositionName"],
        DeletePolicy = StandardCrudDeletePolicy.Conditional
    };
}
```

Create `server/modules/Organization/Crud/PersonCrudQueryCustomizer.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudQueryCustomizer : IStandardCrudQueryCustomizer
{
    public string ResourceKey => "organization.person";

    public IReadOnlyList<StandardCrudQueryCondition> BuildConditions(StandardCrudDescriptor descriptor, StandardCrudListQuery query)
    {
        var conditions = new List<StandardCrudQueryCondition>();
        var filters = query.Filters ?? new Dictionary<string, string?>();

        if (filters.TryGetValue("keyword", out var keyword) && !string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add(new StandardCrudQueryCondition("PersonNo", "contains", keyword));
            conditions.Add(new StandardCrudQueryCondition("ENAME", "contains", keyword));
        }

        AddEquals(filters, conditions, "companyId", "CompanyId");
        AddEquals(filters, conditions, "departmentId", "DepartmentId");
        AddEquals(filters, conditions, "employmentStatus", "EmploymentStatus");
        return conditions;
    }

    private static void AddEquals(
        IReadOnlyDictionary<string, string?> filters,
        ICollection<StandardCrudQueryCondition> conditions,
        string filterKey,
        string field)
    {
        if (filters.TryGetValue(filterKey, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            conditions.Add(new StandardCrudQueryCondition(field, "=", value));
        }
    }
}
```

- [x] **Step 4: 创建显示值 resolver**

Create `server/modules/Organization/Crud/PersonCrudDisplayValueResolver.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudDisplayValueResolver : IStandardCrudDisplayValueResolver
{
    private readonly ICompanyRepository _companyRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IPositionRepository _positionRepository;

    public PersonCrudDisplayValueResolver(
        ICompanyRepository companyRepository,
        IDepartmentRepository departmentRepository,
        IPositionRepository positionRepository)
    {
        _companyRepository = companyRepository;
        _departmentRepository = departmentRepository;
        _positionRepository = positionRepository;
    }

    public string ResourceKey => "organization.person";

    public async Task<IReadOnlyDictionary<string, object?>> ResolveAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> row,
        CancellationToken cancellationToken = default)
    {
        var companyId = ReadGuid(row, "CompanyId");
        var departmentId = ReadGuid(row, "DepartmentId");
        var positionId = ReadGuid(row, "PositionId");

        var company = companyId.HasValue ? await _companyRepository.GetByIdAsync(companyId.Value, cancellationToken) : null;
        var department = departmentId.HasValue ? await _departmentRepository.GetByIdAsync(departmentId.Value, cancellationToken) : null;
        var position = positionId.HasValue ? await _positionRepository.GetByIdAsync(positionId.Value, cancellationToken) : null;

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["CompanyName"] = company?.Name ?? string.Empty,
            ["DepartmentName"] = department?.Name ?? string.Empty,
            ["PositionName"] = position?.Name ?? string.Empty
        };
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, object?> row, string field)
    {
        return row.TryGetValue(field, out var value) && Guid.TryParse(value?.ToString(), out var guid)
            ? guid
            : null;
    }
}
```

- [x] **Step 5: 创建 summary providers**

Create `server/modules/Organization/Crud/PersonUserLinkSummaryProvider.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonUserLinkSummaryProvider : IStandardCrudSummaryProvider
{
    private readonly IPersonUserLinkRepository _repository;

    public PersonUserLinkSummaryProvider(IPersonUserLinkRepository repository)
    {
        _repository = repository;
    }

    public string ResourceKey => "organization.person";
    public string Key => "userLink";

    public async Task<StandardCrudSummaryBlock> BuildAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> detail, CancellationToken cancellationToken = default)
    {
        var personId = Guid.Parse(detail[descriptor.IdField]!.ToString()!);
        var link = await _repository.GetByPersonIdAsync(personId, cancellationToken);
        if (link is null)
        {
            return new StandardCrudSummaryBlock { Status = "notConfigured" };
        }

        return new StandardCrudSummaryBlock
        {
            Status = "ready",
            Data = new PersonUserLinkDto
            {
                Id = link.PersonId.ToString("D"),
                PersonId = link.PersonId.ToString("D"),
                UserId = link.UserId,
                CreatedAt = link.LinkedAt,
                LinkedAt = link.LinkedAt,
                LinkedBy = link.LinkedBy
            }
        };
    }
}
```

Create `server/modules/Organization/Crud/PersonAuthorizationSummaryProvider.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Repositories;
using ICP.Platform.Modules.Organization.Services;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonAuthorizationSummaryProvider : IStandardCrudSummaryProvider
{
    private readonly IPersonUserLinkRepository _userLinkRepository;
    private readonly IAuthorizationSummaryReader _authorizationSummaryReader;

    public PersonAuthorizationSummaryProvider(
        IPersonUserLinkRepository userLinkRepository,
        IAuthorizationSummaryReader authorizationSummaryReader)
    {
        _userLinkRepository = userLinkRepository;
        _authorizationSummaryReader = authorizationSummaryReader;
    }

    public string ResourceKey => "organization.person";
    public string Key => "authorizationSummary";

    public async Task<StandardCrudSummaryBlock> BuildAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> detail, CancellationToken cancellationToken = default)
    {
        var personId = Guid.Parse(detail[descriptor.IdField]!.ToString()!);
        var link = await _userLinkRepository.GetByPersonIdAsync(personId, cancellationToken);
        if (link is null)
        {
            return new StandardCrudSummaryBlock { Status = "notConfigured" };
        }

        var roles = await _authorizationSummaryReader.GetRoleNamesAsync(link.UserId, cancellationToken);
        return new StandardCrudSummaryBlock { Status = "ready", Data = roles };
    }
}
```

- [x] **Step 6: 运行测试并提交**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "PersonCrudDescriptor_DeclaresQueryDisplaySummaryAndConditionalDelete|PersonCrudQueryCustomizer_MapsKeywordToPersonNoAndName" -nr:false -v minimal
```

Expected:

```text
Passed!
```

Commit:

```powershell
git add .\server\modules\Organization .\tests\Organization.Tests
git commit -m "功能：接入人员标准CRUD扩展点"
```

### Task 6: 实现 create/update 字段白名单、disable 和 conditional delete

**Files:**
- Modify: `server/platform-crud/Runtime/StandardCrudService.cs`
- Create: `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`

- [x] **Step 1: 写 write/disable/delete guard 测试**

Create `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Crud.Tests.Support;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudServiceWriteTests
{
    [Fact]
    public async Task CreateAsync_OnlyPersistsCreateFieldsAndReturnsDetail()
    {
        var service = Service();

        var result = await service.CreateAsync(Descriptor(), new Dictionary<string, object?>
        {
            ["PersonNo"] = "P001",
            ["ENAME"] = "张三",
            ["Unexpected"] = "ignored"
        });

        Assert.True(result.Succeeded);
        Assert.False(result.Detail!.Fields.ContainsKey("Unexpected"));
        Assert.Equal("张三", result.Detail.Fields["ENAME"]);
    }

    [Fact]
    public async Task DisableAsync_SetsEnabledFieldFalse()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true }
        ]);
        var service = Service(repository);

        var result = await service.DisableAsync(Descriptor(), "person-1");

        Assert.True(result.Succeeded);
        Assert.False((bool)result.Detail!.Fields["IsEnabled"]!);
    }

    [Fact]
    public async Task DeleteAsync_WhenConditionalWithoutGuard_FailsClosed()
    {
        var service = Service();

        var result = await service.DeleteAsync(Descriptor(StandardCrudDeletePolicy.Conditional), "person-1");

        Assert.False(result.Succeeded);
        Assert.Equal("StandardCrud.DeleteGuardMissing", result.Error!.Code);
    }

    [Fact]
    public async Task DeleteAsync_WhenGuardBlocks_ReturnsConflict()
    {
        var repository = new InMemoryStandardCrudRepository(
        [
            new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }
        ]);
        var service = new StandardCrudService(repository, [], [], [], [], [], [new BlockingDeleteGuard()]);

        var result = await service.DeleteAsync(Descriptor(StandardCrudDeletePolicy.Conditional), "person-1");

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("Organization.Person.DeleteBlockedByUserLink", result.Error.Code);
    }

    private static StandardCrudService Service(InMemoryStandardCrudRepository? repository = null) =>
        new(repository ?? new InMemoryStandardCrudRepository([]), [], [], [], [], [], []);

    private static StandardCrudDescriptor Descriptor(StandardCrudDeletePolicy policy = StandardCrudDeletePolicy.None) => new()
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
            CreateFields: ["PersonNo", "ENAME"],
            UpdateFields: ["ENAME", "IsEnabled"]),
        DeletePolicy = policy
    };

    private sealed class BlockingDeleteGuard : IStandardCrudDeleteGuard
    {
        public string ResourceKey => "organization.person";

        public Task<StandardCrudError?> CanDeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StandardCrudError?>(new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.DeleteBlockedByUserLink",
                Message = "当前人员已绑定账号，不能删除。",
                ReasonKey = "organization.person.deleteBlockedByUserLink"
            });
        }
    }
}
```

- [x] **Step 2: 实现服务写操作**

Modify `server/platform-crud/Runtime/StandardCrudService.cs`:

```csharp
public async Task<StandardCrudMutationResult> CreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
{
    StandardCrudDescriptorValidator.Validate(descriptor);
    var allowed = FilterFields(values, descriptor.FieldSet.CreateFields);
    if (_validators.TryGetValue(descriptor.ResourceKey, out var validator))
    {
        var error = await validator.ValidateCreateAsync(descriptor, allowed, cancellationToken);
        if (error is not null)
        {
            return StandardCrudMutationResult.Failure(error);
        }
    }

    var created = await _repository.CreateAsync(descriptor, allowed, cancellationToken);
    return StandardCrudMutationResult.Success(await BuildDetailFromRowAsync(descriptor, created, cancellationToken));
}

public async Task<StandardCrudMutationResult> UpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
{
    StandardCrudDescriptorValidator.Validate(descriptor);
    var allowed = FilterFields(values, descriptor.FieldSet.UpdateFields);
    if (_validators.TryGetValue(descriptor.ResourceKey, out var validator))
    {
        var error = await validator.ValidateUpdateAsync(descriptor, id, allowed, cancellationToken);
        if (error is not null)
        {
            return StandardCrudMutationResult.Failure(error);
        }
    }

    var updated = await _repository.UpdateAsync(descriptor, id, allowed, cancellationToken);
    return updated is null
        ? StandardCrudMutationResult.Failure(NotFound(descriptor, id))
        : StandardCrudMutationResult.Success(await BuildDetailFromRowAsync(descriptor, updated, cancellationToken));
}

public async Task<StandardCrudMutationResult> DisableAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
{
    StandardCrudDescriptorValidator.Validate(descriptor);
    if (string.IsNullOrWhiteSpace(descriptor.EnabledField))
    {
        return StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 409,
            Code = "StandardCrud.EnabledFieldMissing",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 未声明启停字段。",
            ReasonKey = "standardCrud.enabledFieldMissing"
        });
    }

    return await UpdateAsync(descriptor, id, new Dictionary<string, object?> { [descriptor.EnabledField] = false }, cancellationToken);
}

public async Task<StandardCrudMutationResult> DeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
{
    StandardCrudDescriptorValidator.Validate(descriptor);
    if (descriptor.DeletePolicy == StandardCrudDeletePolicy.None)
    {
        return StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 409,
            Code = "StandardCrud.DeleteNotSupported",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 不支持删除。",
            ReasonKey = "standardCrud.deleteNotSupported"
        });
    }

    if (descriptor.DeletePolicy == StandardCrudDeletePolicy.Conditional && !_deleteGuards.TryGetValue(descriptor.ResourceKey, out var guard))
    {
        return StandardCrudMutationResult.Failure(new StandardCrudError
        {
            StatusCode = 409,
            Code = "StandardCrud.DeleteGuardMissing",
            Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 使用条件删除但未注册删除守卫。",
            ReasonKey = "standardCrud.deleteGuardMissing"
        });
    }

    if (_deleteGuards.TryGetValue(descriptor.ResourceKey, out var deleteGuard))
    {
        var guardError = await deleteGuard.CanDeleteAsync(descriptor, id, cancellationToken);
        if (guardError is not null)
        {
            return StandardCrudMutationResult.Failure(guardError);
        }
    }

    var deleted = await _repository.DeleteAsync(descriptor, id, cancellationToken);
    return deleted ? StandardCrudMutationResult.Success() : StandardCrudMutationResult.Failure(NotFound(descriptor, id));
}

private async Task<StandardCrudDetail> BuildDetailFromRowAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken)
{
    var fields = await ApplyDisplayValuesAsync(descriptor, row, cancellationToken);
    var summaries = new Dictionary<string, StandardCrudSummaryBlock>(StringComparer.Ordinal);
    foreach (var key in descriptor.SummaryBlockKeys)
    {
        summaries[key] = await BuildSummaryAsync(descriptor, fields, key, cancellationToken);
    }

    return new StandardCrudDetail { Fields = fields, Summaries = summaries };
}

private static IReadOnlyDictionary<string, object?> FilterFields(IReadOnlyDictionary<string, object?> values, IReadOnlyList<string> allowedFields)
{
    var filtered = new Dictionary<string, object?>(StringComparer.Ordinal);
    foreach (var field in allowedFields)
    {
        if (values.TryGetValue(field, out var value))
        {
            filtered[field] = value;
        }
    }

    return filtered;
}

private static StandardCrudError NotFound(StandardCrudDescriptor descriptor, string id) => new()
{
    StatusCode = 404,
    Code = "StandardCrud.NotFound",
    Message = $"标准 CRUD 资源 {descriptor.ResourceKey} 的对象 {id} 不存在。",
    ReasonKey = "standardCrud.notFound"
};
```

- [x] **Step 3: 运行测试并提交**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudServiceWriteTests -nr:false -v minimal
```

Expected:

```text
Passed!
```

Commit:

```powershell
git add .\server\platform-crud .\tests\PlatformCrud.Tests
git commit -m "功能：实现标准CRUD写操作与删除守卫"
```

### Task 7: 接入 Person 校验、删除守卫和 DI

**Files:**
- Create: `server/modules/Organization/Crud/PersonCrudValidator.cs`
- Create: `server/modules/Organization/Crud/PersonCrudDeleteGuard.cs`
- Create: `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`
- Modify: `server/modules/Organization/OrganizationModule.cs`
- Modify: `tests/Organization.Tests/PersonListContractTests.cs`

- [x] **Step 1: 写 Person delete guard 合同测试**

Append to `tests/Organization.Tests/PersonListContractTests.cs`:

```csharp
[Fact]
public async Task PersonCrudDeleteGuard_WhenPersonHasUserLink_ReturnsConflict()
{
    var personId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var guard = new PersonCrudDeleteGuard(new InMemoryPersonUserLinkRepositoryWithLink(personId));

    var error = await guard.CanDeleteAsync(PersonCrudDescriptorFactory.Create(), personId.ToString("D"));

    Assert.NotNull(error);
    Assert.Equal("Organization.Person.DeleteBlockedByUserLink", error!.Code);
}
```

Add a small test-only repository class in the same test file:

```csharp
private sealed class InMemoryPersonUserLinkRepositoryWithLink : IPersonUserLinkRepository
{
    private readonly Guid _personId;

    public InMemoryPersonUserLinkRepositoryWithLink(Guid personId)
    {
        _personId = personId;
    }

    public Task<PersonUserLink?> GetByPersonIdAsync(Guid personId, CancellationToken cancellationToken = default) =>
        Task.FromResult<PersonUserLink?>(personId == _personId ? new PersonUserLink
        {
            PersonId = personId,
            UserId = "user-1",
            LinkedAt = DateTime.UtcNow,
            LinkedBy = "test"
        } : null);

    public Task<bool> ExistsByUserIdAsync(string userId, Guid? excludedPersonId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task UpsertAsync(PersonUserLink link, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
```

- [x] **Step 2: 创建 Person validator 和 delete guard**

Create `server/modules/Organization/Crud/PersonCrudValidator.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudValidator : IStandardCrudValidator
{
    private readonly IPersonRepository _repository;

    public PersonCrudValidator(IPersonRepository repository)
    {
        _repository = repository;
    }

    public string ResourceKey => "organization.person";

    public async Task<StandardCrudError?> ValidateCreateAsync(StandardCrudDescriptor descriptor, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        var companyId = ReadGuid(values, "CompanyId");
        var personNo = values.TryGetValue("PersonNo", out var value) ? value?.ToString() ?? string.Empty : string.Empty;
        if (await _repository.ExistsPersonNoAsync(companyId, personNo, null, cancellationToken))
        {
            return Conflict("Organization.Person.PersonNoDuplicated", "同一公司下人员工号必须唯一。");
        }

        return null;
    }

    public async Task<StandardCrudError?> ValidateUpdateAsync(StandardCrudDescriptor descriptor, string id, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        if (values.ContainsKey("PersonNo"))
        {
            return Conflict("Organization.Person.PersonNoReadonly", "人员工号不允许通过标准更新接口修改。");
        }

        await Task.CompletedTask;
        return null;
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, object?> values, string field) =>
        values.TryGetValue(field, out var value) && Guid.TryParse(value?.ToString(), out var guid) ? guid : null;

    private static StandardCrudError Conflict(string code, string message) => new()
    {
        StatusCode = 409,
        Code = code,
        Message = message,
        ReasonKey = code
    };
}
```

Create `server/modules/Organization/Crud/PersonCrudDeleteGuard.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Modules.Organization.Repositories;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudDeleteGuard : IStandardCrudDeleteGuard
{
    private readonly IPersonUserLinkRepository _userLinkRepository;

    public PersonCrudDeleteGuard(IPersonUserLinkRepository userLinkRepository)
    {
        _userLinkRepository = userLinkRepository;
    }

    public string ResourceKey => "organization.person";

    public async Task<StandardCrudError?> CanDeleteAsync(StandardCrudDescriptor descriptor, string id, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(id, out var personId))
        {
            return new StandardCrudError
            {
                StatusCode = 400,
                Code = "Organization.Person.InvalidId",
                Message = "人员标识必须是有效 GUID。",
                ReasonKey = "organization.person.invalidId"
            };
        }

        var link = await _userLinkRepository.GetByPersonIdAsync(personId, cancellationToken);
        return link is null
            ? null
            : new StandardCrudError
            {
                StatusCode = 409,
                Code = "Organization.Person.DeleteBlockedByUserLink",
                Message = "当前人员已绑定账号，不能删除。",
                ReasonKey = "organization.person.deleteBlockedByUserLink"
            };
    }
}
```

- [x] **Step 3: 创建 DI 注册**

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

Modify `server/modules/Organization/OrganizationModule.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Modules.Organization.Crud;
```

Inside `ConfigureServices` add:

```csharp
services.AddStandardCrudKernel();
services.AddScoped<IStandardCrudQueryCustomizer, PersonCrudQueryCustomizer>();
services.AddScoped<IStandardCrudDisplayValueResolver, PersonCrudDisplayValueResolver>();
services.AddScoped<IStandardCrudSummaryProvider, PersonUserLinkSummaryProvider>();
services.AddScoped<IStandardCrudSummaryProvider, PersonAuthorizationSummaryProvider>();
services.AddScoped<IStandardCrudValidator, PersonCrudValidator>();
services.AddScoped<IStandardCrudDeleteGuard, PersonCrudDeleteGuard>();
```

- [x] **Step 4: 运行测试并提交**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "PersonCrudDeleteGuard_WhenPersonHasUserLink_ReturnsConflict|PersonListContractTests" -nr:false -v minimal
```

Expected:

```text
Passed!
```

Commit:

```powershell
git add .\server\platform-crud .\server\modules\Organization .\tests\Organization.Tests
git commit -m "功能：注册人员标准CRUD守卫与扩展"
```

### Task 8: 切换 PersonsController 到标准 CRUD endpoint

**Files:**
- Modify: `server/modules/Organization/Controllers/PersonsController.cs`
- Modify: `tests/Organization.Tests/PersonListContractTests.cs`

- [x] **Step 1: 写 Controller endpoint 合同测试**

Append to `tests/Organization.Tests/PersonListContractTests.cs`:

```csharp
[Fact]
public void PersonsController_DeclaresOptionsAndDeleteEndpoints()
{
    var controllerType = typeof(PersonsController);

    var optionsAction = controllerType.GetMethod("Options");
    var deleteAction = controllerType.GetMethod("Delete");

    Assert.NotNull(optionsAction);
    Assert.NotNull(deleteAction);
    Assert.Contains(optionsAction!.GetCustomAttributes<HttpGetAttribute>(), attribute => attribute.Template == "options");
    Assert.Contains(deleteAction!.GetCustomAttributes<HttpDeleteAttribute>(), attribute => attribute.Template == "{id:guid}");
}
```

- [x] **Step 2: 修改 Controller 构造函数**

Modify `server/modules/Organization/Controllers/PersonsController.cs` constructor:

```csharp
private readonly StandardCrudEndpoint _standardCrudEndpoint;
private static readonly StandardCrudDescriptor PersonCrudDescriptor = PersonCrudDescriptorFactory.Create();

public PersonsController(
    IPersonService personService,
    IIdentityUserDirectory identityUserDirectory,
    StandardCrudEndpoint standardCrudEndpoint)
{
    _personService = personService;
    _identityUserDirectory = identityUserDirectory;
    _standardCrudEndpoint = standardCrudEndpoint;
}
```

Add usings:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Modules.Organization.Crud;
```

- [x] **Step 3: 切换 list/detail/options/disable/delete**

Replace `GetList`, `Get`, `Disable`, and add `Options`, `Delete`:

```csharp
[HttpGet]
public async Task<ActionResult<IReadOnlyList<PersonListItemDto>>> GetList(
    [FromQuery] string? keyword,
    [FromQuery] Guid? companyId,
    [FromQuery] Guid? departmentId,
    [FromQuery] EmploymentStatus? employmentStatus,
    CancellationToken cancellationToken = default)
{
    var result = await _standardCrudEndpoint.ListAsync(
        PersonCrudDescriptor,
        new StandardCrudListQuery(
            Page: 1,
            PageSize: int.MaxValue,
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

[HttpGet("{id:guid}")]
public async Task<ActionResult<PersonDetailDto>> Get(Guid id, CancellationToken cancellationToken = default)
{
    var detail = await _standardCrudEndpoint.GetDetailAsync(PersonCrudDescriptor, id.ToString("D"), cancellationToken);
    return detail is null ? NotFound() : Ok(MapDetail(detail));
}

[HttpGet("options")]
public async Task<ActionResult<IReadOnlyList<StandardCrudOptionItem>>> Options(CancellationToken cancellationToken = default)
{
    var options = await _standardCrudEndpoint.OptionsAsync(PersonCrudDescriptor, new StandardCrudListQuery(PageSize: 100), cancellationToken);
    return Ok(options);
}

[HttpPost("{id:guid}/disable")]
public async Task<ActionResult<PersonDetailDto>> Disable(Guid id, CancellationToken cancellationToken = default)
{
    var result = await _standardCrudEndpoint.DisableAsync(PersonCrudDescriptor, id.ToString("D"), cancellationToken);
    return result.Succeeded && result.Detail is not null
        ? Ok(MapDetail(result.Detail))
        : StatusCode(result.Error!.StatusCode, result.Error);
}

[HttpDelete("{id:guid}")]
public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
{
    var result = await _standardCrudEndpoint.DeleteAsync(PersonCrudDescriptor, id.ToString("D"), cancellationToken);
    return result.Succeeded ? NoContent() : StatusCode(result.Error!.StatusCode, result.Error);
}
```

Add mapping helpers:

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

private static string ReadString(IReadOnlyDictionary<string, object?> row, string key) =>
    row.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

private static bool ReadBool(IReadOnlyDictionary<string, object?> row, string key, bool fallback) =>
    row.TryGetValue(key, out var value) && value is bool typed ? typed : fallback;

private static Guid? ReadGuid(IReadOnlyDictionary<string, object?> row, string key) =>
    Guid.TryParse(ReadString(row, key), out var guid) ? guid : null;

private static DateTime ReadDateTime(IReadOnlyDictionary<string, object?> row, string key) =>
    row.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : default;

private static PersonSummaryBlockDto MapSummary(IReadOnlyDictionary<string, StandardCrudSummaryBlock> summaries, string key) =>
    summaries.TryGetValue(key, out var block)
        ? new PersonSummaryBlockDto { Status = block.Status, Data = block.Data }
        : new PersonSummaryBlockDto { Status = "notConfigured" };
```

- [x] **Step 4: 运行测试并提交**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "PersonListContractTests|PersonServiceTests" -nr:false -v minimal
```

Expected:

```text
Passed!
```

Commit:

```powershell
git add .\server\modules\Organization .\tests\Organization.Tests
git commit -m "功能：切换人员接口到标准CRUD端点"
```

### Task 9: 全量验证与文档回填

**Files:**
- Modify: `docs/verification/organization-module-verification.md`

- [x] **Step 1: 运行平台 CRUD 测试**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 2: 运行 Organization 测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 3: 运行 HostApi 构建**

Run:

```powershell
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
```

Expected:

```text
Build succeeded.
```

- [x] **Step 4: 运行 Organization 前端构建 smoke**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-modules/organization build
```

Expected:

```text
build completed successfully
```

- [x] **Step 5: 回填验证记录**

Append to `docs/verification/organization-module-verification.md`:

```md

## 标准 CRUD 后端内核试点验证

验证日期：2026-05-02

本轮验证目标：

- `server/platform-crud` 已提供 descriptor、扩展点、EF 仓储、标准服务和 endpoint 门面。
- Person 已接入标准 CRUD descriptor、query customizer、display resolver、summary provider、validator 和 delete guard。
- 第一阶段读取主干覆盖 list/detail/options/summary。
- 第二阶段写操作覆盖 create/update/disable/delete，其中 delete 按 `conditional` 策略通过 delete guard 控制。
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
- 查询、显示值、摘要块、校验和删除守卫均通过代码扩展点接入，未下沉为元数据执行逻辑。
```

- [x] **Step 6: 提交**

```powershell
git add .\docs\verification\organization-module-verification.md
git commit -m "验证：记录标准CRUD后端内核试点结果"
```

## Self-Review

- **Spec coverage:** 本计划覆盖 descriptor、扩展点、EF 仓储、list/detail/options、summary 降级、display value、create/update 字段白名单、disable、conditional delete、Person 试点、验证与回滚。
- **Review coverage:** 已吸收 `standard-crud-backend-kernel-plan-consistency-review.md` 中全部 P0 项和 P1 项。
- **完整性扫描:** 本计划没有保留空泛任务；每个阶段都包含明确文件、命令、期望结果和关键代码。
- **Type consistency:** 当前计划使用现有 `ModelDefinition(Name, TableName, Kind, Fields, Indexes)` 与 `FieldDefinition(Name, StoreName, Kind, ValueType, StoreType, Required, IsPrimaryKey, IsAuditField)` 签名。

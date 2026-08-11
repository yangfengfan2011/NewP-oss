# Platform Query Kernel Standard CRUD List Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立独立的平台查询内核，并让 Standard CRUD 列表首个接入数据库层筛选、排序、分页、字段投影和结构化 display value。

**Architecture:** 新增 `server/platform-query` 作为查询契约、字段规划和 EF executor 的代码内核；`server/platform-crud` 只负责把 Standard CRUD descriptor、查询参数和 display resolver 适配到查询内核；Shell 只提交当前 view 需要的字段意图，安全裁剪和依赖补齐由后端完成。导出继续复用 Standard CRUD 列表服务，但按导出列构造查询字段。

**Tech Stack:** .NET 10, EF Core shared-type entity / property bag, xUnit, SQLite SQL shape tests, React + TypeScript + Vite, Vitest, pnpm workspace.

---

## 前置要求

- 设计入口：`docs/superpowers/specs/platform/2026-06-06-platform-query-kernel-standard-crud-list-design.md`。
- 实施前保持当前工作区和分支；如果有未提交内容，不切换分支。需要隔离时另建 git worktree。
- 本计划只实施平台查询内核与 Standard CRUD List 首个接入，不新增第二个 Standard CRUD 资源，不改数据库结构，不处理 `PersonNo` 唯一索引迁移。
- 每个 Task 完成后运行任务内验证命令；提交信息使用简体中文，例如 `功能：新增平台查询字段规划`。

## 文件结构

- Create: `server/platform-query/PlatformQuery.csproj`
  - 平台查询内核项目，引用 `server/platform-persistence-efcore/PlatformPersistence.EfCore.csproj` 和 EF Core。
- Create: `server/platform-query/Descriptors/PlatformQueryDescriptor.cs`
  - 查询资源边界，声明真实模型字段、默认响应字段、默认排序字段和 display value 字段。
- Create: `server/platform-query/Descriptors/PlatformDisplayValueField.cs`
  - 结构化 display value 声明：展示字段、source EID 字段、目标模型、目标 id 字段、目标显示字段。
- Create: `server/platform-query/Models/PlatformQueryCondition.cs`
  - 查询条件模型，承接 `=`、`contains`、条件组和组操作符。
- Create: `server/platform-query/Models/PlatformQueryRequest.cs`
  - 查询请求模型，承接字段、分页、排序和条件。
- Create: `server/platform-query/Models/PlatformQueryFieldPlan.cs`
  - 字段规划结果：`ResponseFields / DbSelectFields / InternalOnlyFields / RequestedDisplayFields`。
- Create: `server/platform-query/Models/PlatformQueryResult.cs`
  - executor 返回分页结果和字段规划信息；`Items` 包含数据库选择字段，最终响应投影由 adapter 完成。
- Create: `server/platform-query/Runtime/IPlatformQueryExecutor.cs`
  - 平台查询入口。
- Create: `server/platform-query/Runtime/EfPlatformQueryExecutor.cs`
  - EF shared-type entity 查询执行器。
- Create: `server/platform-query/Runtime/PlatformQueryFieldPlanner.cs`
  - 统一字段解析、去重、校验和依赖补齐。
- Create: `server/platform-query/Runtime/PlatformQueryValidationException.cs`
  - 字段、排序、条件校验失败的稳定异常。
- Create: `server/platform-query/Runtime/PlatformQueryServiceCollectionExtensions.cs`
  - 注册 `IPlatformQueryExecutor`。
- Create: `server/platform-crud/Runtime/StandardCrudQueryDescriptorAdapter.cs`
  - 将 Standard CRUD descriptor 转成 `PlatformQueryDescriptor`。
- Modify: `ICP-Platform.sln`
  - 加入 `server/platform-query/PlatformQuery.csproj`。
- Modify: `server/platform-crud/PlatformCrud.csproj`
  - 引用 `server/platform-query/PlatformQuery.csproj`。
- Modify: `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`
  - 将 `DisplayValueFields` 从字符串列表改成 `IReadOnlyList<PlatformDisplayValueField>`。
- Modify: `server/platform-crud/Runtime/StandardCrudDescriptorValidator.cs`
  - 校验 display value 声明字段、source 字段和目标字段。
- Modify: `server/platform-crud/Models/StandardCrudListQuery.cs`
  - 增加 `Fields`。
- Modify: `server/platform-crud/Extensions/StandardCrudEndpointRouteBuilderExtensions.cs`
  - 解析 `fields`，从 `Filters` 排除，并把查询校验错误转成 400。
- Modify: `server/platform-crud/Runtime/StandardCrudService.cs`
  - 列表读取改为调用 `IPlatformQueryExecutor`，批量补 display value，再按 response fields 输出。
- Modify: `server/platform-crud/Runtime/StandardCrudExportService.cs`
  - 按导出列构造 `StandardCrudListQuery.Fields`。
- Modify: `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`
  - 注册平台查询内核。
- Modify: `server/platform-crud/Abstractions/IStandardCrudDisplayValueResolver.cs`
  - 增加批量列表解析契约；详情读取用单行列表调用同一入口。
- Modify: `server/platform-crud/Abstractions/IStandardCrudRepository.cs`
  - 移除或停止暴露列表读取职责；保留 detail/options/exists/create/update/delete。
- Modify: `server/platform-crud/Persistence/EfStandardCrudRepository.cs`
  - 删除或隔离旧的内存分页 `ListAsync`，避免保留双真源。
- Modify: `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`
  - 使用结构化 `PlatformDisplayValueField`。
- Modify: `server/modules/Organization/Crud/PersonCrudDisplayValueResolver.cs`
  - 改为批量解析 Company/Department/Position 显示值。
- Modify: `server/modules/Organization/Repositories/ICompanyRepository.cs`
- Modify: `server/modules/Organization/Repositories/IDepartmentRepository.cs`
- Modify: `server/modules/Organization/Repositories/IPositionRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfCompanyRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfDepartmentRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfPositionRepository.cs`
  - 增加批量 id 读取，避免 display value N+1。
- Modify: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`
  - 引用 `server/platform-query`，增加 `Microsoft.EntityFrameworkCore.Sqlite`。
- Create: `tests/PlatformCrud.Tests/PlatformQueryFieldPlannerTests.cs`
- Create: `tests/PlatformCrud.Tests/PlatformQueryExecutorTests.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudEndpointRouteBuilderTests.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudDescriptorTests.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudEfRepositoryTests.cs`
- Modify: `tests/PlatformCrud.Tests/Support/InMemoryStandardCrudRepository.cs`
- Modify: `tests/PlatformCrud.Tests/Support/PlatformCrudModelDefinitions.cs`
- Modify: `tests/Organization.Tests/PersonStandardCrudResourceTests.cs`
- Modify: `tests/Organization.Tests/*RepositoryTests.cs`
  - 按任务补充定向测试。
- Modify: `frontend/packages/sdk/src/standardCrud.ts`
  - 允许 list query 发送 `fields` 字符串；保留 URL 编码入口。
- Modify: `frontend/packages/shell/src/standard-crud/standardCrudMetadataQuery.ts`
  - 新增从 columns 和 row action `visibleWhen` 收集列表请求字段的 helper。
- Modify: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
  - list 请求带上 `fields`；导出保持按导出列请求。
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts`
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-list-host.test.tsx`
- Modify: `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`
  - 补字段请求断言。
- Modify: `docs/phases/current-handoff.md`
- Modify: `docs/verification/verification-matrix.md` 或新增 `docs/verification/records/2026-06-06-platform-query-kernel-standard-crud-list.md`
  - 记录本轮验证和回滚口径。

## Task 1：建立平台查询契约与字段规划

**Files:**
- Create: `server/platform-query/PlatformQuery.csproj`
- Create: `server/platform-query/Descriptors/PlatformQueryDescriptor.cs`
- Create: `server/platform-query/Descriptors/PlatformDisplayValueField.cs`
- Create: `server/platform-query/Models/PlatformQueryCondition.cs`
- Create: `server/platform-query/Models/PlatformQueryRequest.cs`
- Create: `server/platform-query/Models/PlatformQueryFieldPlan.cs`
- Create: `server/platform-query/Runtime/PlatformQueryFieldPlanner.cs`
- Create: `server/platform-query/Runtime/PlatformQueryValidationException.cs`
- Create: `tests/PlatformCrud.Tests/PlatformQueryFieldPlannerTests.cs`
- Modify: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`
- Modify: `ICP-Platform.sln`

- [x] **Step 1: 写字段规划失败测试**

Create `tests/PlatformCrud.Tests/PlatformQueryFieldPlannerTests.cs`:

```csharp
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class PlatformQueryFieldPlannerTests
{
    [Fact]
    public void Build_WhenFieldsContainDisplayValue_ComputesResponseAndDbFields()
    {
        var descriptor = Descriptor();

        var plan = PlatformQueryFieldPlanner.Build(
            descriptor,
            new PlatformQueryRequest(
                Fields: ["EID", "CompanyName", "IsEnabled"],
                Page: 1,
                PageSize: 20,
                SortBy: "PersonNo",
                Conditions:
                [
                    new PlatformQueryCondition("EmploymentStatus", "=", "FullTime")
                ]));

        Assert.Equal(["EID", "CompanyName", "IsEnabled"], plan.ResponseFields);
        Assert.Equal(["EID", "IsEnabled", "PersonNo", "EmploymentStatus", "CompanyId"], plan.DbSelectFields);
        Assert.Equal(["PersonNo", "EmploymentStatus", "CompanyId"], plan.InternalOnlyFields);
        var displayField = Assert.Single(plan.RequestedDisplayFields);
        Assert.Equal("CompanyName", displayField.Field);
        Assert.Equal("CompanyId", displayField.SourceField);
    }

    [Fact]
    public void Build_WhenFieldsAreEmpty_UsesDefaultFields()
    {
        var plan = PlatformQueryFieldPlanner.Build(Descriptor(), new PlatformQueryRequest(Fields: null));

        Assert.Equal(["EID", "PersonNo", "ENAME", "CompanyName"], plan.ResponseFields);
        Assert.Equal(["EID", "PersonNo", "ENAME", "CompanyId"], plan.DbSelectFields);
    }

    [Theory]
    [InlineData("SecretField", "PlatformQuery.UnknownField")]
    [InlineData("CompanyId", "PlatformQuery.DisplayValueSortOrFilterUnsupported")]
    public void Build_WhenInvalidFieldRequested_ThrowsStableCode(string field, string expectedCode)
    {
        var request = field == "SecretField"
            ? new PlatformQueryRequest(Fields: ["SecretField"])
            : new PlatformQueryRequest(Fields: ["EID"], SortBy: "CompanyName");

        var error = Assert.Throws<PlatformQueryValidationException>(() =>
            PlatformQueryFieldPlanner.Build(Descriptor(), request));

        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public void Build_WhenConditionReferencesUnknownField_ThrowsStableCode()
    {
        var error = Assert.Throws<PlatformQueryValidationException>(() =>
            PlatformQueryFieldPlanner.Build(
                Descriptor(),
                new PlatformQueryRequest(
                    Fields: ["EID"],
                    Conditions: [new PlatformQueryCondition("MissingState", "=", true)])));

        Assert.Equal("PlatformQuery.UnknownConditionField", error.Code);
    }

    private static PlatformQueryDescriptor Descriptor() => new()
    {
        ResourceKey = "organization.person",
        ModelName = "Person",
        IdField = "EID",
        AllowedModelFields = ["EID", "PersonNo", "ENAME", "CompanyId", "EmploymentStatus", "IsEnabled"],
        DefaultFields = ["EID", "PersonNo", "ENAME", "CompanyName"],
        DefaultSortField = "ENAME",
        DisplayValueFields =
        [
            new PlatformDisplayValueField(
                Field: "CompanyName",
                SourceField: "CompanyId",
                TargetModelName: "Company",
                TargetIdField: "EID",
                TargetDisplayField: "ENAME")
        ]
    };
}
```

- [x] **Step 2: 加测试项目引用并运行 RED**

Modify `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`:

```xml
<ProjectReference Include="..\..\server\platform-query\PlatformQuery.csproj" />
```

Run:

```powershell
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter PlatformQueryFieldPlannerTests -nr:false -v minimal
```

Expected: FAIL because `server/platform-query` and `PlatformQueryFieldPlanner` do not exist.

- [x] **Step 3: 新建平台查询项目与契约模型**

Create `server/platform-query/PlatformQuery.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>ICP.Platform.Query</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\platform-persistence-efcore\PlatformPersistence.EfCore.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="10.0.1" />
  </ItemGroup>
</Project>
```

Add the project to solution:

```powershell
dotnet sln ICP-Platform.sln add server\platform-query\PlatformQuery.csproj
```

Create contracts with these signatures:

```csharp
namespace ICP.Platform.Query.Descriptors;

public sealed class PlatformQueryDescriptor
{
    public required string ResourceKey { get; init; }
    public required string ModelName { get; init; }
    public required string IdField { get; init; }
    public required IReadOnlyList<string> AllowedModelFields { get; init; }
    public IReadOnlyList<PlatformDisplayValueField> DisplayValueFields { get; init; } = [];
    public IReadOnlyList<string> DefaultFields { get; init; } = [];
    public string? DefaultSortField { get; init; }
}

public sealed record PlatformDisplayValueField(
    string Field,
    string SourceField,
    string TargetModelName,
    string TargetIdField,
    string TargetDisplayField);
```

```csharp
namespace ICP.Platform.Query.Models;

public sealed record PlatformQueryCondition(
    string Field,
    string Operator,
    object? Value,
    string? GroupKey = null,
    string? GroupOperator = null);

public sealed record PlatformQueryRequest(
    IReadOnlyList<string>? Fields,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDirection = null,
    IReadOnlyList<PlatformQueryCondition>? Conditions = null);

public sealed record PlatformQueryFieldPlan(
    IReadOnlyList<string> ResponseFields,
    IReadOnlyList<string> DbSelectFields,
    IReadOnlyList<string> InternalOnlyFields,
    IReadOnlyList<PlatformDisplayValueField> RequestedDisplayFields);
```

```csharp
namespace ICP.Platform.Query.Runtime;

public sealed class PlatformQueryValidationException : InvalidOperationException
{
    public PlatformQueryValidationException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
```

- [x] **Step 4: 实现字段规划内核**

Create `server/platform-query/Runtime/PlatformQueryFieldPlanner.cs` with these rules:

```csharp
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;

namespace ICP.Platform.Query.Runtime;

public static class PlatformQueryFieldPlanner
{
    public static PlatformQueryFieldPlan Build(PlatformQueryDescriptor descriptor, PlatformQueryRequest request)
    {
        var allowedModelFields = descriptor.AllowedModelFields.ToHashSet(StringComparer.Ordinal);
        var displayFields = descriptor.DisplayValueFields.ToDictionary(field => field.Field, StringComparer.Ordinal);
        var allowedResponseFields = allowedModelFields.Concat(displayFields.Keys).ToHashSet(StringComparer.Ordinal);

        var requestedResponseFields = NormalizeFields(request.Fields, descriptor.DefaultFields);
        if (requestedResponseFields.Count == 0)
        {
            requestedResponseFields = NormalizeFields(null, descriptor.DefaultFields);
        }

        foreach (var field in requestedResponseFields)
        {
            if (!allowedResponseFields.Contains(field))
            {
                throw new PlatformQueryValidationException(
                    "PlatformQuery.UnknownField",
                    $"查询资源 {descriptor.ResourceKey} 不允许请求字段 {field}。");
            }
        }

        var dbFields = new List<string>();
        AddField(dbFields, descriptor.IdField);
        foreach (var field in requestedResponseFields.Where(allowedModelFields.Contains))
        {
            AddField(dbFields, field);
        }

        foreach (var condition in request.Conditions ?? [])
        {
            ValidateModelField(descriptor, allowedModelFields, displayFields, condition.Field, "condition");
            AddField(dbFields, condition.Field);
        }

        var sortBy = string.IsNullOrWhiteSpace(request.SortBy) ? descriptor.DefaultSortField : request.SortBy;
        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            ValidateModelField(descriptor, allowedModelFields, displayFields, sortBy, "sort");
            AddField(dbFields, sortBy);
        }

        var requestedDisplayFields = new List<PlatformDisplayValueField>();
        foreach (var field in requestedResponseFields)
        {
            if (displayFields.TryGetValue(field, out var displayField))
            {
                ValidateModelField(descriptor, allowedModelFields, displayFields, displayField.SourceField, "displaySource");
                AddField(dbFields, displayField.SourceField);
                requestedDisplayFields.Add(displayField);
            }
        }

        var internalOnlyFields = dbFields
            .Where(field => !requestedResponseFields.Contains(field, StringComparer.Ordinal))
            .ToList();

        return new PlatformQueryFieldPlan(
            requestedResponseFields,
            dbFields,
            internalOnlyFields,
            requestedDisplayFields);
    }

    private static List<string> NormalizeFields(IReadOnlyList<string>? fields, IReadOnlyList<string> fallback)
    {
        var source = fields is { Count: > 0 } ? fields : fallback;
        var result = new List<string>();
        foreach (var field in source)
        {
            var normalized = field.Trim();
            if (normalized.Length > 0)
            {
                AddField(result, normalized);
            }
        }

        return result;
    }

    private static void ValidateModelField(
        PlatformQueryDescriptor descriptor,
        ISet<string> allowedModelFields,
        IReadOnlyDictionary<string, PlatformDisplayValueField> displayFields,
        string field,
        string purpose)
    {
        if (displayFields.ContainsKey(field) && purpose is "sort" or "condition")
        {
            throw new PlatformQueryValidationException(
                "PlatformQuery.DisplayValueSortOrFilterUnsupported",
                $"查询资源 {descriptor.ResourceKey} 第一版不支持按 display value 字段 {field} 排序或筛选。");
        }

        if (!allowedModelFields.Contains(field))
        {
            var code = purpose == "condition" ? "PlatformQuery.UnknownConditionField" : "PlatformQuery.UnknownField";
            throw new PlatformQueryValidationException(code, $"查询资源 {descriptor.ResourceKey} 不允许使用字段 {field}。");
        }
    }

    private static void AddField(ICollection<string> fields, string field)
    {
        if (!fields.Contains(field, StringComparer.Ordinal))
        {
            fields.Add(field);
        }
    }
}
```

- [x] **Step 5: Run GREEN**

Run:

```powershell
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter PlatformQueryFieldPlannerTests -nr:false -v minimal
```

Expected: PASS.

- [x] **Step 6: Commit**

```powershell
git add ICP-Platform.sln server\platform-query tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj tests\PlatformCrud.Tests\PlatformQueryFieldPlannerTests.cs
git commit -m "功能：新增平台查询字段规划"
```

## Task 2：实现 EF 查询下推 executor 和 SQL 形状测试

**Files:**
- Create: `server/platform-query/Models/PlatformQueryResult.cs`
- Create: `server/platform-query/Runtime/IPlatformQueryExecutor.cs`
- Create: `server/platform-query/Runtime/EfPlatformQueryExecutor.cs`
- Create: `server/platform-query/Runtime/PlatformQueryServiceCollectionExtensions.cs`
- Create: `tests/PlatformCrud.Tests/PlatformQueryExecutorTests.cs`
- Modify: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`
- Modify: `tests/PlatformCrud.Tests/Support/PlatformCrudModelDefinitions.cs`

- [x] **Step 1: 写 SQL 形状与查询行为失败测试**

Modify `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.1" />
```

Modify `tests/PlatformCrud.Tests/Support/PlatformCrudModelDefinitions.cs` to include fields used by the query design:

```csharp
new FieldDefinition("PositionId", "PositionId", FieldKind.Reference, FieldValueType.ObjectId, "string", false, false, false),
new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true)
```

Create `tests/PlatformCrud.Tests/PlatformQueryExecutorTests.cs`:

```csharp
using ICP.Platform.Crud.Tests.Support;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using ICP.Platform.Query.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class PlatformQueryExecutorTests
{
    [Fact]
    public async Task QueryAsync_AppliesFilterSortPagingAndProjectionInSqlShape()
    {
        var sql = new List<string>();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = BuildDbContext(connection, sql);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Set<Dictionary<string, object?>>("Person").AddRange(
            Row("Person_1", "P001", "张三", "FullTime", true),
            Row("Person_2", "P002", "李四", "FullTime", true),
            Row("Person_3", "X003", "王五", "PartTime", true));
        await dbContext.SaveChangesAsync();
        sql.Clear();

        var executor = new EfPlatformQueryExecutor(dbContext);
        var result = await executor.QueryAsync(
            Descriptor(),
            new PlatformQueryRequest(
                Fields: ["EID", "PersonNo", "CompanyName"],
                Page: 1,
                PageSize: 1,
                SortBy: "PersonNo",
                SortDirection: "desc",
                Conditions: [new PlatformQueryCondition("EmploymentStatus", "=", "FullTime")]));

        Assert.Equal(2, result.Total);
        var row = Assert.Single(result.Items);
        Assert.Equal("P002", row["PersonNo"]);
        Assert.True(row.ContainsKey("CompanyId"));
        Assert.False(row.ContainsKey("CreatedAt"));

        var selectSql = string.Join(Environment.NewLine, sql.Where(line => line.Contains("SELECT", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("PersonNo", selectSql, StringComparison.Ordinal);
        Assert.Contains("CompanyId", selectSql, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatedAt", selectSql, StringComparison.Ordinal);
        Assert.Contains("LIMIT", selectSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", selectSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QueryAsync_WhenConditionGroupUsesOr_ReturnsRowsMatchingAnyCondition()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = BuildDbContext(connection, []);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Set<Dictionary<string, object?>>("Person").AddRange(
            Row("Person_1", "P001", "张三", "FullTime", true),
            Row("Person_2", "X002", "李四", "FullTime", true));
        await dbContext.SaveChangesAsync();

        var result = await new EfPlatformQueryExecutor(dbContext).QueryAsync(
            Descriptor(),
            new PlatformQueryRequest(
                Fields: ["EID", "PersonNo"],
                Conditions:
                [
                    new PlatformQueryCondition("PersonNo", "contains", "P", GroupKey: "keyword", GroupOperator: "or"),
                    new PlatformQueryCondition("ENAME", "contains", "李", GroupKey: "keyword", GroupOperator: "or")
                ]));

        Assert.Equal(2, result.Total);
    }

    private static EfPlatformDbContext BuildDbContext(SqliteConnection connection, List<string> sql)
    {
        var options = new DbContextOptionsBuilder<EfPlatformDbContext>()
            .UseSqlite(connection)
            .LogTo(sql.Add, LogLevel.Information)
            .EnableSensitiveDataLogging()
            .Options;

        return new EfPlatformDbContext(options, [PlatformCrudModelDefinitions.Person()]);
    }

    private static Dictionary<string, object?> Row(string id, string personNo, string name, string status, bool isEnabled) => new(StringComparer.Ordinal)
    {
        ["EID"] = id,
        ["PersonNo"] = personNo,
        ["ENAME"] = name,
        ["CompanyId"] = "Company_1",
        ["DepartmentId"] = "Department_1",
        ["PositionId"] = "Position_1",
        ["EmploymentStatus"] = status,
        ["IsEnabled"] = isEnabled,
        ["CreatedAt"] = DateTime.UtcNow,
        ["UpdatedAt"] = DateTime.UtcNow
    };

    private static PlatformQueryDescriptor Descriptor() => new()
    {
        ResourceKey = "organization.person",
        ModelName = "Person",
        IdField = "EID",
        AllowedModelFields = ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled"],
        DefaultFields = ["EID", "PersonNo", "ENAME"],
        DefaultSortField = "ENAME",
        DisplayValueFields =
        [
            new PlatformDisplayValueField("CompanyName", "CompanyId", "Company", "EID", "ENAME")
        ]
    };
}
```

- [x] **Step 2: Run RED**

Run:

```powershell
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter PlatformQueryExecutorTests -nr:false -v minimal
```

Expected: FAIL because `EfPlatformQueryExecutor` does not exist.

- [x] **Step 3: 实现 executor 契约和结果模型**

Create `server/platform-query/Models/PlatformQueryResult.cs`:

```csharp
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Query.Models;

public sealed record PlatformQueryResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Items,
    int Total,
    int Page,
    int PageSize,
    string? SortBy,
    string? SortDirection,
    IReadOnlyList<string> ResponseFields,
    IReadOnlyList<PlatformDisplayValueField> RequestedDisplayFields);
```

Create `server/platform-query/Runtime/IPlatformQueryExecutor.cs`:

```csharp
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;

namespace ICP.Platform.Query.Runtime;

public interface IPlatformQueryExecutor
{
    Task<PlatformQueryResult> QueryAsync(
        PlatformQueryDescriptor descriptor,
        PlatformQueryRequest request,
        CancellationToken cancellationToken = default);
}
```

Create `server/platform-query/Runtime/PlatformQueryServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Query.Runtime;

public static class PlatformQueryServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformQueryKernel(this IServiceCollection services)
    {
        services.AddScoped<IPlatformQueryExecutor, EfPlatformQueryExecutor>();
        return services;
    }
}
```

- [x] **Step 4: 实现 EF 查询下推**

Create `server/platform-query/Runtime/EfPlatformQueryExecutor.cs` with these implementation points:

```csharp
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Query.Descriptors;
using ICP.Platform.Query.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace ICP.Platform.Query.Runtime;

public sealed class EfPlatformQueryExecutor : IPlatformQueryExecutor
{
    private readonly EfPlatformDbContext _dbContext;

    public EfPlatformQueryExecutor(EfPlatformDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PlatformQueryResult> QueryAsync(
        PlatformQueryDescriptor descriptor,
        PlatformQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : request.PageSize;
        var plan = PlatformQueryFieldPlanner.Build(descriptor, request);

        var query = _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName).AsNoTracking();
        query = ApplyConditions(query, request.Conditions ?? []);

        var total = await query.CountAsync(cancellationToken);
        query = ApplySort(query, descriptor, request);
        query = query.Skip((page - 1) * pageSize).Take(pageSize);

        var rows = await query.ToListAsync(cancellationToken);
        var projectedRows = rows.Select(row => Project(row, plan.DbSelectFields)).ToList();

        return new PlatformQueryResult(
            projectedRows,
            total,
            page,
            pageSize,
            request.SortBy,
            request.SortDirection,
            plan.ResponseFields,
            plan.RequestedDisplayFields);
    }
}
```

Replace the temporary `ToListAsync` projection with a dynamic EF `Select` expression before GREEN. The final method must execute `Select` before `ToListAsync` and convert the selected `object?[]` rows back to dictionaries after the database call:

```csharp
query = query.Skip((page - 1) * pageSize).Take(pageSize);
var selector = BuildSelector(plan.DbSelectFields);
var selectedRows = await query.Select(selector).ToListAsync(cancellationToken);
var projectedRows = selectedRows
    .Select(values => BuildRow(plan.DbSelectFields, values))
    .ToList();
```

Add the selector helpers in `EfPlatformQueryExecutor`:

```csharp
private static Expression<Func<Dictionary<string, object?>, object?[]>> BuildSelector(IReadOnlyList<string> fields)
{
    var row = Expression.Parameter(typeof(Dictionary<string, object?>), "row");
    var propertyMethod = typeof(EF)
        .GetMethods()
        .Single(method => method.Name == nameof(EF.Property) && method.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(object));
    var values = fields.Select(field =>
        Expression.Convert(
            Expression.Call(propertyMethod, row, Expression.Constant(field)),
            typeof(object)));

    return Expression.Lambda<Func<Dictionary<string, object?>, object?[]>>(
        Expression.NewArrayInit(typeof(object), values),
        row);
}

private static IReadOnlyDictionary<string, object?> BuildRow(IReadOnlyList<string> fields, object?[] values)
{
    var row = new Dictionary<string, object?>(StringComparer.Ordinal);
    for (var index = 0; index < fields.Count; index++)
    {
        row[fields[index]] = values[index];
    }

    return row;
}
```

The SQL shape test is the acceptance gate: it must prove `CreatedAt` is not selected and `LIMIT/OFFSET` appears.

Condition and sort rules:

```csharp
// "=" compares EF.Property<object?> converted through string/boolean/date semantics already covered by tests.
// "contains" uses EF.Property<string?>(row, field).Contains(valueText).
// ungrouped conditions combine with AND.
// grouped conditions with GroupOperator "or" combine with OR.
// grouped conditions with GroupOperator "and" combine with AND.
// mixed group operators throw PlatformQueryValidationException("PlatformQuery.MixedConditionGroupOperator", ...).
```

- [x] **Step 5: Run GREEN**

Run:

```powershell
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter PlatformQueryExecutorTests -nr:false -v minimal
dotnet build server\platform-query\PlatformQuery.csproj -nr:false -v minimal
```

Expected: both PASS.

- [x] **Step 6: Commit**

```powershell
git add server\platform-query tests\PlatformCrud.Tests ICP-Platform.sln
git commit -m "功能：实现平台查询 EF 执行器"
```

## Task 3：接入 Standard CRUD descriptor、fields 解析和稳定错误

**Files:**
- Modify: `server/platform-crud/PlatformCrud.csproj`
- Modify: `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`
- Modify: `server/platform-crud/Runtime/StandardCrudDescriptorValidator.cs`
- Create: `server/platform-crud/Runtime/StandardCrudQueryDescriptorAdapter.cs`
- Modify: `server/platform-crud/Models/StandardCrudListQuery.cs`
- Modify: `server/platform-crud/Extensions/StandardCrudEndpointRouteBuilderExtensions.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudDescriptorTests.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudEndpointRouteBuilderTests.cs`

- [x] **Step 1: 写 descriptor adapter 和 endpoint fields 失败测试**

Add to `tests/PlatformCrud.Tests/StandardCrudDescriptorTests.cs`:

```csharp
[Fact]
public void Adapter_MapsListFieldsAndDisplayValueFieldsToPlatformQueryDescriptor()
{
    var descriptor = DescriptorWithDisplayValueFields();

    var queryDescriptor = StandardCrudQueryDescriptorAdapter.ToQueryDescriptor(descriptor);

    Assert.Equal(["EID", "PersonNo", "ENAME", "CompanyId", "IsEnabled"], queryDescriptor.AllowedModelFields);
    Assert.Equal(["EID", "PersonNo", "ENAME", "CompanyName"], queryDescriptor.DefaultFields);
    var display = Assert.Single(queryDescriptor.DisplayValueFields);
    Assert.Equal("CompanyName", display.Field);
    Assert.Equal("CompanyId", display.SourceField);
}

[Fact]
public void Validate_WhenDisplayValueSourceFieldIsNotListField_ThrowsStableMessage()
{
    var descriptor = DescriptorWithDisplayValueFields(sourceField: "SecretCompanyId");

    var error = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

    Assert.Equal("标准 CRUD 资源 organization.person 的 display value 字段 CompanyName 引用了未纳入列表字段白名单的 source 字段 SecretCompanyId。", error.Message);
}
```

Add this helper in the same test file:

```csharp
private static StandardCrudDescriptor DescriptorWithDisplayValueFields(string sourceField = "CompanyId") => new()
{
    ResourceKey = "organization.person",
    ModuleId = "Organization",
    ModelName = "Person",
    Route = "api/organization/persons",
    IdField = "EID",
    DisplayField = "ENAME",
    FieldSet = new StandardCrudFieldSet(
        ListFields: ["EID", "PersonNo", "ENAME", "CompanyId", "IsEnabled"],
        DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId", "IsEnabled"],
        CreateFields: ["PersonNo", "ENAME", "CompanyId", "IsEnabled"],
        UpdateFields: ["ENAME", "CompanyId", "IsEnabled"]),
    DisplayValueFields =
    [
        new PlatformDisplayValueField("CompanyName", sourceField, "Company", "EID", "ENAME")
    ]
};
```

Add to `tests/PlatformCrud.Tests/StandardCrudEndpointRouteBuilderTests.cs`:

```csharp
[Fact]
public async Task MapStandardCrudResources_ParsesFieldsOutsideFilters()
{
    var recording = new RecordingStandardCrudService();
    await using var app = BuildApp(recording);
    await app.StartAsync();

    var response = await app.GetTestClient().GetAsync(
        "/api/organization/persons?pageIndex=2&pageSize=5&fields=EID,PersonNo,CompanyName,IsEnabled&keyword=张");

    response.EnsureSuccessStatusCode();
    Assert.Equal(["EID", "PersonNo", "CompanyName", "IsEnabled"], recording.LastListQuery!.Fields);
    Assert.Equal("张", recording.LastListQuery.Filters!["keyword"]);
    Assert.DoesNotContain("fields", recording.LastListQuery.Filters.Keys);
}
```

- [x] **Step 2: Run RED**

Run:

```powershell
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "StandardCrudDescriptorTests|StandardCrudEndpointRouteBuilderTests" -nr:false -v minimal
```

Expected: FAIL because descriptor still uses string display fields and `fields` is still treated as a filter key.

- [x] **Step 3: 修改 Standard CRUD descriptor 和 list query**

Modify `server/platform-crud/PlatformCrud.csproj`:

```xml
<ProjectReference Include="..\platform-query\PlatformQuery.csproj" />
```

Modify `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`:

```csharp
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Crud.Descriptors;

public sealed class StandardCrudDescriptor
{
    // existing properties remain
    public IReadOnlyList<PlatformDisplayValueField> DisplayValueFields { get; init; } = [];
}
```

Modify `server/platform-crud/Models/StandardCrudListQuery.cs`:

```csharp
public sealed record StandardCrudListQuery(
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDirection = null,
    IReadOnlyDictionary<string, string?>? Filters = null,
    IReadOnlyList<string>? Fields = null);
```

- [x] **Step 4: 实现 adapter、validator 和 endpoint 解析**

Create `server/platform-crud/Runtime/StandardCrudQueryDescriptorAdapter.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Query.Descriptors;

namespace ICP.Platform.Crud.Runtime;

public static class StandardCrudQueryDescriptorAdapter
{
    public static PlatformQueryDescriptor ToQueryDescriptor(StandardCrudDescriptor descriptor)
    {
        var defaultFields = descriptor.FieldSet.ListFields
            .Concat(descriptor.DisplayValueFields.Select(field => field.Field))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new PlatformQueryDescriptor
        {
            ResourceKey = descriptor.ResourceKey,
            ModelName = descriptor.ModelName,
            IdField = descriptor.IdField,
            AllowedModelFields = descriptor.FieldSet.ListFields.Distinct(StringComparer.Ordinal).ToList(),
            DefaultFields = defaultFields,
            DefaultSortField = descriptor.DisplayField,
            DisplayValueFields = descriptor.DisplayValueFields
        };
    }
}
```

Extend `StandardCrudDescriptorValidator.Validate` with display value checks:

```csharp
foreach (var displayField in descriptor.DisplayValueFields)
{
    Require(!string.IsNullOrWhiteSpace(displayField.Field), $"标准 CRUD 资源 {descriptor.ResourceKey} 存在空 display value 字段。");
    Require(!string.IsNullOrWhiteSpace(displayField.SourceField), $"标准 CRUD 资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 未声明 source 字段。");
    Require(descriptor.FieldSet.ListFields.Contains(displayField.SourceField, StringComparer.Ordinal),
        $"标准 CRUD 资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 引用了未纳入列表字段白名单的 source 字段 {displayField.SourceField}。");
    Require(!descriptor.FieldSet.ListFields.Contains(displayField.Field, StringComparer.Ordinal),
        $"标准 CRUD 资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 不能同时作为真实列表字段声明。");
    Require(!string.IsNullOrWhiteSpace(displayField.TargetModelName), $"标准 CRUD 资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 未声明目标模型。");
    Require(!string.IsNullOrWhiteSpace(displayField.TargetIdField), $"标准 CRUD 资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 未声明目标 id 字段。");
    Require(!string.IsNullOrWhiteSpace(displayField.TargetDisplayField), $"标准 CRUD 资源 {descriptor.ResourceKey} 的 display value 字段 {displayField.Field} 未声明目标显示字段。");
}
```

Modify `StandardCrudEndpointRouteBuilderExtensions.StandardQueryKeys`:

```csharp
private static readonly HashSet<string> StandardQueryKeys = new(StringComparer.OrdinalIgnoreCase)
{
    "page",
    "pageIndex",
    "pageSize",
    "sortBy",
    "sortDirection",
    "fields"
};
```

Modify `BuildListQuery`:

```csharp
return new StandardCrudListQuery(
    Page: ReadInt(query, "pageIndex", ReadInt(query, "page", 1)),
    PageSize: ReadInt(query, "pageSize", 20),
    SortBy: ReadString(query, "sortBy"),
    SortDirection: ReadString(query, "sortDirection"),
    Filters: filters,
    Fields: ParseFields(ReadString(query, "fields")));
```

Add parser:

```csharp
private static IReadOnlyList<string>? ParseFields(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    var fields = value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(field => field.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    return fields.Count == 0 ? null : fields;
}
```

Catch query validation errors in GET list and export endpoints:

```csharp
catch (PlatformQueryValidationException ex)
{
    return Results.BadRequest(new StandardCrudError
    {
        StatusCode = StatusCodes.Status400BadRequest,
        Code = ex.Code,
        Message = ex.Message
    });
}
```

- [x] **Step 5: Run GREEN**

Run:

```powershell
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "StandardCrudDescriptorTests|StandardCrudEndpointRouteBuilderTests" -nr:false -v minimal
```

Expected: PASS.

- [x] **Step 6: Commit**

```powershell
git add server\platform-crud tests\PlatformCrud.Tests
git commit -m "功能：接入标准 CRUD 查询描述符"
```

## Task 4：Standard CRUD 列表改走平台查询内核，导出按列构造 fields

**Files:**
- Modify: `server/platform-crud/Runtime/StandardCrudService.cs`
- Modify: `server/platform-crud/Runtime/StandardCrudExportService.cs`
- Modify: `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`
- Modify: `server/platform-crud/Abstractions/IStandardCrudDisplayValueResolver.cs`
- Modify: `server/platform-crud/Abstractions/IStandardCrudRepository.cs`
- Modify: `server/platform-crud/Persistence/EfStandardCrudRepository.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudEfRepositoryTests.cs`
- Modify: `tests/PlatformCrud.Tests/Support/InMemoryStandardCrudRepository.cs`

- [x] **Step 1: 写服务层失败测试，锁定 query executor 和 response fields**

Modify `tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs` list test to use a fake platform executor:

```csharp
[Fact]
public async Task ListAsync_UsesPlatformQueryExecutorAndProjectsResponseFields()
{
    var repository = new InMemoryStandardCrudRepository([]);
    var executor = new RecordingPlatformQueryExecutor(new PlatformQueryResult(
        [
            new Dictionary<string, object?>
            {
                ["EID"] = "person-1",
                ["PersonNo"] = "P001",
                ["CompanyId"] = "company-1",
                ["EmploymentStatus"] = "FullTime"
            }
        ],
        Total: 1,
        Page: 1,
        PageSize: 20,
        SortBy: "PersonNo",
        SortDirection: "asc",
        ResponseFields: ["EID", "PersonNo", "CompanyName"],
        RequestedDisplayFields:
        [
            new PlatformDisplayValueField("CompanyName", "CompanyId", "Company", "EID", "ENAME")
        ]));
    var service = new StandardCrudService(
        repository,
        executor,
        [new KeywordQueryCustomizer()],
        [new PersonDisplayResolver()],
        [],
        [],
        [],
        []);

    var result = await service.ListAsync(
        Descriptor(),
        new StandardCrudListQuery(
            SortBy: "PersonNo",
            SortDirection: "asc",
            Filters: new Dictionary<string, string?> { ["keyword"] = "P001" },
            Fields: ["EID", "PersonNo", "CompanyName"]));

    Assert.Equal(["EID", "PersonNo", "CompanyName"], executor.LastRequest!.Fields);
    Assert.Equal("PersonNo", executor.LastRequest.SortBy);
    var condition = Assert.Single(executor.LastRequest.Conditions!);
    Assert.Equal("PersonNo", condition.Field);
    var row = Assert.Single(result.Items);
    Assert.Equal(["EID", "PersonNo", "CompanyName"], row.Keys);
    Assert.Equal("公司一", row["CompanyName"]);
    Assert.False(row.ContainsKey("CompanyId"));
}
```

Add fake executor in the test file:

```csharp
private sealed class RecordingPlatformQueryExecutor : IPlatformQueryExecutor
{
    private readonly PlatformQueryResult _result;

    public RecordingPlatformQueryExecutor(PlatformQueryResult result)
    {
        _result = result;
    }

    public PlatformQueryRequest? LastRequest { get; private set; }

    public Task<PlatformQueryResult> QueryAsync(
        PlatformQueryDescriptor descriptor,
        PlatformQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(_result);
    }
}
```

Update `PersonDisplayResolver` fake to implement the batch method:

```csharp
public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
    StandardCrudDescriptor descriptor,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
    IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
    CancellationToken cancellationToken = default)
{
    IReadOnlyList<IReadOnlyDictionary<string, object?>> result = rows.Select(row =>
        (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["CompanyName"] = row.TryGetValue("CompanyId", out var companyId) && Equals(companyId, "company-1") ? "公司一" : string.Empty
        }).ToList();

    return Task.FromResult(result);
}
```

- [x] **Step 2: 写导出字段失败测试**

Extend `StandardCrudEndpointRouteBuilderTests.MapStandardCrudResources_ExportsCurrentPageAsXlsx`:

```csharp
Assert.Equal(["PersonNo", "ENAME", "CompanyName"], recording.LastListQuery.Fields);
```

Update the request columns in the same test so export covers display value fields:

```csharp
columns = new[]
{
    new { key = "personNo", field = "PersonNo", title = "人员编号" },
    new { key = "name", field = "ENAME", title = "姓名" },
    new { key = "companyName", field = "CompanyName", title = "公司" },
    new { key = "blocked", field = "SecretField", title = "秘密字段" }
}
```

Extend `MapStandardCrudResources_ExportsAllWithNormalizedPaging`:

```csharp
Assert.Equal(["ENAME"], recording.LastListQuery.Fields);
```

- [x] **Step 3: Run RED**

Run:

```powershell
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "StandardCrudServiceReadTests|StandardCrudEndpointRouteBuilderTests" -nr:false -v minimal
```

Expected: FAIL because `StandardCrudService` still calls repository list and export does not set fields.

- [x] **Step 4: 更新 display resolver 契约和 service 构造**

Modify `IStandardCrudDisplayValueResolver`:

```csharp
using ICP.Platform.Query.Descriptors;

public interface IStandardCrudDisplayValueResolver
{
    string ResourceKey { get; }

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
        CancellationToken cancellationToken = default);
}
```

Modify `StandardCrudService` constructor signature:

```csharp
public StandardCrudService(
    IStandardCrudRepository repository,
    IPlatformQueryExecutor queryExecutor,
    IEnumerable<IStandardCrudQueryCustomizer> queryCustomizers,
    IEnumerable<IStandardCrudDisplayValueResolver> displayResolvers,
    IEnumerable<IStandardCrudSummaryProvider> summaryProviders,
    IEnumerable<IStandardCrudValidator> validators,
    IEnumerable<IStandardCrudActionHandler> actionHandlers,
    IEnumerable<IStandardCrudDeleteGuard> deleteGuards)
```

Update all test helpers constructing `StandardCrudService` by passing a fake query executor. Write tests so write-path services use:

```csharp
new RecordingPlatformQueryExecutor(new PlatformQueryResult([], 0, 1, 20, null, null, [], []))
```

- [x] **Step 5: 实现 ListAsync 适配平台查询**

Replace list implementation with this flow:

```csharp
var conditions = BuildConditions(descriptor, query);
var queryDescriptor = StandardCrudQueryDescriptorAdapter.ToQueryDescriptor(descriptor);
var platformRequest = new PlatformQueryRequest(
    query.Fields,
    query.Page,
    query.PageSize,
    query.SortBy,
    query.SortDirection,
    conditions.Select(condition => new PlatformQueryCondition(
        condition.Field,
        condition.Operator,
        condition.Value,
        condition.GroupKey,
        condition.GroupOperator)).ToList());

var result = await _queryExecutor.QueryAsync(queryDescriptor, platformRequest, cancellationToken);
var rows = await ApplyDisplayValuesAsync(descriptor, result.Items, result.RequestedDisplayFields, cancellationToken);
var projectedRows = rows.Select(row => Project(row, result.ResponseFields)).ToList();

return new StandardCrudListResult(
    projectedRows,
    result.Total,
    result.Page,
    result.PageSize,
    result.SortBy,
    result.SortDirection);
```

Add helper:

```csharp
private static IReadOnlyDictionary<string, object?> Project(
    IReadOnlyDictionary<string, object?> row,
    IReadOnlyList<string> responseFields)
{
    var projected = new Dictionary<string, object?>(StringComparer.Ordinal);
    foreach (var field in responseFields)
    {
        projected[field] = row.TryGetValue(field, out var value) ? value : null;
    }

    return projected;
}
```

For detail/create/update/disable returns, call resolver through a one-row `ResolveListAsync` with all descriptor display fields:

```csharp
var displayValues = await resolver.ResolveListAsync(descriptor, [row], descriptor.DisplayValueFields, cancellationToken);
```

- [x] **Step 6: 更新导出 fields 构造**

First update `StandardCrudExportService.NormalizeColumns` so display value columns are exportable:

```csharp
var allowedFields = descriptor.FieldSet.ListFields
    .Concat(descriptor.DisplayValueFields.Select(field => field.Field))
    .ToHashSet(StringComparer.Ordinal);
```

Modify `StandardCrudExportService.BuildQuery` to accept normalized columns:

```csharp
private static StandardCrudListQuery BuildQuery(
    StandardCrudExportListQuery? query,
    string mode,
    IReadOnlyList<StandardCrudExportColumn> columns)
{
    var pageIndex = query?.PageIndex > 0 ? query.PageIndex : 1;
    var pageSize = query?.PageSize > 0 ? query.PageSize : 20;

    if (mode == "all")
    {
        pageIndex = 1;
        pageSize = ExportAllPageSize;
    }

    return new StandardCrudListQuery(
        pageIndex,
        pageSize,
        query?.SortBy,
        query?.SortDirection,
        query?.Filters,
        columns.Select(column => column.Field).Distinct(StringComparer.Ordinal).ToList());
}
```

Call it as:

```csharp
var query = BuildQuery(request.Query, mode, columns);
```

- [x] **Step 7: 移除旧列表仓储双真源**

Modify `IStandardCrudRepository` and `EfStandardCrudRepository`:

- Remove `ListAsync` from the interface.
- Remove `EfStandardCrudRepository.ListAsync`, `ApplyConditions`, `MatchesCondition`, `ApplySort` and `ReadComparable` when they are no longer used.
- Keep `OptionsAsync`, `ExistsAsync`, `GetByIdAsync`, `CreateAsync`, `UpdateAsync`, `DeleteAsync`.

Update `InMemoryStandardCrudRepository` by removing its `ListAsync`; write tests should continue using detail/options/write methods.

Remove or rewrite `StandardCrudEfRepositoryTests.ListAsync_*` tests. The equivalent behavior is now covered by `PlatformQueryExecutorTests`.

- [x] **Step 8: 注册查询内核**

Modify `StandardCrudServiceCollectionExtensions.AddStandardCrudKernel`:

```csharp
services.AddPlatformQueryKernel();
services.AddScoped<IStandardCrudService, StandardCrudService>();
```

Add using:

```csharp
using ICP.Platform.Query.Runtime;
```

- [x] **Step 9: Run GREEN**

Run:

```powershell
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "StandardCrudServiceReadTests|StandardCrudServiceWriteTests|StandardCrudEndpointRouteBuilderTests|PlatformQueryExecutorTests" -nr:false -v minimal
dotnet build server\platform-crud\PlatformCrud.csproj -nr:false -v minimal
```

Expected: both PASS.

- [x] **Step 10: Commit**

```powershell
git add server\platform-crud tests\PlatformCrud.Tests
git commit -m "功能：标准 CRUD 列表接入平台查询内核"
```

## Task 5：Organization 人员 display value 结构化与批量解析

**Files:**
- Modify: `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`
- Modify: `server/modules/Organization/Crud/PersonCrudDisplayValueResolver.cs`
- Modify: `server/modules/Organization/Repositories/ICompanyRepository.cs`
- Modify: `server/modules/Organization/Repositories/IDepartmentRepository.cs`
- Modify: `server/modules/Organization/Repositories/IPositionRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfCompanyRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfDepartmentRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfPositionRepository.cs`
- Modify: `tests/Organization.Tests/PersonStandardCrudResourceTests.cs`
- Modify: `tests/Organization.Tests/EfOrganizationRepositoryTests.cs`

- [x] **Step 1: 写 descriptor 结构化 display value 测试**

Modify `tests/Organization.Tests/PersonStandardCrudResourceTests.cs`:

```csharp
[Fact]
public void PersonCrudDescriptor_DeclaresStructuredDisplayValueFields()
{
    var descriptor = PersonCrudDescriptorFactory.Create();

    Assert.Contains(descriptor.DisplayValueFields, field =>
        field.Field == "CompanyName" &&
        field.SourceField == "CompanyId" &&
        field.TargetModelName == "Company" &&
        field.TargetIdField == "EID" &&
        field.TargetDisplayField == "ENAME");
    Assert.Contains(descriptor.DisplayValueFields, field =>
        field.Field == "DepartmentName" &&
        field.SourceField == "DepartmentId" &&
        field.TargetModelName == "Department");
    Assert.Contains(descriptor.DisplayValueFields, field =>
        field.Field == "PositionName" &&
        field.SourceField == "PositionId" &&
        field.TargetModelName == "Position");
}
```

Replace old string assertions:

```csharp
Assert.Contains("CompanyName", descriptor.DisplayValueFields);
```

with field-object assertions:

```csharp
Assert.Contains(descriptor.DisplayValueFields, field => field.Field == "CompanyName");
```

- [x] **Step 2: 写批量 resolver 测试**

Add to `PersonStandardCrudResourceTests.cs` or a new `PersonCrudDisplayValueResolverTests.cs`:

```csharp
[Fact]
public async Task PersonCrudDisplayValueResolver_ResolvesRequestedDisplayFieldsInBatch()
{
    var resolver = new PersonCrudDisplayValueResolver(
        new FakeCompanyRepository([new Company { Id = "Company_1", Name = "总公司" }]),
        new FakeDepartmentRepository([new Department { Id = "Department_1", Name = "研发部" }]),
        new FakePositionRepository([new Position { Id = "Position_1", Name = "工程师" }]));

    var descriptor = PersonCrudDescriptorFactory.Create();
    var rows = new IReadOnlyDictionary<string, object?>[]
    {
        new Dictionary<string, object?> { ["CompanyId"] = "Company_1", ["DepartmentId"] = "Department_1", ["PositionId"] = "Position_1" },
        new Dictionary<string, object?> { ["CompanyId"] = "Company_1", ["DepartmentId"] = "Department_1", ["PositionId"] = null }
    };

    var resolved = await resolver.ResolveListAsync(
        descriptor,
        rows,
        descriptor.DisplayValueFields,
        CancellationToken.None);

    Assert.Equal("总公司", resolved[0]["CompanyName"]);
    Assert.Equal("研发部", resolved[1]["DepartmentName"]);
    Assert.Equal(string.Empty, resolved[1]["PositionName"]);
}
```

Fake repositories must expose counters and batch methods:

```csharp
public int BatchCallCount { get; private set; }

public Task<IReadOnlyDictionary<string, Company>> GetByIdsAsync(
    IReadOnlyCollection<string> ids,
    CancellationToken cancellationToken = default)
{
    BatchCallCount++;
    return Task.FromResult<IReadOnlyDictionary<string, Company>>(
        _companies.Where(company => ids.Contains(company.Id)).ToDictionary(company => company.Id, StringComparer.Ordinal));
}
```

- [x] **Step 3: Run RED**

Run:

```powershell
dotnet test tests\Organization.Tests\Organization.Tests.csproj --filter "PersonStandardCrudResourceTests|PersonCrudDisplayValueResolverTests" -nr:false -v minimal
```

Expected: FAIL because descriptor and resolver still use string display fields and single-row resolver.

- [x] **Step 4: 更新人员 descriptor**

Modify `PersonCrudDescriptorFactory.Create()`:

```csharp
DisplayValueFields =
[
    new PlatformDisplayValueField("CompanyName", "CompanyId", "Company", "EID", "ENAME"),
    new PlatformDisplayValueField("DepartmentName", "DepartmentId", "Department", "EID", "ENAME"),
    new PlatformDisplayValueField("PositionName", "PositionId", "Position", "EID", "ENAME")
],
```

Add:

```csharp
using ICP.Platform.Query.Descriptors;
```

- [x] **Step 5: 增加组织仓储批量读取**

Add to repository interfaces:

```csharp
Task<IReadOnlyDictionary<string, Company>> GetByIdsAsync(
    IReadOnlyCollection<string> companyIds,
    CancellationToken cancellationToken = default);
```

```csharp
Task<IReadOnlyDictionary<string, Department>> GetByIdsAsync(
    IReadOnlyCollection<string> departmentIds,
    CancellationToken cancellationToken = default);
```

```csharp
Task<IReadOnlyDictionary<string, Position>> GetByIdsAsync(
    IReadOnlyCollection<string> positionIds,
    CancellationToken cancellationToken = default);
```

Implement in EF repositories:

```csharp
public async Task<IReadOnlyDictionary<string, Company>> GetByIdsAsync(
    IReadOnlyCollection<string> companyIds,
    CancellationToken cancellationToken = default)
{
    if (companyIds.Count == 0)
    {
        return new Dictionary<string, Company>(StringComparer.Ordinal);
    }

    var entities = await Entities
        .AsNoTracking()
        .Where(company => companyIds.Contains(EF.Property<string>(company, "EID")))
        .ToListAsync(cancellationToken);

    return entities.Select(Map).ToDictionary(company => company.Id, StringComparer.Ordinal);
}
```

Use the same pattern for departments and positions.

- [x] **Step 6: 实现批量 display resolver**

Modify `PersonCrudDisplayValueResolver.ResolveListAsync`:

```csharp
var requested = requestedDisplayFields.Select(field => field.Field).ToHashSet(StringComparer.Ordinal);
var companyIds = requested.Contains("CompanyName") ? CollectIds(rows, "CompanyId") : [];
var departmentIds = requested.Contains("DepartmentName") ? CollectIds(rows, "DepartmentId") : [];
var positionIds = requested.Contains("PositionName") ? CollectIds(rows, "PositionId") : [];

var companies = await _companyRepository.GetByIdsAsync(companyIds, cancellationToken);
var departments = await _departmentRepository.GetByIdsAsync(departmentIds, cancellationToken);
var positions = await _positionRepository.GetByIdsAsync(positionIds, cancellationToken);

return rows.Select(row => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.Ordinal)
{
    ["CompanyName"] = ReadDisplayName(row, "CompanyId", companies),
    ["DepartmentName"] = ReadDisplayName(row, "DepartmentId", departments),
    ["PositionName"] = ReadDisplayName(row, "PositionId", positions)
}).ToList();
```

Helper methods:

```csharp
private static IReadOnlyCollection<string> CollectIds(
    IEnumerable<IReadOnlyDictionary<string, object?>> rows,
    string field) =>
    rows.Select(row => ReadStringOrNull(row, field))
        .Where(id => id is not null)
        .Select(id => id!)
        .Distinct(StringComparer.Ordinal)
        .ToList();
```

Add three concrete display helpers so each missing source EID or missing target row returns `string.Empty`:

```csharp
private static string ReadDisplayName(
    IReadOnlyDictionary<string, object?> row,
    string sourceField,
    IReadOnlyDictionary<string, Company> companies) =>
    ReadStringOrNull(row, sourceField) is { } id && companies.TryGetValue(id, out var company)
        ? company.Name
        : string.Empty;

private static string ReadDisplayName(
    IReadOnlyDictionary<string, object?> row,
    string sourceField,
    IReadOnlyDictionary<string, Department> departments) =>
    ReadStringOrNull(row, sourceField) is { } id && departments.TryGetValue(id, out var department)
        ? department.Name
        : string.Empty;

private static string ReadDisplayName(
    IReadOnlyDictionary<string, object?> row,
    string sourceField,
    IReadOnlyDictionary<string, Position> positions) =>
    ReadStringOrNull(row, sourceField) is { } id && positions.TryGetValue(id, out var position)
        ? position.Name
        : string.Empty;
```

- [x] **Step 7: Run GREEN**

Run:

```powershell
dotnet test tests\Organization.Tests\Organization.Tests.csproj --filter "PersonStandardCrudResourceTests|PersonCrudDisplayValueResolverTests|EfOrganizationRepositoryTests" -nr:false -v minimal
dotnet build server\modules\Organization\OrganizationModule.csproj -nr:false -v minimal
```

Expected: PASS.

- [x] **Step 8: Commit**

```powershell
git add server\modules\Organization tests\Organization.Tests
git commit -m "功能：人员列表结构化显示值批量解析"
```

## Task 6：Shell 生成 fields 参数并覆盖 row action 依赖字段

**Files:**
- Modify: `frontend/packages/sdk/src/standardCrud.ts`
- Modify: `frontend/packages/shell/src/standard-crud/standardCrudMetadataQuery.ts`
- Modify: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts`
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-list-host.test.tsx`
- Modify: `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`

- [x] **Step 1: 写 SDK URL 编码测试**

Add to `frontend/packages/shell/src/__tests__/standard-crud-sdk.test.ts`:

```ts
it('encodes fields as a comma separated standard CRUD query parameter', () => {
  expect(buildStandardCrudPath('/api/organization/persons', {
    pageIndex: 1,
    pageSize: 20,
    fields: 'EID,PersonNo,CompanyName',
  })).toBe('/api/organization/persons?pageIndex=1&pageSize=20&fields=EID%2CPersonNo%2CCompanyName');
});
```

- [x] **Step 2: 写 Host fields 请求失败测试**

Add to `frontend/packages/shell/src/__tests__/standard-crud-list-host.test.tsx`:

```ts
it('requests view columns and row action state fields through fields parameter', async () => {
  mockStandardCrudModule();
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({
    items: [{ EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentName: '研发部', IsEnabled: true }],
    total: 1,
    page: 1,
    pageSize: 20,
  }), { status: 200, headers: { 'Content-Type': 'application/json' } })));

  const viewDocument = {
    ...personListViewDocument,
    view: {
      ...personListViewDocument.view,
      rowActions: personListViewDocument.view.rowActions.map((action) =>
        action.key === 'disable'
          ? { ...action, visibleWhen: { field: 'IsEnabled', operator: 'equals', value: true } }
          : action),
    },
  };

  await renderHost({}, viewDocument);
  await flushProTableRequest();

  expect(fetch).toHaveBeenCalledWith(
    '/api/organization/persons?pageIndex=1&pageSize=20&fields=EID%2CPersonNo%2CENAME%2CDepartmentName%2CIsEnabled',
    { method: 'GET' },
  );
});
```

Add or update the personnel sample test expected URL in `person-center-crud-pro-sample.test.tsx`:

```ts
expect(globalThis.fetch).toHaveBeenCalledWith(
  expect.stringContaining('fields='),
  { method: 'GET' },
);
expect(decodeURIComponent(String((globalThis.fetch as Mock).mock.calls[0][0]))).toContain(
  'fields=EID,PersonNo,ENAME,CompanyName,DepartmentName,PositionName,EmploymentStatus,IsEnabled',
);
```

- [x] **Step 3: Run RED**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-sdk standard-crud-list-host person-center-crud-pro-sample
```

Expected: FAIL because Host does not add `fields`.

- [x] **Step 4: 实现 fields 收集 helper**

Modify `frontend/packages/shell/src/standard-crud/standardCrudMetadataQuery.ts`:

```ts
export interface StandardCrudListFieldColumnSource {
  field: string;
}

export interface StandardCrudListFieldActionSource {
  visibleWhen?: { field: string };
}

export function collectStandardCrudListRequestFields(
  columns: StandardCrudListFieldColumnSource[],
  rowActions: StandardCrudListFieldActionSource[],
  idField = 'EID',
): string[] {
  const fields: string[] = [];
  const add = (field: string | undefined) => {
    const normalized = field?.trim();
    if (normalized && !fields.includes(normalized)) {
      fields.push(normalized);
    }
  };

  add(idField);
  for (const column of columns) {
    add(column.field);
  }
  for (const action of rowActions) {
    add(action.visibleWhen?.field);
  }

  return fields;
}
```

- [x] **Step 5: Host list 请求追加 fields**

Modify import in `StandardCrudListPageHost.tsx`:

```ts
import {
  buildStandardCrudDefaultFilters,
  buildStandardCrudListQuery,
  collectStandardCrudListRequestFields,
} from '../standard-crud/standardCrudMetadataQuery';
```

Add memo near column/action definitions:

```ts
const listRequestFields = useMemo(
  () => collectStandardCrudListRequestFields(schema.columns, schema.rowActions),
  [schema.columns, schema.rowActions],
);
```

`EID` 是当前 Standard CRUD 前端第一版的默认 id 字段；它必须进入 response fields，因为行操作、抽屉详情和 `registration.getRowId` 依赖行 id。后端仍会按 descriptor 校验 `EID` 是否允许响应，不能因为前端固定提交而绕过字段边界。

Modify table request buildQuery:

```ts
buildQuery: () => ({
  ...buildStandardCrudListQuery(
    schema.query,
    appliedFilters as Record<string, unknown>,
    schema.advancedFilter,
  ),
  ...(listRequestFields.length > 0 ? { fields: listRequestFields.join(',') } : {}),
}),
```

Keep export unchanged: it already sends `columns`, and backend constructs export `Fields`.

- [x] **Step 6: Run GREEN**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-sdk standard-crud-list-host person-center-crud-pro-sample
pnpm --dir frontend --filter @icp-platform/shell build
```

Expected: PASS. Existing Vite chunk-size warnings are acceptable if no new error appears.

- [x] **Step 7: Commit**

```powershell
git add frontend/packages/sdk/src/standardCrud.ts frontend/packages/shell/src/standard-crud/standardCrudMetadataQuery.ts frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx frontend/packages/shell/src/__tests__
git commit -m "功能：标准 CRUD 列表提交字段意图"
```

## Task 7：收口验证、文档和回滚口径

**Files:**
- Modify: `docs/phases/current-handoff.md`
- Modify: `docs/verification/verification-matrix.md`
- Create: `docs/verification/records/2026-06-06-platform-query-kernel-standard-crud-list.md`

- [x] **Step 1: 新增验证记录**

Create `docs/verification/records/2026-06-06-platform-query-kernel-standard-crud-list.md`:

````markdown
# 平台查询内核与 Standard CRUD 列表接入验证记录

日期：2026-06-07

## 范围

- `server/platform-query` 查询契约、字段规划和 EF executor。
- Standard CRUD List 首个接入。
- 人员中心结构化 display value。
- Shell 列表 `fields` 参数。
- Standard CRUD 导出 current/all 字段边界。

## 验证命令

```powershell
dotnet build server\platform-query\PlatformQuery.csproj -nr:false -v minimal
dotnet build apps\host-api\HostApi.csproj -nr:false -v minimal
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrud -nr:false -v minimal
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-sdk standard-crud-list-host person-center-crud-pro-sample
pnpm --dir frontend --filter @icp-platform/shell build
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-06-06-platform-query-kernel-standard-crud-list-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

## 通过标准

- 人员中心列表 SQL 不选择 `CreatedAt / UpdatedAt` 等当前 view 不需要字段。
- SQL 有数据库层分页语义。
- 请求 display value 字段时 SQL 选择 source EID 字段，不选择不存在的 display 字段。
- 未知字段、未知筛选字段、未知排序字段和 display value 排序/筛选稳定失败。
- `CompanyName / DepartmentName / PositionName` 批量解析且不按行触发 N+1。
- 导出 current/all 不缺列，也不绕开字段裁剪。

## 回滚

回退 `server/platform-query`、Standard CRUD adapter、结构化 display value 声明、`fields` 参数解析与 Shell 传参；恢复原先 `DisplayValueFields` 字符串列表和默认 Standard CRUD list 查询。
````

- [x] **Step 2: 更新交接台账**

Modify `docs/phases/current-handoff.md` by adding a new recent-completion entry near the top:

```markdown
### 4.10 平台查询内核与 Standard CRUD 列表接入

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-06-06-platform-query-kernel-standard-crud-list-design.md`
- `docs/superpowers/plans/platform/2026-06-06-platform-query-kernel-standard-crud-list.md`
- `docs/verification/records/2026-06-06-platform-query-kernel-standard-crud-list.md`

完成内容：

- 新增 `server/platform-query` 查询内核，统一字段校验、查询层筛选、排序、分页和投影。
- Standard CRUD List 首个接入平台查询内核，并保留 `StandardCrudListResult` 响应外形。
- 人员中心 display value 改为结构化声明和批量解析。
- Shell 按当前 view columns 与 row action `visibleWhen` 依赖提交 `fields`。
- 导出 current/all 按导出列构造查询字段并复用同一后端字段校验。

验证结论：

- PlatformQuery / PlatformCrud / Organization 后端定向测试通过。
- Shell 标准 CRUD 定向测试与 build 通过。
- AI-first 设计校验和 target 检查通过。

当前推荐下一步：

- 基于平台查询内核评估 lookup 或第二个 Standard CRUD 资源接入；不要在没有详细设计的情况下扩展 display value 排序/筛选。
```

- [x] **Step 3: 更新验证矩阵**

Modify `docs/verification/verification-matrix.md` under Standard CRUD related verification:

```markdown
### Platform Query Kernel Standard CRUD List

- `dotnet build server\platform-query\PlatformQuery.csproj -nr:false -v minimal`
- `dotnet build apps\host-api\HostApi.csproj -nr:false -v minimal`
- `dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrud -nr:false -v minimal`
- `dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal`
- `pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-sdk standard-crud-list-host person-center-crud-pro-sample`
- `pnpm --dir frontend --filter @icp-platform/shell build`

参考：

- [records/2026-06-06-platform-query-kernel-standard-crud-list.md](records/2026-06-06-platform-query-kernel-standard-crud-list.md)
```

- [x] **Step 4: Run full final verification**

Run:

```powershell
dotnet build server\platform-query\PlatformQuery.csproj -nr:false -v minimal
dotnet build apps\host-api\HostApi.csproj -nr:false -v minimal
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrud -nr:false -v minimal
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-sdk standard-crud-list-host person-center-crud-pro-sample
pnpm --dir frontend --filter @icp-platform/shell build
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-06-06-platform-query-kernel-standard-crud-list-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

Expected: all PASS. If one command fails, stop, record the failing command and first actionable failure snippet, then fix within the current task before continuing.

- [x] **Step 5: Commit**

```powershell
git add docs/phases/current-handoff.md docs/verification/verification-matrix.md docs/verification/records/2026-06-06-platform-query-kernel-standard-crud-list.md
git commit -m "验证：记录平台查询内核列表接入"
```

## 一致性 Review

- 设计入口：计划引用 `docs/superpowers/specs/platform/2026-06-06-platform-query-kernel-standard-crud-list-design.md`，并保持同一平台分类目录。
- 范围边界：计划只覆盖 `server/platform-query`、Standard CRUD List 首接、人员中心 display value、Shell `fields`、导出字段边界和验证文档；未加入第二个资源、数据库迁移、required field、`PersonNo` 唯一索引或 display value 排序/筛选。
- 契约一致：`PlatformQueryDescriptor.AllowedModelFields`、`PlatformDisplayValueField`、`PlatformQueryRequest.Fields`、`StandardCrudListQuery.Fields` 与设计命名一致。
- 执行顺序：先字段规划，再 EF executor，再 Standard CRUD adapter/service，再 Organization display resolver，再前端 fields，最后验证文档；每个任务都有 RED/GREEN 验证命令。
- 验证覆盖：包含 SQL 字段裁剪、分页下推、未知字段失败、display value source 字段、批量 resolver、导出 fields、Shell fields 参数、AI-first 设计校验。
- 回滚思路：回退 `server/platform-query`、Standard CRUD adapter、结构化 display value、`fields` 解析与 Shell 传参，可恢复原字符串 display value 和旧 Standard CRUD list 查询路径。

## Self-Review

- Spec coverage: 目标、非目标、字段三分类、Standard CRUD 接入、导出边界、错误处理、验证方案和回滚均有对应任务。
- Placeholder scan: 本计划未发现空白步骤或延后补充项。
- Type consistency: `PlatformQueryDescriptor`、`PlatformQueryRequest`、`PlatformDisplayValueField`、`StandardCrudListQuery.Fields`、`ResolveListAsync` 在后续任务中保持同名同义。

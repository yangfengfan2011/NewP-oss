# 标准 CRUD 写入接管与唯一约束 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Organization `Person` 的 create/update 由标准 CRUD 后端内核接管，并把 `PersonNo` 全局唯一校验沉淀为平台级唯一约束能力。

**Architecture:** 平台新增 `StandardCrudUniqueConstraint`，由 `StandardCrudDescriptor` 静态声明唯一字段、可选 scope、规范化方式和标准错误。`StandardCrudService` 在 create/update 写入前统一执行唯一性校验；update 先读取现有行，合并现有值与更新字段后再校验，避免请求未提交唯一字段时漏检。Organization 模块只在 Person descriptor 中声明 `PersonNo` 全局唯一，Controller create/update 通过 `StandardCrudEndpoint` 写入，`user-link` 特殊入口继续保留在模块服务内。

**Tech Stack:** .NET 10 / ASP.NET Core / C# / xUnit / EF Core shared-type entity / PlatformCrud / Organization module

---

## 文件结构与职责

**Create:**

- `server/platform-crud/Descriptors/StandardCrudUniqueConstraint.cs`
  标准 CRUD 唯一约束声明，只描述字段、可选 scope、规范化方式和错误契约。

**Modify:**

- `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`
  增加 `UniqueConstraints` 集合。
- `server/platform-crud/Runtime/StandardCrudDescriptorValidator.cs`
  增加唯一约束静态校验。
- `server/platform-crud/Abstractions/IStandardCrudRepository.cs`
  增加通用 `ExistsAsync` 等值存在性检查，并接收唯一约束以统一比较规则。
- `server/platform-crud/Persistence/EfStandardCrudRepository.cs`
  实现 `ExistsAsync`，按唯一约束同时规范化存量值与候选值。
- `server/platform-crud/Runtime/StandardCrudService.cs`
  在 create/update 前执行唯一约束；update 先读现有行并合并 candidate row。
- `tests/PlatformCrud.Tests/Support/InMemoryStandardCrudRepository.cs`
  增加 `ExistsAsync` 测试实现。
- `tests/PlatformCrud.Tests/StandardCrudDescriptorTests.cs`
  覆盖唯一约束 descriptor 校验。
- `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`
  覆盖 create/update 唯一性行为。
- `tests/PlatformCrud.Tests/StandardCrudEfRepositoryTests.cs`
  覆盖 EF 仓储 `ExistsAsync`。
- `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`
  声明 `PersonNo` 全局唯一。
- `server/modules/Organization/Crud/PersonCrudValidator.cs`
  移除 `PersonNo` 唯一性专属校验，只保留空 validator 与边界注释。
- `server/modules/Organization/Controllers/PersonsController.cs`
  create/update 切到 `StandardCrudEndpoint`。
- `tests/Organization.Tests/PersonListContractTests.cs`
  增加 Controller create/update 标准 CRUD 接管契约测试。
- `docs/verification/organization-module-verification.md`
  回填本轮验证结果。

**Out of Scope:**

- 不接入 `Position / Company / Department`。
- 不删除 `PersonService` 旧 create/update，实现完成后仍作为短期回滚锚点。
- 不新增数据库唯一索引或迁移。
- 不改前端页面和路由。
- 不把唯一约束写入 metadata 文件。

---

### Task 1: 定义唯一约束 descriptor 与静态校验

**Files:**
- Create: `server/platform-crud/Descriptors/StandardCrudUniqueConstraint.cs`
- Modify: `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`
- Modify: `server/platform-crud/Runtime/StandardCrudDescriptorValidator.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudDescriptorTests.cs`

- [x] **Step 1: 写唯一约束 descriptor 失败测试**

Append to `tests/PlatformCrud.Tests/StandardCrudDescriptorTests.cs`:

```csharp
[Fact]
public void Validate_WhenUniqueConstraintFieldIsMissingFromDetailFields_ThrowsStableMessage()
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
            DetailFields: ["EID", "ENAME"],
            CreateFields: ["PersonNo", "ENAME"],
            UpdateFields: ["ENAME"]),
        UniqueConstraints =
        [
            new StandardCrudUniqueConstraint
            {
                Key = "personNo",
                Fields = ["PersonNo"],
                ErrorCode = "Organization.Person.PersonNoDuplicated",
                Message = "人员工号必须唯一。",
                ReasonKey = "organization.person.personNoDuplicated"
            }
        ]
    };

    var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

    Assert.Equal("标准 CRUD 资源 organization.person 的唯一约束 personNo 字段 PersonNo 必须包含在 DetailFields 中。", ex.Message);
}

[Fact]
public void Validate_WhenUniqueConstraintKeyDuplicated_ThrowsStableMessage()
{
    var descriptor = DescriptorWithUniqueConstraints(
        new StandardCrudUniqueConstraint
        {
            Key = "personNo",
            Fields = ["PersonNo"],
            ErrorCode = "Organization.Person.PersonNoDuplicated",
            Message = "人员工号必须唯一。",
            ReasonKey = "organization.person.personNoDuplicated"
        },
        new StandardCrudUniqueConstraint
        {
            Key = "personNo",
            Fields = ["PersonNo"],
            ErrorCode = "Organization.Person.PersonNoDuplicated",
            Message = "人员工号必须唯一。",
            ReasonKey = "organization.person.personNoDuplicated"
        });

    var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

    Assert.Equal("标准 CRUD 资源 organization.person 的唯一约束 key personNo 重复。", ex.Message);
}

private static StandardCrudDescriptor DescriptorWithUniqueConstraints(params StandardCrudUniqueConstraint[] constraints) => new()
{
    ResourceKey = "organization.person",
    ModuleId = "Organization",
    ModelName = "Person",
    Route = "api/organization/persons",
    IdField = "EID",
    DisplayField = "ENAME",
    FieldSet = new StandardCrudFieldSet(
        ListFields: ["EID", "PersonNo", "ENAME"],
        DetailFields: ["EID", "PersonNo", "ENAME"],
        CreateFields: ["PersonNo", "ENAME"],
        UpdateFields: ["ENAME"]),
    UniqueConstraints = constraints
};
```

- [x] **Step 2: 运行 descriptor 测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudDescriptorTests -nr:false -v minimal
```

Expected:

```text
FAIL
CS0246: The type or namespace name 'StandardCrudUniqueConstraint' could not be found
```

- [x] **Step 3: 新增唯一约束 descriptor 类型**

Create `server/platform-crud/Descriptors/StandardCrudUniqueConstraint.cs`:

```csharp
namespace ICP.Platform.Crud.Descriptors;

public sealed class StandardCrudUniqueConstraint
{
    public required string Key { get; init; }
    public required IReadOnlyList<string> Fields { get; init; }
    public IReadOnlyList<string> ScopeFields { get; init; } = [];
    public bool CaseInsensitive { get; init; } = true;
    public bool TrimWhitespace { get; init; } = true;
    public bool IgnoreWhenAnyFieldMissing { get; init; } = false;
    public required string ErrorCode { get; init; }
    public required string Message { get; init; }
    public required string ReasonKey { get; init; }
}
```

- [x] **Step 4: 在 `StandardCrudDescriptor` 增加唯一约束集合**

Modify `server/platform-crud/Descriptors/StandardCrudDescriptor.cs` and add this property after `DisplayValueFields`:

```csharp
public IReadOnlyList<StandardCrudUniqueConstraint> UniqueConstraints { get; init; } = [];
```

- [x] **Step 5: 增加 descriptor 唯一约束校验**

Modify `server/platform-crud/Runtime/StandardCrudDescriptorValidator.cs`:

```csharp
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

    ValidateUniqueConstraints(descriptor);
}

private static void ValidateUniqueConstraints(StandardCrudDescriptor descriptor)
{
    var keys = new HashSet<string>(StringComparer.Ordinal);
    var readableFields = descriptor.FieldSet.DetailFields.ToHashSet(StringComparer.Ordinal);
    var declaredFields = descriptor.FieldSet.ListFields
        .Concat(descriptor.FieldSet.DetailFields)
        .Concat(descriptor.FieldSet.CreateFields)
        .Concat(descriptor.FieldSet.UpdateFields)
        .ToHashSet(StringComparer.Ordinal);

    foreach (var constraint in descriptor.UniqueConstraints)
    {
        Require(constraint.Key, nameof(constraint.Key), descriptor.ResourceKey);
        Require(constraint.ErrorCode, nameof(constraint.ErrorCode), descriptor.ResourceKey);
        Require(constraint.Message, nameof(constraint.Message), descriptor.ResourceKey);
        Require(constraint.ReasonKey, nameof(constraint.ReasonKey), descriptor.ResourceKey);

        if (!keys.Add(constraint.Key))
        {
            throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 key {constraint.Key} 重复。");
        }

        if (constraint.Fields.Count == 0)
        {
            throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 {constraint.Key} 必须声明 Fields。");
        }

        foreach (var field in constraint.Fields.Concat(constraint.ScopeFields))
        {
            if (string.Equals(field, descriptor.IdField, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 {constraint.Key} 不能包含 IdField。");
            }

            if (!declaredFields.Contains(field))
            {
                throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 {constraint.Key} 字段 {field} 未在字段集合中声明。");
            }

            if (!readableFields.Contains(field))
            {
                throw new InvalidOperationException($"标准 CRUD 资源 {descriptor.ResourceKey} 的唯一约束 {constraint.Key} 字段 {field} 必须包含在 DetailFields 中。");
            }
        }
    }
}
```

- [x] **Step 6: 运行 descriptor 测试确认通过**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudDescriptorTests -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 7: 提交**

```powershell
git add .\server\platform-crud\Descriptors .\server\platform-crud\Runtime\StandardCrudDescriptorValidator.cs .\tests\PlatformCrud.Tests\StandardCrudDescriptorTests.cs
git commit -m "功能：定义标准CRUD唯一约束描述符"
```

### Task 2: 增加仓储 ExistsAsync 能力

**Files:**
- Modify: `server/platform-crud/Abstractions/IStandardCrudRepository.cs`
- Modify: `server/platform-crud/Persistence/EfStandardCrudRepository.cs`
- Modify: `tests/PlatformCrud.Tests/Support/InMemoryStandardCrudRepository.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudEfRepositoryTests.cs`

- [x] **Step 1: 写 EF ExistsAsync 失败测试**

Append to `tests/PlatformCrud.Tests/StandardCrudEfRepositoryTests.cs`:

```csharp
[Fact]
public async Task ExistsAsync_WhenMatchingRowExists_ReturnsTrue()
{
    await using var dbContext = BuildDbContext();
    dbContext.Set<Dictionary<string, object?>>("Person").AddRange(
        Row("person-1", "P001", "张三", true),
        Row("person-2", "P002", "李四", true));
    await dbContext.SaveChangesAsync();

    var repository = new EfStandardCrudRepository(dbContext);

    var exists = await repository.ExistsAsync(
        Descriptor(),
        UniqueConstraint(),
        new Dictionary<string, object?> { ["PersonNo"] = "p001" });

    Assert.True(exists);
}

[Fact]
public async Task ExistsAsync_WhenExcludedIdMatchesOnlyRow_ReturnsFalse()
{
    await using var dbContext = BuildDbContext();
    dbContext.Set<Dictionary<string, object?>>("Person").Add(Row("person-1", "P001", "张三", true));
    await dbContext.SaveChangesAsync();

    var repository = new EfStandardCrudRepository(dbContext);

    var exists = await repository.ExistsAsync(
        Descriptor(),
        UniqueConstraint(),
        new Dictionary<string, object?> { ["PersonNo"] = "P001" },
        excludedId: "person-1");

    Assert.False(exists);
}

[Fact]
public async Task ExistsAsync_WhenConstraintTrimsStoredValue_ReturnsTrue()
{
    await using var dbContext = BuildDbContext();
    dbContext.Set<Dictionary<string, object?>>("Person").Add(Row("person-1", " P001 ", "张三", true));
    await dbContext.SaveChangesAsync();

    var repository = new EfStandardCrudRepository(dbContext);

    var exists = await repository.ExistsAsync(
        Descriptor(),
        UniqueConstraint(),
        new Dictionary<string, object?> { ["PersonNo"] = "P001" });

    Assert.True(exists);
}

[Fact]
public async Task ExistsAsync_WhenConstraintIsCaseSensitive_RespectsCase()
{
    await using var dbContext = BuildDbContext();
    dbContext.Set<Dictionary<string, object?>>("Person").Add(Row("person-1", "P001", "张三", true));
    await dbContext.SaveChangesAsync();

    var repository = new EfStandardCrudRepository(dbContext);

    var exists = await repository.ExistsAsync(
        Descriptor(),
        UniqueConstraint(caseInsensitive: false),
        new Dictionary<string, object?> { ["PersonNo"] = "p001" });

    Assert.False(exists);
}

private static StandardCrudUniqueConstraint UniqueConstraint(bool caseInsensitive = true) => new()
{
    Key = "personNo",
    Fields = ["PersonNo"],
    CaseInsensitive = caseInsensitive,
    TrimWhitespace = true,
    ErrorCode = "Organization.Person.PersonNoDuplicated",
    Message = "人员工号必须唯一。",
    ReasonKey = "organization.person.personNoDuplicated"
};
```

- [x] **Step 2: 运行 EF 仓储测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudEfRepositoryTests -nr:false -v minimal
```

Expected:

```text
FAIL
CS1061: 'EfStandardCrudRepository' does not contain a definition for 'ExistsAsync'
```

- [x] **Step 3: 在仓储接口增加 ExistsAsync**

Modify `server/platform-crud/Abstractions/IStandardCrudRepository.cs` and add before `CreateAsync`:

```csharp
/// <summary>
/// 按等值字段检查资源行是否存在；excludedId 用于 update 时排除当前对象。
/// </summary>
Task<bool> ExistsAsync(
    StandardCrudDescriptor descriptor,
    StandardCrudUniqueConstraint constraint,
    IReadOnlyDictionary<string, object?> equalityValues,
    string? excludedId = null,
    CancellationToken cancellationToken = default);
```

- [x] **Step 4: 实现 EF ExistsAsync**

Modify `server/platform-crud/Persistence/EfStandardCrudRepository.cs` and add this method after `OptionsAsync`:

```csharp
/// <summary>
/// 执行标准等值存在性检查；当前实现与列表查询一样先保持 shared-type entity 的显式内核过滤。
/// </summary>
public async Task<bool> ExistsAsync(
    StandardCrudDescriptor descriptor,
    StandardCrudUniqueConstraint constraint,
    IReadOnlyDictionary<string, object?> equalityValues,
    string? excludedId = null,
    CancellationToken cancellationToken = default)
{
    var rows = await _dbContext.Set<Dictionary<string, object?>>(descriptor.ModelName)
        .AsNoTracking()
        .ToListAsync(cancellationToken);

    return rows.Any(row =>
        (string.IsNullOrWhiteSpace(excludedId) ||
            !string.Equals(ReadString(row, descriptor.IdField), excludedId, StringComparison.Ordinal)) &&
        equalityValues.All(pair => row.TryGetValue(pair.Key, out var value) && UniqueValuesEqual(value, pair.Value, constraint)));
}

private static bool UniqueValuesEqual(object? left, object? right, StandardCrudUniqueConstraint constraint)
{
    var normalizedLeft = NormalizeUniqueValue(left, constraint);
    var normalizedRight = NormalizeUniqueValue(right, constraint);
    if (normalizedLeft is null || normalizedRight is null)
    {
        return normalizedLeft is null && normalizedRight is null;
    }

    return normalizedLeft is string leftText && normalizedRight is string rightText
        ? string.Equals(
            leftText,
            rightText,
            constraint.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
        : Equals(normalizedLeft, normalizedRight);
}

private static object? NormalizeUniqueValue(object? value, StandardCrudUniqueConstraint constraint)
{
    if (value is not string text)
    {
        return value;
    }

    return constraint.TrimWhitespace ? text.Trim() : text;
}
```

- [x] **Step 5: 实现测试仓储 ExistsAsync**

Modify `tests/PlatformCrud.Tests/Support/InMemoryStandardCrudRepository.cs`: first change the class declaration from `public sealed class InMemoryStandardCrudRepository` to `public class InMemoryStandardCrudRepository`, then add this virtual method before `CreateAsync`:

```csharp
public virtual Task<bool> ExistsAsync(
    StandardCrudDescriptor descriptor,
    StandardCrudUniqueConstraint constraint,
    IReadOnlyDictionary<string, object?> equalityValues,
    string? excludedId = null,
    CancellationToken cancellationToken = default)
{
    var exists = _rows.Any(row =>
        (string.IsNullOrWhiteSpace(excludedId) ||
            !string.Equals(row[descriptor.IdField]?.ToString(), excludedId, StringComparison.Ordinal)) &&
        equalityValues.All(pair =>
            row.TryGetValue(pair.Key, out var value) &&
            UniqueValuesEqual(value, pair.Value, constraint)));

    return Task.FromResult(exists);
}

private static bool UniqueValuesEqual(object? left, object? right, StandardCrudUniqueConstraint constraint)
{
    var normalizedLeft = NormalizeUniqueValue(left, constraint);
    var normalizedRight = NormalizeUniqueValue(right, constraint);
    if (normalizedLeft is null || normalizedRight is null)
    {
        return normalizedLeft is null && normalizedRight is null;
    }

    return normalizedLeft is string leftText && normalizedRight is string rightText
        ? string.Equals(
            leftText,
            rightText,
            constraint.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
        : Equals(normalizedLeft, normalizedRight);
}

private static object? NormalizeUniqueValue(object? value, StandardCrudUniqueConstraint constraint)
{
    if (value is not string text)
    {
        return value;
    }

    return constraint.TrimWhitespace ? text.Trim() : text;
}
```

- [x] **Step 6: 运行 EF 仓储测试确认通过**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudEfRepositoryTests -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 7: 提交**

```powershell
git add .\server\platform-crud\Abstractions\IStandardCrudRepository.cs .\server\platform-crud\Persistence\EfStandardCrudRepository.cs .\tests\PlatformCrud.Tests\Support\InMemoryStandardCrudRepository.cs .\tests\PlatformCrud.Tests\StandardCrudEfRepositoryTests.cs
git commit -m "功能：增加标准CRUD存在性检查"
```

### Task 3: 在 StandardCrudService 中执行唯一约束

**Files:**
- Modify: `server/platform-crud/Runtime/StandardCrudService.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`

- [ ] **Step 1: 写 create/update 唯一性失败测试**

Append to `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`:

```csharp
[Fact]
public async Task CreateAsync_WhenUniqueConstraintConflicts_ReturnsConflict()
{
    var repository = new InMemoryStandardCrudRepository(
    [
        new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true }
    ]);
    var service = Service(repository);

    var result = await service.CreateAsync(
        DescriptorWithUniqueConstraint(),
        new Dictionary<string, object?> { ["PersonNo"] = "p001", ["ENAME"] = "李四" });

    Assert.False(result.Succeeded);
    Assert.Equal(409, result.Error!.StatusCode);
    Assert.Equal("Organization.Person.PersonNoDuplicated", result.Error.Code);
}

[Fact]
public async Task UpdateAsync_WhenUniqueFieldNotSubmitted_StillChecksExistingUniqueValue()
{
    var repository = new InMemoryStandardCrudRepository(
    [
        new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true },
        new Dictionary<string, object?> { ["EID"] = "person-2", ["PersonNo"] = "P002", ["ENAME"] = "李四", ["IsEnabled"] = true }
    ]);
    var service = Service(repository);

    var result = await service.UpdateAsync(
        DescriptorWithUniqueConstraint(),
        "person-1",
        new Dictionary<string, object?> { ["ENAME"] = "张三-更新" });

    Assert.True(result.Succeeded);
    Assert.Equal("P001", result.Detail!.Fields["PersonNo"]);
}

[Fact]
public async Task UpdateAsync_WhenUniqueFieldNotSubmitted_PassesExistingValueToUniqueCheck()
{
    var repository = new RecordingExistsStandardCrudRepository(
    [
        new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true }
    ]);
    var service = Service(repository);

    var result = await service.UpdateAsync(
        DescriptorWithUniqueConstraint(),
        "person-1",
        new Dictionary<string, object?> { ["ENAME"] = "张三-更新" });

    Assert.True(result.Succeeded);
    Assert.Equal("person-1", repository.LastExcludedId);
    Assert.NotNull(repository.LastExistsValues);
    Assert.Equal("P001", repository.LastExistsValues!["PersonNo"]);
}

[Fact]
public async Task UpdateAsync_WhenUniqueValueChangesToExistingValue_ReturnsConflict()
{
    var repository = new InMemoryStandardCrudRepository(
    [
        new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true },
        new Dictionary<string, object?> { ["EID"] = "person-2", ["PersonNo"] = "P002", ["ENAME"] = "李四", ["IsEnabled"] = true }
    ]);
    var service = Service(repository);

    var result = await service.UpdateAsync(
        DescriptorWithUniqueConstraint(includePersonNoInUpdate: true),
        "person-1",
        new Dictionary<string, object?> { ["PersonNo"] = "p002", ["ENAME"] = "张三-更新" });

    Assert.False(result.Succeeded);
    Assert.Equal("Organization.Person.PersonNoDuplicated", result.Error!.Code);
}

private static StandardCrudDescriptor DescriptorWithUniqueConstraint(bool includePersonNoInUpdate = false) => new()
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
        UpdateFields: includePersonNoInUpdate ? ["PersonNo", "ENAME", "IsEnabled"] : ["ENAME", "IsEnabled"]),
    UniqueConstraints =
    [
        new StandardCrudUniqueConstraint
        {
            Key = "personNo",
            Fields = ["PersonNo"],
            ErrorCode = "Organization.Person.PersonNoDuplicated",
            Message = "人员工号必须唯一。",
            ReasonKey = "organization.person.personNoDuplicated"
        }
    ]
};

private sealed class RecordingExistsStandardCrudRepository : InMemoryStandardCrudRepository
{
    public RecordingExistsStandardCrudRepository(IEnumerable<Dictionary<string, object?>> rows)
        : base(rows)
    {
    }

    public IReadOnlyDictionary<string, object?>? LastExistsValues { get; private set; }
    public string? LastExcludedId { get; private set; }

    public override Task<bool> ExistsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudUniqueConstraint constraint,
        IReadOnlyDictionary<string, object?> equalityValues,
        string? excludedId = null,
        CancellationToken cancellationToken = default)
    {
        LastExistsValues = new Dictionary<string, object?>(equalityValues, StringComparer.Ordinal);
        LastExcludedId = excludedId;
        return base.ExistsAsync(descriptor, constraint, equalityValues, excludedId, cancellationToken);
    }
}
```

- [ ] **Step 2: 运行写入测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudServiceWriteTests -nr:false -v minimal
```

Expected:

```text
FAIL
CreateAsync_WhenUniqueConstraintConflicts ... Assert.False() Failure
UpdateAsync_WhenUniqueValueChangesToExistingValue ... Assert.False() Failure
```

- [ ] **Step 3: 在 CreateAsync 中调用唯一性校验**

Modify `server/platform-crud/Runtime/StandardCrudService.cs` inside `CreateAsync` so the first part becomes:

```csharp
StandardCrudDescriptorValidator.Validate(descriptor);
var allowed = FilterFields(values, descriptor.FieldSet.CreateFields);
var uniqueError = await ValidateUniqueConstraintsAsync(descriptor, allowed, excludedId: null, cancellationToken);
if (uniqueError is not null)
{
    return StandardCrudMutationResult.Failure(uniqueError);
}

if (_validators.TryGetValue(descriptor.ResourceKey, out var validator))
{
    var error = await validator.ValidateCreateAsync(descriptor, allowed, cancellationToken);
    if (error is not null)
    {
        return StandardCrudMutationResult.Failure(error);
    }
}
```

- [ ] **Step 4: 在 UpdateAsync 中先读现有行并合并 candidate row**

Replace `UpdateAsync` in `server/platform-crud/Runtime/StandardCrudService.cs` with:

```csharp
public async Task<StandardCrudMutationResult> UpdateAsync(
    StandardCrudDescriptor descriptor,
    string id,
    IReadOnlyDictionary<string, object?> values,
    CancellationToken cancellationToken = default)
{
    StandardCrudDescriptorValidator.Validate(descriptor);
    var existing = await _repository.GetByIdAsync(descriptor, id, cancellationToken);
    if (existing is null)
    {
        return StandardCrudMutationResult.Failure(NotFound(descriptor, id));
    }

    var allowed = new Dictionary<string, object?>(
        FilterFields(values, descriptor.FieldSet.UpdateFields),
        StringComparer.Ordinal);
    ApplyUpdateAuditFields(descriptor, allowed);

    var candidate = new Dictionary<string, object?>(existing, StringComparer.Ordinal);
    foreach (var pair in allowed)
    {
        candidate[pair.Key] = pair.Value;
    }

    var uniqueError = await ValidateUniqueConstraintsAsync(descriptor, candidate, excludedId: id, cancellationToken);
    if (uniqueError is not null)
    {
        return StandardCrudMutationResult.Failure(uniqueError);
    }

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
```

- [ ] **Step 5: 增加唯一约束校验 helper**

Add these private helpers to `server/platform-crud/Runtime/StandardCrudService.cs` before `FilterFields`:

```csharp
// 唯一约束属于标准写入前置校验；descriptor 只声明约束，执行顺序由内核统一控制。
private async Task<StandardCrudError?> ValidateUniqueConstraintsAsync(
    StandardCrudDescriptor descriptor,
    IReadOnlyDictionary<string, object?> values,
    string? excludedId,
    CancellationToken cancellationToken)
{
    foreach (var constraint in descriptor.UniqueConstraints)
    {
        var equalityValues = BuildUniqueEqualityValues(constraint, values);
        if (equalityValues is null)
        {
            continue;
        }

        if (await _repository.ExistsAsync(descriptor, constraint, equalityValues, excludedId, cancellationToken))
        {
            return new StandardCrudError
            {
                StatusCode = 409,
                Code = constraint.ErrorCode,
                Message = constraint.Message,
                ReasonKey = constraint.ReasonKey
            };
        }
    }

    return null;
}

private static IReadOnlyDictionary<string, object?>? BuildUniqueEqualityValues(
    StandardCrudUniqueConstraint constraint,
    IReadOnlyDictionary<string, object?> values)
{
    var equalityValues = new Dictionary<string, object?>(StringComparer.Ordinal);
    foreach (var field in constraint.Fields.Concat(constraint.ScopeFields))
    {
        if (!values.TryGetValue(field, out var value) || value is null)
        {
            if (constraint.IgnoreWhenAnyFieldMissing)
            {
                return null;
            }

            equalityValues[field] = null;
            continue;
        }

        equalityValues[field] = NormalizeUniqueValue(value, constraint);
    }

    return equalityValues;
}

private static object? NormalizeUniqueValue(object? value, StandardCrudUniqueConstraint constraint)
{
    if (value is not string text)
    {
        return value;
    }

    var normalized = constraint.TrimWhitespace ? text.Trim() : text;
    return constraint.CaseInsensitive ? normalized.ToUpperInvariant() : normalized;
}
```

- [ ] **Step 6: 确认仓储比较按约束规范化双方值**

确认 `server/platform-crud/Persistence/EfStandardCrudRepository.cs` 和 `tests/PlatformCrud.Tests/Support/InMemoryStandardCrudRepository.cs` 的 `ExistsAsync` 都调用 `UniqueValuesEqual(value, pair.Value, constraint)`，而不是直接调用 `ValuesEqual` 或固定使用 `StringComparison.OrdinalIgnoreCase`。这一点保证存量值和候选值都按 `TrimWhitespace / CaseInsensitive` 统一比较。

```csharp
equalityValues.All(pair => row.TryGetValue(pair.Key, out var value) && UniqueValuesEqual(value, pair.Value, constraint))
```

- [ ] **Step 7: 运行写入测试确认通过**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrudServiceWriteTests -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 8: 提交**

```powershell
git add .\server\platform-crud\Runtime\StandardCrudService.cs .\tests\PlatformCrud.Tests\StandardCrudServiceWriteTests.cs
git commit -m "功能：执行标准CRUD唯一约束校验"
```

### Task 4: Person descriptor 声明全局唯一并收缩 validator

**Files:**
- Modify: `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`
- Modify: `server/modules/Organization/Crud/PersonCrudValidator.cs`
- Modify: `tests/Organization.Tests/PersonListContractTests.cs`

- [ ] **Step 1: 写 Person descriptor 唯一约束合同测试**

Append to `tests/Organization.Tests/PersonListContractTests.cs`:

```csharp
[Fact]
public void PersonCrudDescriptor_DeclaresPersonNoGlobalUniqueConstraint()
{
    var descriptor = PersonCrudDescriptorFactory.Create();

    var constraint = Assert.Single(descriptor.UniqueConstraints);
    Assert.Equal("personNo", constraint.Key);
    Assert.Equal(["PersonNo"], constraint.Fields);
    Assert.Empty(constraint.ScopeFields);
    Assert.Equal("Organization.Person.PersonNoDuplicated", constraint.ErrorCode);
    Assert.Equal("人员工号必须唯一。", constraint.Message);
}
```

- [ ] **Step 2: 运行 Organization 合同测试确认失败**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter PersonCrudDescriptor_DeclaresPersonNoGlobalUniqueConstraint -nr:false -v minimal
```

Expected:

```text
FAIL
Assert.Single() Failure
```

- [ ] **Step 3: 在 Person descriptor 声明全局唯一**

Modify `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`:

```csharp
using ICP.Platform.Crud.Descriptors;

namespace ICP.Platform.Modules.Organization.Crud;

public static class PersonCrudDescriptorFactory
{
    /// <summary>
    /// 创建人员标准 CRUD 描述符；这里只声明字段、扩展点和删除策略，执行流程由 PlatformCrud 内核负责。
    /// </summary>
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
            DetailFields: ["EID", "PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled", "CreatedAt", "UpdatedAt"],
            CreateFields: ["PersonNo", "ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled"],
            UpdateFields: ["ENAME", "CompanyId", "DepartmentId", "PositionId", "EmploymentStatus", "IsEnabled", "UpdatedAt"]),
        OptionLabelFields = ["PersonNo", "ENAME"],
        SummaryBlockKeys = ["userLink", "authorizationSummary"],
        DisplayValueFields = ["CompanyName", "DepartmentName", "PositionName"],
        DeletePolicy = StandardCrudDeletePolicy.Conditional,
        UniqueConstraints =
        [
            new StandardCrudUniqueConstraint
            {
                Key = "personNo",
                Fields = ["PersonNo"],
                ErrorCode = "Organization.Person.PersonNoDuplicated",
                Message = "人员工号必须唯一。",
                ReasonKey = "organization.person.personNoDuplicated"
            }
        ]
    };
}
```

- [ ] **Step 4: 收缩 PersonCrudValidator**

Replace `server/modules/Organization/Crud/PersonCrudValidator.cs` with:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Modules.Organization.Crud;

public sealed class PersonCrudValidator : IStandardCrudValidator
{
    public string ResourceKey => "organization.person";

    /// <summary>
    /// PersonNo 全局唯一已经由 StandardCrudUniqueConstraint 承接；这里保留人员专属校验入口，避免后续差异规则回流到 Controller。
    /// </summary>
    public Task<StandardCrudError?> ValidateCreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StandardCrudError?>(null);

    /// <summary>
    /// 标准更新白名单阻断 PersonNo 写入；后续人员专属组合规则只允许继续放在此入口。
    /// </summary>
    public Task<StandardCrudError?> ValidateUpdateAsync(
        StandardCrudDescriptor descriptor,
        string id,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StandardCrudError?>(null);
}
```

- [ ] **Step 5: 运行 Organization 合同测试确认通过**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter PersonCrudDescriptor_DeclaresPersonNoGlobalUniqueConstraint -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 6: 提交**

```powershell
git add .\server\modules\Organization\Crud\PersonCrudDescriptorFactory.cs .\server\modules\Organization\Crud\PersonCrudValidator.cs .\tests\Organization.Tests\PersonListContractTests.cs
git commit -m "功能：声明人员工号全局唯一约束"
```

### Task 5: 切换 PersonsController create/update 到 StandardCrudEndpoint

**Files:**
- Modify: `server/modules/Organization/Controllers/PersonsController.cs`
- Modify: `tests/Organization.Tests/PersonListContractTests.cs`

- [ ] **Step 1: 增加 Controller create/update 接管测试**

Modify `tests/Organization.Tests/PersonListContractTests.cs`.

Add these tests:

```csharp
[Fact]
public async Task PersonsController_Create_DelegatesToStandardCrudEndpoint()
{
    var standardCrudService = new RecordingStandardCrudService([], MutationDetail());
    var controller = BuildController(standardCrudService);

    var response = await controller.Create(new PersonDto
    {
        PersonNo = "P100",
        Name = "标准创建",
        CompanyId = CompanyId.ToString("D"),
        DepartmentId = DepartmentId.ToString("D"),
        EmploymentStatus = EmploymentStatus.FullTime,
        IsEnabled = true
    });

    var created = Assert.IsType<CreatedAtActionResult>(response.Result);
    Assert.Equal("CreateAsync", standardCrudService.LastMutation);
    Assert.Equal("P100", standardCrudService.LastMutationValues!["PersonNo"]);
    Assert.Equal("标准创建", standardCrudService.LastMutationValues["ENAME"]);
    Assert.IsType<PersonDto>(created.Value);
}

[Fact]
public async Task PersonsController_Update_DelegatesToStandardCrudEndpointWithoutPersonNo()
{
    var personId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    var standardCrudService = new RecordingStandardCrudService([], MutationDetail(personId));
    var controller = BuildController(standardCrudService);

    var response = await controller.Update(personId, new PersonDto
    {
        PersonNo = "SHOULD-NOT-BE-SENT",
        Name = "标准更新",
        CompanyId = CompanyId.ToString("D"),
        DepartmentId = DepartmentId.ToString("D"),
        EmploymentStatus = EmploymentStatus.Contractor,
        IsEnabled = true
    });

    var ok = Assert.IsType<OkObjectResult>(response.Result);
    Assert.Equal("UpdateAsync", standardCrudService.LastMutation);
    Assert.False(standardCrudService.LastMutationValues!.ContainsKey("PersonNo"));
    Assert.Equal("标准更新", standardCrudService.LastMutationValues["ENAME"]);
    Assert.IsType<PersonDto>(ok.Value);
}

[Fact]
public async Task PersonsController_Create_WhenStandardCrudReturnsConflict_UsesStandardError()
{
    var standardCrudService = new RecordingStandardCrudService(
        [],
        mutationError: new StandardCrudError
        {
            StatusCode = 409,
            Code = "Organization.Person.PersonNoDuplicated",
            Message = "人员工号必须唯一。",
            ReasonKey = "organization.person.personNoDuplicated"
        });
    var controller = BuildController(standardCrudService);

    var response = await controller.Create(new PersonDto
    {
        PersonNo = "P100",
        Name = "重复人员",
        IsEnabled = true
    });

    var conflict = Assert.IsType<ObjectResult>(response.Result);
    Assert.Equal(409, conflict.StatusCode);
    var error = Assert.IsType<StandardCrudError>(conflict.Value);
    Assert.Equal("Organization.Person.PersonNoDuplicated", error.Code);
}

[Fact]
public async Task PersonsController_Create_WithDuplicatePersonNo_UsesRealStandardCrudUniqueConstraint()
{
    var controller = BuildControllerWithRealStandardCrudRows(
        StandardCrudPersonRow(Guid.Parse("66666666-6666-6666-6666-666666666666"), "P100", "已存在人员"));

    var response = await controller.Create(new PersonDto
    {
        PersonNo = " p100 ",
        Name = "重复人员",
        IsEnabled = true
    });

    var conflict = Assert.IsType<ObjectResult>(response.Result);
    Assert.Equal(409, conflict.StatusCode);
    var error = Assert.IsType<StandardCrudError>(conflict.Value);
    Assert.Equal("Organization.Person.PersonNoDuplicated", error.Code);
}

[Fact]
public async Task PersonsController_Update_WhenExistingPersonNoAlreadyConflicts_UsesRealStandardCrudUniqueConstraint()
{
    var personId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    var controller = BuildControllerWithRealStandardCrudRows(
        StandardCrudPersonRow(personId, "P100", "待更新人员"),
        StandardCrudPersonRow(Guid.Parse("88888888-8888-8888-8888-888888888888"), "p100", "冲突人员"));

    var response = await controller.Update(personId, new PersonDto
    {
        PersonNo = "SHOULD-NOT-BE-SENT",
        Name = "更新人员",
        IsEnabled = true
    });

    var conflict = Assert.IsType<ObjectResult>(response.Result);
    Assert.Equal(409, conflict.StatusCode);
    var error = Assert.IsType<StandardCrudError>(conflict.Value);
    Assert.Equal("Organization.Person.PersonNoDuplicated", error.Code);
}
```

Add this helper near other helpers:

```csharp
private static StandardCrudDetail MutationDetail(Guid? id = null) => new()
{
    Fields = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["EID"] = (id ?? Guid.Parse("55555555-5555-5555-5555-555555555555")).ToString("D"),
        ["PersonNo"] = "P100",
        ["ENAME"] = "标准人员",
        ["CompanyId"] = CompanyId.ToString("D"),
        ["DepartmentId"] = DepartmentId.ToString("D"),
        ["EmploymentStatus"] = "FullTime",
        ["IsEnabled"] = true,
        ["CreatedAt"] = DateTime.UtcNow
    }
};

private static PersonsController BuildControllerWithRealStandardCrudRows(params Dictionary<string, object?>[] rows)
{
    var standardCrudService = new StandardCrudService(
        new ControllerStandardCrudRepository(rows),
        [],
        [],
        [],
        [new PersonCrudValidator()],
        [],
        []);

    return new PersonsController(
        BuildService(),
        new FakeIdentityUserDirectory(),
        new StandardCrudEndpoint(standardCrudService));
}

private static Dictionary<string, object?> StandardCrudPersonRow(Guid id, string personNo, string name) => new(StringComparer.Ordinal)
{
    ["EID"] = id.ToString("D"),
    ["PersonNo"] = personNo,
    ["ENAME"] = name,
    ["CompanyId"] = null,
    ["DepartmentId"] = null,
    ["PositionId"] = null,
    ["EmploymentStatus"] = "FullTime",
    ["IsEnabled"] = true,
    ["CreatedAt"] = DateTime.UtcNow,
    ["UpdatedAt"] = null
};
```

- [ ] **Step 2: 扩展 RecordingStandardCrudService 记录 mutation**

Modify nested `RecordingStandardCrudService` in `tests/Organization.Tests/PersonListContractTests.cs`:

```csharp
private readonly StandardCrudError? _mutationError;

public RecordingStandardCrudService(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> items,
    StandardCrudDetail? detail = null,
    StandardCrudError? mutationError = null)
{
    _items = items;
    _detail = detail;
    _mutationError = mutationError;
}

public string? LastMutation { get; private set; }
public IReadOnlyDictionary<string, object?>? LastMutationValues { get; private set; }
```

Replace `CreateAsync` and `UpdateAsync` in the same nested class:

```csharp
public Task<StandardCrudMutationResult> CreateAsync(
    StandardCrudDescriptor descriptor,
    IReadOnlyDictionary<string, object?> values,
    CancellationToken cancellationToken = default)
{
    LastMutation = nameof(CreateAsync);
    LastMutationValues = new Dictionary<string, object?>(values, StringComparer.Ordinal);
    return Task.FromResult(_mutationError is null
        ? StandardCrudMutationResult.Success(_detail)
        : StandardCrudMutationResult.Failure(_mutationError));
}

public Task<StandardCrudMutationResult> UpdateAsync(
    StandardCrudDescriptor descriptor,
    string id,
    IReadOnlyDictionary<string, object?> values,
    CancellationToken cancellationToken = default)
{
    LastMutation = nameof(UpdateAsync);
    LastMutationValues = new Dictionary<string, object?>(values, StringComparer.Ordinal);
    return Task.FromResult(_mutationError is null
        ? StandardCrudMutationResult.Success(_detail)
        : StandardCrudMutationResult.Failure(_mutationError));
}
```

Add this nested repository to the test file for the real `StandardCrudService` link tests:

```csharp
private sealed class ControllerStandardCrudRepository : IStandardCrudRepository
{
    private readonly List<Dictionary<string, object?>> _rows;

    public ControllerStandardCrudRepository(IEnumerable<Dictionary<string, object?>> rows)
    {
        _rows = rows.Select(row => new Dictionary<string, object?>(row, StringComparer.Ordinal)).ToList();
    }

    public Task<StandardCrudListResult> ListAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        IReadOnlyList<StandardCrudQueryCondition> conditions,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new StandardCrudListResult([], 0, query.Page, query.PageSize, query.SortBy, query.SortDirection));

    public Task<IReadOnlyDictionary<string, object?>?> GetByIdAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default)
    {
        var row = _rows.FirstOrDefault(item => string.Equals(item[descriptor.IdField]?.ToString(), id, StringComparison.Ordinal));
        return Task.FromResult<IReadOnlyDictionary<string, object?>?>(row is null ? null : Project(row, descriptor.FieldSet.DetailFields));
    }

    public Task<IReadOnlyList<StandardCrudOptionItem>> OptionsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudListQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StandardCrudOptionItem>>([]);

    public Task<bool> ExistsAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudUniqueConstraint constraint,
        IReadOnlyDictionary<string, object?> equalityValues,
        string? excludedId = null,
        CancellationToken cancellationToken = default)
    {
        var exists = _rows.Any(row =>
            (string.IsNullOrWhiteSpace(excludedId) ||
                !string.Equals(row[descriptor.IdField]?.ToString(), excludedId, StringComparison.Ordinal)) &&
            equalityValues.All(pair =>
                row.TryGetValue(pair.Key, out var value) &&
                UniqueValuesEqual(value, pair.Value, constraint)));

        return Task.FromResult(exists);
    }

    public Task<IReadOnlyDictionary<string, object?>> CreateAsync(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default)
    {
        var row = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        row[descriptor.IdField] = Guid.NewGuid().ToString("D");
        row["CreatedAt"] = DateTime.UtcNow;
        _rows.Add(row);
        return Task.FromResult(Project(row, descriptor.FieldSet.DetailFields));
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

        return Task.FromResult<IReadOnlyDictionary<string, object?>?>(Project(row, descriptor.FieldSet.DetailFields));
    }

    public Task<bool> DeleteAsync(
        StandardCrudDescriptor descriptor,
        string id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    private static IReadOnlyDictionary<string, object?> Project(IReadOnlyDictionary<string, object?> row, IReadOnlyList<string> fields)
    {
        var projected = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            projected[field] = row.TryGetValue(field, out var value) ? value : null;
        }

        return projected;
    }

    private static bool UniqueValuesEqual(object? left, object? right, StandardCrudUniqueConstraint constraint)
    {
        var normalizedLeft = NormalizeUniqueValue(left, constraint);
        var normalizedRight = NormalizeUniqueValue(right, constraint);
        if (normalizedLeft is null || normalizedRight is null)
        {
            return normalizedLeft is null && normalizedRight is null;
        }

        return normalizedLeft is string leftText && normalizedRight is string rightText
            ? string.Equals(
                leftText,
                rightText,
                constraint.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            : Equals(normalizedLeft, normalizedRight);
    }

    private static object? NormalizeUniqueValue(object? value, StandardCrudUniqueConstraint constraint)
    {
        if (value is not string text)
        {
            return value;
        }

        return constraint.TrimWhitespace ? text.Trim() : text;
    }
}
```

- [ ] **Step 3: 运行 Controller 测试确认失败**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "PersonsController_Create_DelegatesToStandardCrudEndpoint|PersonsController_Update_DelegatesToStandardCrudEndpointWithoutPersonNo|PersonsController_Create_WhenStandardCrudReturnsConflict|PersonsController_Create_WithDuplicatePersonNo_UsesRealStandardCrudUniqueConstraint|PersonsController_Update_WhenExistingPersonNoAlreadyConflicts_UsesRealStandardCrudUniqueConstraint" -nr:false -v minimal
```

Expected:

```text
FAIL
Assert.Equal() Failure
Expected: CreateAsync
Actual:   null
```

- [ ] **Step 4: 在 PersonsController 增加 mutation mapping helpers**

Modify `server/modules/Organization/Controllers/PersonsController.cs` and add these helpers before `MapListItem`:

```csharp
/// <summary>
/// 将既有 PersonDto 写入请求映射为标准 CRUD 字段，创建允许写入 PersonNo。
/// </summary>
private static IReadOnlyDictionary<string, object?> MapCreateValues(PersonDto dto) =>
    new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["PersonNo"] = dto.PersonNo,
        ["ENAME"] = dto.DisplayName,
        ["CompanyId"] = dto.CompanyId,
        ["DepartmentId"] = dto.DepartmentId,
        ["PositionId"] = dto.PositionId,
        ["EmploymentStatus"] = dto.EmploymentStatus?.ToString(),
        ["IsEnabled"] = dto.IsEnabled
    };

/// <summary>
/// 更新请求不传 PersonNo，工号只读由字段白名单和映射层共同保护。
/// </summary>
private static IReadOnlyDictionary<string, object?> MapUpdateValues(PersonDto dto) =>
    new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["ENAME"] = dto.DisplayName,
        ["CompanyId"] = dto.CompanyId,
        ["DepartmentId"] = dto.DepartmentId,
        ["PositionId"] = dto.PositionId,
        ["EmploymentStatus"] = dto.EmploymentStatus?.ToString(),
        ["IsEnabled"] = dto.IsEnabled
    };

private static PersonDto MapMutationDto(StandardCrudDetail detail) => new()
{
    Id = ReadString(detail.Fields, "EID"),
    PersonNo = ReadString(detail.Fields, "PersonNo"),
    DisplayName = ReadString(detail.Fields, "ENAME"),
    CompanyId = ReadStringOrNull(detail.Fields, "CompanyId"),
    DepartmentId = ReadStringOrNull(detail.Fields, "DepartmentId"),
    PositionId = ReadStringOrNull(detail.Fields, "PositionId"),
    EmploymentStatus = Enum.TryParse<EmploymentStatus>(ReadString(detail.Fields, "EmploymentStatus"), ignoreCase: true, out var status)
        ? status
        : EmploymentStatus.FullTime,
    IsEnabled = ReadBool(detail.Fields, "IsEnabled", true),
    CreatedAt = ReadDateTime(detail.Fields, "CreatedAt"),
    UpdatedAt = ReadNullableDateTime(detail.Fields, "UpdatedAt")
};

private static string? ReadStringOrNull(IReadOnlyDictionary<string, object?> row, string key)
{
    var value = ReadString(row, key);
    return string.IsNullOrWhiteSpace(value) ? null : value;
}

private static DateTime? ReadNullableDateTime(IReadOnlyDictionary<string, object?> row, string key) =>
    row.TryGetValue(key, out var value) && value is DateTime dateTime ? dateTime : null;
```

- [ ] **Step 5: 切换 Create/Update 方法**

Replace `Create` and `Update` in `server/modules/Organization/Controllers/PersonsController.cs`:

```csharp
[HttpPost]
public async Task<ActionResult<PersonDto>> Create(
    [FromBody] PersonDto dto,
    CancellationToken cancellationToken = default)
{
    var result = await _standardCrudEndpoint.CreateAsync(PersonCrudDescriptor, MapCreateValues(dto), cancellationToken);
    return result.Succeeded && result.Detail is not null
        ? CreatedAtAction(nameof(Get), new { id = ReadString(result.Detail.Fields, "EID") }, MapMutationDto(result.Detail))
        : StatusCode(result.Error!.StatusCode, result.Error);
}

[HttpPut("{id:guid}")]
public async Task<ActionResult<PersonDto>> Update(
    Guid id,
    [FromBody] PersonDto dto,
    CancellationToken cancellationToken = default)
{
    var result = await _standardCrudEndpoint.UpdateAsync(PersonCrudDescriptor, id.ToString("D"), MapUpdateValues(dto), cancellationToken);
    return result.Succeeded && result.Detail is not null
        ? Ok(MapMutationDto(result.Detail))
        : StatusCode(result.Error!.StatusCode, result.Error);
}
```

- [ ] **Step 6: 运行 Controller 测试确认通过**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "PersonsController_Create_DelegatesToStandardCrudEndpoint|PersonsController_Update_DelegatesToStandardCrudEndpointWithoutPersonNo|PersonsController_Create_WhenStandardCrudReturnsConflict|PersonsController_Create_WithDuplicatePersonNo_UsesRealStandardCrudUniqueConstraint|PersonsController_Update_WhenExistingPersonNoAlreadyConflicts_UsesRealStandardCrudUniqueConstraint" -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 7: 提交**

```powershell
git add .\server\modules\Organization\Controllers\PersonsController.cs .\tests\Organization.Tests\PersonListContractTests.cs
git commit -m "功能：切换人员写入到标准CRUD端点"
```

### Task 6: 全量验证与记录

**Files:**
- Modify: `docs/verification/organization-module-verification.md`

- [ ] **Step 1: 运行平台 CRUD 测试**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 2: 运行 Organization 测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 3: 运行 HostApi 构建**

Run:

```powershell
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 4: 运行 Organization 前端构建 smoke**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-modules/organization build
```

Expected:

```text
build completed successfully
```

- [ ] **Step 5: 回填验证记录**

Append to `docs/verification/organization-module-verification.md`:

```md

## 标准 CRUD 写入接管与唯一约束验证

验证日期：2026-05-04

本轮验证目标：

- `Person create/update` 已由 `PersonsController` 委托 `StandardCrudEndpoint` 接管。
- `PersonNo` 全局唯一由 `StandardCrudUniqueConstraint` 声明并由 `StandardCrudService` 执行。
- `PersonCrudValidator` 不再承载人员工号唯一性，避免资源专属 service 样板继续扩散。
- `user-link` 特殊入口仍保留在 Organization 模块服务内。

验证命令：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
pnpm --dir .\frontend --filter @icp-modules/organization build
```

验证结论：

- 标准 CRUD 唯一约束、仓储存在性检查和 Person 写入接管通过验证。
- `PersonNo` 按当前阶段要求为平台内全局唯一，不再按 `CompanyId` 分组。
- 本轮未引入数据库唯一索引；并发强一致性如需支持，应另开设计。
```

- [ ] **Step 6: 提交**

```powershell
git add .\docs\verification\organization-module-verification.md
git commit -m "验证：记录人员写入接管与唯一约束结果"
```

## 一致性 Review

在正式执行本计划前，先做一次计划一致性 review，确认：

- 本计划与 `docs/superpowers/specs/2026-05-04-standard-crud-person-closeout-and-template-design.md` 一致。
- `PersonNo` 是全局唯一，Person descriptor 不启用 `ScopeFields`。
- create/update 必须切到 `StandardCrudEndpoint`。
- 唯一性执行在 `StandardCrudService`，不在 Controller、metadata 或 Person 专属 service。
- update 先读现有行并合并 candidate row，再执行唯一性校验。
- 本计划未引入第二资源、数据库索引、前端页面变更或 Host/Shell 内部改造。

## Self-Review

- **Spec coverage:** Task 1-3 覆盖平台唯一约束、descriptor 校验、仓储存在性和服务写入流程；Task 4-5 覆盖 Person descriptor、validator 收缩和 create/update endpoint 接管；Task 6 覆盖验证记录。
- **Completeness scan:** 本计划没有保留空泛章节、未命名文件或无期望结果的命令。
- **Type consistency:** 计划统一使用 `StandardCrudUniqueConstraint`、`UniqueConstraints`、`ExistsAsync`、`ValidateUniqueConstraintsAsync`、`MapCreateValues`、`MapUpdateValues` 和 `MapMutationDto`。

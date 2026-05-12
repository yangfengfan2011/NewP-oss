# 标准 CRUD 必填字段校验 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让标准 CRUD 资源通过 descriptor 显式声明写入必填字段，并由平台内核在写入前返回稳定校验错误。

**Architecture:** 在 `StandardCrudDescriptor` 上新增 `RequiredFields` 契约，字段、作用范围和错误契约由资源声明，缺失判断和执行顺序由 `StandardCrudService` 统一实现。Person 试点只声明当前模型已非空且属于标准写入主干的 `PersonNo`、`ENAME`、`IsEnabled`，不收紧 Organization 归属字段的 optional 口径。

**Tech Stack:** .NET 10 / C# / xUnit / ASP.NET Core Minimal API / PowerShell

---

## 文件结构与职责

**Create**

- `server/platform-crud/Descriptors/StandardCrudRequiredField.cs`  
  标准 CRUD 必填字段声明模型，承载字段名、作用范围和稳定错误契约。
- `server/platform-crud/Descriptors/StandardCrudRequiredFieldScope.cs`  
  必填字段适用范围枚举：创建、更新、创建与更新。

**Modify**

- `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`  
  增加 `RequiredFields`，与 `UniqueConstraints` 同级作为标准写入前置契约。
- `server/platform-crud/Runtime/StandardCrudDescriptorValidator.cs`  
  增加 required field 静态校验，拦截字段缺失、错误契约缺失、重复声明和字段集合不一致。
- `server/platform-crud/Runtime/StandardCrudService.cs`  
  在 create/update 写入前执行必填校验，并保证执行顺序早于唯一约束和模块 validator。
- `tests/PlatformCrud.Tests/StandardCrudDescriptorTests.cs`  
  覆盖 required field 静态校验失败信号。
- `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`  
  覆盖 create/update 必填运行时行为。
- `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`  
  Person 试点声明 `PersonNo`、`ENAME`、`IsEnabled` 的必填契约。
- `tests/Organization.Tests/PersonStandardCrudResourceTests.cs`  
  覆盖 Person required field 声明，并锁定 Organization optional 字段不被误收紧。
- `docs/verification/organization-module-verification.md`  
  记录标准 CRUD 必填字段校验平台化验证。

---

### Task 1: 新增标准 CRUD 必填字段契约

**Files:**
- Create: `server/platform-crud/Descriptors/StandardCrudRequiredFieldScope.cs`
- Create: `server/platform-crud/Descriptors/StandardCrudRequiredField.cs`
- Modify: `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`
- Test: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`

- [x] **Step 1: 写 descriptor 契约失败测试**

Append this test to `tests/PlatformCrud.Tests/StandardCrudDescriptorTests.cs`:

```csharp
[Fact]
public void Descriptor_CanDeclareRequiredFields()
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
            ListFields: ["EID", "PersonNo", "ENAME"],
            DetailFields: ["EID", "PersonNo", "ENAME"],
            CreateFields: ["PersonNo", "ENAME"],
            UpdateFields: ["ENAME"]),
        RequiredFields =
        [
            new StandardCrudRequiredField
            {
                Field = "PersonNo",
                Scope = StandardCrudRequiredFieldScope.CreateOnly,
                ErrorCode = "Organization.Person.PersonNoRequired",
                Message = "人员工号不能为空。",
                ReasonKey = "organization.person.personNoRequired"
            }
        ]
    };

    var required = Assert.Single(descriptor.RequiredFields);

    Assert.Equal("PersonNo", required.Field);
    Assert.Equal(StandardCrudRequiredFieldScope.CreateOnly, required.Scope);
}
```

- [x] **Step 2: 运行契约测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter Descriptor_CanDeclareRequiredFields -nr:false -v minimal
```

Expected: FAIL，提示 `StandardCrudRequiredField`、`StandardCrudRequiredFieldScope` 或 `RequiredFields` 不存在。

- [x] **Step 3: 新增必填范围枚举**

Create `server/platform-crud/Descriptors/StandardCrudRequiredFieldScope.cs`:

```csharp
namespace ICP.Platform.Crud.Descriptors;

public enum StandardCrudRequiredFieldScope
{
    CreateOnly,
    UpdateOnly,
    CreateAndUpdate
}
```

- [x] **Step 4: 新增必填字段契约模型**

Create `server/platform-crud/Descriptors/StandardCrudRequiredField.cs`:

```csharp
namespace ICP.Platform.Crud.Descriptors;

/// <summary>
/// 声明标准 CRUD 资源的写入必填字段；这里只表达字段、作用范围和错误契约，缺失判断由运行时内核执行。
/// </summary>
public sealed class StandardCrudRequiredField
{
    /// <summary>
    /// 必填字段名；必须来自当前资源 descriptor 的标准字段集合，避免形成隐式字段契约。
    /// </summary>
    public required string Field { get; init; }

    /// <summary>
    /// 必填规则适用的写入阶段；更新阶段只校验本次提交字段是否被清空。
    /// </summary>
    public StandardCrudRequiredFieldScope Scope { get; init; } = StandardCrudRequiredFieldScope.CreateAndUpdate;

    /// <summary>
    /// 必填失败时返回给 API 调用方的稳定错误码。
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// 必填失败时返回给 API 调用方的本地化错误消息。
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 必填失败时返回给前端或日志定位的稳定原因键。
    /// </summary>
    public required string ReasonKey { get; init; }
}
```

- [x] **Step 5: 在 descriptor 上声明 RequiredFields**

Modify `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`.

Replace the tail of the class with:

```csharp
    public IReadOnlyList<string> SummaryBlockKeys { get; init; } = [];
    public IReadOnlyList<string> DisplayValueFields { get; init; } = [];

    /// <summary>
    /// 标准写入前置必填字段声明；这里只描述字段、作用范围和错误契约，缺失判断由运行时内核执行。
    /// </summary>
    public IReadOnlyList<StandardCrudRequiredField> RequiredFields { get; init; } = [];

    /// <summary>
    /// 标准写入前置唯一约束声明；这里只描述字段、作用域和错误契约，查重流程由运行时内核执行。
    /// </summary>
    public IReadOnlyList<StandardCrudUniqueConstraint> UniqueConstraints { get; init; } = [];
}
```

- [x] **Step 6: 运行契约测试确认通过**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter Descriptor_CanDeclareRequiredFields -nr:false -v minimal
```

Expected: PASS。

- [x] **Step 7: 提交**

```powershell
git add .\server\platform-crud\Descriptors\StandardCrudRequiredFieldScope.cs .\server\platform-crud\Descriptors\StandardCrudRequiredField.cs .\server\platform-crud\Descriptors\StandardCrudDescriptor.cs .\tests\PlatformCrud.Tests\StandardCrudDescriptorTests.cs
git commit -m "功能：新增标准CRUD必填字段契约"
```

---

### Task 2: 必填字段 descriptor 静态校验

**Files:**
- Modify: `tests/PlatformCrud.Tests/StandardCrudDescriptorTests.cs`
- Modify: `server/platform-crud/Runtime/StandardCrudDescriptorValidator.cs`
- Test: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`

- [x] **Step 1: 写 required field 静态校验失败测试**

Append these tests and helper to `tests/PlatformCrud.Tests/StandardCrudDescriptorTests.cs`:

```csharp
[Fact]
public void Validate_WhenRequiredFieldContractMissing_ThrowsStableMessage()
{
    var descriptor = DescriptorWithRequiredFields(
        new StandardCrudRequiredField
        {
            Field = "ENAME",
            ErrorCode = "",
            Message = "姓名不能为空。",
            ReasonKey = "organization.person.nameRequired"
        });

    var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

    Assert.Equal("标准 CRUD 资源 organization.person 的必填字段 ENAME 缺少 ErrorCode。", ex.Message);
}

[Fact]
public void Validate_WhenRequiredFieldDuplicatedInSameScope_ThrowsStableMessage()
{
    var descriptor = DescriptorWithRequiredFields(
        Required("ENAME", StandardCrudRequiredFieldScope.CreateAndUpdate),
        Required("ENAME", StandardCrudRequiredFieldScope.CreateAndUpdate));

    var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

    Assert.Equal("标准 CRUD 资源 organization.person 的必填字段 ENAME 在 CreateAndUpdate 范围内重复声明。", ex.Message);
}

[Fact]
public void Validate_WhenRequiredFieldIsNotDeclaredInAnyFieldSet_ThrowsStableMessage()
{
    var descriptor = DescriptorWithRequiredFields(Required("UnknownField", StandardCrudRequiredFieldScope.CreateOnly));

    var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

    Assert.Equal("标准 CRUD 资源 organization.person 的必填字段 UnknownField 未在字段集合中声明。", ex.Message);
}

[Fact]
public void Validate_WhenRequiredFieldIsNotInDetailFields_ThrowsStableMessage()
{
    var descriptor = DescriptorWithRequiredFields(Required("PersonNo", StandardCrudRequiredFieldScope.CreateOnly));

    var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

    Assert.Equal("标准 CRUD 资源 organization.person 的必填字段 PersonNo 必须包含在 DetailFields 中。", ex.Message);
}

[Fact]
public void Validate_WhenRequiredCreateFieldIsNotWritable_ThrowsStableMessage()
{
    var descriptor = DescriptorWithRequiredFields(Required("PositionId", StandardCrudRequiredFieldScope.CreateOnly));

    var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

    Assert.Equal("标准 CRUD 资源 organization.person 的必填字段 PositionId 必须包含在 CreateFields 中。", ex.Message);
}

[Fact]
public void Validate_WhenRequiredUpdateFieldIsNotWritable_ThrowsStableMessage()
{
    var descriptor = DescriptorWithRequiredFields(
        [Required("PersonNo", StandardCrudRequiredFieldScope.UpdateOnly)],
        includePersonNoInDetail: true);

    var ex = Assert.Throws<InvalidOperationException>(() => StandardCrudDescriptorValidator.Validate(descriptor));

    Assert.Equal("标准 CRUD 资源 organization.person 的必填字段 PersonNo 必须包含在 UpdateFields 中。", ex.Message);
}

private static StandardCrudRequiredField Required(
    string field,
    StandardCrudRequiredFieldScope scope = StandardCrudRequiredFieldScope.CreateAndUpdate) => new()
{
    Field = field,
    Scope = scope,
    ErrorCode = $"Organization.Person.{field}Required",
    Message = $"{field} 不能为空。",
    ReasonKey = $"organization.person.{field}Required"
};

private static StandardCrudDescriptor DescriptorWithRequiredFields(
    params StandardCrudRequiredField[] requiredFields) =>
    DescriptorWithRequiredFields(requiredFields, includePersonNoInDetail: false);

private static StandardCrudDescriptor DescriptorWithRequiredFields(
    IReadOnlyList<StandardCrudRequiredField> requiredFields,
    bool includePersonNoInDetail)
{
    return new StandardCrudDescriptor
    {
        ResourceKey = "organization.person",
        ModuleId = "Organization",
        ModelName = "Person",
        Route = "api/organization/persons",
        IdField = "EID",
        DisplayField = "ENAME",
        FieldSet = new StandardCrudFieldSet(
            ListFields: ["EID", "ENAME", "PersonNo", "PositionId"],
            DetailFields: includePersonNoInDetail
                ? ["EID", "ENAME", "PersonNo", "PositionId"]
                : ["EID", "ENAME", "PositionId"],
            CreateFields: ["ENAME", "PersonNo"],
            UpdateFields: ["ENAME"]),
        RequiredFields = requiredFields
    };
}
```

- [x] **Step 2: 运行静态校验测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "RequiredFieldContractMissing|RequiredFieldDuplicatedInSameScope|RequiredFieldIsNotDeclaredInAnyFieldSet|RequiredFieldIsNotInDetailFields|RequiredCreateFieldIsNotWritable|RequiredUpdateFieldIsNotWritable" -nr:false -v minimal
```

Expected: FAIL，至少一个测试提示没有执行 required field 静态校验。

- [x] **Step 3: 在 descriptor validator 中挂载 required 校验**

Modify `server/platform-crud/Runtime/StandardCrudDescriptorValidator.cs`.

In `Validate`, insert before `ValidateUniqueConstraints(descriptor);`:

```csharp
        ValidateRequiredFields(descriptor);
```

- [x] **Step 4: 增加 required field 静态校验实现**

Add these methods to `server/platform-crud/Runtime/StandardCrudDescriptorValidator.cs`, before `ValidateUniqueConstraints`:

```csharp
    // 必填声明只做静态边界校验：字段归属和错误契约必须清晰，运行时缺失判断由服务内核统一接管。
    private static void ValidateRequiredFields(StandardCrudDescriptor descriptor)
    {
        var declarations = new HashSet<(string Field, StandardCrudRequiredFieldScope Scope)>();
        var declaredFields = descriptor.FieldSet.ListFields
            .Concat(descriptor.FieldSet.DetailFields)
            .Concat(descriptor.FieldSet.CreateFields)
            .Concat(descriptor.FieldSet.UpdateFields)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var requiredField in descriptor.RequiredFields)
        {
            RequireRequiredFieldValue(requiredField.Field, nameof(requiredField.Field), descriptor.ResourceKey);
            RequireRequiredFieldValue(requiredField.ErrorCode, nameof(requiredField.ErrorCode), descriptor.ResourceKey, requiredField.Field);
            RequireRequiredFieldValue(requiredField.Message, nameof(requiredField.Message), descriptor.ResourceKey, requiredField.Field);
            RequireRequiredFieldValue(requiredField.ReasonKey, nameof(requiredField.ReasonKey), descriptor.ResourceKey, requiredField.Field);

            if (!declarations.Add((requiredField.Field, requiredField.Scope)))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 资源 {descriptor.ResourceKey} 的必填字段 {requiredField.Field} 在 {requiredField.Scope} 范围内重复声明。");
            }

            if (!declaredFields.Contains(requiredField.Field))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 资源 {descriptor.ResourceKey} 的必填字段 {requiredField.Field} 未在字段集合中声明。");
            }

            if (!descriptor.FieldSet.DetailFields.Contains(requiredField.Field, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 资源 {descriptor.ResourceKey} 的必填字段 {requiredField.Field} 必须包含在 DetailFields 中。");
            }

            if (RequiresCreate(requiredField.Scope)
                && !descriptor.FieldSet.CreateFields.Contains(requiredField.Field, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 资源 {descriptor.ResourceKey} 的必填字段 {requiredField.Field} 必须包含在 CreateFields 中。");
            }

            if (RequiresUpdate(requiredField.Scope)
                && !descriptor.FieldSet.UpdateFields.Contains(requiredField.Field, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"标准 CRUD 资源 {descriptor.ResourceKey} 的必填字段 {requiredField.Field} 必须包含在 UpdateFields 中。");
            }
        }
    }

    private static bool RequiresCreate(StandardCrudRequiredFieldScope scope) =>
        scope is StandardCrudRequiredFieldScope.CreateOnly or StandardCrudRequiredFieldScope.CreateAndUpdate;

    private static bool RequiresUpdate(StandardCrudRequiredFieldScope scope) =>
        scope is StandardCrudRequiredFieldScope.UpdateOnly or StandardCrudRequiredFieldScope.CreateAndUpdate;

    private static void RequireRequiredFieldValue(string value, string name, string resourceKey, string? field = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            var fieldPart = string.IsNullOrWhiteSpace(field) ? string.Empty : $" {field}";
            throw new InvalidOperationException($"标准 CRUD 资源 {resourceKey} 的必填字段{fieldPart} 缺少 {name}。");
        }
    }
```

- [x] **Step 5: 运行静态校验测试确认通过**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "StandardCrudDescriptorTests" -nr:false -v minimal
```

Expected: PASS。

- [x] **Step 6: 提交**

```powershell
git add .\server\platform-crud\Runtime\StandardCrudDescriptorValidator.cs .\tests\PlatformCrud.Tests\StandardCrudDescriptorTests.cs
git commit -m "功能：校验标准CRUD必填字段声明"
```

---

### Task 3: 标准 CRUD 写入必填运行时校验

**Files:**
- Modify: `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`
- Modify: `server/platform-crud/Runtime/StandardCrudService.cs`
- Test: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`

- [x] **Step 1: 写 create/update 运行时失败测试**

Append these tests to `tests/PlatformCrud.Tests/StandardCrudServiceWriteTests.cs`:

```csharp
[Fact]
public async Task CreateAsync_WhenRequiredFieldIsMissing_ReturnsBadRequestBeforeUniqueCheck()
{
    var repository = new RecordingExistsStandardCrudRepository(
    [
        new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true }
    ]);
    var service = Service(repository);

    var result = await service.CreateAsync(
        DescriptorWithRequiredAndUniqueConstraint(),
        new Dictionary<string, object?> { ["ENAME"] = "李四", ["IsEnabled"] = true });

    Assert.False(result.Succeeded);
    Assert.Equal(400, result.Error!.StatusCode);
    Assert.Equal("Organization.Person.PersonNoRequired", result.Error.Code);
    Assert.Null(repository.LastExistsValues);
}

[Fact]
public async Task CreateAsync_WhenRequiredStringIsBlank_ReturnsBadRequest()
{
    var service = Service();

    var result = await service.CreateAsync(
        DescriptorWithRequiredFields(),
        new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "   ", ["IsEnabled"] = true });

    Assert.False(result.Succeeded);
    Assert.Equal("Organization.Person.NameRequired", result.Error!.Code);
}

[Fact]
public async Task CreateAsync_WhenRequiredBooleanIsFalse_Passes()
{
    var service = Service();

    var result = await service.CreateAsync(
        DescriptorWithRequiredFields(),
        new Dictionary<string, object?> { ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = false });

    Assert.True(result.Succeeded);
    Assert.False((bool)result.Detail!.Fields["IsEnabled"]!);
}

[Fact]
public async Task UpdateAsync_WhenRequiredFieldIsNotSubmitted_DoesNotBlockExistingEmptyValue()
{
    var repository = new InMemoryStandardCrudRepository(
    [
        new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "", ["IsEnabled"] = true }
    ]);
    var service = Service(repository);

    var result = await service.UpdateAsync(
        DescriptorWithRequiredFields(),
        "person-1",
        new Dictionary<string, object?> { ["IsEnabled"] = false });

    Assert.True(result.Succeeded);
    Assert.False((bool)result.Detail!.Fields["IsEnabled"]!);
}

[Fact]
public async Task UpdateAsync_WhenRequiredFieldIsSubmittedAsNull_ReturnsBadRequest()
{
    var repository = new InMemoryStandardCrudRepository(
    [
        new Dictionary<string, object?> { ["EID"] = "person-1", ["PersonNo"] = "P001", ["ENAME"] = "张三", ["IsEnabled"] = true }
    ]);
    var service = Service(repository);

    var result = await service.UpdateAsync(
        DescriptorWithRequiredFields(),
        "person-1",
        new Dictionary<string, object?> { ["ENAME"] = null });

    Assert.False(result.Succeeded);
    Assert.Equal(400, result.Error!.StatusCode);
    Assert.Equal("Organization.Person.NameRequired", result.Error.Code);
}
```

Append these helpers near existing descriptor helpers:

```csharp
private static StandardCrudDescriptor DescriptorWithRequiredFields() => new()
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
        UpdateFields: ["ENAME", "IsEnabled"]),
    RequiredFields =
    [
        new StandardCrudRequiredField
        {
            Field = "PersonNo",
            Scope = StandardCrudRequiredFieldScope.CreateOnly,
            ErrorCode = "Organization.Person.PersonNoRequired",
            Message = "人员工号不能为空。",
            ReasonKey = "organization.person.personNoRequired"
        },
        new StandardCrudRequiredField
        {
            Field = "ENAME",
            Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
            ErrorCode = "Organization.Person.NameRequired",
            Message = "人员姓名不能为空。",
            ReasonKey = "organization.person.nameRequired"
        },
        new StandardCrudRequiredField
        {
            Field = "IsEnabled",
            Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
            ErrorCode = "Organization.Person.EnabledRequired",
            Message = "人员启用状态不能为空。",
            ReasonKey = "organization.person.enabledRequired"
        }
    ]
};

private static StandardCrudDescriptor DescriptorWithRequiredAndUniqueConstraint()
{
    var descriptor = DescriptorWithRequiredFields();
    return new StandardCrudDescriptor
    {
        ResourceKey = descriptor.ResourceKey,
        ModuleId = descriptor.ModuleId,
        ModelName = descriptor.ModelName,
        Route = descriptor.Route,
        IdField = descriptor.IdField,
        DisplayField = descriptor.DisplayField,
        EnabledField = descriptor.EnabledField,
        FieldSet = descriptor.FieldSet,
        RequiredFields = descriptor.RequiredFields,
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

- [x] **Step 2: 运行运行时校验测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "RequiredFieldIsMissing|RequiredStringIsBlank|RequiredBooleanIsFalse|RequiredFieldIsNotSubmitted|RequiredFieldIsSubmittedAsNull" -nr:false -v minimal
```

Expected: FAIL，至少一个测试显示缺失字段没有返回 `StandardCrudError`。

- [x] **Step 3: 在 create/update 中调用必填校验**

Modify `server/platform-crud/Runtime/StandardCrudService.cs`.

In `CreateAsync`, after `ApplyCreateAuditFields(descriptor, allowed);`, insert:

```csharp
        var requiredError = ValidateRequiredFields(descriptor, allowed, isCreate: true);
        if (requiredError is not null)
        {
            return StandardCrudMutationResult.Failure(requiredError);
        }
```

In `UpdateAsync`, after `ApplyUpdateAuditFields(descriptor, allowed);`, insert:

```csharp
        var requiredError = ValidateRequiredFields(descriptor, allowed, isCreate: false);
        if (requiredError is not null)
        {
            return StandardCrudMutationResult.Failure(requiredError);
        }
```

- [x] **Step 4: 增加必填校验实现**

Add these methods to `server/platform-crud/Runtime/StandardCrudService.cs`, before `ValidateUniqueConstraintsAsync`:

```csharp
    // 必填校验属于标准写入前置校验；创建检查缺失，更新只检查本次提交字段是否被清空。
    private static StandardCrudError? ValidateRequiredFields(
        StandardCrudDescriptor descriptor,
        IReadOnlyDictionary<string, object?> values,
        bool isCreate)
    {
        foreach (var requiredField in descriptor.RequiredFields)
        {
            if (!AppliesToMutation(requiredField.Scope, isCreate))
            {
                continue;
            }

            if (isCreate)
            {
                if (!values.TryGetValue(requiredField.Field, out var value) || IsMissingRequiredValue(value))
                {
                    return RequiredFieldError(requiredField);
                }

                continue;
            }

            if (values.TryGetValue(requiredField.Field, out var updateValue) && IsMissingRequiredValue(updateValue))
            {
                return RequiredFieldError(requiredField);
            }
        }

        return null;
    }

    private static bool AppliesToMutation(StandardCrudRequiredFieldScope scope, bool isCreate)
    {
        return isCreate
            ? scope is StandardCrudRequiredFieldScope.CreateOnly or StandardCrudRequiredFieldScope.CreateAndUpdate
            : scope is StandardCrudRequiredFieldScope.UpdateOnly or StandardCrudRequiredFieldScope.CreateAndUpdate;
    }

    private static bool IsMissingRequiredValue(object? value)
    {
        return value is null || value is string text && string.IsNullOrWhiteSpace(text);
    }

    private static StandardCrudError RequiredFieldError(StandardCrudRequiredField requiredField) => new()
    {
        StatusCode = 400,
        Code = requiredField.ErrorCode,
        Message = requiredField.Message,
        ReasonKey = requiredField.ReasonKey
    };
```

- [x] **Step 5: 运行运行时校验测试确认通过**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "StandardCrudServiceWriteTests" -nr:false -v minimal
```

Expected: PASS。

- [x] **Step 6: 提交**

```powershell
git add .\server\platform-crud\Runtime\StandardCrudService.cs .\tests\PlatformCrud.Tests\StandardCrudServiceWriteTests.cs
git commit -m "功能：标准CRUD写入执行必填校验"
```

---

### Task 4: Person 试点声明必填字段

**Files:**
- Modify: `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`
- Modify: `tests/Organization.Tests/PersonStandardCrudResourceTests.cs`
- Test: `tests/Organization.Tests/Organization.Tests.csproj`

- [x] **Step 1: 写 Person descriptor 必填声明失败测试**

Append this test to `tests/Organization.Tests/PersonStandardCrudResourceTests.cs`:

```csharp
[Fact]
public void PersonCrudDescriptor_DeclaresRequiredFieldsWithoutTighteningOptionalOrganizationFields()
{
    var descriptor = PersonCrudDescriptorFactory.Create();

    var required = descriptor.RequiredFields.ToDictionary(item => item.Field, StringComparer.Ordinal);

    Assert.Equal(StandardCrudRequiredFieldScope.CreateOnly, required["PersonNo"].Scope);
    Assert.Equal("Organization.Person.PersonNoRequired", required["PersonNo"].ErrorCode);
    Assert.Equal(StandardCrudRequiredFieldScope.CreateAndUpdate, required["ENAME"].Scope);
    Assert.Equal("Organization.Person.NameRequired", required["ENAME"].ErrorCode);
    Assert.Equal(StandardCrudRequiredFieldScope.CreateAndUpdate, required["IsEnabled"].Scope);
    Assert.Equal("Organization.Person.EnabledRequired", required["IsEnabled"].ErrorCode);
    Assert.DoesNotContain("CompanyId", required.Keys);
    Assert.DoesNotContain("DepartmentId", required.Keys);
    Assert.DoesNotContain("EmploymentStatus", required.Keys);
}
```

- [x] **Step 2: 运行 Person descriptor 测试确认失败**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter PersonCrudDescriptor_DeclaresRequiredFieldsWithoutTighteningOptionalOrganizationFields -nr:false -v minimal
```

Expected: FAIL，提示 `descriptor.RequiredFields` 为空或缺少对应字段。

- [x] **Step 3: 在 Person descriptor 中声明必填字段**

Modify `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`.

Insert `RequiredFields` before `UniqueConstraints`:

```csharp
        RequiredFields =
        [
            new StandardCrudRequiredField
            {
                Field = "PersonNo",
                Scope = StandardCrudRequiredFieldScope.CreateOnly,
                ErrorCode = "Organization.Person.PersonNoRequired",
                Message = "人员工号不能为空。",
                ReasonKey = "organization.person.personNoRequired"
            },
            new StandardCrudRequiredField
            {
                Field = "ENAME",
                Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
                ErrorCode = "Organization.Person.NameRequired",
                Message = "人员姓名不能为空。",
                ReasonKey = "organization.person.nameRequired"
            },
            new StandardCrudRequiredField
            {
                Field = "IsEnabled",
                Scope = StandardCrudRequiredFieldScope.CreateAndUpdate,
                ErrorCode = "Organization.Person.EnabledRequired",
                Message = "人员启用状态不能为空。",
                ReasonKey = "organization.person.enabledRequired"
            }
        ],
```

- [x] **Step 4: 运行 Person descriptor 与 metadata optional 测试确认通过**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "PersonStandardCrudResourceTests|OrganizationModelMetadataProviderTests" -nr:false -v minimal
```

Expected: PASS，且 `Person_OrganizationFields_AreProjectedAsOptional` 继续通过。

- [x] **Step 5: 运行 Organization 全量测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Expected: PASS。

- [x] **Step 6: 提交**

```powershell
git add .\server\modules\Organization\Crud\PersonCrudDescriptorFactory.cs .\tests\Organization.Tests\PersonStandardCrudResourceTests.cs
git commit -m "功能：人员标准CRUD声明必填字段"
```

---

### Task 5: 验证记录与收口验证

**Files:**
- Modify: `docs/verification/organization-module-verification.md`
- Check: `docs/superpowers/specs/platform/2026-05-12-standard-crud-required-field-validation-design.md`
- Test: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`
- Test: `tests/Organization.Tests/Organization.Tests.csproj`
- Build: `apps/host-api/HostApi.csproj`

- [x] **Step 1: 运行平台 CRUD 全量测试**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected: PASS。

- [x] **Step 2: 运行 Organization 全量测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Expected: PASS。

- [x] **Step 3: 构建 HostApi**

Run:

```powershell
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
```

Expected: build succeeds with 0 errors.

- [x] **Step 4: 扫描必填字段入口与边界**

Run:

```powershell
rg -n "StandardCrudRequiredField|RequiredFields|ValidateRequiredFields|PersonNoRequired|NameRequired|EnabledRequired" server/platform-crud server/modules/Organization tests
```

Expected: output includes `server/platform-crud/Descriptors`, `StandardCrudDescriptorValidator`, `StandardCrudService`, `PersonCrudDescriptorFactory`, `StandardCrudDescriptorTests`, `StandardCrudServiceWriteTests`, and `PersonStandardCrudResourceTests`.

Run:

```powershell
rg -n "CompanyIdRequired|DepartmentIdRequired|EmploymentStatusRequired" server/modules/Organization tests/Organization.Tests
```

Expected: no matches.

- [x] **Step 5: 运行设计文档 AI-first 校验**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-12-standard-crud-required-field-validation-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

Expected: both commands pass.

- [x] **Step 6: 回填验证记录**

Append to `docs/verification/organization-module-verification.md`:

````md

## 标准 CRUD 必填字段校验平台化验证

验证日期：2026-05-12

验证目标：

- 标准 CRUD 资源可通过 descriptor 显式声明写入必填字段。
- `StandardCrudService` 在唯一约束和模块 validator 之前执行必填校验。
- Person 试点只声明 `PersonNo`、`ENAME`、`IsEnabled` 必填，不收紧 `CompanyId`、`DepartmentId`、`EmploymentStatus` 的 optional 口径。
- 必填失败返回稳定 `StandardCrudError`，不依赖数据库异常或模块专属 validator。

验证命令：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
rg -n "StandardCrudRequiredField|RequiredFields|ValidateRequiredFields|PersonNoRequired|NameRequired|EnabledRequired" server/platform-crud server/modules/Organization tests
rg -n "CompanyIdRequired|DepartmentIdRequired|EmploymentStatusRequired" server/modules/Organization tests/Organization.Tests
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-12-standard-crud-required-field-validation-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

验证结论：

- 标准 CRUD 必填字段声明、静态校验和运行时写入校验已由平台内核承接。
- 创建阶段会拦截缺失、`null` 和空白字符串；更新阶段只拦截本次提交字段被清空，不因历史空值阻塞无关字段更新。
- `false` 作为布尔值通过必填校验。
- Person 的 `CompanyId`、`DepartmentId`、`EmploymentStatus` 仍保持 optional，后续如需改为必填应单独设计 metadata、迁移和前端表单口径。
````

- [x] **Step 7: 提交验证记录**

```powershell
git add .\docs\verification\organization-module-verification.md
git commit -m "验证：记录标准CRUD必填校验结果"
```

## Self-Review

- **Spec coverage:** Task 1 覆盖 descriptor 契约；Task 2 覆盖静态校验；Task 3 覆盖 create/update 运行时缺失判断、执行顺序和 `false` 合法场景；Task 4 覆盖 Person 试点和 optional 字段不误收紧；Task 5 覆盖验证记录、入口扫描、边界扫描和 Host 构建。
- **空洞步骤检查:** 本计划没有保留空泛步骤；每个代码步骤都给出具体文件、代码块、命令和期望结果。
- **Type consistency:** 本计划统一使用 `StandardCrudRequiredField`、`StandardCrudRequiredFieldScope`、`RequiredFields`、`ValidateRequiredFields`、`PersonNoRequired`、`NameRequired`、`EnabledRequired`。

# EID 值协议平台化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将平台对象 `EID` 新建值统一为 `{ModelName}_{Guid:N}`，并清理当前内建模块写入路径中的裸 GUID 假设。

**Architecture:** 新增平台级对象 ID 生成器和独立 DI 注册入口，由标准 CRUD 和各模块 service 创建路径统一调用。当前按空库重建处理，不做历史裸 GUID 回填；所有平台对象主键和引用字段按字符串流转，Organization 中残留的 `Guid` 路由、DTO、领域模型和仓储接口分批收口。

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core shared-type entity / xUnit / PlatformCrud / ConfigCenter / Authorization / Organization

---

## 文件结构与职责

**Create:**

- `server/platform-crud/ObjectIds/IPlatformObjectIdGenerator.cs`  
  平台对象 ID 生成接口，公共入口只暴露 `NewId(objectName)`。
- `server/platform-crud/ObjectIds/PlatformObjectIdGenerator.cs`  
  默认生成 `{ModelName}_{Guid:N}`，并校验对象名前缀。

**Modify:**

- `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`  
  提供独立对象 ID 生成器注册入口，并让标准 CRUD 内核复用它。
- `server/platform-crud/Persistence/EfStandardCrudRepository.cs`  
  标准 CRUD 创建时通过生成器补 `EID`。
- `tests/PlatformCrud.Tests/StandardCrudEfRepositoryTests.cs`  
  覆盖 EF 标准 CRUD 创建路径的新 ID 协议。
- `tests/PlatformCrud.Tests/Support/InMemoryStandardCrudRepository.cs`  
  测试仓储用同一协议生成 ID，避免测试继续证明旧行为。
- `server/modules/ConfigCenter/Models/ConfigItem.cs`
- `server/modules/ConfigCenter/Models/ValueListItem.cs`
- `server/modules/ConfigCenter/ConfigCenterModule.cs`
- `server/modules/ConfigCenter/Services/ConfigService.cs`
- `server/modules/ConfigCenter/Services/ValueListService.cs`
- `tests/ConfigCenter.Tests/*`  
  ConfigCenter 新建配置项和值列表时接入统一 ID。
- `server/modules/Authorization/Models/Role.cs`
- `server/modules/Authorization/Models/Permission.cs`
- `server/modules/Authorization/Models/RolePermission.cs`
- `server/modules/Authorization/Models/UserRole.cs`
- `server/modules/Authorization/AuthorizationModule.cs`
- `server/modules/Authorization/Services/PermissionService.cs`
- `server/modules/Authorization/Services/RoleService.cs`
- `tests/Authorization.Tests/*`  
  Authorization 新建角色、权限、角色权限、用户角色时接入统一 ID。
- `server/modules/Organization/Models/*.cs`
- `server/modules/Organization/DTOs/*.cs`
- `server/modules/Organization/Services/*.cs`
- `server/modules/Organization/Repositories/*.cs`
- `server/modules/Organization/Persistence/Ef/*.cs`
- `server/modules/Organization/Controllers/*.cs`
- `server/modules/Organization/Crud/*.cs`
- `tests/Organization.Tests/*`  
  Organization 主数据和人员账号关联从 `Guid` 主键收口为字符串协议 ID。
- `docs/verification/organization-module-verification.md`  
  回填空库前提与 EID 值协议验证结果。

**Out of Scope:**

- 不生成历史主键回填 migration。
- 不实现统一资源 API。
- 不改文件存储模块的文件 ID。
- 不改用户账号 ID、权限 code、配置 key 的业务标识语义。

---

### Task 1: 平台对象 ID 生成器与标准 CRUD 创建路径

**Files:**
- Create: `server/platform-crud/ObjectIds/IPlatformObjectIdGenerator.cs`
- Create: `server/platform-crud/ObjectIds/PlatformObjectIdGenerator.cs`
- Modify: `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`
- Modify: `server/platform-crud/Persistence/EfStandardCrudRepository.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudEfRepositoryTests.cs`
- Modify: `tests/PlatformCrud.Tests/Support/InMemoryStandardCrudRepository.cs`
- Test: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`

- [x] **Step 1: 写对象 ID 生成器和 EF 创建路径失败测试**

Append to `tests/PlatformCrud.Tests/StandardCrudEfRepositoryTests.cs`:

```csharp
[Fact]
public async Task CreateAsync_WhenIdIsMissing_GeneratesModelNamePrefixedEid()
{
    await using var dbContext = BuildDbContext();
    var repository = new EfStandardCrudRepository(
        dbContext,
        new FixedObjectIdGenerator("Person_00000000000000000000000000000001"));

    var created = await repository.CreateAsync(
        Descriptor(),
        new Dictionary<string, object?>
        {
            ["PersonNo"] = "P900",
            ["ENAME"] = "协议人员",
            ["IsEnabled"] = true
        });

    Assert.Equal("Person_00000000000000000000000000000001", created["EID"]);
}

[Fact]
public void PlatformObjectIdGenerator_NewId_UsesModelNamePrefixAndLowerHex()
{
    var id = new PlatformObjectIdGenerator().NewId("Person");

    Assert.Matches("^Person_[0-9a-f]{32}$", id);
}

[Theory]
[InlineData("")]
[InlineData("organization.person")]
[InlineData("Person/User")]
public void PlatformObjectIdGenerator_NewId_RejectsInvalidObjectName(string objectName)
{
    var error = Assert.Throws<ArgumentException>(() => new PlatformObjectIdGenerator().NewId(objectName));

    Assert.Contains("对象名称必须匹配", error.Message, StringComparison.Ordinal);
}

private sealed class FixedObjectIdGenerator : IPlatformObjectIdGenerator
{
    private readonly string _id;

    public FixedObjectIdGenerator(string id)
    {
        _id = id;
    }

    public string NewId(string objectName) => _id;
}
```

Also add:

```csharp
using ICP.Platform.Crud.ObjectIds;
```

- [x] **Step 2: 运行平台 CRUD 测试确认失败**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "CreateAsync_WhenIdIsMissing_GeneratesModelNamePrefixedEid|PlatformObjectIdGenerator" -nr:false -v minimal
```

Expected:

```text
FAIL
CS0246: The type or namespace name 'IPlatformObjectIdGenerator' could not be found
```

- [x] **Step 3: 新增对象 ID 生成器接口和实现**

Create `server/platform-crud/ObjectIds/IPlatformObjectIdGenerator.cs`:

```csharp
namespace ICP.Platform.Crud.ObjectIds;

public interface IPlatformObjectIdGenerator
{
    /// <summary>
    /// 为平台对象生成 EID 值；objectName 必须是稳定模型名，返回格式固定为 {ModelName}_{Guid:N}。
    /// </summary>
    string NewId(string objectName);
}
```

Create `server/platform-crud/ObjectIds/PlatformObjectIdGenerator.cs`:

```csharp
using System.Text.RegularExpressions;

namespace ICP.Platform.Crud.ObjectIds;

public sealed partial class PlatformObjectIdGenerator : IPlatformObjectIdGenerator
{
    /// <summary>
    /// 生成平台对象标识。这里不接受 resourceKey 或自由文本，避免点号、斜杠等进入 URL path 和数据库主键。
    /// </summary>
    public string NewId(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName) || !ObjectNamePattern().IsMatch(objectName))
        {
            throw new ArgumentException("对象名称必须匹配 ^[A-Za-z][A-Za-z0-9]*$。", nameof(objectName));
        }

        return $"{objectName}_{Guid.NewGuid():N}";
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectNamePattern();
}
```

- [x] **Step 4: 在标准 CRUD DI 和 EF 仓储中接入生成器**

Modify `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ICP.Platform.Crud.Extensions;

public static class StandardCrudServiceCollectionExtensions
{
    /// <summary>
    /// 注册平台对象 ID 生成能力；该能力独立于标准 CRUD，供模块 service 写入 EID 时复用。
    /// </summary>
    public static IServiceCollection AddPlatformObjectIdGeneration(this IServiceCollection services)
    {
        services.TryAddSingleton<IPlatformObjectIdGenerator, PlatformObjectIdGenerator>();
        return services;
    }

    /// <summary>
    /// 注册标准 CRUD 内核基础服务；模块扩展点仍由各模块显式注册，避免平台默认耦合具体业务。
    /// </summary>
    public static IServiceCollection AddStandardCrudKernel(this IServiceCollection services)
    {
        services.AddPlatformObjectIdGeneration();
        services.AddScoped<IStandardCrudRepository, EfStandardCrudRepository>();
        services.AddScoped<IStandardCrudService, StandardCrudService>();
        services.AddScoped<StandardCrudEndpoint>();
        return services;
    }
}
```

Modify `server/platform-crud/Persistence/EfStandardCrudRepository.cs`:

```csharp
using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Models;
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Crud.Persistence;

public sealed class EfStandardCrudRepository : IStandardCrudRepository
{
    private readonly EfPlatformDbContext _dbContext;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;

    public EfStandardCrudRepository(
        EfPlatformDbContext dbContext,
        IPlatformObjectIdGenerator objectIdGenerator)
    {
        _dbContext = dbContext;
        _objectIdGenerator = objectIdGenerator;
    }
```

Replace the missing-id block in `CreateAsync`:

```csharp
if (!row.ContainsKey(descriptor.IdField))
{
    row[descriptor.IdField] = _objectIdGenerator.NewId(descriptor.ModelName);
}
```

- [x] **Step 5: 更新测试仓储默认生成规则**

Modify `tests/PlatformCrud.Tests/Support/InMemoryStandardCrudRepository.cs`:

```csharp
if (!row.ContainsKey(descriptor.IdField))
{
    row[descriptor.IdField] = $"{descriptor.ModelName}_{Guid.NewGuid():N}";
}
```

- [x] **Step 6: 运行平台 CRUD 定向测试确认通过**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "CreateAsync_WhenIdIsMissing_GeneratesModelNamePrefixedEid|PlatformObjectIdGenerator" -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 7: 运行平台 CRUD 全量测试**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 8: 提交**

```powershell
git add .\server\platform-crud\ObjectIds .\server\platform-crud\Extensions\StandardCrudServiceCollectionExtensions.cs .\server\platform-crud\Persistence\EfStandardCrudRepository.cs .\tests\PlatformCrud.Tests\StandardCrudEfRepositoryTests.cs .\tests\PlatformCrud.Tests\Support\InMemoryStandardCrudRepository.cs
git commit -m "功能：新增平台对象ID生成器"
```

---

### Task 2: ConfigCenter 新建路径接入对象 ID 生成器

**Files:**
- Modify: `server/modules/ConfigCenter/Models/ConfigItem.cs`
- Modify: `server/modules/ConfigCenter/Models/ValueListItem.cs`
- Modify: `server/modules/ConfigCenter/ConfigCenterModule.cs`
- Modify: `server/modules/ConfigCenter/Services/ConfigService.cs`
- Modify: `server/modules/ConfigCenter/Services/ValueListService.cs`
- Modify: `tests/ConfigCenter.Tests/ConfigCenterModuleTests.cs`
- Modify: `tests/ConfigCenter.Tests/ValueListServiceTests.cs`
- Test: `tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj`

- [x] **Step 1: 写 ConfigCenter 失败测试**

Append to `tests/ConfigCenter.Tests/ConfigCenterModuleTests.cs`:

```csharp
[Fact]
public async Task CreateConfigAsync_AssignsConfigItemPrefixedId()
{
    var service = new ConfigService(new ConfigRepository(), new FixedObjectIdGenerator("ConfigItem_00000000000000000000000000000001"));

    var created = await service.CreateConfigAsync(new ConfigItemDto
    {
        Key = "Test.Protocol",
        Type = "string",
        ModuleId = "Test",
        DisplayName = "协议测试"
    });

    Assert.Equal("ConfigItem_00000000000000000000000000000001", created.Id);
}

private sealed class FixedObjectIdGenerator : IPlatformObjectIdGenerator
{
    private readonly string _id;

    public FixedObjectIdGenerator(string id)
    {
        _id = id;
    }

    public string NewId(string objectName) => _id;
}
```

Append to `tests/ConfigCenter.Tests/ValueListServiceTests.cs`:

```csharp
[Fact]
public async Task CreateAsync_AssignsValueListItemPrefixedId()
{
    var service = new ValueListService(new ValueListRepository(), new FixedObjectIdGenerator("ValueListItem_00000000000000000000000000000001"));

    var created = await service.CreateAsync(new ValueListItemDto
    {
        Code = "protocol",
        DisplayName = "协议"
    });

    Assert.Equal("ValueListItem_00000000000000000000000000000001", created.Id);
}

private sealed class FixedObjectIdGenerator : IPlatformObjectIdGenerator
{
    private readonly string _id;

    public FixedObjectIdGenerator(string id)
    {
        _id = id;
    }

    public string NewId(string objectName) => _id;
}
```

Add `using ICP.Platform.Crud.ObjectIds;`.

- [x] **Step 2: 运行 ConfigCenter 定向测试确认失败**

Run:

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "CreateConfigAsync_AssignsConfigItemPrefixedId|CreateAsync_AssignsValueListItemPrefixedId" -nr:false -v minimal
```

Expected:

```text
FAIL
CS1729: 'ConfigService' does not contain a constructor that takes 2 arguments
```

- [x] **Step 3: 移除模型默认裸 GUID**

Modify `server/modules/ConfigCenter/Models/ConfigItem.cs`:

```csharp
public string Id { get; set; } = string.Empty;
```

Modify `server/modules/ConfigCenter/Models/ValueListItem.cs`:

```csharp
public string Id { get; set; } = string.Empty;
```

- [x] **Step 4: 在 ConfigService 注入并使用生成器**

Modify `server/modules/ConfigCenter/Services/ConfigService.cs`:

```csharp
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.ConfigCenter.DTOs;
using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;

namespace ICP.Platform.Modules.ConfigCenter.Services;

public class ConfigService : IConfigService
{
    private readonly IConfigRepository _configRepository;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;

    public ConfigService(
        IConfigRepository configRepository,
        IPlatformObjectIdGenerator objectIdGenerator)
    {
        _configRepository = configRepository;
        _objectIdGenerator = objectIdGenerator;
    }
```

In `CreateConfigAsync`, set:

```csharp
var item = new ConfigItem
{
    Id = _objectIdGenerator.NewId(nameof(ConfigItem)),
    Key = dto.Key,
    Value = dto.Value,
    Type = dto.Type,
    ModuleId = dto.ModuleId,
    Group = dto.Group,
    Environment = dto.Environment,
    TenantId = dto.TenantId,
    DisplayName = dto.DisplayName,
    Description = dto.Description,
    IsSensitive = dto.IsSensitive,
    UpdatedBy = createdBy
};
```

- [x] **Step 5: 在 ValueListService 注入并使用生成器**

Modify `server/modules/ConfigCenter/Services/ValueListService.cs`:

```csharp
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.ConfigCenter.DTOs;
using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;
using ICP.Platform.Sdk.UserMessages;

namespace ICP.Platform.Modules.ConfigCenter.Services;

public class ValueListService : IValueListService
{
    private readonly IValueListRepository _repository;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;

    public ValueListService(
        IValueListRepository repository,
        IPlatformObjectIdGenerator objectIdGenerator)
    {
        _repository = repository;
        _objectIdGenerator = objectIdGenerator;
    }
```

In `CreateAsync`, set:

```csharp
var item = new ValueListItem
{
    Id = _objectIdGenerator.NewId(nameof(ValueListItem)),
    Code = code,
    Name = name,
    Description = dto.Description?.Trim(),
    UpdatedBy = updatedBy
};
```

- [x] **Step 6: 在 ConfigCenter 模块装配点注册对象 ID 生成器**

Modify `server/modules/ConfigCenter/ConfigCenterModule.cs`:

Add this using:

```csharp
using ICP.Platform.Crud.Extensions;
```

Replace only `ConfigureServices` with:

```csharp
public override void ConfigureServices(IServiceCollection services)
{
    services.AddPlatformObjectIdGeneration();
    services.AddScoped<IConfigService, ConfigService>();
    services.AddScoped<IConfigRepository, EfConfigRepository>();
    services.AddScoped<IValueListService, ValueListService>();
    services.AddScoped<IValueListRepository, EfValueListRepository>();
    services.AddScoped<ILegacySettingParser, LegacySettingXmlParser>();
    services.AddScoped<ILegacySettingImportService, LegacySettingImportService>();
}
```

- [x] **Step 7: 更新既有测试构造器**

Where existing tests use:

```csharp
new ConfigService(repository)
new ValueListService(repository)
```

replace with:

```csharp
new ConfigService(repository, new PlatformObjectIdGenerator())
new ValueListService(repository, new PlatformObjectIdGenerator())
```

Add `using ICP.Platform.Crud.ObjectIds;` to touched test files.

- [x] **Step 8: 运行 ConfigCenter 测试**

Run:

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 9: 提交**

```powershell
git add .\server\modules\ConfigCenter\Models\ConfigItem.cs .\server\modules\ConfigCenter\Models\ValueListItem.cs .\server\modules\ConfigCenter\ConfigCenterModule.cs .\server\modules\ConfigCenter\Services\ConfigService.cs .\server\modules\ConfigCenter\Services\ValueListService.cs .\tests\ConfigCenter.Tests
git commit -m "功能：ConfigCenter接入平台对象ID"
```

---

### Task 3: Authorization 新建路径接入对象 ID 生成器

**Files:**
- Modify: `server/modules/Authorization/Models/Role.cs`
- Modify: `server/modules/Authorization/Models/Permission.cs`
- Modify: `server/modules/Authorization/Models/RolePermission.cs`
- Modify: `server/modules/Authorization/Models/UserRole.cs`
- Modify: `server/modules/Authorization/AuthorizationModule.cs`
- Modify: `server/modules/Authorization/Services/PermissionService.cs`
- Modify: `server/modules/Authorization/Services/RoleService.cs`
- Modify: `tests/Authorization.Tests/RoleServiceTests.cs`
- Modify: `tests/Authorization.Tests/PermissionServiceTests.cs`
- Test: `tests/Authorization.Tests/Authorization.Tests.csproj`

- [x] **Step 1: 写 Authorization 失败测试**

Append to `tests/Authorization.Tests/RoleServiceTests.cs`:

```csharp
[Fact]
public async Task CreateRoleAsync_AssignsRolePrefixedId()
{
    var service = BuildRoleService(new SequenceObjectIdGenerator(
        "Role_00000000000000000000000000000001"));

    var created = await service.CreateRoleAsync(new RoleDto
    {
        Code = "ProtocolRole",
        DisplayName = "协议角色",
        IsEnabled = true
    });

    Assert.Equal("Role_00000000000000000000000000000001", created.Id);
}

[Fact]
public async Task AssignRolePermissionsAsync_AssignsRolePermissionPrefixedIds()
{
    var generator = new SequenceObjectIdGenerator(
        "RolePermission_00000000000000000000000000000001");
    var roleRepository = new RoleRepository();
    var permissionRepository = new PermissionRepository();
    var rolePermissionRepository = new RolePermissionRepository();
    var service = new RoleService(roleRepository, permissionRepository, rolePermissionRepository, new UserRoleRepository(), generator);
    var role = new Role { Id = "Role_00000000000000000000000000000001", Code = "Admin", Name = "管理员" };
    var permission = new Permission { Id = "Permission_00000000000000000000000000000001", Code = "Test.View", DisplayName = "查看", ModuleId = "Test" };
    await roleRepository.CreateAsync(role);
    await permissionRepository.CreateAsync(permission);

    await service.AssignRolePermissionsAsync(role.Id, new AssignRolePermissionsRequest { PermissionCodes = ["Test.View"] });

    var links = await rolePermissionRepository.GetByRoleIdAsync(role.Id);
    Assert.Equal("RolePermission_00000000000000000000000000000001", Assert.Single(links).Id);
}

private static RoleService BuildRoleService(IPlatformObjectIdGenerator generator) =>
    new(new RoleRepository(), new PermissionRepository(), new RolePermissionRepository(), new UserRoleRepository(), generator);

private sealed class SequenceObjectIdGenerator : IPlatformObjectIdGenerator
{
    private readonly Queue<string> _ids;

    public SequenceObjectIdGenerator(params string[] ids)
    {
        _ids = new Queue<string>(ids);
    }

    public string NewId(string objectName) => _ids.Dequeue();
}
```

Append to `tests/Authorization.Tests/PermissionServiceTests.cs`:

```csharp
[Fact]
public async Task SyncPermissionsAsync_AssignsPermissionPrefixedId()
{
    var repository = new PermissionRepository();
    var service = new PermissionService(repository, new FixedObjectIdGenerator("Permission_00000000000000000000000000000001"));

    await service.SyncPermissionsAsync(
    [
        new PermissionDescriptor
        {
            Code = "Test.View",
            DisplayName = "查看",
            ModuleId = "Test",
            Group = "测试"
        }
    ]);

    var created = Assert.Single(await repository.GetAllAsync());
    Assert.Equal("Permission_00000000000000000000000000000001", created.Id);
}

private sealed class FixedObjectIdGenerator : IPlatformObjectIdGenerator
{
    private readonly string _id;

    public FixedObjectIdGenerator(string id)
    {
        _id = id;
    }

    public string NewId(string objectName) => _id;
}
```

Add `using ICP.Platform.Crud.ObjectIds;` and `using ICP.Platform.Sdk;` as needed.

- [x] **Step 2: 运行 Authorization 定向测试确认失败**

Run:

```powershell
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj --filter "CreateRoleAsync_AssignsRolePrefixedId|AssignRolePermissionsAsync_AssignsRolePermissionPrefixedIds|SyncPermissionsAsync_AssignsPermissionPrefixedId" -nr:false -v minimal
```

Expected:

```text
FAIL
CS1729: 'RoleService' does not contain a constructor that takes 5 arguments
```

- [x] **Step 3: 移除 Authorization 模型默认裸 GUID**

For these files, change the `Id` initializer to `string.Empty`:

```csharp
public string Id { get; set; } = string.Empty;
```

Files:

- `server/modules/Authorization/Models/Role.cs`
- `server/modules/Authorization/Models/Permission.cs`
- `server/modules/Authorization/Models/RolePermission.cs`
- `server/modules/Authorization/Models/UserRole.cs`

- [x] **Step 4: 在 RoleService 注入并使用生成器**

Modify `server/modules/Authorization/Services/RoleService.cs`:

```csharp
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;

namespace ICP.Platform.Modules.Authorization.Services;

public class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IRolePermissionRepository _rolePermissionRepository;
    private readonly IUserRoleRepository _userRoleRepository;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;

    public RoleService(
        IRoleRepository roleRepository,
        IPermissionRepository permissionRepository,
        IRolePermissionRepository rolePermissionRepository,
        IUserRoleRepository userRoleRepository,
        IPlatformObjectIdGenerator objectIdGenerator)
    {
        _roleRepository = roleRepository;
        _permissionRepository = permissionRepository;
        _rolePermissionRepository = rolePermissionRepository;
        _userRoleRepository = userRoleRepository;
        _objectIdGenerator = objectIdGenerator;
    }
```

In `CreateRoleAsync`, set:

```csharp
var role = new Role
{
    Id = _objectIdGenerator.NewId(nameof(Role)),
    Code = dto.Code,
    Name = dto.DisplayName,
    Description = dto.Description,
    CreatedBy = createdBy
};
```

In `AssignRolePermissionsAsync`, set:

```csharp
var rolePermissions = permissionIds.Select(permissionId => new RolePermission
{
    Id = _objectIdGenerator.NewId(nameof(RolePermission)),
    RoleId = roleId,
    PermissionId = permissionId,
    GrantedBy = grantedBy
}).ToList();
```

In `AssignUserRolesAsync`, set:

```csharp
var userRoles = roleIds.Select(roleId => new UserRole
{
    Id = _objectIdGenerator.NewId(nameof(UserRole)),
    UserId = userId,
    RoleId = roleId,
    AssignedBy = assignedBy
}).ToList();
```

- [x] **Step 5: 在 PermissionService 注入并使用生成器**

Modify `server/modules/Authorization/Services/PermissionService.cs`:

```csharp
using ICP.Platform.Crud.ObjectIds;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Modules.Authorization.Models;
using ICP.Platform.Modules.Authorization.Repositories;
using ICP.Platform.Sdk;

namespace ICP.Platform.Modules.Authorization.Services;

public class PermissionService : IPermissionService
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IPlatformObjectIdGenerator _objectIdGenerator;

    public PermissionService(
        IPermissionRepository permissionRepository,
        IPlatformObjectIdGenerator objectIdGenerator)
    {
        _permissionRepository = permissionRepository;
        _objectIdGenerator = objectIdGenerator;
    }
```

When creating a missing permission, set:

```csharp
var permission = new Permission
{
    Id = _objectIdGenerator.NewId(nameof(Permission)),
    Code = descriptor.Code,
    DisplayName = descriptor.DisplayName,
    Description = descriptor.Description,
    ModuleId = descriptor.ModuleId,
    Group = descriptor.Group
};
```

- [x] **Step 6: 在 Authorization 模块装配点注册对象 ID 生成器**

Modify `server/modules/Authorization/AuthorizationModule.cs`:

Add this using:

```csharp
using ICP.Platform.Crud.Extensions;
```

Replace only `ConfigureServices` with:

```csharp
public override void ConfigureServices(IServiceCollection services)
{
    services.AddPlatformObjectIdGeneration();

    // 注册核心服务
    services.AddScoped<IAuthorizationService, AuthorizationService>();
    services.AddScoped<IRoleService, RoleService>();
    services.AddScoped<IPermissionService, PermissionService>();

    // 注册仓储
    services.AddScoped<IRoleRepository, EfRoleRepository>();
    services.AddScoped<IPermissionRepository, EfPermissionRepository>();
    services.AddScoped<IRolePermissionRepository, EfRolePermissionRepository>();
    services.AddScoped<IUserRoleRepository, EfUserRoleRepository>();
}
```

- [x] **Step 7: 更新既有 Authorization 测试构造器**

Replace:

```csharp
new RoleService(roleRepository, permissionRepository, rolePermissionRepository, userRoleRepository)
new PermissionService(repository)
```

with:

```csharp
new RoleService(roleRepository, permissionRepository, rolePermissionRepository, userRoleRepository, new PlatformObjectIdGenerator())
new PermissionService(repository, new PlatformObjectIdGenerator())
```

Add `using ICP.Platform.Crud.ObjectIds;` to touched test files.

- [x] **Step 8: 运行 Authorization 测试**

Run:

```powershell
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 9: 提交**

```powershell
git add .\server\modules\Authorization\Models .\server\modules\Authorization\AuthorizationModule.cs .\server\modules\Authorization\Services .\tests\Authorization.Tests
git commit -m "功能：Authorization接入平台对象ID"
```

---

### Task 4: Organization 主数据与路由收口为字符串 ID

**Files:**
- Modify: `server/modules/Organization/Models/Company.cs`
- Modify: `server/modules/Organization/Models/Department.cs`
- Modify: `server/modules/Organization/Models/Position.cs`
- Modify: `server/modules/Organization/Models/Person.cs`
- Modify: `server/modules/Organization/DTOs/PersonListItemDto.cs`
- Modify: `server/modules/Organization/DTOs/PersonDetailDto.cs`
- Modify: `server/modules/Organization/DTOs/DepartmentTreeNodeDto.cs`
- Modify: `server/modules/Organization/Services/ICompanyService.cs`
- Modify: `server/modules/Organization/Services/IDepartmentService.cs`
- Modify: `server/modules/Organization/Services/IPositionService.cs`
- Modify: `server/modules/Organization/Services/IPersonService.cs`
- Modify: `server/modules/Organization/Services/CompanyService.cs`
- Modify: `server/modules/Organization/Services/DepartmentService.cs`
- Modify: `server/modules/Organization/Services/PositionService.cs`
- Modify: `server/modules/Organization/Services/PersonService.cs`
- Modify: `server/modules/Organization/Repositories/*.cs`
- Modify: `server/modules/Organization/Persistence/Ef/*.cs`
- Modify: `server/modules/Organization/Controllers/*.cs`
- Test: `tests/Organization.Tests/Organization.Tests.csproj`

- [x] **Step 1: 写 Organization ID 协议失败测试**

Append to `tests/Organization.Tests/CompanyServiceTests.cs`:

```csharp
[Fact]
public async Task CreateCompany_AssignsCompanyPrefixedId()
{
    var service = new CompanyService(new InMemoryCompanyRepository(), new FixedObjectIdGenerator("Company_00000000000000000000000000000001"));

    var created = await service.CreateAsync(new CompanyDto
    {
        Code = "ICP",
        DisplayName = "ICP研发中心"
    });

    Assert.Equal("Company_00000000000000000000000000000001", created.Id);
}
```

Append to `tests/Organization.Tests/PersonListContractTests.cs`:

```csharp
[Fact]
public async Task PersonsController_GetList_ReturnsStringIdWithoutGuidParsing()
{
    var standardCrudService = new RecordingStandardCrudService(
    [
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["EID"] = "Person_00000000000000000000000000000001",
            ["PersonNo"] = "P001",
            ["ENAME"] = "张三",
            ["CompanyName"] = "ICP研发中心",
            ["DepartmentName"] = "人力资源部",
            ["EmploymentStatus"] = "FullTime",
            ["IsEnabled"] = true
        }
    ]);
    var controller = BuildController(standardCrudService);

    var response = await controller.GetList(null, null, null, null);

    var ok = Assert.IsType<OkObjectResult>(response.Result);
    var item = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<PersonListItemDto>>(ok.Value));
    Assert.Equal("Person_00000000000000000000000000000001", item.Id);
}
```

Add a `FixedObjectIdGenerator` test helper to Organization test files:

```csharp
private sealed class FixedObjectIdGenerator : IPlatformObjectIdGenerator
{
    private readonly string _id;

    public FixedObjectIdGenerator(string id)
    {
        _id = id;
    }

    public string NewId(string objectName) => _id;
}
```

- [x] **Step 2: 运行 Organization 定向测试确认失败**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "CreateCompany_AssignsCompanyPrefixedId|PersonsController_GetList_ReturnsStringIdWithoutGuidParsing" -nr:false -v minimal
```

Expected:

```text
FAIL
CS1729: 'CompanyService' does not contain a constructor that takes 2 arguments
```

- [x] **Step 3: 将 Organization 领域模型主键和引用字段改为 string**

Apply these model shapes:

`server/modules/Organization/Models/Company.cs`:

```csharp
namespace ICP.Platform.Modules.Organization.Models;

public sealed class Company
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
```

`server/modules/Organization/Models/Department.cs`:

```csharp
namespace ICP.Platform.Modules.Organization.Models;

public sealed class Department
{
    public string Id { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string? ParentDepartmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
```

`server/modules/Organization/Models/Position.cs`:

```csharp
namespace ICP.Platform.Modules.Organization.Models;

public sealed class Position
{
    public string Id { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string DepartmentId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
```

`server/modules/Organization/Models/Person.cs`:

```csharp
namespace ICP.Platform.Modules.Organization.Models;

public sealed class Person
{
    public string Id { get; set; } = string.Empty;
    public string PersonNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CompanyId { get; set; }
    public string? DepartmentId { get; set; }
    public string? PositionId { get; set; }
    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.FullTime;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
```

- [x] **Step 4: 改 DTO 主键为 string**

Modify `server/modules/Organization/DTOs/PersonListItemDto.cs`:

```csharp
public required string Id { get; init; }
```

Modify `server/modules/Organization/DTOs/PersonDetailDto.cs`:

```csharp
public required string Id { get; init; }
public string? CompanyId { get; init; }
public string? DepartmentId { get; init; }
public string? PositionId { get; init; }
```

Modify `server/modules/Organization/DTOs/DepartmentTreeNodeDto.cs`:

```csharp
public required string Id { get; init; }
public string? ParentDepartmentId { get; init; }
```

- [x] **Step 5: 改 Organization service/repository 接口参数为 string**

Examples:

`server/modules/Organization/Services/ICompanyService.cs`:

```csharp
Task<CompanyDto?> GetByIdAsync(string companyId, CancellationToken cancellationToken = default);
Task<CompanyDto> UpdateAsync(string companyId, CompanyDto dto, CancellationToken cancellationToken = default);
```

`server/modules/Organization/Repositories/IPersonRepository.cs`:

```csharp
Task<Person?> GetByIdAsync(string personId, CancellationToken cancellationToken = default);
Task<IReadOnlyList<Person>> GetListAsync(
    string? keyword,
    string? companyId,
    string? departmentId,
    EmploymentStatus? employmentStatus,
    CancellationToken cancellationToken = default);
Task<bool> ExistsPersonNoAsync(
    string? companyId,
    string personNo,
    string? excludedPersonId = null,
    CancellationToken cancellationToken = default);
```

For `IDepartmentRepository`, use these string signatures:

```csharp
Task<List<Department>> GetByCompanyIdAsync(string companyId, CancellationToken cancellationToken = default);
Task<Department?> GetByIdAsync(string departmentId, CancellationToken cancellationToken = default);
Task<bool> ExistsSiblingCodeAsync(
    string companyId,
    string? parentDepartmentId,
    string code,
    string? excludedDepartmentId = null,
    CancellationToken cancellationToken = default);
```

For `IPositionRepository`, use these string signatures:

```csharp
Task<List<Position>> GetByDepartmentIdAsync(string departmentId, CancellationToken cancellationToken = default);
Task<Position?> GetByIdAsync(string positionId, CancellationToken cancellationToken = default);
```

For `IPersonRepository`, use these string signatures:

```csharp
Task<Person?> GetByIdAsync(string personId, CancellationToken cancellationToken = default);
Task<IReadOnlyList<Person>> GetListAsync(
    string? keyword,
    string? companyId,
    string? departmentId,
    EmploymentStatus? employmentStatus,
    CancellationToken cancellationToken = default);
Task<bool> ExistsPersonNoAsync(
    string? companyId,
    string personNo,
    string? excludedPersonId = null,
    CancellationToken cancellationToken = default);
```

For `IDepartmentService`, use these string signatures:

```csharp
Task<List<DepartmentDto>> GetByCompanyIdAsync(string companyId, CancellationToken cancellationToken = default);
Task<DepartmentDto?> GetByIdAsync(string departmentId, CancellationToken cancellationToken = default);
Task<DepartmentDto> UpdateAsync(string departmentId, DepartmentDto dto, CancellationToken cancellationToken = default);
```

For `IPositionService`, use these string signatures:

```csharp
Task<List<PositionDto>> GetByDepartmentIdAsync(string departmentId, CancellationToken cancellationToken = default);
Task<PositionDto?> GetByIdAsync(string positionId, CancellationToken cancellationToken = default);
Task<PositionDto> UpdateAsync(string positionId, PositionDto dto, CancellationToken cancellationToken = default);
```

For `IPersonService`, use these string signatures:

```csharp
Task<PersonDto?> GetByIdAsync(string personId, CancellationToken cancellationToken = default);
Task<PersonDetailDto?> GetDetailAsync(string personId, CancellationToken cancellationToken = default);
Task<IReadOnlyList<PersonListItemDto>> GetListAsync(
    string? keyword,
    string? companyId,
    string? departmentId,
    EmploymentStatus? employmentStatus,
    CancellationToken cancellationToken = default);
Task<PersonDto> UpdateAsync(string personId, PersonDto dto, CancellationToken cancellationToken = default);
Task<PersonDetailDto> DisableAsync(string personId, CancellationToken cancellationToken = default);
Task<PersonUserLinkDto> LinkUserAsync(string personId, LinkPersonUserRequest request, CancellationToken cancellationToken = default);
```

Keep `PersonUserLink` internals unchanged in this task; Task 5 owns `PersonUserLink` model, repository, service mapping, summary provider, and delete guard changes.

- [x] **Step 6: 注入生成器并生成 Organization 对象 ID**

For each service constructor, add `IPlatformObjectIdGenerator objectIdGenerator`.

Example `CompanyService`:

```csharp
private readonly ICompanyRepository _companyRepository;
private readonly IPlatformObjectIdGenerator _objectIdGenerator;

public CompanyService(
    ICompanyRepository companyRepository,
    IPlatformObjectIdGenerator objectIdGenerator)
{
    _companyRepository = companyRepository;
    _objectIdGenerator = objectIdGenerator;
}
```

In `CreateAsync`:

```csharp
var company = new Company
{
    Id = _objectIdGenerator.NewId(nameof(Company)),
    Code = dto.Code,
    Name = dto.DisplayName,
    SortOrder = ParseSortOrder(dto.SortOrder)
};
```

Use equivalent calls:

```csharp
_objectIdGenerator.NewId(nameof(Department))
_objectIdGenerator.NewId(nameof(Position))
_objectIdGenerator.NewId(nameof(Person))
```

- [x] **Step 7: 改 EF 仓储为字符串读写**

Example change in `EfPersonRepository`:

```csharp
public async Task<Person?> GetByIdAsync(string personId, CancellationToken cancellationToken = default)
{
    var entity = await Entities
        .AsNoTracking()
        .FirstOrDefaultAsync(person => EF.Property<string>(person, "EID") == personId, cancellationToken);

    return entity is null ? null : Map(entity);
}
```

In `Map`:

```csharp
Id = GetString(entity, "EID") ?? string.Empty,
CompanyId = GetString(entity, "CompanyId"),
DepartmentId = GetString(entity, "DepartmentId"),
PositionId = GetString(entity, "PositionId"),
```

In `CopyToEntity`:

```csharp
entity["EID"] = person.Id;
entity["CompanyId"] = person.CompanyId;
entity["DepartmentId"] = person.DepartmentId;
entity["PositionId"] = person.PositionId;
```

Remove helper methods that parse platform object IDs as `Guid` unless they are still needed for non-object values.

- [x] **Step 8: 改 Controllers 路由约束**

Replace route attributes:

```csharp
[HttpGet("{id:guid}")]
[HttpPut("{id:guid}")]
[HttpPost("{id:guid}/disable")]
[HttpDelete("{id:guid}")]
```

with:

```csharp
[HttpGet("{id}")]
[HttpPut("{id}")]
[HttpPost("{id}/disable")]
[HttpDelete("{id}")]
```

Change action parameters from `Guid id` to `string id`.

In `PersonsController.MapListItem`, replace:

```csharp
Id = Guid.Parse(ReadString(row, "EID")),
```

with:

```csharp
Id = ReadString(row, "EID"),
```

In `MapDetail`, replace:

```csharp
Id = Guid.Parse(ReadString(detail.Fields, "EID")),
CompanyId = ReadGuid(detail.Fields, "CompanyId"),
DepartmentId = ReadGuid(detail.Fields, "DepartmentId"),
PositionId = ReadGuid(detail.Fields, "PositionId"),
```

with:

```csharp
Id = ReadString(detail.Fields, "EID"),
CompanyId = ReadStringOrNull(detail.Fields, "CompanyId"),
DepartmentId = ReadStringOrNull(detail.Fields, "DepartmentId"),
PositionId = ReadStringOrNull(detail.Fields, "PositionId"),
```

- [x] **Step 9: 更新 Organization 测试固定 ID**

Use stable protocol IDs:

```csharp
private const string CompanyId = "Company_11111111111111111111111111111111";
private const string DepartmentId = "Department_22222222222222222222222222222222";
private const string PositionId = "Position_33333333333333333333333333333333";
private const string PersonId = "Person_44444444444444444444444444444444";
```

Replace `Guid.Parse(created.Id)` in Organization tests with `created.Id`.

- [x] **Step 10: 运行 Organization 定向测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "CompanyServiceTests|DepartmentServiceTests|PositionServiceTests|PersonListContractTests|PersonServiceTests" -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 11: 提交**

```powershell
git add .\server\modules\Organization .\tests\Organization.Tests
git commit -m "功能：Organization收口平台对象ID"
```

---

### Task 5: PersonUserLink 独立 EID 与摘要/删除守卫字符串化

**Files:**
- Modify: `server/modules/Organization/Models/PersonUserLink.cs`
- Modify: `server/modules/Organization/DTOs/Generated/PersonUserLinkDto.g.cs`
- Modify: `server/modules/Organization/Repositories/IPersonUserLinkRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfPersonUserLinkRepository.cs`
- Modify: `server/modules/Organization/Services/PersonService.cs`
- Modify: `server/modules/Organization/Crud/PersonUserLinkSummaryProvider.cs`
- Modify: `server/modules/Organization/Crud/PersonAuthorizationSummaryProvider.cs`
- Modify: `server/modules/Organization/Crud/PersonCrudDeleteGuard.cs`
- Test: `tests/Organization.Tests/Organization.Tests.csproj`

- [x] **Step 1: 写 PersonUserLink 独立 EID 失败测试**

Append to `tests/Organization.Tests/PersonServiceTests.cs`:

```csharp
[Fact]
public async Task LinkUserAsync_AssignsPersonUserLinkPrefixedId()
{
    var service = BuildService(new SequenceObjectIdGenerator(
        "Person_00000000000000000000000000000001",
        "PersonUserLink_00000000000000000000000000000001"));
    var person = await service.CreateAsync(new PersonDto
    {
        PersonNo = "P900",
        Name = "绑定人员",
        IsEnabled = true
    });

    var link = await service.LinkUserAsync(person.Id, new LinkPersonUserRequest
    {
        UserId = "user-1",
        LinkedBy = "tester"
    });

    Assert.Equal("PersonUserLink_00000000000000000000000000000001", link.Id);
    Assert.Equal(person.Id, link.PersonId);
}
```

- [x] **Step 2: 运行 PersonUserLink 定向测试确认失败**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter LinkUserAsync_AssignsPersonUserLinkPrefixedId -nr:false -v minimal
```

Expected:

```text
FAIL
Assert.Equal() Failure
Expected: PersonUserLink_00000000000000000000000000000001
Actual:   Person_00000000000000000000000000000001
```

- [x] **Step 3: 改 PersonUserLink 模型**

Modify `server/modules/Organization/Models/PersonUserLink.cs`:

```csharp
namespace ICP.Platform.Modules.Organization.Models;

public sealed class PersonUserLink
{
    public string Id { get; set; } = string.Empty;
    public string PersonId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    public string? LinkedBy { get; set; }
}
```

- [x] **Step 4: 改 PersonUserLinkRepository 接口和 EF 实现**

Modify `server/modules/Organization/Repositories/IPersonUserLinkRepository.cs`:

```csharp
Task<PersonUserLink?> GetByPersonIdAsync(string personId, CancellationToken cancellationToken = default);
Task<bool> ExistsByUserIdAsync(
    string userId,
    string? excludedPersonId = null,
    CancellationToken cancellationToken = default);
Task UpsertAsync(PersonUserLink link, CancellationToken cancellationToken = default);
```

In `EfPersonUserLinkRepository`, compare string fields:

```csharp
FirstOrDefaultAsync(link => EF.Property<string>(link, "PersonId") == personId, cancellationToken);
```

In `ToEntity`:

```csharp
var entity = new Dictionary<string, object?>(StringComparer.Ordinal)
{
    ["EID"] = link.Id
};
```

In `CopyToEntity`:

```csharp
entity["EID"] = link.Id;
entity["PersonId"] = link.PersonId;
entity["UserId"] = link.UserId;
entity["LinkedAt"] = link.LinkedAt;
entity["LinkedBy"] = link.LinkedBy;
```

- [x] **Step 5: 改 PersonService 绑定逻辑**

In `LinkUserAsync`, set:

```csharp
var link = new PersonUserLink
{
    Id = _objectIdGenerator.NewId(nameof(PersonUserLink)),
    PersonId = personId,
    UserId = request.UserId,
    LinkedAt = DateTime.UtcNow,
    LinkedBy = string.IsNullOrWhiteSpace(request.LinkedBy) ? "system" : request.LinkedBy
};
```

Map link DTO:

```csharp
private static PersonUserLinkDto MapToDto(PersonUserLink link) => new()
{
    Id = link.Id,
    PersonId = link.PersonId,
    UserId = link.UserId,
    CreatedAt = link.LinkedAt,
    LinkedAt = link.LinkedAt,
    LinkedBy = link.LinkedBy
};
```

- [x] **Step 6: 改 summary provider 和 delete guard**

In `PersonUserLinkSummaryProvider`:

```csharp
var personId = detail[descriptor.IdField]?.ToString() ?? string.Empty;
var link = await _repository.GetByPersonIdAsync(personId, cancellationToken);
```

In `PersonAuthorizationSummaryProvider`:

```csharp
var personId = detail[descriptor.IdField]?.ToString() ?? string.Empty;
var link = await _userLinkRepository.GetByPersonIdAsync(personId, cancellationToken);
```

In `PersonCrudDeleteGuard`:

```csharp
if (string.IsNullOrWhiteSpace(id))
{
    return new StandardCrudError
    {
        StatusCode = 400,
        Code = "Organization.Person.InvalidId",
        Message = "人员标识不能为空。",
        ReasonKey = "organization.person.invalidId"
    };
}

var link = await _userLinkRepository.GetByPersonIdAsync(id, cancellationToken);
```

- [x] **Step 7: 运行 Organization 全量测试**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [x] **Step 8: 提交**

```powershell
git add .\server\modules\Organization .\tests\Organization.Tests
git commit -m "功能：人员账号关联使用独立EID"
```

---

### Task 6: 文档回填与全链路验证

**Files:**
- Modify: `docs/verification/organization-module-verification.md`
- Test: `tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj`
- Test: `tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj`
- Test: `tests/Authorization.Tests/Authorization.Tests.csproj`
- Test: `tests/Organization.Tests/Organization.Tests.csproj`
- Build: `apps/host-api/HostApi.csproj`

- [ ] **Step 1: 搜索裸 GUID 生成残留**

Run:

```powershell
rg -n "Id \\{ get; set; \\} = Guid\\.NewGuid|Guid\\.(Parse|TryParse)\\(|\\{id:guid\\}|Guid\\.NewGuid\\(\\)\\.ToString\\(\\\"D\\\"\\)" server/modules/Organization server/platform-crud
rg -n "Id \\{ get; set; \\} = Guid\\.NewGuid|Guid\\.NewGuid\\(\\)\\.ToString\\(\\)" server/modules/ConfigCenter server/modules/Authorization
```

Expected:

```text
No matches for platform object ID creation, EID parsing, route GUID constraints, or Organization service string-ID parsing paths.
```

If matches remain only for temp directories, non-platform file IDs, or test database names outside the searched platform object paths, leave them and record that they are outside this EID protocol scope.

- [ ] **Step 2: 回填验证记录**

Append to `docs/verification/organization-module-verification.md`:

```md

## EID 值协议平台化验证

验证日期：2026-05-10

前提：

- 当前开发数据库按空库重建处理，不保留旧裸 GUID 主键数据兼容承诺。

验证目标：

- 平台对象新建 `EID` 使用 `{ModelName}_{Guid:N}`。
- PlatformCrud、ConfigCenter、Authorization、Organization 的新建路径不再生成裸 GUID `EID`。
- Organization 人员、组织主数据和人员账号关联的主键与引用字段按字符串 ID 流转。
- API 路由不再使用 `{id:guid}` 约束。

验证命令：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
```

验证结论：

- 新建平台对象 ID 协议已由平台生成器统一承接。
- 本轮未做历史数据回填；如需导入旧库数据，应单独设计迁移。
```

- [ ] **Step 3: 运行全链路后端验证**

Run:

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
```

Expected:

```text
All test commands pass.
HostApi build succeeds.
```

- [ ] **Step 4: 提交**

```powershell
git add .\docs\verification\organization-module-verification.md
git commit -m "验证：记录EID值协议平台化结果"
```

## Self-Review

- **Spec coverage:** Task 1 覆盖平台 ID 生成器和标准 CRUD；Task 2 覆盖 ConfigCenter；Task 3 覆盖 Authorization；Task 4 和 Task 5 覆盖 Organization 字符串 ID 与 PersonUserLink 独立 EID；Task 6 覆盖验证记录和残留扫描。
- **Placeholder scan:** 本计划未使用 `TODO`、`TBD`、`类似前一任务` 或无命令的泛化步骤。
- **Type consistency:** 所有平台对象 ID 统一使用 `string`；生成器接口名统一为 `IPlatformObjectIdGenerator`；默认实现统一为 `PlatformObjectIdGenerator`；ID 格式统一为 `{ModelName}_{Guid:N}`。

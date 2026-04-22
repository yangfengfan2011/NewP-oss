# 模型真源统一与开发态自动结构同步实施计划

> **供后续 agent 执行时使用：** 默认应结合 `superpowers:subagent-driven-development`（优先）或 `superpowers:executing-plans` 按任务逐步实施。本计划继续使用复选框 `- [ ]` 跟踪执行状态。

**目标：** 把 `modules/<ModuleId>/metadata/models/*.model.json` 收口为持久化模型与基础 DTO 的唯一真源，并在 `Development + SqlServer` 下接入启动前自动 schema sync，使系统无需先生成 migration 三件套也能正常运行。

**实施主线：** 本次实现按“先真源、再 provider、再基础 DTO、再开发启动接线”的顺序推进。运行时仍保持 `ModelDefinition -> EfPlatformDbContext` 的强类型内核，但 `ModelDefinition`、`PlatformPersistenceModelCatalog`、基础 DTO 与开发态 schema sync 都统一改为由 `model.json` 投影结果驱动。

**技术栈：** .NET 10、C#、xUnit、Microsoft.Extensions.Configuration、现有 `server/platform-modeling` metadata/投影组件、现有 `server/tools/dev-schema-sync`、SQL Server

---

## 文件范围

- `modules/Authorization/metadata/models/*.model.json`
  - 授权模块模型真源
- `modules/ConfigCenter/metadata/models/*.model.json`
  - 配置中心模型真源，补齐 `ValueListItem`
- `modules/Organization/metadata/models/*.model.json`
  - 组织模块模型真源，补齐 `Company / Department / Position / PersonUserLink`
- `server/platform-modeling/metadata/*`
  - `model.json` 的字段契约扩展与反序列化模型
- `server/platform-modeling/Projection/ModelMetadataProjector.cs`
  - 从 `model.json` 投影 `ModelDefinition`
- `server/platform-modeling/Dto/*`
  - 基础 DTO 描述、属性命名、CLR 类型投影
- `server/tools/model-dto-sync/*`
  - 基础 DTO 与签名 provider 生成工具
- `server/modules/*/Persistence/Descriptors/*ModelMetadataProvider.cs`
  - 模块级 provider，只从 `model.json` 读取
- `server/platform-persistence-composition/PlatformPersistenceModelCatalog.cs`
  - 只聚合 provider 输出
- `server/platform-persistence-devsync/**`
  - Host 与 CLI 共用的开发态 schema sync 内核
- `apps/host-api/Extensions/PersistenceBootstrapExtensions.cs`
  - 开发环境分支判断与启动前自动结构同步
- `apps/host-api/HostApi.csproj`
  - 复制 `modules/**/metadata/models/*.model.json` 到输出目录
- `tests/Modeling.Tests/*`
  - metadata、DTO 生成、契约语义测试
- `tests/Authorization.Tests/*`
  - 授权模型 provider 与基础 DTO 测试
- `tests/ConfigCenter.Tests/*`
  - 配置中心模型 provider 与基础 DTO 测试
- `tests/Organization.Tests/*`
  - 组织模块模型 provider 与基础 DTO 测试
- `tests/DevSchemaSync.Tests/*`
  - 共享 dev sync 内核与一致性检查测试
- `tests/Persistence.Tests/*`
  - Host 启动分支与自动 schema sync 集成测试

---

### 任务 1：扩展 `model.json` 契约语义与基础 DTO 投影能力

**Files:**
- Create: `server/platform-modeling/metadata/ModelMetadataContractSection.cs`
- Modify: `server/platform-modeling/metadata/ModelMetadataField.cs`
- Modify: `server/platform-modeling/Projection/ModelMetadataProjector.cs`
- Modify: `server/platform-modeling/Dto/ModelDtoGenerator.cs`
- Modify: `server/platform-modeling/Dto/ModelDtoProperty.cs`
- Create: `tests/Modeling.Tests/ModelMetadataContractProjectionTests.cs`
- Modify: `tests/Modeling.Tests/ModelDtoGeneratorTests.cs`

- [ ] **Step 1: 先写字段契约与 DTO 属性命名的失败测试**

```csharp
using ICP.Platform.Modeling;
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Dto;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelMetadataContractProjectionTests
{
    [Fact]
    public void Project_WhenContractMarksEnum_PreservesStringStoreTypeButUsesEnumValueType()
    {
        var document = new ModelMetadataDocument
        {
            SchemaVersion = "1.0",
            ModuleId = "Organization",
            Model = new ModelMetadataModel
            {
                Name = "Position",
                Kind = "Entity",
                Fields =
                [
                    new ModelMetadataField
                    {
                        Name = "EmploymentStatus",
                        Storage = new ModelMetadataStorageSection
                        {
                            Type = "string",
                            Nullable = false
                        },
                        Contract = new ModelMetadataContractSection
                        {
                            ValueType = "enum",
                            ClrType = "EmploymentStatus"
                        }
                    }
                ]
            }
        };

        var definition = new ModelMetadataProjector().Project(document);
        var field = Assert.Single(definition.Fields, item => item.Name == "EmploymentStatus");

        Assert.Equal("string", field.StoreType);
        Assert.Equal(FieldValueType.Enum, field.ValueType);
    }

    [Fact]
    public void Generate_WhenDefinitionContainsKernelFixedFields_UsesStableDtoPropertyNames()
    {
        var definition = new ModelDefinition(
            "ConfigItem",
            "ConfigItem",
            ModelKind.Entity,
            new[]
            {
                new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
                new FieldDefinition("ENAME", "ENAME", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
                new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true)
            },
            Array.Empty<IndexDefinition>());

        var descriptor = new ModelDtoGenerator().Describe(definition);

        Assert.Contains(descriptor.Properties, item => item.PropertyName == "Id");
        Assert.Contains(descriptor.Properties, item => item.PropertyName == "DisplayName");
        Assert.Contains(descriptor.Properties, item => item.PropertyName == "UpdatedAt");
    }
}
```

- [ ] **Step 2: 运行定向测试确认当前缺少 `Contract` 分区与 DTO 别名规则**

Run: `dotnet test tests/Modeling.Tests/Modeling.Tests.csproj --filter "FullyQualifiedName~ModelMetadataContractProjectionTests|FullyQualifiedName~ModelDtoGeneratorTests" -v minimal`
Expected: FAIL，提示 `ModelMetadataContractSection` 不存在，且 `ModelDtoGenerator` 仍输出 `EID` / `ENAME`

- [ ] **Step 3: 最小实现 `contract` 分区、ValueType 覆盖与固定字段 DTO 命名**

```csharp
namespace ICP.Platform.Modeling.Metadata;

public sealed class ModelMetadataContractSection
{
    public string? ValueType { get; init; }

    public string? ClrType { get; init; }
}
```

```csharp
namespace ICP.Platform.Modeling.Metadata;

public sealed class ModelMetadataField
{
    public string Name { get; init; } = string.Empty;
    public string? HistoryName { get; init; }
    public ModelMetadataStorageSection? Storage { get; init; }
    public ModelMetadataContractSection? Contract { get; init; }
    public string? UiType { get; init; }
    public ModelMetadataLayoutSection? Layout { get; init; }
    public ModelMetadataFormSection? Form { get; init; }
    public ModelMetadataViewSection? View { get; init; }
    public ModelMetadataDataSource? DataSource { get; init; }
}
```

```csharp
private static FieldValueType MapValueType(ModelMetadataField field)
{
    if (!string.IsNullOrWhiteSpace(field.Contract?.ValueType))
    {
        return field.Contract.ValueType.ToLowerInvariant() switch
        {
            "enum" => FieldValueType.Enum,
            "objectid" => FieldValueType.ObjectId,
            "bool" => FieldValueType.Boolean,
            "datetime" => FieldValueType.DateTime,
            "description" => FieldValueType.Description,
            _ => FieldValueType.Text
        };
    }

    return field.Storage!.Type.ToLowerInvariant() switch
    {
        "string" => FieldValueType.Text,
        "text" => FieldValueType.Description,
        "json" => FieldValueType.Description,
        "bool" => FieldValueType.Boolean,
        "date" => FieldValueType.DateTime,
        "datetime" => FieldValueType.DateTime,
        "guid" => FieldValueType.ObjectId,
        _ => FieldValueType.Text
    };
}
```

```csharp
private static string ResolvePropertyName(FieldDefinition field)
{
    return field.Name switch
    {
        "EID" => "Id",
        "ENAME" => "DisplayName",
        "ECODE" => "Code",
        _ => field.Name
    };
}
```

- [ ] **Step 4: 更新 DTO 生成测试，补枚举 CLR 类型断言**

```csharp
[Fact]
public void Generate_ForEnumFields_UsesClrTypeWhenProvided()
{
    var definition = new ModelDefinition(
        "Position",
        "Position",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("EmploymentStatus", "EmploymentStatus", FieldKind.Scalar, FieldValueType.Enum, "string", true, false, false)
        },
        Array.Empty<IndexDefinition>());

    var code = new ModelDtoGenerator().Generate(definition, new Dictionary<string, string>
    {
        ["EmploymentStatus"] = "EmploymentStatus"
    });

    Assert.Contains("public EmploymentStatus EmploymentStatus { get; init; }", code, StringComparison.Ordinal);
    Assert.Contains("public string? EmploymentStatus_DisplayValue { get; init; }", code, StringComparison.Ordinal);
}
```

- [ ] **Step 5: 运行建模测试确认通过**

Run: `dotnet test tests/Modeling.Tests/Modeling.Tests.csproj -v minimal`
Expected: PASS

- [ ] **Step 6: 提交建模契约扩展**

```bash
git add server/platform-modeling/metadata server/platform-modeling/Projection server/platform-modeling/Dto tests/Modeling.Tests
git commit -m "功能：扩展模型契约与基础 DTO 投影"
```

### 任务 2：补齐 Authorization 模型真源并拆出非基础 DTO

**Files:**
- Create: `modules/Authorization/metadata/models/Role.model.json`
- Create: `modules/Authorization/metadata/models/Permission.model.json`
- Create: `modules/Authorization/metadata/models/RolePermission.model.json`
- Create: `modules/Authorization/metadata/models/UserRole.model.json`
- Create: `server/modules/Authorization/DTOs/PermissionGroupDto.cs`
- Create: `server/modules/Authorization/DTOs/RolePermissionsDto.cs`
- Create: `server/modules/Authorization/DTOs/AssignRolePermissionsRequest.cs`
- Create: `server/modules/Authorization/DTOs/AssignUserRolesRequest.cs`
- Modify: `server/modules/Authorization/DTOs/PermissionDto.cs`
- Modify: `server/modules/Authorization/DTOs/RoleDto.cs`
- Modify: `server/modules/Authorization/DTOs/UserRoleDto.cs`
- Create: `tests/Authorization.Tests/AuthorizationModelMetadataProviderTests.cs`

- [ ] **Step 1: 先写授权模块 metadata provider 失败测试**

```csharp
using ICP.Platform.Modeling.Validation;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.Authorization.Tests;

public class AuthorizationModelMetadataProviderTests
{
    [Fact]
    public void GetDefinitions_LoadsAuthorizationModelsFromMetadata()
    {
        var definitions = new AuthorizationModelMetadataProvider().GetDefinitions();
        var validator = new ModelDefinitionValidator();

        Assert.Contains(definitions, item => item.Name == "Role");
        Assert.Contains(definitions, item => item.Name == "Permission");
        Assert.Contains(definitions, item => item.Name == "RolePermission");
        Assert.Contains(definitions, item => item.Name == "UserRole");

        foreach (var definition in definitions)
        {
            validator.Validate(definition);
        }
    }
}
```

- [ ] **Step 2: 拆出仍需手写保留的聚合/请求 DTO，给基础 DTO 生成让路**

```csharp
namespace ICP.Platform.Modules.Authorization.DTOs;

public record PermissionGroupDto
{
    public required string ModuleId { get; init; }
    public string? Group { get; init; }
    public required List<PermissionDto> Permissions { get; init; }
}
```

```csharp
namespace ICP.Platform.Modules.Authorization.DTOs;

public record RolePermissionsDto
{
    public required string RoleId { get; init; }
    public required List<string> PermissionCodes { get; init; }
}

public record AssignRolePermissionsRequest
{
    public required List<string> PermissionCodes { get; init; }
}
```

```csharp
namespace ICP.Platform.Modules.Authorization.DTOs;

public record AssignUserRolesRequest
{
    public required List<string> RoleCodes { get; init; }
}
```

- [ ] **Step 3: 写出 4 个授权模型真源文件**

```json
{
  "schemaVersion": "1.0",
  "type": "model",
  "Role": {
    "kind": "Entity",
    "fields": [
      { "name": "Code", "storage": { "type": "string" } },
      { "name": "Name", "storage": { "type": "string" } },
      { "name": "Description", "storage": { "type": "text", "nullable": true } },
      { "name": "IsEnabled", "storage": { "type": "bool" } },
      { "name": "IsBuiltIn", "storage": { "type": "bool" } }
    ],
    "indexes": [
      { "name": "UX_Role_Code", "fields": ["Code"], "unique": true }
    ]
  }
}
```

```json
{
  "schemaVersion": "1.0",
  "type": "model",
  "Permission": {
    "kind": "Entity",
    "fields": [
      { "name": "Code", "storage": { "type": "string" } },
      { "name": "DisplayName", "storage": { "type": "string" } },
      { "name": "Description", "storage": { "type": "text", "nullable": true } },
      { "name": "ModuleId", "storage": { "type": "string" } },
      { "name": "Group", "storage": { "type": "string", "nullable": true } }
    ],
    "indexes": [
      { "name": "UX_Permission_Code", "fields": ["Code"], "unique": true },
      { "name": "IX_Permission_Module", "fields": ["ModuleId"], "unique": false }
    ]
  }
}
```

```json
{
  "schemaVersion": "1.0",
  "type": "model",
  "RolePermission": {
    "kind": "Entity",
    "fields": [
      { "name": "RoleId", "storage": { "type": "string" } },
      { "name": "PermissionId", "storage": { "type": "string" } },
      { "name": "GrantedAt", "storage": { "type": "datetime" } },
      { "name": "GrantedBy", "storage": { "type": "string", "nullable": true } }
    ],
    "indexes": [
      { "name": "UX_RolePermission_Role_Permission", "fields": ["RoleId", "PermissionId"], "unique": true }
    ]
  }
}
```

```json
{
  "schemaVersion": "1.0",
  "type": "model",
  "UserRole": {
    "kind": "Entity",
    "fields": [
      { "name": "UserId", "storage": { "type": "string" } },
      { "name": "RoleId", "storage": { "type": "string" } },
      { "name": "AssignedAt", "storage": { "type": "datetime" } },
      { "name": "AssignedBy", "storage": { "type": "string", "nullable": true } }
    ],
    "indexes": [
      { "name": "UX_UserRole_User_Role", "fields": ["UserId", "RoleId"], "unique": true },
      { "name": "IX_UserRole_Role", "fields": ["RoleId"], "unique": false }
    ]
  }
}
```

- [ ] **Step 4: 运行授权模块定向测试确认 metadata 真源可读**

Run: `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj --filter "FullyQualifiedName~AuthorizationModelMetadataProviderTests" -v minimal`
Expected: PASS

- [ ] **Step 5: 提交授权模块真源补齐**

```bash
git add modules/Authorization/metadata/models server/modules/Authorization/DTOs tests/Authorization.Tests/AuthorizationModelMetadataProviderTests.cs
git commit -m "功能：补齐授权模块模型真源"
```

### 任务 3：补齐 Organization 与 ConfigCenter 模型真源，并为枚举字段补 `contract`

**Files:**
- Create: `modules/Organization/metadata/models/Company.model.json`
- Create: `modules/Organization/metadata/models/Department.model.json`
- Create: `modules/Organization/metadata/models/Position.model.json`
- Create: `modules/Organization/metadata/models/PersonUserLink.model.json`
- Modify: `modules/Organization/metadata/models/Person.model.json`
- Create: `modules/ConfigCenter/metadata/models/ValueListItem.model.json`
- Modify: `modules/ConfigCenter/metadata/models/ConfigItem.model.json`
- Create: `server/modules/ConfigCenter/DTOs/ConfigGroupDto.cs`
- Create: `tests/Organization.Tests/OrganizationModelMetadataProviderTests.cs`
- Modify: `tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs`

- [ ] **Step 1: 先写 Organization / ConfigCenter provider 失败测试**

```csharp
using ICP.Platform.Modules.Organization.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class OrganizationModelMetadataProviderTests
{
    [Fact]
    public void GetDefinitions_LoadsAllOrganizationModelsFromMetadata()
    {
        var definitions = new OrganizationModelMetadataProvider().GetDefinitions();

        Assert.Contains(definitions, item => item.Name == "Company");
        Assert.Contains(definitions, item => item.Name == "Department");
        Assert.Contains(definitions, item => item.Name == "Position");
        Assert.Contains(definitions, item => item.Name == "Person");
        Assert.Contains(definitions, item => item.Name == "PersonUserLink");
    }
}
```

```csharp
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using Xunit;

namespace ICP.Platform.Modules.ConfigCenter.Tests;

public class ConfigCenterModelMetadataProviderTests
{
    [Fact]
    public void GetDefinitions_LoadsConfigItemAndValueListItemFromMetadata()
    {
        var definitions = new ConfigCenterModelMetadataProvider().GetDefinitions();

        Assert.Contains(definitions, item => item.Name == "ConfigItem");
        Assert.Contains(definitions, item => item.Name == "ValueListItem");
    }
}
```

- [ ] **Step 2: 为 `Person` 与 `Position` 补枚举契约，为 `ConfigItem` / `ValueListItem` 保留基础 DTO 兼容字段**

```json
{
  "name": "EmploymentStatus",
  "storage": {
    "type": "string",
    "nullable": true
  },
  "contract": {
    "valueType": "enum",
    "clrType": "EmploymentStatus"
  },
  "dataSource": {
    "type": "enum",
    "key": "EmploymentStatus"
  }
}
```

```json
{
  "schemaVersion": "1.0",
  "type": "model",
  "ValueListItem": {
    "kind": "Entity",
    "fields": [
      { "name": "Code", "storage": { "type": "string" } },
      { "name": "Name", "storage": { "type": "string" } },
      { "name": "Description", "storage": { "type": "text", "nullable": true } }
    ],
    "indexes": [
      { "name": "UX_ValueListItem_Code", "fields": ["Code"], "unique": true }
    ]
  }
}
```

- [ ] **Step 3: 补齐 Company / Department / Position / PersonUserLink 模型文件**

```json
{
  "schemaVersion": "1.0",
  "type": "model",
  "Company": {
    "kind": "Entity",
    "fields": [
      { "name": "Code", "storage": { "type": "string" } },
      { "name": "Name", "storage": { "type": "string" } },
      { "name": "IsEnabled", "storage": { "type": "bool" } },
      { "name": "SortOrder", "storage": { "type": "string" } }
    ],
    "indexes": [
      { "name": "UX_Company_Code", "fields": ["Code"], "unique": true }
    ]
  }
}
```

```json
{
  "schemaVersion": "1.0",
  "type": "model",
  "Department": {
    "kind": "Entity",
    "fields": [
      { "name": "CompanyId", "storage": { "type": "guid" } },
      { "name": "ParentDepartmentId", "storage": { "type": "guid", "nullable": true } },
      { "name": "Code", "storage": { "type": "string" } },
      { "name": "Name", "storage": { "type": "string" } },
      { "name": "IsEnabled", "storage": { "type": "bool" } },
      { "name": "SortOrder", "storage": { "type": "string" } }
    ],
    "indexes": [
      { "name": "IX_Department_Company", "fields": ["CompanyId"], "unique": false },
      { "name": "IX_Department_Parent", "fields": ["ParentDepartmentId"], "unique": false },
      { "name": "IX_Department_Company_Parent_Code", "fields": ["CompanyId", "ParentDepartmentId", "Code"], "unique": false }
    ]
  }
}
```

```json
{
  "schemaVersion": "1.0",
  "type": "model",
  "Position": {
    "kind": "Entity",
    "fields": [
      { "name": "CompanyId", "storage": { "type": "guid" } },
      { "name": "DepartmentId", "storage": { "type": "guid" } },
      { "name": "Code", "storage": { "type": "string" } },
      { "name": "Name", "storage": { "type": "string" } },
      {
        "name": "EmploymentStatus",
        "storage": { "type": "string" },
        "contract": { "valueType": "enum", "clrType": "EmploymentStatus" },
        "dataSource": { "type": "enum", "key": "EmploymentStatus" }
      },
      { "name": "IsEnabled", "storage": { "type": "bool" } },
      { "name": "SortOrder", "storage": { "type": "string" } }
    ],
    "indexes": [
      { "name": "IX_Position_Department", "fields": ["DepartmentId"], "unique": false },
      { "name": "IX_Position_Company", "fields": ["CompanyId"], "unique": false }
    ]
  }
}
```

```json
{
  "schemaVersion": "1.0",
  "type": "model",
  "PersonUserLink": {
    "kind": "Entity",
    "fields": [
      { "name": "PersonId", "storage": { "type": "guid" } },
      { "name": "UserId", "storage": { "type": "string" } },
      { "name": "LinkedAt", "storage": { "type": "datetime" } },
      { "name": "LinkedBy", "storage": { "type": "string" } }
    ],
    "indexes": [
      { "name": "UX_PersonUserLink_Person", "fields": ["PersonId"], "unique": true },
      { "name": "UX_PersonUserLink_User", "fields": ["UserId"], "unique": true }
    ]
  }
}
```

- [ ] **Step 4: 拆出 `ConfigGroupDto`，避免占住 `ConfigItemDto` 文件**

```csharp
namespace ICP.Platform.Modules.ConfigCenter.DTOs;

public class ConfigGroupDto
{
    public required string ModuleId { get; set; }
    public string? Group { get; set; }
    public List<ConfigItemDto> Items { get; set; } = new();
}
```

- [ ] **Step 5: 运行组织与配置中心定向测试**

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter "FullyQualifiedName~OrganizationModelMetadataProviderTests" -v minimal`
Expected: PASS

Run: `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj --filter "FullyQualifiedName~ConfigCenterModelMetadataProviderTests" -v minimal`
Expected: PASS

- [ ] **Step 6: 提交组织与配置中心真源补齐**

```bash
git add modules/Organization/metadata/models modules/ConfigCenter/metadata/models server/modules/ConfigCenter/DTOs tests/Organization.Tests tests/ConfigCenter.Tests
git commit -m "功能：补齐组织与配置中心模型真源"
```

### 任务 4：用 metadata provider 取代手写 `ModelDefinition`

**Files:**
- Create: `server/modules/Authorization/Persistence/Descriptors/AuthorizationModelMetadataProvider.cs`
- Create: `server/modules/Organization/Persistence/Descriptors/OrganizationModelMetadataProvider.cs`
- Modify: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs`
- Modify: `server/modules/Authorization/Persistence/Descriptors/AuthorizationModelDefinitions.cs`
- Modify: `server/modules/Organization/Persistence/Descriptors/OrganizationModelDefinitions.cs`
- Modify: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelDefinitions.cs`
- Modify: `tests/Authorization.Tests/AuthorizationModelDefinitionTests.cs`
- Modify: `tests/Organization.Tests/OrganizationModelDefinitionTests.cs`
- Modify: `tests/ConfigCenter.Tests/ConfigCenterModelDefinitionTests.cs`

- [ ] **Step 1: 先写等价测试，锁定 provider 输出要和旧定义一致**

```csharp
[Fact]
public void Authorization_MetadataProvider_MatchesLegacyDefinitions()
{
    var legacy = AuthorizationModelDefinitions.All;
    var projected = new AuthorizationModelMetadataProvider().GetDefinitions();

    Assert.Equal(legacy.Select(item => item.Name), projected.Select(item => item.Name));
    Assert.Equal(
        legacy.SelectMany(item => item.Indexes.Select(index => $"{item.Name}:{index.Name}:{string.Join(",", index.Fields)}:{index.IsUnique}")),
        projected.SelectMany(item => item.Indexes.Select(index => $"{item.Name}:{index.Name}:{string.Join(",", index.Fields)}:{index.IsUnique}")));
}
```

- [ ] **Step 2: 提供共享 metadata provider 读取逻辑，并让旧 `*ModelDefinitions` 暂时转为 facade**

```csharp
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modeling.Metadata;
using ICP.Platform.Modeling.Projection;
using ICP.Platform.Modeling.Validation;

namespace ICP.Platform.Modules.Authorization.Persistence.Descriptors;

public sealed class AuthorizationModelMetadataProvider : IModelDefinitionProvider
{
    public IReadOnlyList<ModelDefinition> GetDefinitions()
    {
        var repositoryRoot = RepositoryRootLocator.Find();
        var typeMap = new JsonStorageTypeMapLoader().LoadFromFile(Path.Combine(repositoryRoot, "server", "platform-modeling", "metadata", "storage-types.json"));
        var loader = new JsonModelMetadataDocumentLoader();
        var validator = new ModelMetadataValidator();
        var projector = new ModelMetadataProjector();

        var files = Directory.GetFiles(Path.Combine(repositoryRoot, "modules", "Authorization", "metadata", "models"), "*.model.json");
        return files
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(path => loader.LoadFromFile(path))
            .Select(document =>
            {
                validator.Validate(document, typeMap);
                return projector.Project(document, typeMap, "SqlServer");
            })
            .ToArray();
    }
}
```

```csharp
public static class AuthorizationModelDefinitions
{
    private static readonly Lazy<IReadOnlyList<ModelDefinition>> Definitions = new(() =>
        new AuthorizationModelMetadataProvider().GetDefinitions());

    public static IReadOnlyList<ModelDefinition> All => Definitions.Value;
}
```

- [ ] **Step 3: 运行模块测试确认 facade 切换通过**

Run: `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj -v minimal`
Expected: PASS

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj -v minimal`
Expected: PASS

Run: `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj -v minimal`
Expected: PASS

- [ ] **Step 4: 删除旧手写字段常量定义，保留最小 facade**

```csharp
public static class OrganizationModelDefinitions
{
    private static readonly Lazy<IReadOnlyList<ModelDefinition>> Definitions = new(() =>
        new OrganizationModelMetadataProvider().GetDefinitions());

    public static IReadOnlyList<ModelDefinition> All => Definitions.Value;
}
```

- [ ] **Step 5: 提交 provider 切换**

```bash
git add server/modules/Authorization/Persistence/Descriptors server/modules/Organization/Persistence/Descriptors server/modules/ConfigCenter/Persistence/Descriptors tests/Authorization.Tests tests/Organization.Tests tests/ConfigCenter.Tests
git commit -m "功能：切换模块模型 provider 到元数据投影"
```

### 任务 5：让 catalog 只聚合 provider 结果，并增强 `model-dto-sync`

**Files:**
- Modify: `server/platform-persistence-composition/PlatformPersistenceModelCatalog.cs`
- Modify: `server/tools/model-dto-sync/Program.cs`
- Create: `server/tools/model-dto-sync/ModuleDtoSyncRunner.cs`
- Create: `server/tools/model-dto-sync/GeneratedCodeWriter.cs`
- Create: `server/tools/model-dto-sync/DtoSignatureWriter.cs`
- Modify: `tests/Organization.Tests/EfOrganizationRepositoryTests.cs`
- Create: `tests/Modeling.Tests/ModelDtoSyncRunnerTests.cs`

- [ ] **Step 1: 先写 catalog 聚合与 DTO 生成输出失败测试**

```csharp
using ICP.Platform.Persistence.Composition;
using Xunit;

namespace ICP.Platform.Modeling.Tests;

public class ModelDtoSyncRunnerTests
{
    [Fact]
    public void PlatformPersistenceModelCatalog_ReturnsProviderProjectedDefinitionsOnly()
    {
        var definitions = PlatformPersistenceModelCatalog.GetDefinitions();

        Assert.Contains(definitions, item => item.Name == "Role");
        Assert.Contains(definitions, item => item.Name == "Company");
        Assert.Contains(definitions, item => item.Name == "ValueListItem");
    }
}
```

- [ ] **Step 2: 让 catalog 只引用 provider，不再引用手写 `.All` 常量**

```csharp
using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Modules.Authorization.Persistence.Descriptors;
using ICP.Platform.Modules.ConfigCenter.Persistence.Descriptors;
using ICP.Platform.Modules.Organization.Persistence.Descriptors;

namespace ICP.Platform.Persistence.Composition;

public static class PlatformPersistenceModelCatalog
{
    public static IReadOnlyList<ModelDefinition> GetDefinitions() =>
        new IModelDefinitionProvider[]
        {
            new ConfigCenterModelMetadataProvider(),
            new AuthorizationModelMetadataProvider(),
            new OrganizationModelMetadataProvider()
        }
        .SelectMany(static provider => provider.GetDefinitions())
        .ToArray();
}
```

- [ ] **Step 3: 把 `model-dto-sync` 变成真实文件输出工具**

```csharp
using ICP.Platform.Modeling.Dto;
using ICP.Platform.Persistence.Composition;

namespace ICP.Platform.Tools.ModelDtoSync;

public static class ModuleDtoSyncRunner
{
    public static IReadOnlyList<string> Run(string repositoryRoot)
    {
        var generator = new ModelDtoGenerator();
        var writtenFiles = new List<string>();

        foreach (var definition in PlatformPersistenceModelCatalog.GetDefinitions())
        {
            var moduleId = ResolveModuleId(definition.Name);
            var outputPath = Path.Combine(repositoryRoot, "server", "modules", moduleId, "DTOs", "Generated", definition.Name + "Dto.g.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, generator.Generate(definition), Encoding.UTF8);
            writtenFiles.Add(outputPath);
        }

        return writtenFiles;
    }
}
```

- [ ] **Step 4: 运行建模与 catalog 相关测试**

Run: `dotnet test tests/Modeling.Tests/Modeling.Tests.csproj -v minimal`
Expected: PASS

Run: `dotnet test tests/Persistence.Tests/Persistence.Tests.csproj --filter "FullyQualifiedName~HostPersistenceBootstrapTests" -v minimal`
Expected: PASS

- [ ] **Step 5: 提交 catalog 与 DTO 工具升级**

```bash
git add server/platform-persistence-composition server/tools/model-dto-sync tests/Modeling.Tests tests/Persistence.Tests
git commit -m "功能：统一 catalog 聚合与基础 DTO 同步工具"
```

### 任务 6：生成基础 DTO 与签名 provider，替换受管基础 DTO 文件

**Files:**
- Create: `server/modules/Authorization/DTOs/Generated/RoleDto.g.cs`
- Create: `server/modules/Authorization/DTOs/Generated/PermissionDto.g.cs`
- Create: `server/modules/Authorization/DTOs/Generated/RolePermissionDto.g.cs`
- Create: `server/modules/Authorization/DTOs/Generated/UserRoleDto.g.cs`
- Create: `server/modules/ConfigCenter/DTOs/Generated/ConfigItemDto.g.cs`
- Create: `server/modules/ConfigCenter/DTOs/Generated/ValueListItemDto.g.cs`
- Create: `server/modules/Organization/DTOs/Generated/CompanyDto.g.cs`
- Create: `server/modules/Organization/DTOs/Generated/DepartmentDto.g.cs`
- Create: `server/modules/Organization/DTOs/Generated/PersonDto.g.cs`
- Create: `server/modules/Organization/DTOs/Generated/PersonUserLinkDto.g.cs`
- Create: `server/modules/Organization/DTOs/Generated/PositionDto.g.cs`
- Create: `server/modules/*/DTOs/Generated/*BaseDtoSignatureProvider.g.cs`
- Delete: `server/modules/Authorization/DTOs/RoleDto.cs`
- Delete: `server/modules/Authorization/DTOs/PermissionDto.cs`
- Delete: `server/modules/Authorization/DTOs/UserRoleDto.cs`
- Delete: `server/modules/ConfigCenter/DTOs/ConfigItemDto.cs`
- Delete: `server/modules/ConfigCenter/DTOs/ValueListItemDto.cs`
- Delete: `server/modules/Organization/DTOs/CompanyDto.cs`
- Delete: `server/modules/Organization/DTOs/DepartmentDto.cs`
- Delete: `server/modules/Organization/DTOs/PersonDto.cs`
- Delete: `server/modules/Organization/DTOs/PersonUserLinkDto.cs`
- Delete: `server/modules/Organization/DTOs/PositionDto.cs`
- Create: `tests/Authorization.Tests/AuthorizationGeneratedDtoTests.cs`
- Create: `tests/ConfigCenter.Tests/ConfigCenterGeneratedDtoTests.cs`
- Create: `tests/Organization.Tests/OrganizationGeneratedDtoTests.cs`

- [ ] **Step 1: 先写基础 DTO 与签名 provider 失败测试**

```csharp
using System.Reflection;
using Xunit;

namespace ICP.Platform.Modules.Organization.Tests;

public class OrganizationGeneratedDtoTests
{
    [Fact]
    public void PersonDto_UsesEnumClrTypeAndDisplayValueCompanion()
    {
        var dtoType = typeof(OrganizationModule).Assembly.GetType("ICP.Platform.Modules.Organization.DTOs.PersonDto");
        Assert.NotNull(dtoType);

        Assert.Equal("EmploymentStatus", dtoType!.GetProperty("EmploymentStatus")!.PropertyType.Name);
        Assert.Equal(typeof(string), dtoType.GetProperty("EmploymentStatus_DisplayValue")!.PropertyType);
        Assert.NotNull(dtoType.GetProperty("Id"));
    }
}
```

- [ ] **Step 2: 生成并落盘基础 DTO 文件**

```csharp
public sealed record PersonDto
{
    public Guid Id { get; init; }
    public string PersonNo { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Guid? CompanyId { get; init; }
    public string? CompanyId_DisplayValue { get; init; }
    public Guid? DepartmentId { get; init; }
    public string? DepartmentId_DisplayValue { get; init; }
    public Guid? PositionId { get; init; }
    public string? PositionId_DisplayValue { get; init; }
    public EmploymentStatus? EmploymentStatus { get; init; }
    public string? EmploymentStatus_DisplayValue { get; init; }
    public bool IsEnabled { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

```csharp
public sealed class OrganizationBaseDtoSignatureProvider : IBaseDtoSignatureProvider
{
    public IReadOnlyDictionary<string, string> GetSignatures() =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CompanyDto"] = "sha256:2e2d7c4d2db9c37edbb11e9190f8d6ef4f0ee9f0d95efeb5d2a6d687cf6d5f91",
            ["DepartmentDto"] = "sha256:9ef0c18f48bbf4ca30b3ddbc0e36d590b7eb2b2bf2c20f4c0651d6a5d0a1a9e2",
            ["PersonDto"] = "sha256:7a34be6f2e8a4f9802c2db2455c5fbb4a8c57b86c355d235e6809b643e25c37c",
            ["PersonUserLinkDto"] = "sha256:dfdc7244b0354df8e0a321c112f6723d4ac0f98b19c16d8cf3fc1b91a82c17ab",
            ["PositionDto"] = "sha256:eb5cb7d51033684711df0f0f8f967038b67f36d9eb0cb2d41a3028f6cbbf8fe9"
        };
}
```

- [ ] **Step 3: 把原本混在基础 DTO 文件里的非基础类型迁走**

```csharp
namespace ICP.Platform.Modules.Authorization.DTOs;

public record UserRoleAssignmentsDto
{
    public required string UserId { get; init; }
    public required List<string> RoleCodes { get; init; }
}
```

```csharp
namespace ICP.Platform.Modules.Authorization.DTOs;

public record RolePermissionAssignmentsDto
{
    public required string RoleId { get; init; }
    public required List<string> PermissionCodes { get; init; }
}
```

- [ ] **Step 4: 运行模块 DTO 回归测试**

Run: `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj -v minimal`
Expected: PASS

Run: `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj -v minimal`
Expected: PASS

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj -v minimal`
Expected: PASS

- [ ] **Step 5: 提交基础 DTO 收口**

```bash
git add server/modules/Authorization/DTOs server/modules/ConfigCenter/DTOs server/modules/Organization/DTOs tests/Authorization.Tests tests/ConfigCenter.Tests tests/Organization.Tests
git commit -m "功能：统一生成基础 DTO 与签名清单"
```

### 任务 7：抽出共享 dev sync 内核并加入真源一致性检查

**Files:**
- Create: `server/platform-persistence-devsync/PlatformPersistence.DevSync.csproj`
- Create: `server/platform-persistence-devsync/Coordination/DevelopmentSchemaSyncCoordinator.cs`
- Create: `server/platform-persistence-devsync/Coordination/ModelSourceInventory.cs`
- Create: `server/platform-persistence-devsync/Coordination/BaseDtoSignatureVerifier.cs`
- Create: `server/platform-persistence-devsync/Coordination/DevelopmentSchemaSyncOptions.cs`
- Modify: `server/tools/dev-schema-sync/Program.cs`
- Modify: `server/tools/dev-schema-sync/DevSchemaSync.csproj`
- Create: `tests/DevSchemaSync.Tests/Coordination/DevelopmentSchemaSyncCoordinatorTests.cs`
- Modify: `ICP-Platform.sln`

- [ ] **Step 1: 先写一致性检查与高风险阻断失败测试**

```csharp
using ICP.Platform.Tools.DevSchemaSync.Planning;
using ICP.Platform.Persistence.DevSync.Coordination;
using Xunit;

namespace ICP.Platform.DevSchemaSync.Tests.Coordination;

public class DevelopmentSchemaSyncCoordinatorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenPlanContainsConfirmOperations_ReturnsBlockedResult()
    {
        var coordinator = DevelopmentSchemaSyncCoordinator.CreateForTests(
            inventory: ModelSourceInventory.Success(["Role"]),
            signaturesMatch: true,
            plan: new SchemaPlan(
                new SchemaPlanSummary(1, 1, 0, 1, 0),
                ["Role"],
                [new SchemaOperation("DropColumn", "Confirm", "Role", "Description", null, "drop Description", ["ALTER TABLE [Role] DROP COLUMN [Description]"])],
                []));

        var result = await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.False(result.StartupAllowed);
        Assert.Equal("ConfirmOperationsDetected", result.FailureReason);
    }
}
```

- [ ] **Step 2: 建立共享内核项目，并把 CLI 改为只做参数外壳**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>ICP.Platform.Persistence.DevSync</RootNamespace>
    <AssemblyName>PlatformPersistence.DevSync</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\\platform-modeling\\PlatformModeling.csproj" />
    <ProjectReference Include="..\\platform-persistence-composition\\PlatformPersistence.Composition.csproj" />
    <ProjectReference Include="..\\platform-persistence-efcore\\PlatformPersistence.EfCore.csproj" />
  </ItemGroup>
</Project>
```

```csharp
var coordinator = DevSchemaSyncCliBootstrap.CreateCoordinator(repositoryRoot, args);
var result = await coordinator.ExecuteAsync(CancellationToken.None);
return result.ExitCode;
```

- [ ] **Step 3: 实现真源 inventory 与 DTO 签名校验**

```csharp
public sealed record ModelSourceInventoryResult(
    bool Success,
    IReadOnlyList<string> MissingModels,
    IReadOnlyList<string> LoadedModels);

public sealed class BaseDtoSignatureVerifier
{
    public bool Verify(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual,
        out IReadOnlyList<string> mismatches)
    {
        mismatches = expected
            .Where(pair => !actual.TryGetValue(pair.Key, out var value) || !string.Equals(value, pair.Value, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToArray();

        return mismatches.Count == 0;
    }
}
```

- [ ] **Step 4: 运行 dev sync 相关测试**

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj -v minimal`
Expected: PASS

- [ ] **Step 5: 提交共享 dev sync 内核**

```bash
git add ICP-Platform.sln server/platform-persistence-devsync server/tools/dev-schema-sync tests/DevSchemaSync.Tests
git commit -m "功能：抽出共享开发态结构同步内核"
```

### 任务 8：在 Host 开发启动链路接入自动 schema sync，并做最终回归

**Files:**
- Modify: `apps/host-api/Extensions/PersistenceBootstrapExtensions.cs`
- Modify: `apps/host-api/appsettings.Development.json`
- Modify: `apps/host-api/HostApi.csproj`
- Modify: `tests/Persistence.Tests/HostPersistenceBootstrapTests.cs`
- Create: `tests/Persistence.Tests/DevelopmentSchemaSyncBootstrapTests.cs`
- Modify: `docs/verification/dev-schema-sync-tool-verification.md`

说明：

- 本任务按最小宿主改动原则推进，默认只修改 `PersistenceBootstrapExtensions.cs`、开发配置和 `HostApi.csproj`。
- 只有在实际代码验证后确认注册顺序或启动入口必须调整时，才允许把 `apps/host-api/Program.cs` 纳入改动范围，并应同步回写设计文档与一致性 review。

- [ ] **Step 1: 先写 Host 开发启动分支失败测试**

```csharp
using ICP.Platform.HostApi.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Persistence.Tests;

public class DevelopmentSchemaSyncBootstrapTests
{
    [Fact]
    public void AddPlatformPersistence_WhenDevelopmentSchemaSyncEnabled_RegistersCoordinator()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "SqlServer",
                ["Persistence:AutoMigrate"] = "true",
                ["DevelopmentSchemaSync:Enabled"] = "true",
                ["ConnectionStrings:SqlServer"] = "Server=(localdb)\\mssqllocaldb;Database=ICP_Platform;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();

        services.AddPlatformPersistence(configuration);

        Assert.Contains(services, item => item.ServiceType.Name.Contains("DevelopmentSchemaSyncCoordinator", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: 给 Host 增加配置与 metadata 文件复制**

```json
{
  "Persistence": {
    "Adapter": "EfCore",
    "Provider": "SqlServer",
    "AutoMigrate": true,
    "AutoSeed": true
  },
  "DevelopmentSchemaSync": {
    "Enabled": true
  }
}
```

```xml
<ItemGroup>
  <Content Include="..\..\modules\**\metadata\models\*.model.json">
    <Link>modules\%(RecursiveDir)%(Filename)%(Extension)</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

- [ ] **Step 3: 在启动链路里优先走开发态 schema sync**

```csharp
public static async Task ApplyPlatformPersistenceAsync(
    this WebApplication app,
    CancellationToken cancellationToken = default)
{
    using var scope = app.Services.CreateScope();
    var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    if (environment.IsDevelopment()
        && configuration.GetValue<bool>("DevelopmentSchemaSync:Enabled")
        && string.Equals(configuration["Persistence:Provider"], "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        var coordinator = scope.ServiceProvider.GetRequiredService<DevelopmentSchemaSyncCoordinator>();
        var result = await coordinator.ExecuteAsync(cancellationToken);
        if (!result.StartupAllowed)
        {
            throw new InvalidOperationException(result.BuildMessage());
        }

        return;
    }

    var migrationRunner = scope.ServiceProvider.GetRequiredService<IPersistenceMigrationRunner>();
    await migrationRunner.MigrateAsync(cancellationToken);
}
```

- [ ] **Step 4: 运行完整后端回归与开发态 smoke**

Run: `dotnet build ICP-Platform.sln -v minimal`
Expected: PASS

Run: `dotnet test tests/Persistence.Tests/Persistence.Tests.csproj -v minimal`
Expected: PASS

Run: `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj -v minimal`
Expected: PASS

Run: `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj -v minimal`
Expected: PASS

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj -v minimal`
Expected: PASS

Run: `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj -v minimal`
Expected: PASS

- [ ] **Step 5: 更新验证记录并提交**

```bash
git add apps/host-api server/platform-persistence-devsync server/tools/dev-schema-sync server/tools/model-dto-sync tests/Persistence.Tests docs/verification/dev-schema-sync-tool-verification.md
git commit -m "功能：接入开发环境自动结构同步启动链路"
```

## 计划自检

- 规格覆盖：计划覆盖了 `model.json` 真源统一、模块 provider 切换、基础 DTO 收口、字段契约扩展、catalog 聚合、共享 dev sync 内核、Host 开发态自动启动与回归验证。
- 占位检查：未保留任何占位内容；每个任务都给出目标文件、代码方向和验证命令。
- 类型一致性：计划统一采用 `contract.valueType / contract.clrType` 作为 DTO 投影扩展，`PlatformPersistenceModelCatalog` 只聚合 provider，`DevelopmentSchemaSyncCoordinator` 负责启动前一致性检查与风险阻断。

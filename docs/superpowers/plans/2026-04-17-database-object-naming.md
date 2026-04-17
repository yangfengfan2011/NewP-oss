# 数据库对象命名规范落地 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 `docs/designs/database-object-naming-design.md` 落地到当前持久化与 DTO 链路，完成首批核心模块的 PascalCase 表/列命名、协议字段保留规则，以及“一套 DTO + `*_DisplayValue`”生成闭环。

**Architecture:** 先用测试锁定 naming contract，再统一 `ModelDefinition` 与 EF 仓储的存储字段命名，随后新增基于 `ModelDefinition` 的 DTO 生成器，并优先替换 `ConfigCenter`、`Authorization`、`Organization` 这三条当前仍值得继续维护的主线模块。`Audit`、`Files`、`Demo`、`ModuleTemplateSample` 不在本轮改造范围内，只保持冻结或样板定位。

**Tech Stack:** .NET 10 / C#, EF Core, xUnit, System.Text.Json, PowerShell, 多数据库 migration（SqlServer / MySql / Oracle）

---

## 文件与职责映射

**共享建模与持久化内核**

- Modify: `server/platform-modeling/Projection/ModelMetadataProjector.cs`
- Modify: `server/platform-modeling/Validation/ModelMetadataValidator.cs`
- Create: `server/platform-modeling/Dto/ModelDtoProperty.cs`
- Create: `server/platform-modeling/Dto/ModelDtoDescriptor.cs`
- Create: `server/platform-modeling/Dto/ModelDtoGenerator.cs`
- Modify: `server/platform-persistence-efcore/Projection/EfModelProjector.cs`

**模块模型定义与仓储映射**

- Modify: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelDefinitions.cs`
- Modify: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs`
- Modify: `server/modules/ConfigCenter/Persistence/Ef/EfConfigRepository.cs`
- Modify: `server/modules/ConfigCenter/Persistence/Ef/EfValueListRepository.cs`
- Modify: `server/modules/Authorization/Persistence/Descriptors/AuthorizationModelDefinitions.cs`
- Modify: `server/modules/Authorization/Persistence/Ef/EfRoleRepository.cs`
- Modify: `server/modules/Authorization/Persistence/Ef/EfPermissionRepository.cs`
- Modify: `server/modules/Authorization/Persistence/Ef/EfRolePermissionRepository.cs`
- Modify: `server/modules/Authorization/Persistence/Ef/EfUserRoleRepository.cs`
- Modify: `server/modules/Organization/Persistence/Descriptors/OrganizationModelDefinitions.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfCompanyRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfDepartmentRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfPositionRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfPersonRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfPersonUserLinkRepository.cs`

**DTO 生成与控制器接入**

- Modify: `server/modules/ConfigCenter/Controllers/ConfigController.cs`
- Modify: `server/modules/ConfigCenter/Controllers/ValueListController.cs`
- Modify: `server/modules/ConfigCenter/DTOs/ConfigItemDto.cs`
- Modify: `server/modules/ConfigCenter/DTOs/ValueListItemDto.cs`
- Modify: `server/modules/ConfigCenter/Services/IConfigService.cs`
- Modify: `server/modules/ConfigCenter/Services/ConfigService.cs`
- Modify: `server/modules/Authorization/Controllers/RoleController.cs`
- Modify: `server/modules/Authorization/DTOs/RoleDto.cs`
- Modify: `server/modules/Authorization/DTOs/PermissionDto.cs`
- Modify: `server/modules/Authorization/DTOs/UserRoleDto.cs`
- Modify: `server/modules/Authorization/Services/IRoleService.cs`
- Modify: `server/modules/Authorization/Services/RoleService.cs`
- Modify: `server/modules/Organization/DTOs/CompanyDto.cs`
- Modify: `server/modules/Organization/DTOs/DepartmentDto.cs`
- Modify: `server/modules/Organization/DTOs/PersonDto.cs`
- Modify: `server/modules/Organization/DTOs/PositionDto.cs`
- Modify: `server/modules/Organization/DTOs/PersonUserLinkDto.cs`
- Delete: `server/modules/Organization/DTOs/Requests/CreateCompanyRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/UpdateCompanyRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/CreateDepartmentRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/UpdateDepartmentRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/CreatePersonRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/UpdatePersonRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/CreatePositionRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/UpdatePositionRequest.cs`
- Modify: `server/modules/Organization/Controllers/CompaniesController.cs`
- Modify: `server/modules/Organization/Controllers/DepartmentsController.cs`
- Modify: `server/modules/Organization/Controllers/PersonsController.cs`
- Modify: `server/modules/Organization/Controllers/PositionsController.cs`
- Modify: `server/modules/Organization/Services/ICompanyService.cs`
- Modify: `server/modules/Organization/Services/CompanyService.cs`
- Modify: `server/modules/Organization/Services/IDepartmentService.cs`
- Modify: `server/modules/Organization/Services/DepartmentService.cs`
- Modify: `server/modules/Organization/Services/IPersonService.cs`
- Modify: `server/modules/Organization/Services/PersonService.cs`
- Modify: `server/modules/Organization/Services/IPositionService.cs`
- Modify: `server/modules/Organization/Services/PositionService.cs`
- Keep Manual: `server/modules/Organization/DTOs/PersonDetailDto.cs`
- Keep Manual: `server/modules/Organization/DTOs/DepartmentTreeNodeDto.cs`
- Keep Manual: `server/modules/Organization/DTOs/IdentityUserOptionDto.cs`

**DTO 生成工具**

- Create: `server/tools/model-dto-sync/ModelDtoSync.csproj`
- Create: `server/tools/model-dto-sync/Program.cs`

**迁移与验证**

- Modify: `server/platform-persistence-efcore.migrations.sqlserver/Migrations/*`
- Modify: `server/platform-persistence-efcore.migrations.mysql/Migrations/*`
- Modify: `server/platform-persistence-efcore.migrations.oracle/Migrations/*`
- Test: `tests/Modeling.Tests/ModelMetadataProjectorTests.cs`
- Create: `tests/Modeling.Tests/ModelDtoGeneratorTests.cs`
- Modify: `tests/Persistence.Tests/EfModelProjectionTests.cs`
- Modify: `tests/ConfigCenter.Tests/ConfigCenterModelDefinitionTests.cs`
- Modify: `tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs`
- Modify: `tests/Authorization.Tests/AuthorizationModelDefinitionTests.cs`
- Modify: `tests/Authorization.Tests/EfAuthorizationRepositoryTests.cs`
- Create: `tests/Organization.Tests/OrganizationModelDefinitionTests.cs`
- Modify: `tests/Organization.Tests/EfOrganizationRepositoryTests.cs`
- Modify: `docs/current-status.md`
- Modify: `docs/designs/database-object-naming-design.md`

### Task 1: 锁定 naming contract 并统一手写 ModelDefinition

**Files:**
- Modify: `tests/ConfigCenter.Tests/ConfigCenterModelDefinitionTests.cs`
- Modify: `tests/Authorization.Tests/AuthorizationModelDefinitionTests.cs`
- Create: `tests/Organization.Tests/OrganizationModelDefinitionTests.cs`
- Modify: `tests/Persistence.Tests/EfModelProjectionTests.cs`
- Modify: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelDefinitions.cs`
- Modify: `server/modules/Authorization/Persistence/Descriptors/AuthorizationModelDefinitions.cs`
- Modify: `server/modules/Organization/Persistence/Descriptors/OrganizationModelDefinitions.cs`

- [ ] **Step 1: 写出会失败的命名契约测试**

```csharp
[Fact]
public void ValueListItem_UsesPascalCaseTableAndStoreNames()
{
    var definition = Assert.Single(
        ConfigCenterModelDefinitions.All,
        model => model.Name == "ValueListItem");

    Assert.Equal("ValueListItem", definition.TableName);
    Assert.Contains(definition.Fields, field => field.Name == "Id" && field.StoreName == "Id" && field.IsPrimaryKey);
    Assert.Contains(definition.Fields, field => field.Name == "Name" && field.StoreName == "Name");
}
```

```csharp
[Fact]
public void Authorization_DefinitionsUsePascalCaseStoreNames()
{
    var role = Assert.Single(AuthorizationModelDefinitions.All, item => item.Name == "Role");

    Assert.Equal("Role", role.TableName);
    Assert.Contains(role.Fields, field => field.Name == "Id" && field.StoreName == "Id");
    Assert.Contains(role.Fields, field => field.Name == "Code" && field.StoreName == "Code");
    Assert.Contains(role.Fields, field => field.Name == "Name" && field.StoreName == "Name");
}
```

```csharp
[Fact]
public void Person_DefinitionUsesPascalCaseStoreNames()
{
    var person = Assert.Single(OrganizationModelDefinitions.All, item => item.Name == "Person");

    Assert.Equal("Person", person.TableName);
    Assert.Contains(person.Fields, field => field.Name == "Id" && field.StoreName == "Id");
    Assert.Contains(person.Fields, field => field.Name == "CompanyId" && field.StoreName == "CompanyId");
    Assert.Contains(person.Fields, field => field.Name == "EmploymentStatus" && field.StoreName == "EmploymentStatus");
}
```

- [ ] **Step 2: 运行定向测试，确认现状失败**

Run:

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "FullyQualifiedName~ConfigCenterModelDefinitionTests" -nr:false -v minimal
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj --filter "FullyQualifiedName~AuthorizationModelDefinitionTests" -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "FullyQualifiedName~OrganizationModelDefinitionTests" -nr:false -v minimal
dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj --filter "FullyQualifiedName~EfModelProjectionTests" -nr:false -v minimal
```

Expected:

- `ConfigCenter` 仍断言 `value_list_items`
- `Authorization` 仍使用 `Id / Code / Name`
- `Organization` 尚无命名契约测试
- `EfModelProjectionTests` 仍断言 `config_items`

- [ ] **Step 3: 最小改动统一手写定义的表名、列名，并保持当前逻辑字段语义不扩散**

```csharp
public static readonly ModelDefinition ValueListItem = new(
    "ValueListItem",
    "ValueListItem",
    ModelKind.Entity,
    new[]
    {
        new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
        new FieldDefinition("Code", "Code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
        new FieldDefinition("Name", "Name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
        new FieldDefinition("Description", "Description", FieldKind.Scalar, FieldValueType.Description, "string", false, false, false),
        new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
        new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
        new FieldDefinition("UpdatedBy", "UpdatedBy", FieldKind.Audit, FieldValueType.User, "string", false, false, true)
    },
    new[]
    {
        new IndexDefinition("UX_ValueListItem_Code", new[] { "Code" }, true)
    });
```

```csharp
public static readonly ModelDefinition Role = new(
    "Role",
    "Role",
    ModelKind.Entity,
    new[]
    {
        new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
        new FieldDefinition("Code", "Code", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
        new FieldDefinition("Name", "Name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
        new FieldDefinition("Description", "Description", FieldKind.Scalar, FieldValueType.Description, "string", false, false, false),
        new FieldDefinition("IsEnabled", "IsEnabled", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
        new FieldDefinition("IsBuiltIn", "IsBuiltIn", FieldKind.Scalar, FieldValueType.Boolean, "bool", true, false, false),
        new FieldDefinition("CreatedAt", "CreatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", true, false, true),
        new FieldDefinition("CreatedBy", "CreatedBy", FieldKind.Audit, FieldValueType.User, "string", false, false, true),
        new FieldDefinition("UpdatedAt", "UpdatedAt", FieldKind.Audit, FieldValueType.DateTime, "datetime", false, false, true),
        new FieldDefinition("UpdatedBy", "UpdatedBy", FieldKind.Audit, FieldValueType.User, "string", false, false, true)
    },
    new[]
    {
        new IndexDefinition("UX_Role_Code", new[] { "Code" }, true)
    });
```

```csharp
new FieldDefinition("Id", "Id", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
new FieldDefinition("Name", "Name", FieldKind.Scalar, FieldValueType.Text, "string", true, false, false),
new FieldDefinition("CompanyId", "CompanyId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
new FieldDefinition("DepartmentId", "DepartmentId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
```

- [ ] **Step 4: 重新运行模型定义与投影测试，确认通过**

Run:

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "FullyQualifiedName~ConfigCenterModelDefinitionTests" -nr:false -v minimal
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj --filter "FullyQualifiedName~AuthorizationModelDefinitionTests" -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "FullyQualifiedName~OrganizationModelDefinitionTests" -nr:false -v minimal
dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj --filter "FullyQualifiedName~EfModelProjectionTests" -nr:false -v minimal
```

Expected:

- 四组测试全部 PASS

- [ ] **Step 5: 提交**

```bash
git add tests/ConfigCenter.Tests/ConfigCenterModelDefinitionTests.cs tests/Authorization.Tests/AuthorizationModelDefinitionTests.cs tests/Organization.Tests/OrganizationModelDefinitionTests.cs tests/Persistence.Tests/EfModelProjectionTests.cs server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelDefinitions.cs server/modules/Authorization/Persistence/Descriptors/AuthorizationModelDefinitions.cs server/modules/Organization/Persistence/Descriptors/OrganizationModelDefinitions.cs
git commit -m "设计：统一首批模型定义命名约束"
```

### Task 2: 对齐 metadata projector、EF 仓储与三套迁移

**Files:**
- Modify: `server/platform-modeling/Projection/ModelMetadataProjector.cs`
- Modify: `server/modules/ConfigCenter/Persistence/Ef/EfConfigRepository.cs`
- Modify: `server/modules/ConfigCenter/Persistence/Ef/EfValueListRepository.cs`
- Modify: `server/modules/Authorization/Persistence/Ef/EfRoleRepository.cs`
- Modify: `server/modules/Authorization/Persistence/Ef/EfPermissionRepository.cs`
- Modify: `server/modules/Authorization/Persistence/Ef/EfRolePermissionRepository.cs`
- Modify: `server/modules/Authorization/Persistence/Ef/EfUserRoleRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfCompanyRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfDepartmentRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfPositionRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfPersonRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfPersonUserLinkRepository.cs`
- Modify: `tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs`
- Modify: `tests/Authorization.Tests/EfAuthorizationRepositoryTests.cs`
- Modify: `tests/Organization.Tests/EfOrganizationRepositoryTests.cs`
- Modify: `server/platform-persistence-efcore.migrations.sqlserver/Migrations/*`
- Modify: `server/platform-persistence-efcore.migrations.mysql/Migrations/*`
- Modify: `server/platform-persistence-efcore.migrations.oracle/Migrations/*`

- [ ] **Step 1: 补仓储命名映射失败测试**

```csharp
[Fact]
public async Task RoleRepository_MapsDomainFields_To_PascalCaseStoreNames()
{
    var role = await repository.GetByCodeAsync("admin");

    Assert.NotNull(role);
    Assert.Equal("admin", role!.Code);
    Assert.Equal("管理员", role.Name);
}
```

```csharp
[Fact]
public async Task PersonRepository_MapsDomainId_To_PascalCaseStoreName()
{
    var person = await repository.GetByIdAsync(seedPersonId);

    Assert.NotNull(person);
    Assert.Equal(seedPersonId, person!.Id);
}
```

- [ ] **Step 2: 运行定向仓储测试，确认字段改名后仓储读取失败**

Run:

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "FullyQualifiedName~ConfigCenterModelMetadataProviderTests" -nr:false -v minimal
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj --filter "FullyQualifiedName~EfAuthorizationRepositoryTests" -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "FullyQualifiedName~EfOrganizationRepositoryTests" -nr:false -v minimal
```

Expected:

- 由于仓储仍按旧 store name 读写，断言失败

- [ ] **Step 3: 最小改动修正 projector 与仓储层映射**

```csharp
private static Role Map(IReadOnlyDictionary<string, object?> entity) => new()
{
    Id = GetString(entity, "Id")!,
    Code = GetString(entity, "Code")!,
    Name = GetString(entity, "Name")!,
    Description = GetString(entity, "Description"),
    IsEnabled = GetBool(entity, "IsEnabled"),
    IsBuiltIn = GetBool(entity, "IsBuiltIn"),
    CreatedAt = GetDateTime(entity, "CreatedAt"),
    CreatedBy = GetString(entity, "CreatedBy"),
    UpdatedAt = GetNullableDateTime(entity, "UpdatedAt"),
    UpdatedBy = GetString(entity, "UpdatedBy")
};
```

```csharp
entity["Id"] = role.Id;
entity["Code"] = role.Code;
entity["Name"] = role.Name;
```

```csharp
var definition = _projector.Project(document, typeMap, "SqlServer");
Assert.Equal("ConfigItem", definition.TableName);
Assert.Contains(definition.Fields, field => field.Name == "Key" && field.StoreName == "Key");
```

- [ ] **Step 4: 生成三套命名迁移并更新 snapshot**

Run:

```powershell
dotnet ef migrations add AdoptPascalCaseNaming --project .\server\platform-persistence-efcore.migrations.sqlserver\PlatformPersistence.EfCore.SqlServerMigrations.csproj --startup-project .\apps\host-api\HostApi.csproj --context EfPlatformDbContext
dotnet ef migrations add AdoptPascalCaseNaming --project .\server\platform-persistence-efcore.migrations.mysql\PlatformPersistence.EfCore.MySqlMigrations.csproj --startup-project .\apps\host-api\HostApi.csproj --context EfPlatformDbContext
dotnet ef migrations add AdoptPascalCaseNaming --project .\server\platform-persistence-efcore.migrations.oracle\PlatformPersistence.EfCore.OracleMigrations.csproj --startup-project .\apps\host-api\HostApi.csproj --context EfPlatformDbContext
```

Expected:

- 三套 migration 目录新增一组 `AdoptPascalCaseNaming` 迁移文件
- 三套 `EfPlatformDbContextModelSnapshot.cs` 更新为 PascalCase 表/列名

- [ ] **Step 5: 运行持久化与仓储测试**

Run:

```powershell
dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj -nr:false -v minimal
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "FullyQualifiedName~ConfigCenterModelMetadataProviderTests" -nr:false -v minimal
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj --filter "FullyQualifiedName~EfAuthorizationRepositoryTests" -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "FullyQualifiedName~EfOrganizationRepositoryTests" -nr:false -v minimal
```

Expected:

- PASS；仓储能继续返回原有领域对象，而底层字段已切到 PascalCase store name

- [ ] **Step 6: 提交**

```bash
git add server/platform-modeling/Projection/ModelMetadataProjector.cs server/modules/ConfigCenter/Persistence/Ef/EfConfigRepository.cs server/modules/ConfigCenter/Persistence/Ef/EfValueListRepository.cs server/modules/Authorization/Persistence/Ef/EfRoleRepository.cs server/modules/Authorization/Persistence/Ef/EfPermissionRepository.cs server/modules/Authorization/Persistence/Ef/EfRolePermissionRepository.cs server/modules/Authorization/Persistence/Ef/EfUserRoleRepository.cs server/modules/Organization/Persistence/Ef/EfCompanyRepository.cs server/modules/Organization/Persistence/Ef/EfDepartmentRepository.cs server/modules/Organization/Persistence/Ef/EfPositionRepository.cs server/modules/Organization/Persistence/Ef/EfPersonRepository.cs server/modules/Organization/Persistence/Ef/EfPersonUserLinkRepository.cs tests/ConfigCenter.Tests/ConfigCenterModelMetadataProviderTests.cs tests/Authorization.Tests/EfAuthorizationRepositoryTests.cs tests/Organization.Tests/EfOrganizationRepositoryTests.cs server/platform-persistence-efcore.migrations.sqlserver/Migrations server/platform-persistence-efcore.migrations.mysql/Migrations server/platform-persistence-efcore.migrations.oracle/Migrations
git commit -m "功能：对齐命名后的持久化映射与迁移"
```

### Task 3: 新增基于 ModelDefinition 的单 DTO 生成器

**Files:**
- Create: `server/platform-modeling/Dto/ModelDtoProperty.cs`
- Create: `server/platform-modeling/Dto/ModelDtoDescriptor.cs`
- Create: `server/platform-modeling/Dto/ModelDtoGenerator.cs`
- Create: `server/tools/model-dto-sync/ModelDtoSync.csproj`
- Create: `server/tools/model-dto-sync/Program.cs`
- Create: `tests/Modeling.Tests/ModelDtoGeneratorTests.cs`

- [ ] **Step 1: 先写 DTO 生成规则失败测试**

```csharp
[Fact]
public void Generate_ForReferenceAndEnumFields_AddsDisplayValueCompanions()
{
    var definition = new ModelDefinition(
        "Person",
        "Person",
        ModelKind.Entity,
        new[]
        {
            new FieldDefinition("EID", "EID", FieldKind.Scalar, FieldValueType.ObjectId, "string", true, true, false),
            new FieldDefinition("DepartmentId", "DepartmentId", FieldKind.Reference, FieldValueType.ObjectId, "string", true, false, false),
            new FieldDefinition("EmploymentStatus", "EmploymentStatus", FieldKind.Scalar, FieldValueType.Enum, "string", true, false, false)
        },
        Array.Empty<IndexDefinition>());

    var code = new ModelDtoGenerator().Generate(definition);

    Assert.Contains("public string DepartmentId { get; init; }", code);
    Assert.Contains("public string? DepartmentId_DisplayValue { get; init; }", code);
    Assert.Contains("public string EmploymentStatus { get; init; }", code);
    Assert.Contains("public string? EmploymentStatus_DisplayValue { get; init; }", code);
}
```

- [ ] **Step 2: 运行建模测试，确认生成器尚不存在而失败**

Run:

```powershell
dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj --filter "FullyQualifiedName~ModelDtoGeneratorTests" -nr:false -v minimal
```

Expected:

- FAIL，提示 `ModelDtoGenerator` 尚不存在

- [ ] **Step 3: 实现最小 DTO 生成器与同步工具**

```csharp
public sealed record ModelDtoProperty(
    string PropertyName,
    string TypeName,
    bool Nullable);
```

```csharp
public sealed class ModelDtoGenerator
{
    public string Generate(ModelDefinition definition)
    {
        var properties = BuildProperties(definition);
        var builder = new StringBuilder();
        builder.AppendLine("public sealed class " + definition.Name + "Dto");
        builder.AppendLine("{");
        foreach (var property in properties)
        {
            builder.AppendLine($"    public {property.TypeName}{(property.Nullable ? "?" : string.Empty)} {property.PropertyName} {{ get; init; }}");
        }
        builder.AppendLine("}");
        return builder.ToString();
    }
}
```

```csharp
if (field.Kind == FieldKind.Reference || field.ValueType == FieldValueType.Enum)
{
    properties.Add(new ModelDtoProperty(field.Name + "_DisplayValue", "string", true));
}
```

```csharp
// Program.cs
var definitions = PlatformPersistenceModelCatalog.GetDefinitions();
foreach (var definition in definitions)
{
    var code = generator.Generate(definition);
    File.WriteAllText(targetPath, code);
}
```

- [ ] **Step 4: 运行建模测试与工具构建**

Run:

```powershell
dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -nr:false -v minimal
dotnet build .\server\tools\model-dto-sync\ModelDtoSync.csproj -nr:false -v minimal
```

Expected:

- PASS；生成器能输出 `*_DisplayValue` 规则
- `model-dto-sync` 构建通过

- [ ] **Step 5: 提交**

```bash
git add server/platform-modeling/Dto server/tools/model-dto-sync tests/Modeling.Tests/ModelDtoGeneratorTests.cs
git commit -m "功能：新增模型单 DTO 生成器"
```

### Task 4: 试点替换 ConfigCenter 与 Authorization 的 DTO

**Files:**
- Modify: `server/modules/ConfigCenter/Controllers/ConfigController.cs`
- Modify: `server/modules/ConfigCenter/Controllers/ValueListController.cs`
- Modify: `server/modules/ConfigCenter/DTOs/ConfigItemDto.cs`
- Modify: `server/modules/ConfigCenter/DTOs/ValueListItemDto.cs`
- Modify: `server/modules/ConfigCenter/Services/IConfigService.cs`
- Modify: `server/modules/ConfigCenter/Services/ConfigService.cs`
- Modify: `server/modules/Authorization/Controllers/RoleController.cs`
- Modify: `server/modules/Authorization/DTOs/RoleDto.cs`
- Modify: `server/modules/Authorization/DTOs/PermissionDto.cs`
- Modify: `server/modules/Authorization/DTOs/UserRoleDto.cs`
- Modify: `server/modules/Authorization/Services/IRoleService.cs`
- Modify: `server/modules/Authorization/Services/RoleService.cs`
- Modify: `tests/ConfigCenter.Tests/ConfigCenterModuleTests.cs`
- Modify: `tests/Authorization.Tests/AuthorizationServiceTests.cs`

- [ ] **Step 1: 写出同一 DTO 读写规则失败测试**

```csharp
[Fact]
public async Task CreateConfig_Ignores_DisplayValue_CompanionFields()
{
    var dto = new ConfigItemDto
    {
        Id = string.Empty,
        Key = "ConfigCenter.TestKey",
        Value = "true",
        Type = "bool",
        ModuleId = "ConfigCenter",
        DisplayName = "测试开关",
        ModuleId_DisplayValue = "配置中心"
    };

    await controller.Create(dto, CancellationToken.None);

    Assert.Equal("ConfigCenter", saved.ModuleId);
}
```

- [ ] **Step 2: 运行模块测试，确认控制器和 DTO 仍使用多套输入模型**

Run:

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj --filter "FullyQualifiedName~ConfigCenterModuleTests" -nr:false -v minimal
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj --filter "FullyQualifiedName~AuthorizationServiceTests" -nr:false -v minimal
```

Expected:

- `ConfigCenter` 控制器和服务仍依赖 `ConfigItemCreateDto / ConfigItemUpdateDto`
- `Authorization` 的 `RoleController / IRoleService / RoleService` 仍依赖 `CreateRoleRequest / UpdateRoleRequest`

- [ ] **Step 3: 用生成输出覆盖单模型 DTO，并同步切换控制器与服务输入**

```csharp
public sealed class ConfigItemDto
{
    public string Id { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string? Value { get; init; }
    public string Type { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
    public string? ModuleId_DisplayValue { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}
```

```csharp
Task<ConfigItemDto> CreateConfigAsync(
    ConfigItemDto dto,
    string? createdBy = null,
    CancellationToken cancellationToken = default)
```

```csharp
[HttpPost]
public Task<ActionResult<RoleDto>> CreateRole(
    [FromBody] RoleDto dto,
    CancellationToken cancellationToken)
```

```csharp
Task<RoleDto> CreateRoleAsync(
    RoleDto dto,
    string? createdBy = null,
    CancellationToken cancellationToken = default)
```

```csharp
var item = new ConfigItem
{
    Key = dto.Key,
    Value = dto.Value,
    Type = dto.Type,
    ModuleId = dto.ModuleId,
    DisplayName = dto.DisplayName
};
```

```text
保留手工 action DTO：
- `AssignRolePermissionsRequest`
- `AssignUserRolesRequest`
```

- [ ] **Step 4: 重新运行 ConfigCenter / Authorization 测试**

Run:

```powershell
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -nr:false -v minimal
```

Expected:

- PASS；`ConfigCenter` 改为同一 DTO 输入/输出
- `Authorization` 现有单 DTO 语义保持可用

- [ ] **Step 5: 提交**

```bash
git add server/modules/ConfigCenter/Controllers/ConfigController.cs server/modules/ConfigCenter/Controllers/ValueListController.cs server/modules/ConfigCenter/DTOs/ConfigItemDto.cs server/modules/ConfigCenter/DTOs/ValueListItemDto.cs server/modules/ConfigCenter/Services/IConfigService.cs server/modules/ConfigCenter/Services/ConfigService.cs server/modules/Authorization/Controllers/RoleController.cs server/modules/Authorization/DTOs/RoleDto.cs server/modules/Authorization/DTOs/PermissionDto.cs server/modules/Authorization/DTOs/UserRoleDto.cs server/modules/Authorization/Services/IRoleService.cs server/modules/Authorization/Services/RoleService.cs tests/ConfigCenter.Tests/ConfigCenterModuleTests.cs tests/Authorization.Tests/AuthorizationServiceTests.cs
git commit -m "重构：ConfigCenter 与 Authorization 切换单模型 DTO"
```

### Task 5: 试点替换 Organization DTO 并保留聚合 DTO 手工维护

**Files:**
- Modify: `server/modules/Organization/DTOs/CompanyDto.cs`
- Modify: `server/modules/Organization/DTOs/DepartmentDto.cs`
- Modify: `server/modules/Organization/DTOs/PersonDto.cs`
- Modify: `server/modules/Organization/DTOs/PositionDto.cs`
- Modify: `server/modules/Organization/DTOs/PersonUserLinkDto.cs`
- Delete: `server/modules/Organization/DTOs/Requests/CreateCompanyRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/UpdateCompanyRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/CreateDepartmentRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/UpdateDepartmentRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/CreatePersonRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/UpdatePersonRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/CreatePositionRequest.cs`
- Delete: `server/modules/Organization/DTOs/Requests/UpdatePositionRequest.cs`
- Modify: `server/modules/Organization/Controllers/CompaniesController.cs`
- Modify: `server/modules/Organization/Controllers/DepartmentsController.cs`
- Modify: `server/modules/Organization/Controllers/PersonsController.cs`
- Modify: `server/modules/Organization/Controllers/PositionsController.cs`
- Modify: `server/modules/Organization/Services/ICompanyService.cs`
- Modify: `server/modules/Organization/Services/CompanyService.cs`
- Modify: `server/modules/Organization/Services/IDepartmentService.cs`
- Modify: `server/modules/Organization/Services/DepartmentService.cs`
- Modify: `server/modules/Organization/Services/IPersonService.cs`
- Modify: `server/modules/Organization/Services/PersonService.cs`
- Modify: `server/modules/Organization/Services/IPositionService.cs`
- Modify: `server/modules/Organization/Services/PositionService.cs`
- Keep: `server/modules/Organization/DTOs/PersonDetailDto.cs`
- Keep: `server/modules/Organization/DTOs/DepartmentTreeNodeDto.cs`
- Keep: `server/modules/Organization/DTOs/IdentityUserOptionDto.cs`
- Modify: `tests/Organization.Tests/OrganizationModuleTests.cs`

- [ ] **Step 1: 写出 `_DisplayValue` 与“聚合 DTO 不自动覆盖”测试**

```csharp
[Fact]
public void PersonDto_ContainsReferenceAndEnumDisplayValueFields()
{
    var dtoType = typeof(PersonDto);

    Assert.NotNull(dtoType.GetProperty("CompanyId_DisplayValue"));
    Assert.NotNull(dtoType.GetProperty("DepartmentId_DisplayValue"));
    Assert.NotNull(dtoType.GetProperty("PositionId_DisplayValue"));
    Assert.NotNull(dtoType.GetProperty("EmploymentStatus_DisplayValue"));
}
```

```csharp
[Fact]
public void PersonDetailDto_RemainsManualAggregateDto()
{
    var dtoType = typeof(PersonDetailDto);
    Assert.NotNull(dtoType.GetProperty("AuthorizationSummary"));
}
```

- [ ] **Step 2: 运行 Organization 测试，确认 DTO 尚未对齐**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Expected:

- `PersonDto` 仍缺少 `*_DisplayValue`
- `Company / Department / Person / Position` 相关 controller 与 service 仍依赖独立 `Create*Request / Update*Request`

- [ ] **Step 3: 覆盖单模型 DTO，并让聚合 DTO 继续手工维护**

```csharp
public sealed class PersonDto
{
    public Guid Id { get; init; }
    public string PersonNo { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Guid CompanyId { get; init; }
    public string? CompanyId_DisplayValue { get; init; }
    public Guid DepartmentId { get; init; }
    public string? DepartmentId_DisplayValue { get; init; }
    public Guid? PositionId { get; init; }
    public string? PositionId_DisplayValue { get; init; }
    public EmploymentStatus EmploymentStatus { get; init; }
    public string? EmploymentStatus_DisplayValue { get; init; }
}
```

```csharp
public sealed class PersonDetailDto
{
    public PersonDto Person { get; init; } = new();
    public PersonUserLinkDto? UserLink { get; init; }
    public IReadOnlyList<string> AuthorizationSummary { get; init; } = [];
}
```

```csharp
public async Task<ActionResult<PersonDto>> Create(
    [FromBody] PersonDto dto,
    CancellationToken cancellationToken = default)
```

```csharp
Task<PersonDto> CreateAsync(PersonDto dto, CancellationToken cancellationToken = default);
Task<PersonDto> UpdateAsync(Guid personId, PersonDto dto, CancellationToken cancellationToken = default);
```

```text
保留手工 action DTO：
- `LinkPersonUserRequest`
- `DepartmentTreeNodeDto`
- `IdentityUserOptionDto`
- `PersonDetailDto`
```

- [ ] **Step 4: 运行 Organization 后端测试并做前端静态构建**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
pnpm --dir .\frontend\packages\shell exec tsc --noEmit
pnpm --dir .\frontend\packages\shell exec vite build
```

Expected:

- Organization 后端测试 PASS
- shell 类型检查和构建通过

- [ ] **Step 5: 提交**

```bash
git add server/modules/Organization/DTOs/CompanyDto.cs server/modules/Organization/DTOs/DepartmentDto.cs server/modules/Organization/DTOs/PersonDto.cs server/modules/Organization/DTOs/PositionDto.cs server/modules/Organization/DTOs/PersonUserLinkDto.cs server/modules/Organization/Controllers/CompaniesController.cs server/modules/Organization/Controllers/DepartmentsController.cs server/modules/Organization/Controllers/PersonsController.cs server/modules/Organization/Controllers/PositionsController.cs server/modules/Organization/Services/ICompanyService.cs server/modules/Organization/Services/CompanyService.cs server/modules/Organization/Services/IDepartmentService.cs server/modules/Organization/Services/DepartmentService.cs server/modules/Organization/Services/IPersonService.cs server/modules/Organization/Services/PersonService.cs server/modules/Organization/Services/IPositionService.cs server/modules/Organization/Services/PositionService.cs tests/Organization.Tests/OrganizationModuleTests.cs
git rm server/modules/Organization/DTOs/Requests/CreateCompanyRequest.cs server/modules/Organization/DTOs/Requests/UpdateCompanyRequest.cs server/modules/Organization/DTOs/Requests/CreateDepartmentRequest.cs server/modules/Organization/DTOs/Requests/UpdateDepartmentRequest.cs server/modules/Organization/DTOs/Requests/CreatePersonRequest.cs server/modules/Organization/DTOs/Requests/UpdatePersonRequest.cs server/modules/Organization/DTOs/Requests/CreatePositionRequest.cs server/modules/Organization/DTOs/Requests/UpdatePositionRequest.cs
git commit -m "重构：Organization 切换单模型 DTO 规则"
```

### Task 6: 文档收口与全链路验证

**Files:**
- Modify: `docs/current-status.md`
- Modify: `docs/designs/database-object-naming-design.md`
- Modify: `docs/modules/configcenter-implementation.md`
- Modify: `docs/modules/authorization-implementation.md`
- Modify: `docs/modules/organization-implementation.md`

- [ ] **Step 1: 写出需要更新的文档断言清单**

```text
1. 命名规范已从设计进入实施状态
2. 首批核心模块表/列命名已切到 PascalCase
3. 单模型 DTO 已统一为“一套 DTO + *_DisplayValue”
4. ConfigCenter / Authorization / Organization 的实施说明不再引用旧 Create/Update DTO
```

- [ ] **Step 2: 更新状态文档与模块实现说明**

```markdown
- `ConfigCenter` / `Authorization` / `Organization` 已统一为 PascalCase 表列命名
- DTO 规则已切换为单模型单 DTO，展示补充字段统一使用 `*_DisplayValue`
- `Audit` / `Files` 继续保持冻结，不纳入本轮 DTO 改造
```

- [ ] **Step 3: 运行完整后端验证与最小 smoke**

Run:

```powershell
dotnet build .\ICP-Platform.sln -nr:false -v minimal
dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj -nr:false -v minimal
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
powershell -NoProfile -File .\tests\integration\persistence\Invoke-PersistenceSmoke.ps1
```

Expected:

- 构建通过
- 建模、持久化、三个核心模块测试通过
- persistence smoke 至少能完成启动、迁移应用与最小查询

- [ ] **Step 4: 提交**

```bash
git add docs/current-status.md docs/designs/database-object-naming-design.md docs/modules/configcenter-implementation.md docs/modules/authorization-implementation.md docs/modules/organization-implementation.md
git commit -m "文档：收口数据库对象命名规范实施说明"
```

## 第二阶段候选批次：补齐剩余元数据 model

本节不并入第一阶段验收，也不和当前命名统一、DTO 生成落地混在同一批提交里。

目标是：在第一阶段稳定后，再把当前仍以手写 `ModelDefinition` 为主的对象，逐步迁到 metadata model 链路。

### Scope

- `Authorization`
  - `Role`
  - `Permission`
  - `RolePermission`
  - `UserRole`
- `Organization`
  - `Company`
  - `Department`
  - `Position`
  - `Person`
  - `PersonUserLink`
- `ConfigCenter`
  - `ValueListItem`

### 前置条件

- 第一阶段已完成并稳定通过：
  - PascalCase 表/列命名
  - `model -> DTO` 单 DTO 生成
  - 三套数据库 migration 与 smoke 验证
- `ConfigCenter` 的 metadata provider 改造已验证可作为模板复用
- 设计文档或增补说明已明确：
  - 哪些对象继续沿用普通字段 `Id / Code / Name / DisplayName`
  - 哪些对象需要引入平台协议字段
  - 元数据 authoring 的源目录、验证入口与回滚方式

### 建议拆分顺序

- 批次 2A：`ValueListItem`
  - 原因：同属 `ConfigCenter`，最容易复用现有 metadata provider 模式
- 批次 2B：`Authorization`
  - 原因：对象数量适中，边界清晰，且和 `Organization` 的引用关系较少
- 批次 2C：`Organization`
  - 原因：引用关系最多，且包含聚合 DTO 与树形结构，适合最后收口

### 每批最小任务

- 为目标模块新增或扩展对应的 metadata provider
- 让 `PlatformPersistenceModelCatalog` 优先从 metadata model 装载目标对象
- 保持现有领域模型、仓储接口与 DTO 契约不因为“改成 metadata model”而额外变形
- 为每个迁移批次补：
  - projector 测试
  - validator 测试
  - 模块持久化测试
  - schema diff / migration 验证

### 每批完成信号

- 目标对象不再依赖手写 `ModelDefinition` 作为主定义入口
- metadata 投影结果与原先手写定义在字段语义上保持等价
- 模块测试、持久化测试、migration smoke 全部通过
- 文档明确记录：
  - 该批次迁了哪些对象
  - 失败信号看哪里
  - 如何回退到手写定义入口

### 非目标

- 不在本阶段顺带做新的业务字段重命名
- 不在本阶段扩大到 `Audit / Files / Demo / ModuleTemplateSample`
- 不把元数据 model 改造和新的业务功能开发混在同一批

## 自检

- 命名规范覆盖：Task 1 + Task 2 覆盖 PascalCase 表/列名、协议字段与迁移
- 单 DTO 规则覆盖：Task 3 + Task 4 + Task 5 覆盖生成器、`*_DisplayValue`、同一 DTO 忽略写入
- 模块范围覆盖：当前仍值得维护的 `ConfigCenter / Authorization / Organization` 全部进入计划；`Audit / Files / Demo / ModuleTemplateSample` 明确不在本轮实施范围
- 元数据 model 路线覆盖：第二阶段候选批次单独列出 `ValueListItem / Authorization / Organization` 的后续迁移顺序与前置条件
- Placeholder scan：本计划未使用占位符写法
- 类型一致性：普通字段示例保持当前模型语义，协议字段仅在设计已声明且模型实际采用处保留，DTO 展示补充统一使用 `*_DisplayValue`

Plan complete and saved to `docs/superpowers/plans/2026-04-17-database-object-naming.md`. Two execution options:

1. Subagent-Driven (recommended) - I dispatch a fresh subagent per task, review between tasks, fast iteration

2. Inline Execution - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?

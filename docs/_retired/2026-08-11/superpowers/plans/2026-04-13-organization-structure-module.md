# Organization Structure Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增首个真实业务模块“组织结构模块”，打通 `公司 / 部门 / 岗位 / 人员 / 人员绑定账号 / 授权摘要只读展示` 的最小闭环，验证平台标准业务模块接入能力。

**Architecture:** 后端新增独立 `Organization` 模块，使用平台现有模块契约、持久化模型定义和 EF 仓储承载组织主数据；前端新增独立 `organization` 模块，以“人员中心”为主入口，同时提供组织树、岗位管理和人员详情工作台。跨模块边界上，账号主数据仍归 `Identity Lite`，授权角色主数据仍归 `Authorization`，组织模块只消费它们的只读查询能力。

**Tech Stack:** .NET 10, ASP.NET Core, EF Core, xUnit, React, TypeScript, pnpm

---

## File Structure

### Backend module files

- Create: `server/modules/Organization/OrganizationModule.csproj`
- Create: `server/modules/Organization/OrganizationModule.cs`
- Create: `server/modules/Organization/README.md`
- Create: `server/modules/Organization/Models/Company.cs`
- Create: `server/modules/Organization/Models/Department.cs`
- Create: `server/modules/Organization/Models/Position.cs`
- Create: `server/modules/Organization/Models/Person.cs`
- Create: `server/modules/Organization/Models/PersonUserLink.cs`
- Create: `server/modules/Organization/Models/EmploymentStatus.cs`
- Create: `server/modules/Organization/DTOs/CompanyDto.cs`
- Create: `server/modules/Organization/DTOs/DepartmentDto.cs`
- Create: `server/modules/Organization/DTOs/DepartmentTreeNodeDto.cs`
- Create: `server/modules/Organization/DTOs/PositionDto.cs`
- Create: `server/modules/Organization/DTOs/PersonDto.cs`
- Create: `server/modules/Organization/DTOs/PersonDetailDto.cs`
- Create: `server/modules/Organization/DTOs/PersonUserLinkDto.cs`
- Create: `server/modules/Organization/DTOs/IdentityUserOptionDto.cs`
- Create: `server/modules/Organization/DTOs/Requests/CreateCompanyRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/UpdateCompanyRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/CreateDepartmentRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/UpdateDepartmentRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/CreatePositionRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/UpdatePositionRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/CreatePersonRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/UpdatePersonRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/LinkPersonUserRequest.cs`
- Create: `server/modules/Organization/Repositories/ICompanyRepository.cs`
- Create: `server/modules/Organization/Repositories/IDepartmentRepository.cs`
- Create: `server/modules/Organization/Repositories/IPositionRepository.cs`
- Create: `server/modules/Organization/Repositories/IPersonRepository.cs`
- Create: `server/modules/Organization/Repositories/IPersonUserLinkRepository.cs`
- Create: `server/modules/Organization/Persistence/Descriptors/OrganizationModelDefinitions.cs`
- Create: `server/modules/Organization/Persistence/Ef/EfCompanyRepository.cs`
- Create: `server/modules/Organization/Persistence/Ef/EfDepartmentRepository.cs`
- Create: `server/modules/Organization/Persistence/Ef/EfPositionRepository.cs`
- Create: `server/modules/Organization/Persistence/Ef/EfPersonRepository.cs`
- Create: `server/modules/Organization/Persistence/Ef/EfPersonUserLinkRepository.cs`
- Create: `server/modules/Organization/Services/ICompanyService.cs`
- Create: `server/modules/Organization/Services/IDepartmentService.cs`
- Create: `server/modules/Organization/Services/IPositionService.cs`
- Create: `server/modules/Organization/Services/IPersonService.cs`
- Create: `server/modules/Organization/Services/IIdentityUserDirectory.cs`
- Create: `server/modules/Organization/Services/IAuthorizationSummaryReader.cs`
- Create: `server/modules/Organization/Services/CompanyService.cs`
- Create: `server/modules/Organization/Services/DepartmentService.cs`
- Create: `server/modules/Organization/Services/PositionService.cs`
- Create: `server/modules/Organization/Services/PersonService.cs`
- Create: `server/modules/Organization/Services/NullAuthorizationSummaryReader.cs`
- Create: `server/modules/Organization/Services/NullIdentityUserDirectory.cs`
- Create: `server/modules/Organization/Controllers/CompaniesController.cs`
- Create: `server/modules/Organization/Controllers/DepartmentsController.cs`
- Create: `server/modules/Organization/Controllers/PositionsController.cs`
- Create: `server/modules/Organization/Controllers/PersonsController.cs`

### Frontend module files

- Create: `frontend/modules/organization/package.json`
- Create: `frontend/modules/organization/tsconfig.json`
- Create: `frontend/modules/organization/src/index.ts`
- Create: `frontend/modules/organization/src/services/organizationService.ts`
- Create: `frontend/modules/organization/src/pages/OrganizationTreePage.tsx`
- Create: `frontend/modules/organization/src/pages/PersonListPage.tsx`
- Create: `frontend/modules/organization/src/pages/PersonDetailPage.tsx`
- Create: `frontend/modules/organization/src/pages/PositionManagementPage.tsx`
- Create: `frontend/modules/organization/src/components/OrganizationTreePanel.tsx`
- Create: `frontend/modules/organization/src/components/PersonEditorForm.tsx`
- Create: `frontend/modules/organization/src/components/PersonUserLinkCard.tsx`
- Create: `frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx`
- Create: `frontend/modules/organization/src/components/BindUserDialog.tsx`

### Integration and docs files

- Create: `tests/Organization.Tests/Organization.Tests.csproj`
- Create: `tests/Organization.Tests/OrganizationModuleTests.cs`
- Create: `tests/Organization.Tests/CompanyServiceTests.cs`
- Create: `tests/Organization.Tests/DepartmentServiceTests.cs`
- Create: `tests/Organization.Tests/PersonServiceTests.cs`
- Create: `tests/Organization.Tests/EfOrganizationRepositoryTests.cs`
- Create: `apps/host-api/modules/Organization/plugin.json`
- Create: `docs/modules/organization-implementation.md`
- Create: `docs/verification/organization-module-verification.md`
- Modify: `ICP-Platform.sln`
- Modify: `apps/host-api/HostApi.csproj`
- Modify: `frontend/packages/shell/src/builtin-runtime-plugins.ts`
- Modify: `docs/current-status.md`

### Responsibility notes

- `OrganizationModule.cs` 只负责模块元数据、权限声明和服务注册。
- `Services/*` 负责业务规则，不把规则散落到 Controller 或页面。
- `Persistence/Descriptors/OrganizationModelDefinitions.cs` 负责把组织模块模型接到平台持久化底座。
- `NullIdentityUserDirectory` 和 `NullAuthorizationSummaryReader` 提供首版可替换的跨模块只读适配点，避免组织模块直接依赖其他模块内部实现。
- 前端页面以“人员中心”为主入口，组件负责复用，不把 API 调用散落在多个页面内。

### Task 1: 搭好模块骨架与接入壳

**Files:**
- Create: `server/modules/Organization/OrganizationModule.csproj`
- Create: `server/modules/Organization/OrganizationModule.cs`
- Create: `server/modules/Organization/README.md`
- Create: `frontend/modules/organization/package.json`
- Create: `frontend/modules/organization/tsconfig.json`
- Create: `frontend/modules/organization/src/index.ts`
- Create: `frontend/modules/organization/src/pages/OrganizationTreePage.tsx`
- Create: `tests/Organization.Tests/Organization.Tests.csproj`
- Create: `tests/Organization.Tests/OrganizationModuleTests.cs`
- Create: `apps/host-api/modules/Organization/plugin.json`
- Modify: `apps/host-api/HostApi.csproj`
- Modify: `frontend/packages/shell/src/builtin-runtime-plugins.ts`
- Modify: `ICP-Platform.sln`

- [ ] **Step 1: 写模块注册 failing test**

```csharp
[Fact]
public void OrganizationModule_ExposesExpectedMetadata()
{
    var module = new OrganizationModule();

    Assert.Equal("Organization", module.ModuleId);
    Assert.Contains(module.GetPermissions(), p => p.Code == "Organization.Person.View");
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter OrganizationModuleTests -nr:false -v minimal`
Expected: FAIL，提示 `OrganizationModule` 或测试项目尚不存在。

- [ ] **Step 3: 创建后端最小模块壳**

```csharp
public class OrganizationModule : ModuleBase, IPermissionProvider
{
    public override string ModuleId => "Organization";
    public override string DisplayName => "组织结构";
    public override string Description => "公司、部门、岗位、人员管理模块";

    public IReadOnlyList<PermissionDescriptor> GetPermissions() => new[]
    {
        new PermissionDescriptor { Code = "Organization.Person.View", DisplayName = "查看人员", ModuleId = ModuleId, Group = "人员" }
    };
}
```

- [ ] **Step 4: 创建前端最小模块壳**

```ts
const organizationModule: FrontendModule = {
  metadata: { moduleId: 'organization', displayName: '组织结构', version: '0.1.0', description: '组织结构管理模块' },
  routes: [{ path: '/organization/tree', component: OrganizationTreePage, requireAuth: true, permissions: ['Organization.Person.View'] }],
  menus: [{ id: 'organization', label: '组织结构', path: '/organization/tree', icon: 'office-building', order: 50, permissions: ['Organization.Person.View'] }],
  permissions: [{ code: 'Organization.Person.View', name: '查看人员', category: '人员' }]
};
```

- [ ] **Step 5: 接入宿主与前端内建模块清单**

```json
{
  "moduleId": "Organization",
  "displayName": "组织结构",
  "version": "1.0.0",
  "description": "开发期内建插件清单",
  "entryAssembly": "../../OrganizationModule.dll",
  "dependencies": [],
  "hostVersion": { "min": "1.0.0", "max": "2.0.0" }
}
```

- [ ] **Step 6: 重新运行模块测试**

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter OrganizationModuleTests -nr:false -v minimal`
Expected: PASS

- [ ] **Step 7: 提交骨架**

```bash
git add server/modules/Organization frontend/modules/organization tests/Organization.Tests apps/host-api/modules/Organization apps/host-api/HostApi.csproj frontend/packages/shell/src/builtin-runtime-plugins.ts ICP-Platform.sln
git commit -m "功能：新增组织结构模块骨架"
```

### Task 2: 落组织主数据模型、持久化定义与服务规则

**Files:**
- Create: `server/modules/Organization/Models/Company.cs`
- Create: `server/modules/Organization/Models/Department.cs`
- Create: `server/modules/Organization/Models/Position.cs`
- Create: `server/modules/Organization/Models/EmploymentStatus.cs`
- Create: `server/modules/Organization/DTOs/CompanyDto.cs`
- Create: `server/modules/Organization/DTOs/DepartmentDto.cs`
- Create: `server/modules/Organization/DTOs/DepartmentTreeNodeDto.cs`
- Create: `server/modules/Organization/DTOs/PositionDto.cs`
- Create: `server/modules/Organization/DTOs/Requests/CreateCompanyRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/UpdateCompanyRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/CreateDepartmentRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/UpdateDepartmentRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/CreatePositionRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/UpdatePositionRequest.cs`
- Create: `server/modules/Organization/Repositories/ICompanyRepository.cs`
- Create: `server/modules/Organization/Repositories/IDepartmentRepository.cs`
- Create: `server/modules/Organization/Repositories/IPositionRepository.cs`
- Create: `server/modules/Organization/Persistence/Descriptors/OrganizationModelDefinitions.cs`
- Create: `server/modules/Organization/Persistence/Ef/EfCompanyRepository.cs`
- Create: `server/modules/Organization/Persistence/Ef/EfDepartmentRepository.cs`
- Create: `server/modules/Organization/Persistence/Ef/EfPositionRepository.cs`
- Create: `server/modules/Organization/Services/ICompanyService.cs`
- Create: `server/modules/Organization/Services/IDepartmentService.cs`
- Create: `server/modules/Organization/Services/IPositionService.cs`
- Create: `server/modules/Organization/Services/CompanyService.cs`
- Create: `server/modules/Organization/Services/DepartmentService.cs`
- Create: `server/modules/Organization/Services/PositionService.cs`
- Create: `tests/Organization.Tests/CompanyServiceTests.cs`
- Create: `tests/Organization.Tests/DepartmentServiceTests.cs`
- Create: `tests/Organization.Tests/EfOrganizationRepositoryTests.cs`
- Modify: `server/modules/Organization/OrganizationModule.cs`

- [ ] **Step 1: 写 Company/Department failing tests**

```csharp
[Fact]
public async Task CreateDepartment_WithCircularParent_ThrowsInvalidOperationException()
{
    var service = BuildDepartmentService();
    var root = await service.CreateAsync(new CreateDepartmentRequest { CompanyId = companyId, Code = "ROOT", Name = "根部门" });

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        service.UpdateAsync(root.Id, new UpdateDepartmentRequest { ParentDepartmentId = root.Id, Name = "根部门" }));
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter "CompanyServiceTests|DepartmentServiceTests" -nr:false -v minimal`
Expected: FAIL，提示模型、仓储或服务尚未实现。

- [ ] **Step 3: 实现主数据模型与 EF 描述**

```csharp
public sealed class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public Guid? ParentDepartmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
}
```

- [ ] **Step 4: 实现服务层最小规则**

```csharp
if (request.ParentDepartmentId == departmentId)
{
    throw new InvalidOperationException("部门不能指向自己作为上级。");
}
if (await _departmentRepository.ExistsSiblingCodeAsync(request.CompanyId, request.ParentDepartmentId, request.Code, departmentId))
{
    throw new InvalidOperationException("同一父节点下部门编码必须唯一。");
}
```

- [ ] **Step 5: 接入模块服务注册**

```csharp
services.AddScoped<ICompanyRepository, EfCompanyRepository>();
services.AddScoped<IDepartmentRepository, EfDepartmentRepository>();
services.AddScoped<IPositionRepository, EfPositionRepository>();
services.AddScoped<ICompanyService, CompanyService>();
services.AddScoped<IDepartmentService, DepartmentService>();
services.AddScoped<IPositionService, PositionService>();
```

- [ ] **Step 6: 运行测试验证通过**

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter "CompanyServiceTests|DepartmentServiceTests|EfOrganizationRepositoryTests" -nr:false -v minimal`
Expected: PASS

- [ ] **Step 7: 提交组织主数据底座**

```bash
git add server/modules/Organization tests/Organization.Tests
git commit -m "功能：新增组织主数据模型与服务规则"
```

### Task 3: 落人员模型与账号绑定规则

**Files:**
- Create: `server/modules/Organization/Models/Person.cs`
- Create: `server/modules/Organization/Models/PersonUserLink.cs`
- Create: `server/modules/Organization/DTOs/PersonDto.cs`
- Create: `server/modules/Organization/DTOs/PersonDetailDto.cs`
- Create: `server/modules/Organization/DTOs/PersonUserLinkDto.cs`
- Create: `server/modules/Organization/DTOs/IdentityUserOptionDto.cs`
- Create: `server/modules/Organization/DTOs/Requests/CreatePersonRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/UpdatePersonRequest.cs`
- Create: `server/modules/Organization/DTOs/Requests/LinkPersonUserRequest.cs`
- Create: `server/modules/Organization/Repositories/IPersonRepository.cs`
- Create: `server/modules/Organization/Repositories/IPersonUserLinkRepository.cs`
- Create: `server/modules/Organization/Persistence/Ef/EfPersonRepository.cs`
- Create: `server/modules/Organization/Persistence/Ef/EfPersonUserLinkRepository.cs`
- Create: `server/modules/Organization/Services/IPersonService.cs`
- Create: `server/modules/Organization/Services/IIdentityUserDirectory.cs`
- Create: `server/modules/Organization/Services/PersonService.cs`
- Create: `server/modules/Organization/Services/NullIdentityUserDirectory.cs`
- Create: `tests/Organization.Tests/PersonServiceTests.cs`
- Modify: `server/modules/Organization/Persistence/Descriptors/OrganizationModelDefinitions.cs`
- Modify: `server/modules/Organization/OrganizationModule.cs`

- [ ] **Step 1: 写人员与账号绑定 failing tests**

```csharp
[Fact]
public async Task LinkUser_WhenUserAlreadyBound_ThrowsInvalidOperationException()
{
    var service = BuildPersonService();
    var personA = await service.CreateAsync(new CreatePersonRequest { PersonNo = "P001", Name = "张三", CompanyId = companyId, DepartmentId = departmentId });
    var personB = await service.CreateAsync(new CreatePersonRequest { PersonNo = "P002", Name = "李四", CompanyId = companyId, DepartmentId = departmentId });

    await service.LinkUserAsync(personA.Id, new LinkPersonUserRequest { UserId = "user-001" });

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        service.LinkUserAsync(personB.Id, new LinkPersonUserRequest { UserId = "user-001" }));
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter PersonServiceTests -nr:false -v minimal`
Expected: FAIL，提示 `PersonService` 或 `LinkUserAsync` 尚未实现。

- [ ] **Step 3: 实现人员模型与绑定模型**

```csharp
public sealed class PersonUserLink
{
    public Guid PersonId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset LinkedAt { get; set; }
    public string LinkedBy { get; set; } = "system";
}
```

- [ ] **Step 4: 实现账号绑定最小规则**

```csharp
if (await _personUserLinkRepository.ExistsByUserIdAsync(request.UserId, personId))
{
    throw new InvalidOperationException("该账号已绑定其他人员。");
}

await _identityUserDirectory.EnsureUserExistsAsync(request.UserId);
await _personUserLinkRepository.UpsertAsync(new PersonUserLink { PersonId = personId, UserId = request.UserId, LinkedAt = _clock.UtcNow });
```

- [ ] **Step 5: 注册跨模块只读适配点默认实现**

```csharp
services.AddScoped<IIdentityUserDirectory, NullIdentityUserDirectory>();
services.AddScoped<IPersonService, PersonService>();
```

- [ ] **Step 6: 运行测试验证通过**

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter "PersonServiceTests|EfOrganizationRepositoryTests" -nr:false -v minimal`
Expected: PASS

- [ ] **Step 7: 提交人员与账号绑定规则**

```bash
git add server/modules/Organization tests/Organization.Tests
git commit -m "功能：新增人员管理与账号绑定规则"
```

### Task 4: 暴露组织 API 并接好授权摘要读取点

**Files:**
- Create: `server/modules/Organization/Services/IAuthorizationSummaryReader.cs`
- Create: `server/modules/Organization/Services/NullAuthorizationSummaryReader.cs`
- Create: `server/modules/Organization/Controllers/CompaniesController.cs`
- Create: `server/modules/Organization/Controllers/DepartmentsController.cs`
- Create: `server/modules/Organization/Controllers/PositionsController.cs`
- Create: `server/modules/Organization/Controllers/PersonsController.cs`
- Modify: `server/modules/Organization/Services/PersonService.cs`
- Modify: `server/modules/Organization/OrganizationModule.cs`
- Modify: `tests/Organization.Tests/OrganizationModuleTests.cs`

- [ ] **Step 1: 写 PersonDetail API failing test**

```csharp
[Fact]
public async Task GetPersonDetail_WhenUserBound_ReturnsAuthorizationSummary()
{
    var service = BuildPersonService(new FakeAuthorizationSummaryReader("平台管理员"));
    var person = await service.CreateAsync(new CreatePersonRequest { PersonNo = "P100", Name = "王五", CompanyId = companyId, DepartmentId = departmentId });
    await service.LinkUserAsync(person.Id, new LinkPersonUserRequest { UserId = "admin" });

    var detail = await service.GetDetailAsync(person.Id);

    Assert.Contains("平台管理员", detail.AuthorizationSummary);
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter "PersonServiceTests|OrganizationModuleTests" -nr:false -v minimal`
Expected: FAIL，提示详情模型或授权摘要读取尚未实现。

- [ ] **Step 3: 实现只读授权摘要接口与详情组装**

```csharp
public interface IAuthorizationSummaryReader
{
    Task<IReadOnlyList<string>> GetRoleNamesAsync(string userId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: 暴露 REST 接口**

```csharp
[ApiController]
[Route("api/organization/persons")]
public sealed class PersonsController : ControllerBase
{
    [HttpGet("{id:guid}")]
    public Task<PersonDetailDto> Get(Guid id, CancellationToken cancellationToken) =>
        _personService.GetDetailAsync(id, cancellationToken);
}
```

- [ ] **Step 5: 运行测试验证通过**

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter "OrganizationModuleTests|PersonServiceTests" -nr:false -v minimal`
Expected: PASS

- [ ] **Step 6: 构建宿主确认模块能装载**

Run: `dotnet build apps/host-api/HostApi.csproj -v minimal`
Expected: PASS

- [ ] **Step 7: 提交 API 与摘要读取点**

```bash
git add server/modules/Organization tests/Organization.Tests apps/host-api/HostApi.csproj
git commit -m "功能：新增组织结构模块 API 与授权摘要读取"
```

### Task 5: 实现前端组织树、人员列表与人员详情工作台

**Files:**
- Create: `frontend/modules/organization/src/services/organizationService.ts`
- Create: `frontend/modules/organization/src/pages/OrganizationTreePage.tsx`
- Create: `frontend/modules/organization/src/pages/PersonListPage.tsx`
- Create: `frontend/modules/organization/src/pages/PersonDetailPage.tsx`
- Create: `frontend/modules/organization/src/pages/PositionManagementPage.tsx`
- Create: `frontend/modules/organization/src/components/OrganizationTreePanel.tsx`
- Create: `frontend/modules/organization/src/components/PersonEditorForm.tsx`
- Create: `frontend/modules/organization/src/components/PersonUserLinkCard.tsx`
- Create: `frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx`
- Modify: `frontend/modules/organization/src/index.ts`

- [ ] **Step 1: 写前端 smoke 清单**

在计划执行记录中固定 3 个页面 smoke：

```text
1. 打开 /organization/tree 能看到组织树标题与公司/部门容器
2. 打开 /organization/persons 能看到人员列表与筛选区
3. 打开 /organization/persons/:id 能看到人员基础信息、账号绑定卡片和授权摘要卡片
```

- [ ] **Step 2: 实现 API 服务层**

```ts
export async function getPersonDetail(id: string): Promise<PersonDetailDto> {
  const response = await fetch(`/api/organization/persons/${id}`);
  if (!response.ok) throw new Error('加载人员详情失败');
  return response.json();
}
```

- [ ] **Step 3: 实现模块注册**

```ts
routes: [
  { path: '/organization/tree', component: OrganizationTreePage, requireAuth: true, permissions: ['Organization.Department.View'] },
  { path: '/organization/persons', component: PersonListPage, requireAuth: true, permissions: ['Organization.Person.View'] },
  { path: '/organization/persons/:id', component: PersonDetailPage, requireAuth: true, permissions: ['Organization.Person.View'] }
]
```

- [ ] **Step 4: 实现人员详情工作台**

```tsx
return (
  <section>
    <h1>人员详情</h1>
    <PersonEditorForm person={detail} />
    <PersonUserLinkCard link={detail.userLink} />
    <AuthorizationSummaryCard items={detail.authorizationSummary} />
  </section>
);
```

- [ ] **Step 5: 运行前端模块构建**

Run: `pnpm --dir frontend --filter @icp-platform/organization build`
Expected: PASS

- [ ] **Step 6: 运行前端全量构建**

Run: `pnpm --dir frontend build`
Expected: PASS

- [ ] **Step 7: 提交前端工作台**

```bash
git add frontend/modules/organization
git commit -m "功能：新增组织结构前端工作台"
```

### Task 6: 落绑定账号对话框与跨模块只读联通

**Files:**
- Create: `frontend/modules/organization/src/components/BindUserDialog.tsx`
- Modify: `frontend/modules/organization/src/components/PersonUserLinkCard.tsx`
- Modify: `frontend/modules/organization/src/services/organizationService.ts`
- Modify: `frontend/modules/organization/src/pages/PersonDetailPage.tsx`
- Modify: `server/modules/Organization/Services/IIdentityUserDirectory.cs`
- Modify: `server/modules/Organization/Services/NullIdentityUserDirectory.cs`
- Modify: `server/modules/Organization/Controllers/PersonsController.cs`
- Modify: `tests/Organization.Tests/PersonServiceTests.cs`

- [ ] **Step 1: 写绑定账号 UI/API smoke**

```text
1. 打开人员详情页
2. 点击“绑定账号”
3. 看到账号选择对话框
4. 选择已有账号后提交
5. 页面刷新显示“已绑定账号”
```

- [ ] **Step 2: 扩充后端查询接口**

```csharp
public interface IIdentityUserDirectory
{
    Task EnsureUserExistsAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IdentityUserOptionDto>> SearchAsync(string keyword, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: 实现绑定账号对话框**

```tsx
<dialog open={open}>
  <h2>绑定账号</h2>
  <input value={keyword} onChange={e => setKeyword(e.target.value)} />
  <button onClick={() => onConfirm(selectedUserId)}>确认绑定</button>
</dialog>
```

- [ ] **Step 4: 将绑定动作接到详情页**

```ts
export async function linkPersonUser(personId: string, userId: string) {
  await fetch(`/api/organization/persons/${personId}/user-link`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ userId })
  });
}
```

- [ ] **Step 5: 运行后端与前端定向验证**

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj --filter PersonServiceTests -nr:false -v minimal`
Expected: PASS

Run: `pnpm --dir frontend --filter @icp-platform/organization build`
Expected: PASS

- [ ] **Step 6: 提交绑定账号联通**

```bash
git add server/modules/Organization frontend/modules/organization tests/Organization.Tests
git commit -m "功能：打通组织结构模块绑定账号联通"
```

### Task 7: 收口验证文档、实现说明与状态更新

**Files:**
- Create: `docs/modules/organization-implementation.md`
- Create: `docs/verification/organization-module-verification.md`
- Modify: `docs/current-status.md`
- Modify: `docs/designs/organization-structure-design.md`

- [ ] **Step 1: 写实现说明初稿**

```md
## 实现范围
- 公司、部门、岗位、人员
- 人员绑定账号
- 授权摘要只读展示
```

- [ ] **Step 2: 写验证记录**

```md
## 验证命令
- dotnet test tests/Organization.Tests/Organization.Tests.csproj
- dotnet build apps/host-api/HostApi.csproj
- pnpm --dir frontend --filter @icp-platform/organization build
- pnpm --dir frontend build
```

- [ ] **Step 3: 更新当前状态文档**

```md
### 组织结构模块
状态：已完成首版闭环
已落地：组织主数据、人员中心、账号绑定、授权摘要只读展示
```

- [ ] **Step 4: 运行最终验证**

Run: `dotnet test tests/Organization.Tests/Organization.Tests.csproj -nr:false -v minimal`
Expected: PASS

Run: `dotnet build apps/host-api/HostApi.csproj -v minimal`
Expected: PASS

Run: `pnpm --dir frontend build`
Expected: PASS

- [ ] **Step 5: 提交文档收口**

```bash
git add docs/modules/organization-implementation.md docs/verification/organization-module-verification.md docs/current-status.md docs/designs/organization-structure-design.md
git commit -m "文档：收口组织结构模块实现与验证记录"
```

## Self-Review Checklist

- 规格覆盖：
  - `公司 / 部门 / 岗位 / 人员`：Task 2、Task 3
  - `人员中心`：Task 5
  - `人员 - 用户账号` 关联：Task 3、Task 6
  - `授权摘要只读展示`：Task 4、Task 5
  - 模块接入闭环与验证：Task 1、Task 7
- 占位词扫描：未发现需要后补说明的占位内容。
- 类型一致性：
  - 后端统一使用 `Organization` 模块名、`PersonUserLink` 绑定模型、`IIdentityUserDirectory` 与 `IAuthorizationSummaryReader` 作为跨模块只读接口。
  - 账号查询结果统一使用 `IdentityUserOptionDto`，避免在服务层和前端层重复定义搜索结果模型。
  - 前端统一使用 `organization` 模块 id 与 `/organization/*` 路由前缀。

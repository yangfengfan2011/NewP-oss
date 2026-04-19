# 组织模块人员中心试点 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 打通组织模块“人员中心”首轮最小闭环，让该试点真实验证 `nav / view / model` 显示层主线、标准 CRUD 主线以及多语言消费规则，而不把删除、复杂摘要交互、权限与规则中心混入首轮范围。

**Architecture:** 首轮试点以“导航入口 + 人员列表 + 人员详情 + 编辑 + 停用 + 只读摘要”作为唯一闭环。后端优先补齐 `persons list / detail / create / update / disable / options` 的最小契约与 `summaryBlocks` 状态化读取模型；前端保留现有 route 真源，在现有页面骨架上收口列表、详情和摘要呈现；metadata 侧只新增组织模块必需的 `nav / view / model / dic` 文件，不扩展到 `tree view`、`route metadata` 或删除主链路。

**Tech Stack:** .NET 10 / ASP.NET Core / C# / xUnit / React 18 / TypeScript / Vite / 组织模块现有前端包构建

**Preconditions:** 本计划默认 `.dic / .model / .settings` 的 metadata consumer alignment 已完成或已具备可用实现，当前试点只消费其结果，不在本计划内重复实现相关底层收口。

---

## 文件结构与职责

**Create:**

- `modules/Organization/metadata/navigation/Organization.nav.json`  
  组织模块左侧导航 authoring 真源，首轮只承接“组织结构”容器与“人员中心”入口。
- `modules/Organization/metadata/views/PersonList.view.json`  
  人员列表页 authoring 真源，收口 query、advancedFilter、columns、export、pageActions、rowActions。
- `modules/Organization/metadata/views/PersonDetail.view.json`  
  人员详情页 authoring 真源，收口 sections、summaryBlocks、pageActions。
- `modules/Organization/metadata/models/Person.model.json`  
  人员模型 authoring 真源，提供 `create / edit / view` 字段静态语义与 section 布局。
- `modules/Organization/metadata/dictionaries/PersonListView.dic.json`  
  人员列表页文案。
- `modules/Organization/metadata/dictionaries/PersonDetailView.dic.json`  
  人员详情页文案。
- `tests/Organization.Tests/PersonListContractTests.cs`  
  persons list / disable / detail summary 状态化的后端定向测试。

**Modify:**

- `server/modules/Organization/Controllers/PersonsController.cs`
- `server/modules/Organization/DTOs/PersonDetailDto.cs`
- `server/modules/Organization/DTOs/PersonDto.cs`
- `server/modules/Organization/Services/IPersonService.cs`
- `server/modules/Organization/Services/PersonService.cs`
- `server/modules/Organization/Repositories/IPersonRepository.cs`
- `server/modules/Organization/Persistence/Ef/EfPersonRepository.cs`
- `tests/Organization.Tests/PersonServiceTests.cs`
- `frontend/modules/organization/src/services/organizationService.ts`
- `frontend/modules/organization/src/pages/PersonListPage.tsx`
- `frontend/modules/organization/src/pages/PersonDetailPage.tsx`
- `frontend/modules/organization/src/components/PersonEditorForm.tsx`
- `frontend/modules/organization/src/components/PersonUserLinkCard.tsx`
- `frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx`
- `frontend/modules/organization/src/index.ts`
- `docs/designs/modules/organization-person-center-pilot-design.md`
  仅在实现冻结结论与设计文本不一致时做最小回填。

**Out of Scope in this plan:**

- `frontend/modules/organization/src/components/BindUserDialog.tsx`
- `frontend/modules/organization/src/pages/OrganizationTreePage.tsx`
- `frontend/modules/organization/src/pages/PositionManagementPage.tsx`
- 删除主链路
- 权限、规则中心、`tree view`、`route metadata`

---

### Task 1: 后端补齐 persons list / disable / detail summary 状态化主干

**Files:**
- Create: `tests/Organization.Tests/PersonListContractTests.cs`
- Modify: `server/modules/Organization/Controllers/PersonsController.cs`
- Modify: `server/modules/Organization/Services/IPersonService.cs`
- Modify: `server/modules/Organization/Services/PersonService.cs`
- Modify: `server/modules/Organization/Repositories/IPersonRepository.cs`
- Modify: `server/modules/Organization/Persistence/Ef/EfPersonRepository.cs`
- Modify: `server/modules/Organization/DTOs/PersonDetailDto.cs`
- Test: `tests/Organization.Tests/PersonServiceTests.cs`

- [ ] **Step 1: 先为 persons list 写 failing test**

```csharp
[Fact]
public async Task GetListAsync_WithKeywordAndCompanyId_ReturnsProjectedRows()
{
    var service = BuildServiceWithPersons(
        new Person
        {
            PersonNo = "P001",
            Name = "张三",
            CompanyId = CompanyId,
            DepartmentId = DepartmentId,
            EmploymentStatus = EmploymentStatus.FullTime,
            IsEnabled = true
        },
        new Person
        {
            PersonNo = "P002",
            Name = "李四",
            CompanyId = CompanyId2,
            DepartmentId = DepartmentId,
            EmploymentStatus = EmploymentStatus.Contractor,
            IsEnabled = false
        });

    var result = await service.GetListAsync(
        keyword: "张",
        companyId: CompanyId,
        departmentId: null,
        employmentStatus: null,
        cancellationToken: default);

    Assert.Single(result.Items);
    Assert.Equal("P001", result.Items[0].PersonNo);
}
```

- [ ] **Step 2: 为 detail summary 状态化结构写 failing test**

```csharp
[Fact]
public async Task GetDetailAsync_WhenUserNotBound_ReturnsNotConfiguredSummaryState()
{
    var service = BuildService();
    var created = await service.CreateAsync(new PersonDto
    {
        PersonNo = "P010",
        Name = "未绑定人员",
        CompanyId = CompanyId,
        DepartmentId = DepartmentId
    });

    var detail = await service.GetDetailAsync(created.Id);

    Assert.NotNull(detail);
    Assert.Equal("notConfigured", detail!.UserLinkSummary.Status);
    Assert.Equal("notConfigured", detail.AuthorizationSummary.Status);
}
```

- [ ] **Step 3: 为停用动作写 failing test**

```csharp
[Fact]
public async Task DisableAsync_WhenPersonExists_SetsIsEnabledFalse()
{
    var service = BuildService();
    var created = await service.CreateAsync(new PersonDto
    {
        PersonNo = "P011",
        Name = "待停用人员",
        CompanyId = CompanyId,
        DepartmentId = DepartmentId,
        IsEnabled = true
    });

    var detail = await service.DisableAsync(created.Id);

    Assert.False(detail.IsEnabled);
}
```

- [ ] **Step 4: 跑定向测试，确认当前失败**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "FullyQualifiedName~PersonListContractTests|FullyQualifiedName~PersonServiceTests" -nr:false -v minimal
```

Expected:

```text
FAIL
... IPersonService does not contain a definition for GetListAsync / DisableAsync ...
```

- [ ] **Step 5: 为后端读模型补最小 DTO 结构**

```csharp
public sealed record PersonSummaryBlockDto
{
    public required string Status { get; init; }
    public object? Data { get; init; }
}

public sealed record PersonListItemDto
{
    public required Guid Id { get; init; }
    public required string PersonNo { get; init; }
    public required string Name { get; init; }
    public required string CompanyName { get; init; }
    public required string DepartmentName { get; init; }
    public required EmploymentStatus EmploymentStatus { get; init; }
    public required bool IsEnabled { get; init; }
}
```

- [ ] **Step 6: 在 `IPersonService` 中补 list / disable 契约**

```csharp
Task<PersonListResultDto> GetListAsync(
    string? keyword,
    Guid? companyId,
    Guid? departmentId,
    EmploymentStatus? employmentStatus,
    CancellationToken cancellationToken = default);

Task<PersonDetailDto> DisableAsync(Guid personId, CancellationToken cancellationToken = default);
```

- [ ] **Step 7: 在 `PersonService` 中实现最小主干**

```csharp
public async Task<PersonListResultDto> GetListAsync(
    string? keyword,
    Guid? companyId,
    Guid? departmentId,
    EmploymentStatus? employmentStatus,
    CancellationToken cancellationToken = default)
{
    var items = await _personRepository.GetListAsync(keyword, companyId, departmentId, employmentStatus, cancellationToken);
    return new PersonListResultDto
    {
        Items = items.Select(MapToListItem).ToList(),
        Total = items.Count
    };
}

public async Task<PersonDetailDto> DisableAsync(Guid personId, CancellationToken cancellationToken = default)
{
    var person = await _personRepository.GetByIdAsync(personId, cancellationToken)
        ?? throw new InvalidOperationException($"人员 '{personId}' 不存在。");

    person.IsEnabled = false;
    person.UpdatedAt = DateTime.UtcNow;
    await _personRepository.UpdateAsync(person, cancellationToken);

    return (await GetDetailAsync(personId, cancellationToken))!;
}
```

- [ ] **Step 8: 把 detail summary 收口为状态化结构**

```csharp
private static PersonSummaryBlockDto BuildUserLinkSummary(PersonUserLink? link)
{
    return link is null
        ? new PersonSummaryBlockDto { Status = "notConfigured", Data = null }
        : new PersonSummaryBlockDto
        {
            Status = "ready",
            Data = new
            {
                link.UserId,
                link.LinkedAt,
                link.LinkedBy
            }
        };
}
```

- [ ] **Step 9: 在 controller 中补 list / disable 入口**

```csharp
[HttpGet]
public async Task<ActionResult<PersonListResultDto>> GetList(
    [FromQuery] string? keyword,
    [FromQuery] Guid? companyId,
    [FromQuery] Guid? departmentId,
    [FromQuery] EmploymentStatus? employmentStatus,
    CancellationToken cancellationToken = default)
{
    return Ok(await _personService.GetListAsync(keyword, companyId, departmentId, employmentStatus, cancellationToken));
}

[HttpPost("{id:guid}/disable")]
public async Task<ActionResult<PersonDetailDto>> Disable(Guid id, CancellationToken cancellationToken = default)
{
    try
    {
        return Ok(await _personService.DisableAsync(id, cancellationToken));
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(new { message = ex.Message });
    }
}
```

- [ ] **Step 10: 重新运行后端定向测试，确认通过**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter "FullyQualifiedName~PersonListContractTests|FullyQualifiedName~PersonServiceTests" -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 11: 提交**

```bash
git add tests/Organization.Tests/PersonListContractTests.cs tests/Organization.Tests/PersonServiceTests.cs server/modules/Organization/Controllers/PersonsController.cs server/modules/Organization/Services/IPersonService.cs server/modules/Organization/Services/PersonService.cs server/modules/Organization/Repositories/IPersonRepository.cs server/modules/Organization/Persistence/Ef/EfPersonRepository.cs server/modules/Organization/DTOs/PersonDetailDto.cs
git commit -m "功能：补齐人员中心后端列表与停用主干"
```

### Task 2: 新增组织模块 metadata 与 dic authoring 真源

**Files:**
- Create: `modules/Organization/metadata/navigation/Organization.nav.json`
- Create: `modules/Organization/metadata/views/PersonList.view.json`
- Create: `modules/Organization/metadata/views/PersonDetail.view.json`
- Create: `modules/Organization/metadata/models/Person.model.json`
- Create: `modules/Organization/metadata/dictionaries/PersonListView.dic.json`
- Create: `modules/Organization/metadata/dictionaries/PersonDetailView.dic.json`

- [ ] **Step 1: 先为 metadata 文件结构写最小 smoke 断言**

```csharp
[Fact]
public void OrganizationMetadataFiles_UseExpectedTypeHeaders()
{
    var root = FindRepositoryRoot();
    Assert.Contains("\"type\": \"nav\"", File.ReadAllText(Path.Combine(root, "modules", "Organization", "metadata", "navigation", "Organization.nav.json")));
    Assert.Contains("\"type\": \"view\"", File.ReadAllText(Path.Combine(root, "modules", "Organization", "metadata", "views", "PersonList.view.json")));
    Assert.Contains("\"type\": \"model\"", File.ReadAllText(Path.Combine(root, "modules", "Organization", "metadata", "models", "Person.model.json")));
    Assert.Contains("\"type\": \"dic\"", File.ReadAllText(Path.Combine(root, "modules", "Organization", "metadata", "dictionaries", "PersonListView.dic.json")));
}
```

- [ ] **Step 2: 跑组织模块定向测试，确认当前失败**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter FullyQualifiedName~OrganizationMetadataFiles -nr:false -v minimal
```

Expected:

```text
FAIL
... Could not find file ...
```

- [ ] **Step 3: 新增导航 metadata**

```json
{
  "schemaVersion": "1.0",
  "type": "nav",
  "navigation": {
    "items": [
      { "key": "organization", "titleKey": "nav.organization", "icon": "building", "order": 150, "path": "" },
      { "key": "organization.personCenter", "titleKey": "nav.organization_personCenter", "icon": "users", "parentKey": "organization", "order": 160, "path": "/organization/persons" }
    ]
  }
}
```

- [ ] **Step 4: 新增列表 view metadata**

```json
{
  "schemaVersion": "1.0",
  "type": "view",
  "view": {
    "key": "organization.person.list",
    "dicKey": "PersonListView",
    "titleKey": "Title",
    "descriptionKey": "Description",
    "mode": "list",
    "model": "Person",
    "editorMode": "page",
    "detailViewKey": "organization.person.detail",
    "query": {
      "fields": [
        { "key": "keyword", "labelKey": "Query_Keyword", "source": "preset", "preset": "keyword", "operator": "like" },
        { "key": "companyId", "labelKey": "Query_Company", "source": "model", "field": "companyId", "operator": "=" },
        { "key": "departmentId", "labelKey": "Query_Department", "source": "model", "field": "departmentId", "operator": "=" },
        { "key": "employmentStatus", "labelKey": "Query_EmploymentStatus", "source": "model", "field": "employmentStatus", "operator": "=" }
      ]
    }
  }
}
```

- [ ] **Step 5: 新增详情 view metadata**

```json
{
  "schemaVersion": "1.0",
  "type": "view",
  "view": {
    "key": "organization.person.detail",
    "dicKey": "PersonDetailView",
    "titleKey": "Title",
    "descriptionKey": "Description",
    "mode": "detail",
    "model": "Person",
    "sections": ["basic", "organization"],
    "summaryBlocks": ["userLink", "authorizationSummary"],
    "pageActions": [
      { "key": "edit", "labelKey": "Action_Edit" },
      { "key": "disable", "labelKey": "Action_Disable", "confirmKey": "Confirm_Disable" }
    ]
  }
}
```

- [ ] **Step 6: 新增 Person model metadata 与两份 dic 文件**

```json
{
  "schemaVersion": "1.0",
  "type": "model",
  "Person": {
    "kind": "Entity",
    "sections": [
      { "key": "basic", "titleKey": "Section_Basic" },
      { "key": "organization", "titleKey": "Section_Organization" }
    ],
    "fields": []
  }
}
```

- [ ] **Step 7: 重新运行 metadata smoke**

Run:

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter FullyQualifiedName~OrganizationMetadataFiles -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 8: 提交**

```bash
git add modules/Organization/metadata/navigation/Organization.nav.json modules/Organization/metadata/views/PersonList.view.json modules/Organization/metadata/views/PersonDetail.view.json modules/Organization/metadata/models/Person.model.json modules/Organization/metadata/dictionaries/PersonListView.dic.json modules/Organization/metadata/dictionaries/PersonDetailView.dic.json tests/Organization.Tests/OrganizationModuleTests.cs
git commit -m "设计：新增组织模块人员中心元数据真源"
```

### Task 3: 前端收口人员列表页与详情页最小闭环

**Files:**
- Modify: `frontend/modules/organization/src/services/organizationService.ts`
- Modify: `frontend/modules/organization/src/pages/PersonListPage.tsx`
- Modify: `frontend/modules/organization/src/pages/PersonDetailPage.tsx`
- Modify: `frontend/modules/organization/src/components/PersonEditorForm.tsx`
- Modify: `frontend/modules/organization/src/components/PersonUserLinkCard.tsx`
- Modify: `frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx`
- Modify: `frontend/modules/organization/src/index.ts`

- [ ] **Step 1: 先为前端服务层补最小类型与请求拼装草图**

```ts
export interface PersonListItemDto {
  id: string;
  personNo: string;
  name: string;
  companyName: string;
  departmentName: string;
  employmentStatus: string;
  isEnabled: boolean;
}

export async function getPersons(query: {
  keyword?: string;
  companyId?: string;
  departmentId?: string;
  employmentStatus?: string;
}): Promise<{ items: PersonListItemDto[]; total: number }> {
  const search = new URLSearchParams();
  if (query.keyword) search.set('keyword', query.keyword);
  if (query.companyId) search.set('companyId', query.companyId);
  if (query.departmentId) search.set('departmentId', query.departmentId);
  if (query.employmentStatus) search.set('employmentStatus', query.employmentStatus);

  return fetchJson(`/api/organization/persons?${search.toString()}`, '加载人员列表失败');
}
```

- [ ] **Step 2: 运行前端模块构建，确认当前缺口真实暴露在编译阶段**

Run:

```powershell
pnpm --dir .\frontend\modules\organization build
```

Expected:

```text
FAIL
... PersonListPage / PersonDetailPage 引用的 persons 服务能力尚未补齐 ...
```

- [ ] **Step 3: 在服务层补 persons list / disable / summary 状态 DTO**

```ts
export interface PersonSummaryBlockDto {
  status: 'ready' | 'notConfigured' | 'dependencyUnavailable';
  data: unknown | null;
}

export interface PersonListItemDto {
  id: string;
  personNo: string;
  name: string;
  companyName: string;
  departmentName: string;
  employmentStatus: string;
  isEnabled: boolean;
}

export async function getPersons(query: {
  keyword?: string;
  companyId?: string;
  departmentId?: string;
  employmentStatus?: string;
}): Promise<{ items: PersonListItemDto[]; total: number }> {
  const search = new URLSearchParams();
  if (query.keyword) search.set('keyword', query.keyword);
  if (query.companyId) search.set('companyId', query.companyId);
  if (query.departmentId) search.set('departmentId', query.departmentId);
  if (query.employmentStatus) search.set('employmentStatus', query.employmentStatus);

  return fetchJson(`/api/organization/persons?${search.toString()}`, '加载人员列表失败');
}
```

- [ ] **Step 4: 把列表页从空容器收口为真实列表**

```tsx
const [rows, setRows] = useState<PersonListItemDto[]>([]);
const [loading, setLoading] = useState(false);

useEffect(() => {
  let cancelled = false;
  setLoading(true);
  void getPersons({ keyword, companyId }).then((result) => {
    if (!cancelled) {
      setRows(result.items);
      setLoading(false);
    }
  });
  return () => { cancelled = true; };
}, [keyword, companyId]);
```

- [ ] **Step 5: 把详情页与三个详情组件切到状态化 summary 承接**

```tsx
<PersonUserLinkCard summary={detail.userLinkSummary} onBind={undefined} />
<AuthorizationSummaryCard summary={detail.authorizationSummary} />
```

```tsx
if (summary.status !== 'ready') {
  return <EmptyState message="当前账号绑定摘要未配置或依赖暂不可用。" />;
}
```

- [ ] **Step 6: 在模块入口中保留现有 route 真源，不新增 route metadata 依赖**

```ts
routes: [
  { path: '/organization/persons', component: PersonListPage, requireAuth: true, permissions: ['Organization.Person.View'] },
  { path: '/organization/persons/:id', component: PersonDetailPage, requireAuth: true, permissions: ['Organization.Person.View'] }
]
```

- [ ] **Step 7: 运行前端模块构建**

Run:

```powershell
pnpm --dir .\frontend\modules\organization build
```

Expected:

```text
tsc completed successfully
```

- [ ] **Step 8: 提交**

```bash
git add frontend/modules/organization/src/services/organizationService.ts frontend/modules/organization/src/pages/PersonListPage.tsx frontend/modules/organization/src/pages/PersonDetailPage.tsx frontend/modules/organization/src/components/PersonEditorForm.tsx frontend/modules/organization/src/components/PersonUserLinkCard.tsx frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx frontend/modules/organization/src/index.ts
git commit -m "功能：收口人员中心前端列表与详情闭环"
```

### Task 4: 试点设计回填与最小联调验证

**Files:**
- Modify: `docs/designs/modules/organization-person-center-pilot-design.md`

- [ ] **Step 1: 如实现与冻结结论不一致，最小回填试点设计**

```md
- 首轮删除策略已冻结为 none
- 首轮 summaryBlocks 已冻结为块位 + 状态化承接 + 只读降级
- 首轮必须实施边界已明确为导航入口、人员列表、人员详情、编辑、停用、只读摘要
```

- [ ] **Step 2: 运行后端构建与测试**

Run:

```powershell
dotnet build .\server\modules\Organization\OrganizationModule.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Build succeeded.
Passed!
```

- [ ] **Step 3: 运行前端构建**

Run:

```powershell
pnpm --dir .\frontend\modules\organization build
pnpm --dir .\frontend build
```

Expected:

```text
tsc completed successfully
workspace build completed successfully
```

- [ ] **Step 4: 如已存在直接覆盖组织 metadata 消费的稳定测试入口，补一次最小 smoke；若不存在，则跳过该步并在结果中显式说明**

Run:

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~Organization -nr:false -v minimal
```

Expected:

```text
Passed!
或
当前无直接覆盖组织 metadata 消费的稳定测试入口，本步跳过并记录为已知验证缺口
```

- [ ] **Step 5: 提交**

```bash
git add docs/designs/modules/organization-person-center-pilot-design.md
git commit -m "验证：收口人员中心试点设计与联调结果"
```

## Self-Review

- **Spec coverage:** 本计划覆盖了试点范围冻结 review 中明确要求的最小闭环：导航入口、人员列表、详情、编辑、停用、只读摘要；未把删除、复杂摘要交互、权限与规则中心拉回首轮。
- **Placeholder scan:** 所有任务都给出明确文件、命令和最小代码/配置片段，没有保留 `TODO`、`待定`、`类似前一任务` 这类占位表达。
- **Type consistency:** 计划统一使用 `PersonListItemDto`、`PersonSummaryBlockDto`、`GetListAsync`、`DisableAsync`、`summaryBlocks` 这些术语；同时始终保持 `delete = none` 与 `summaryBlocks = 状态化承接` 的冻结结论。

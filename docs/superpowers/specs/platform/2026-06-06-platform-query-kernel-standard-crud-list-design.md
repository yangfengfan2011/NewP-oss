# 平台查询内核与 Standard CRUD 列表接入详细设计

## 1. 背景

人员中心已经由 Standard CRUD Host 和 PlatformCrud 内核接管列表查询，但当前 `EfStandardCrudRepository.ListAsync` 在 EF 查询层 `ToListAsync` 后，才在内存中应用条件、排序、分页和字段裁剪。

当前已确认两个问题：

- 人员中心列表没有按当前 view 需要的列裁剪查询字段，而是先读取模型全部列。
- 分页没有下推到数据库查询层，而是在内存中 `Skip / Take`。

如果只在 Standard CRUD List 内修复这些能力，后续 lookup、配置中心列表、权限中心列表、元数据配置中心、文件中心或只读选择器仍会面临“引用 Standard CRUD”或“重写一套动态查询”的问题。因此本轮设计把查询能力下沉为平台查询内核，Standard CRUD List 作为首个接入者。

同时，人员中心列表中 `CompanyName / DepartmentName / PositionName` 不是 Person 表真实存储列。Person 表存储的是 `CompanyId / DepartmentId / PositionId`，这些字段的值是结构化 EID，格式为 `{ModelName}_{Guid:N}`。显示字段应被建模为“引用 EID 指向目标模型显示字段”的投影，而不是普通后处理字段。

## 2. 目标

- 建立平台通用查询内核，提供字段白名单、请求字段解析、查询层筛选、排序、分页、投影和引用显示值解析能力。
- Standard CRUD List 改为调用平台查询内核，作为第一批接入样板。
- 前端 Standard CRUD List Host 按当前 `view.columns` 向后端提交本次列表需要的字段。
- 后端不信任前端字段，仍以平台查询 descriptor 为安全边界裁剪字段。
- 筛选字段、排序字段、行状态依赖字段和 display source 字段都必须由查询内核统一校验，不允许绕过字段白名单。
- 将 `DisplayValueFields` 从字符串列表改造成结构化引用显示值声明，明确展示字段、源 EID 字段、目标模型和目标显示字段。
- 返回 API 外形保持 `StandardCrudListResult`，不改变列表响应格式。

## 3. 非目标

- 不新增第二个 Standard CRUD 资源。
- 不要求其他功能点在本轮全部接入平台查询内核；本轮只接 Standard CRUD List。
- 不处理 required field / 模型必填校验。
- 不处理 `PersonNo` 数据库唯一索引或历史数据迁移。
- 不引入用户级列显隐持久化；本轮只消费当前 view metadata 解析出的列。
- 不把前端 `fields` 当作数据库查询真源；它只表达当前视图意图，最终字段由后端内核校验和补齐。
- 不重做导出格式和导出模式；本轮只确保导出复用列表查询时不会被新的字段裁剪语义破坏。

## 4. 范围

平台查询内核范围：

- 新增或调整平台查询 descriptor、request、result、executor、display value 声明。
- 查询内核单独建立 `server/platform-query` 项目，避免概念继续绑死在 Standard CRUD 内部。

Standard CRUD 接入范围：

- `server/platform-crud/Descriptors/StandardCrudDescriptor.cs`
- `server/platform-crud/Runtime/StandardCrudDescriptorValidator.cs`
- `server/platform-crud/Models/StandardCrudListQuery.cs`
- `server/platform-crud/Extensions/StandardCrudEndpointRouteBuilderExtensions.cs`
- `server/platform-crud/Runtime/StandardCrudService.cs`
- `server/platform-crud/Runtime/StandardCrudExportService.cs`
- `server/platform-crud/Persistence/EfStandardCrudRepository.cs` 或替换为通用 query executor 调用
- `server/modules/Organization/Crud/PersonCrudDescriptorFactory.cs`
- `server/modules/Organization/Crud/PersonCrudDisplayValueResolver.cs`
- `tests/PlatformCrud.Tests/`
- `tests/Organization.Tests/`

前端范围：

- `frontend/packages/sdk/src/standardCrud.ts`
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- `frontend/packages/shell/src/__tests__/standard-crud-list-host.test.tsx`
- `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`

文档范围：

- `docs/superpowers/specs/platform/2026-06-06-platform-query-kernel-standard-crud-list-design.md`
- 后续实施计划
- `docs/phases/current-handoff.md`

## 5. 设计约束

- 元数据只声明“当前 view 需要哪些字段”，不承载查询执行流程。
- 查询执行、字段校验、依赖补齐、分页、投影必须收口在平台查询代码内核。
- Host/Shell 只提交 view intent，不拥有字段安全判断。
- Display value 是平台引用显示值契约，不是模块私有的任意后处理字段。
- Standard CRUD 不应成为其他功能点复用查询能力的必经路径；其他功能点应面向平台查询内核。
- 仍使用 EF shared-type entity / property bag，不引入 Person 专属 repository。

## 6. 总体方案

本轮采用“两层架构”：

1. 平台查询内核：负责通用查询能力。
2. Standard CRUD List 适配层：把 Standard CRUD descriptor、view fields、filters 和排序转换为平台查询请求。

Standard CRUD 前端请求示例：

```text
GET /api/organization/persons?pageIndex=1&pageSize=20&fields=EID,PersonNo,ENAME,CompanyName,DepartmentName,PositionName,EmploymentStatus,IsEnabled
```

`fields` 参数采用逗号分隔字符串，由 SDK 统一生成和编码；后端负责按逗号拆分、去空白、去重并保持字段顺序。

后端处理步骤：

1. Endpoint 将 `fields` 解析到 `StandardCrudListQuery.Fields`。
2. Standard CRUD adapter 将 `StandardCrudDescriptor` 转换为 `PlatformQueryDescriptor`。
3. Platform Query Kernel 校验 requested fields，只允许请求真实模型字段和声明过的 display value 字段。
4. Platform Query Kernel 统一校验筛选字段、排序字段、行状态依赖字段和 display source 字段，所有字段都必须来自查询 descriptor 的显式声明。
5. Platform Query Kernel 计算 DB select fields：
   - 请求中的真实模型字段。
   - `IdField`。
   - 筛选字段。
   - 排序字段。
   - 行动作 `visibleWhen` 等状态依赖字段；这些字段由 Shell 从 row action condition 中采集后一并提交，后端仍按 descriptor 校验并补齐。
   - 请求 display value 字段对应的 source EID 字段。
6. Query executor 在 EF 查询层执行 `Where / Count / OrderBy / Skip / Take / Select`。
7. Display value resolver 根据结构化声明批量补充展示字段。
8. Standard CRUD adapter 将查询结果包装为 `StandardCrudListResult`。

字段解析分三类结果：

- `ResponseFields`：本次 API 响应允许返回的字段，来自请求字段或默认响应字段，只包含真实模型字段和声明过的 display value 字段。
- `DbSelectFields`：EF 查询层必须读取的真实模型字段，包括响应真实字段、`IdField`、筛选字段、排序字段、行状态依赖字段和 display value 的 `SourceField`。
- `InternalOnlyFields`：为筛选、排序、行状态或 display value 补齐而读取、但不应出现在响应中的字段。

## 7. 关键模型

### 7.1 PlatformQueryDescriptor

平台查询 descriptor 描述一次可查询资源的边界：

```csharp
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
```

`AllowedModelFields` 只表示真实模型字段，`DisplayValueFields` 表示可响应的引用显示字段。查询内核计算响应字段时使用 `AllowedResponseFields = AllowedModelFields + DisplayValueFields.Field`；计算数据库字段时只允许真实模型字段和 display value 的 `SourceField` 进入 EF `Select`。

Standard CRUD 的 `FieldSet.ListFields` 会映射为 `AllowedModelFields / DefaultFields`，结构化 display value 字段单独映射为 `DisplayValueFields`。其他功能点后续可以直接声明自己的查询 descriptor。

### 7.2 PlatformQueryRequest

```csharp
public sealed record PlatformQueryRequest(
    IReadOnlyList<string>? Fields,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDirection = null,
    IReadOnlyList<PlatformQueryCondition>? Conditions = null);
```

`Fields` 为空时使用 `DefaultFields`，非空时表达当前调用方的视图字段意图。

`SortBy` 和 `Conditions[*].Field` 必须按 `AllowedModelFields` 校验；第一版不支持按 display value 字段排序或筛选，避免把跨模型显示值解析混入 SQL 查询内核。

### 7.3 PlatformDisplayValueField

将 Standard CRUD 的 `DisplayValueFields` 从字符串列表改造成结构化声明，并作为平台查询内核可复用模型：

```csharp
public sealed record PlatformDisplayValueField(
    string Field,
    string SourceField,
    string TargetModelName,
    string TargetIdField,
    string TargetDisplayField);
```

人员中心声明示例：

```csharp
DisplayValueFields =
[
    new PlatformDisplayValueField(
        Field: "CompanyName",
        SourceField: "CompanyId",
        TargetModelName: "Company",
        TargetIdField: "EID",
        TargetDisplayField: "ENAME"),
    new PlatformDisplayValueField(
        Field: "DepartmentName",
        SourceField: "DepartmentId",
        TargetModelName: "Department",
        TargetIdField: "EID",
        TargetDisplayField: "ENAME"),
    new PlatformDisplayValueField(
        Field: "PositionName",
        SourceField: "PositionId",
        TargetModelName: "Position",
        TargetIdField: "EID",
        TargetDisplayField: "ENAME")
]
```

`SourceField` 存储结构化 EID，目标模型由 EID 前缀和 descriptor 声明双重约束。第一版不要求从 EID 前缀反推模型，但 resolver 可用它作为防御性校验信号。

### 7.4 PlatformQueryExecutor

```csharp
public interface IPlatformQueryExecutor
{
    Task<PlatformQueryResult> QueryAsync(
        PlatformQueryDescriptor descriptor,
        PlatformQueryRequest request,
        CancellationToken cancellationToken = default);
}
```

它负责查询层筛选、排序、分页、投影和字段裁剪，不依赖 Standard CRUD。

### 7.5 Display Value Resolver

现有单行 resolver 容易形成 N+1 查询。本轮应新增批量解析契约，例如：

```csharp
Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ResolveListAsync(
    PlatformQueryDescriptor descriptor,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
    IReadOnlyList<PlatformDisplayValueField> requestedDisplayFields,
    CancellationToken cancellationToken);
```

模块 resolver 只处理显示值解析，不决定 SQL 查询字段。依赖字段由查询内核从 `SourceField` 自动补齐。

## 8. 核心流程

Standard CRUD List 主流程：

1. Shell 从 `schema.columns` 采集字段，提交 `fields`。
2. Endpoint 解析 `fields/pageIndex/pageSize/sortBy/sortDirection`。
3. Standard CRUD adapter 生成 `PlatformQueryRequest`。
4. Query Kernel 校验 requested fields、筛选字段、排序字段和依赖字段，并计算 response fields、db select fields、requested display fields。
5. Query executor 根据 db select fields 下推查询。
6. Service 调用批量 display resolver 补充 `CompanyName / DepartmentName / PositionName`。
7. Standard CRUD adapter 按 response fields 输出 `StandardCrudListResult`。

字段分类：

- 真实模型字段：Person 表实际列，例如 `EID / PersonNo / ENAME / CompanyId`。
- 引用显示值字段：例如 `CompanyName`，声明为 `CompanyId -> Company.ENAME`。
- 内核必需字段：id、筛选、排序、动作状态和 display source 字段。

## 9. 接口与集成点

Standard CRUD 后端接口新增可选查询参数：

```text
fields=EID,PersonNo,ENAME,CompanyName
```

前端 SDK `list` 支持 `fields` 参数，并以逗号分隔字符串编码。Shell 不需要知道 display value 依赖，但需要把当前 view columns 字段和 row action `visibleWhen` 条件字段合并后提交；后端继续校验这些字段是否允许查询。

Endpoint 解析时必须把 `fields` 视为标准查询参数，加入 `StandardQueryKeys` 并从 `Filters` 中排除。否则旧逻辑会把 `fields` 当作业务筛选 key 透传给模块 query customizer，造成字段意图和筛选意图混淆。

其他功能点未来不引用 `StandardCrudRepository`，而是引用 `IPlatformQueryExecutor` 或平台查询 adapter。

Organization 只更新 descriptor 和 resolver，不新增人员专属列表接口。

`StandardCrudExportService` 当前复用 `IStandardCrudService.ListAsync` 读取导出数据。本轮需要明确导出口径：

- `current` 导出按用户提交的导出列构造查询字段，并保留当前页分页语义。
- `all` 导出按用户提交的导出列构造查询字段，并保留现有 `ExportAllPageSize` 上限语义。
- 导出列仍由后端按 descriptor 和 display value 声明校验；不因前端列请求而返回未授权字段。

## 10. 数据与状态

本轮不新增持久化数据，不修改数据库结构，不新增迁移。

运行时状态来自：

- `view.columns`
- `StandardCrudListQuery.Fields`
- `PlatformQueryRequest.Fields`
- `PlatformQueryDescriptor.AllowedModelFields`
- `PlatformQueryDescriptor.DisplayValueFields`
- EF model property 元数据

## 11. 错误处理与风险

错误处理：

- 请求未知字段：返回 400 或稳定错误，避免 metadata 漂移静默失败。
- 请求未授权字段：返回 400，不静默回退全字段。
- 请求未知筛选字段或排序字段：返回 400 或稳定错误，不允许进入 EF 动态表达式。
- 请求按 display value 字段筛选或排序：第一版返回 400 或稳定错误，后续如需支持必须另开设计。
- display value source EID 为空：展示字段返回空字符串或 null，由 resolver 稳定处理。
- target 模型或字段不存在：启动期 descriptor 校验优先发现；运行期作为稳定错误处理。

风险：

- 平台查询内核如果一次做得过宽，会变成通用 SQL 生成器。本轮只支持 Standard CRUD 现有条件能力：`=`、`contains`、`and/or` 条件组、排序、分页、字段投影。
- 批量 resolver 需要避免跨模块直接访问对方持久化细节；本轮人员中心解析 Company/Department/Position 仍在 Organization 模块内。
- 不同数据库 provider 对动态表达式翻译能力不同，需要用 SQL 形状测试和现有 provider smoke 兜底。
- 如果 view.columns 漏掉行状态字段，行操作 `visibleWhen` 可能缺值；本轮由 Shell 从 row action condition 采集状态依赖字段并随 `fields` 提交，后端按 descriptor 校验并补齐。
- 导出服务复用列表查询后，如果没有同步构造导出字段，可能出现导出列缺值或导出读取全字段；本轮把导出字段裁剪纳入同一查询 adapter 处理。

## 12. 验证方案

最小验证：

```powershell
dotnet build server\platform-query\PlatformQuery.csproj -nr:false -v minimal
dotnet build apps\host-api\HostApi.csproj -nr:false -v minimal
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrud -nr:false -v minimal
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-list-host person-center-crud-pro-sample
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-06-06-platform-query-kernel-standard-crud-list-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

失败信号：

- 人员中心列表 SQL 仍选择 `CreatedAt / UpdatedAt` 等当前 view 不需要的字段。
- 人员中心列表 SQL 没有查询层分页语义。
- 请求 `CompanyName` 时 SQL 直接选择 `CompanyName`，而不是选择 `CompanyId` 后由 resolver 补显示值。
- 其他功能点只能通过引用 Standard CRUD 才能复用查询下推能力。
- 请求未知字段没有稳定失败。
- 请求未知筛选字段、未知排序字段或 display value 排序没有稳定失败。
- `CompanyName / DepartmentName / PositionName` 返回缺失或触发 N+1 查询。
- 导出 current/all 因列表字段裁剪缺少导出列，或导出绕开字段裁剪继续读取全字段。

## 13. 已决策口径

- 平台查询内核单独建立 `server/platform-query` 项目；Standard CRUD 通过 adapter 接入，不作为其他功能点复用查询能力的入口。
- `fields` 参数采用逗号分隔字符串，SDK 统一生成和 URL 编码，后端统一解析、去重并保持字段顺序。
- `fields` 是标准查询参数，不进入 `Filters`；筛选字段和排序字段由查询内核按真实模型字段白名单校验。
- 第一版不支持按 display value 字段筛选或排序。
- 行动作 `visibleWhen` 依赖字段由 Shell 根据 row action condition 字段一并提交；后端仍按 descriptor 校验并补齐，不允许前端绕过字段边界。
- 导出 current/all 继续复用 Standard CRUD 列表服务，但必须按导出列构造查询字段，并由同一平台查询 adapter 校验和补齐依赖字段。

## 14. 实施拆分建议

1. 建立平台查询内核模型与 executor，先覆盖字段校验、查询层分页和字段投影，并区分 `ResponseFields / DbSelectFields / InternalOnlyFields`。
2. 更新 Standard CRUD descriptor adapter：把 `FieldSet.ListFields` 和结构化 display value 字段映射为平台查询 descriptor。
3. 更新前端 SDK / Shell：从 view columns 和 row action condition 生成 `fields`。
4. 更新后端 endpoint/service：解析 `fields`，把 `fields` 从 filters 排除，调用平台查询内核。
5. 更新 display resolver：支持批量解析引用显示值。
6. 更新导出服务：按导出列构造查询字段，继续保留 current/all 模式边界。
7. 补 SQL 形状测试、字段错误测试、导出字段测试、人员中心 display value 测试。
8. 更新交接台账和验证记录。

## 15. AI-First 最小检查清单

- 入口在哪里：`IPlatformQueryExecutor` 是查询能力入口；`StandardCrudListPageHost` 生成 `fields`，Standard CRUD service 作为首个 adapter 调用查询内核。
- 边界在哪里：本轮只建设平台查询内核和 Standard CRUD List 首个接入；不新增资源、不改数据库结构、不改非接入功能点。
- 如何验证：运行 PlatformCrud / Organization 后端定向测试、Shell 标准 CRUD 前端测试、AI-first 设计校验。
- 失败信号是什么：SQL 全字段查询、内存分页、display value 字段被误当真实列、其他功能点只能引用 Standard CRUD 才能复用查询能力、未知字段或未知排序/筛选字段静默忽略、display value 排序/筛选未稳定失败、显示值 N+1 或缺失、导出列缺失或导出绕开字段裁剪。
- 如何回滚：回退平台查询内核、Standard CRUD adapter、结构化 display value 声明、`fields` 参数解析与 SDK/Shell 传参；恢复原先 `DisplayValueFields` 字符串列表和默认 Standard CRUD list 查询。

# 人员中心后台 CRUD Pro 样板固化详细设计

## 1. 背景

当前前端已经完成 Ant Design Shell、Pro Components adapter、人员中心和日志审计低风险页面的首轮改造，但整体仍处于“用了 AntD 组件，还没有形成平台级后台信息层级与 CRUD 页面规范闭环”的中间态。

人员中心是当前最完整的标准 CRUD 试点，已经接入 runtime metadata、standard CRUD registry、列表/详情 Host、lookup、summary、权限动作和保存/停用流程。它适合作为第一版后台 CRUD Pro 样板继续固化，而不是立刻把抽象推广到其他页面族。

本轮不再以配置管理作为下一页验证对象。配置管理后续可能放弃，因此本设计将验收重心放回人员中心本身：通过人员中心沉淀可复用后台 CRUD 页面规范、组件边界和 Host 编排方式。

## 2. 目标

- 将人员中心固化为平台第一版完整后台 CRUD 样板，覆盖列表页、详情页和表单页。
- 建立 Ant Design Pro 风格的信息层级：页面标题区、主操作、筛选区、表格区、行操作、详情抽屉、表单抽屉、危险动作确认和反馈状态各有唯一职责归属。
- 抽取平台级 CRUD 页面组件和 adapter，让人员中心通过 standard CRUD 契约接入，而不是在模块内私有堆 UI。
- 新增/编辑默认使用右侧抽屉，不再把跳转到独立页面作为日常维护默认体验。
- 查看默认使用右侧详情抽屉，同时保留独立详情页作为深链接、复杂关联信息和回滚出口。
- 旧新建/编辑路由兼容到列表页，并自动打开对应抽屉。
- 将 `create / refresh / view / edit` 定义为平台 CRUD 必备标准动作；将 `delete` 定义为平台必须提供的危险动作协议能力；将 `disable` 明确为可选标准动作。

## 3. 非目标

- 不重做配置管理、权限中心、文件中心、插件运维或元数据配置中心。
- 不改变后端 API 路由、DTO 契约、认证、权限 code 或 runtime metadata authoring 结构。
- 不把 Pro Components 私有模型变成菜单、路由、权限、metadata 或 standard CRUD 的事实源。
- 不把人员中心业务语义固化为所有 CRUD 资源必须实现的规则。
- 不要求所有资源都支持 `disable`、`enable`、`bind`、`export` 或 `import`。
- 不在本轮清理所有旧 `sh-*` 组件和样式，只收缩人员中心样板路径中直接影响规范闭环的过渡态。

## 4. 范围

### 4.1 涉及目录

- `frontend/packages/ui/src/pro-adapters/`
  - 新增或重塑无业务语义的 CRUD 页面、表格、详情抽屉、表单抽屉和危险动作组件。
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
  - 升级为列表、详情抽屉、表单抽屉和标准动作的编排入口。
- `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
  - 保留独立详情页能力，调整为与抽屉详情共享字段、summary 和动作投影。
- `frontend/packages/shell/src/standard-crud/`
  - 收口动作运行时、详情加载、提交、删除、刷新和错误状态。
- `frontend/packages/shell/src/navigation/`
  - 增加旧新建/编辑路由到列表抽屉状态的兼容适配。
- `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
  - 人员资源继续声明字段、columns、filters、lookup、summary、动作和业务 handler。
- `frontend/packages/shell/src/__tests__/`
  - 增加人员中心 CRUD Pro 样板、路由兼容、动作和抽屉状态测试。

### 4.2 不触碰范围

- `server/**`
- `apps/**`
- runtime metadata 文档结构
- standard CRUD SDK 契约的破坏性变更
- 登录、认证、权限判断和模块注册模型

## 5. 设计约束

- UI 包只提供布局和视觉承载，不知道人员、公司、部门等业务语义。
- Shell 负责把 standard CRUD 契约、runtime metadata、路由、权限和服务调用投影为后台 CRUD 体验。
- Organization 只声明人员资源能力，不拥有页面 chrome 和通用抽屉状态机。
- `MenuConfig`、runtime metadata、权限 code 和 standard CRUD registration 继续是平台事实源。
- Pro Components 只能通过平台 adapter 使用，不能反向决定平台契约。
- 右侧抽屉是默认新增/编辑承载方式；弹窗只用于短小确认类表单；独立页面只用于复杂流程、深链接或回滚。
- `disable` 是可选标准动作，不是平台 CRUD 必备动作。
- 删除是平台必须提供的危险动作协议能力，但具体资源只有显式声明 `delete` 且具备稳定后端能力和独立权限时才展示删除入口。

## 6. 总体方案

本轮采用“平台 CRUD 样板组件 + 人员中心接入”的方案。

分层如下：

```text
Organization personStandardCrudResource
  声明字段、筛选、列、lookup、summary、动作和 handler
        ↓
Shell standard CRUD Host
  编排列表、详情抽屉、表单抽屉、旧路由兼容、权限、刷新和错误状态
        ↓
UI CRUD Pro adapters
  承载页面、表格、筛选条、详情抽屉、表单抽屉和危险动作确认
        ↓
Ant Design / Pro Components
  ProTable / ProForm / ProDescriptions / Drawer / Popconfirm 等视觉实现
```

选择该方案的原因：

- 它把人员中心从“单页改造”升级为平台样板，符合平台内核仓库定位。
- 当前已有 standard CRUD 契约和 Pro adapter，抽取成本可控。
- 抽象留在 UI/Shell 层，避免人员中心业务规则污染平台 CRUD。
- 后续如恢复其他页面族，也可直接复用样板，而不需要再从人员中心拆第二遍。

不选择的方案：

- 只写规范不改组件：风险低但停留在纸面，无法消除当前人员中心过渡态。
- 人员中心专属改造：短期可见，但抽象仍锁在 Organization 模块内，后续难复用。

## 7. 关键模型

### 7.1 页面职责模型

```ts
interface CrudPageOwnership {
  shell: 'breadcrumb' | 'route-compatibility' | 'standard-crud-orchestration';
  workspace: 'title' | 'description' | 'primary-action' | 'filter-region';
  table: 'data-list' | 'refresh' | 'density' | 'column-setting' | 'pagination';
  detailDrawer: 'readonly-detail' | 'summary' | 'detail-actions';
  formDrawer: 'create' | 'edit' | 'field-groups' | 'submit' | 'cancel';
}
```

约束：

- Shell 不写人员业务逻辑。
- 模块不控制通用页面布局。
- 表格工具栏不放主创建按钮。
- 分页只由表格区承载。

### 7.2 标准动作模型

```ts
type RequiredCrudAction = 'create' | 'refresh' | 'view' | 'edit';
type PlatformCrudProtocolAction = 'delete';
type OptionalCrudAction = 'disable' | 'enable' | 'export' | 'import' | 'bind' | 'unbind';
```

规则：

- `create` 放在页面标题区右侧。
- `refresh` 放在表格卡片右上角，与密度和列设置同层。
- `view / edit` 是行级普通操作。
- `delete` 是平台内建危险动作协议；只有资源显式声明并具备后端能力和权限时才展示，展示后必须确认。
- `disable` 是可选标准动作；资源声明后才显示，且可选择是否按危险动作处理。

### 7.3 抽屉状态模型

```ts
type CrudDrawerMode = 'closed' | 'create' | 'view' | 'edit';

interface CrudDrawerState {
  mode: CrudDrawerMode;
  id?: string;
  source: 'user-action' | 'route-compatibility';
}
```

规则：

- 列表页是唯一主工作台。
- 详情抽屉和表单抽屉都从列表上下文派生。
- 保存、删除、停用成功后刷新列表。
- 删除当前抽屉记录成功后关闭抽屉。

## 8. 核心流程

### 8.1 列表主流程

1. 用户进入 `/organization/persons`。
2. Shell 通过 runtime metadata 和 standard CRUD registry 找到人员资源。
3. `StandardCrudListPageHost` 构建筛选状态、列表请求、columns、动作和抽屉状态。
4. UI `CrudWorkspacePage` 输出页面标题、主操作、筛选区和表格区。
5. `CrudTableSection` 通过 ProTable 渲染列表、分页、刷新、密度和列设置。

### 8.2 新增流程

1. 用户点击页面标题区“新建人员”。
2. Host 设置 `drawerState = { mode: 'create' }`。
3. `CrudFormDrawer` 打开右侧抽屉，按人员字段分组渲染表单。
4. 提交时使用 standard CRUD mutation 映射。
5. 成功后刷新列表，并按实现选择关闭抽屉或切回详情抽屉。
6. 失败时错误留在抽屉内，保留用户输入。

### 8.3 查看与编辑流程

1. 用户点击行内“查看”。
2. Host 设置 `drawerState = { mode: 'view', id }`。
3. `CrudDetailDrawer` 加载详情和 summary。
4. 用户点击详情抽屉内“编辑”。
5. Host 切换为 `drawerState = { mode: 'edit', id }`。
6. `CrudFormDrawer` 复用字段、lookup 和提交映射。
7. 保存成功后刷新列表，并刷新当前详情数据。

### 8.4 删除流程

1. 资源显式声明 `delete` 后，用户点击行内或详情抽屉内“删除”。
2. `CrudDangerAction` 显示确认文案。
3. 确认后调用资源声明的 delete handler 或 standard CRUD delete client。
4. 成功后刷新列表。
5. 如果当前抽屉显示的是被删除记录，关闭抽屉。
6. 失败时显示错误，不静默吞掉。

### 8.5 刷新流程

1. 用户点击表格工具栏“刷新”。
2. Host 重新请求当前筛选和分页下的列表数据。
3. 刷新不重置筛选条件。
4. 如果详情抽屉打开，可同步刷新当前详情。
5. 加载中刷新按钮显示 loading 或 disabled，避免重复请求。

### 8.6 旧路由兼容流程

- 旧路由兼容由标准 CRUD 路由适配器根据资源 `listRoutePath` 生成，Shell 只消费适配结果，不写人员中心专属路由特例。
- 访问 `/organization/persons/new`：回到 `/organization/persons?drawer=create`，并打开新建抽屉。
- 访问 `/organization/persons/:id`：仍可进入独立详情页。
- 列表内点击“查看”：默认使用 `/organization/persons?drawer=view&id=<id>` 或等价内部状态打开详情抽屉。
- 访问 `/organization/persons/:id/edit`：如后续存在，回到列表并打开编辑抽屉。

## 9. 接口与集成点

### 9.1 UI 包接入点

- `CrudWorkspacePage`
- `CrudSearchBar`
- `CrudTableSection`
- `CrudDetailDrawer`
- `CrudFormDrawer`
- `CrudDangerAction`
- 现有 `SearchTablePage`、`FormPage`、`DetailPage` 可以作为底层 adapter 被复用或收敛。

### 9.2 Shell 接入点

- `StandardCrudListPageHost.tsx`
  - 主编排入口，负责列表、抽屉、动作和刷新。
- `StandardCrudDetailPageHost.tsx`
  - 独立详情页入口，继续支持深链接。
- `standardCrudActionRuntime.tsx`
  - 标准动作和可选动作的权限、确认、loading、错误和刷新协议。
- `standardCrudRouteAdapter.ts`
  - 旧路由到列表抽屉状态的兼容适配。

### 9.3 Organization 接入点

- `personStandardCrudResource.tsx`
  - 继续声明人员字段、筛选、columns、lookup、summary、actions、handlers、mapListItem、mapDetail 和 mutation 映射。
  - `disable` 保留为人员资源可选动作。
  - `delete` 只有在后端和权限明确后才展示。

## 10. 数据与状态

列表页维护：

- `filters`
- `appliedFilters`
- `tableQuery`
- `rows`
- `total`
- `loading`
- `error`
- `drawerState`
- `detailLoading`
- `detailError`
- `saving`
- `actionErrors`

一致性要求：

- 筛选、分页、刷新始终回到后端权威数据。
- 保存成功后必须刷新列表。
- 删除成功后必须刷新列表，并关闭当前记录抽屉。
- 停用成功后刷新列表和当前详情。
- 详情和表单错误只影响抽屉，不应导致列表白屏。
- 旧路由兼容只改变前端入口状态，不改变后端接口语义。

## 11. 错误处理与风险

### 11.1 错误处理

- 列表加载失败：表格区显示错误状态和刷新入口。
- 详情加载失败：详情抽屉内显示错误，不影响列表。
- 表单保存失败：表单抽屉内显示错误，保留用户输入。
- 删除失败：危险动作区域或抽屉内显示错误，不刷新列表。
- 刷新失败：保留旧数据并显示错误提示。
- lookup 加载失败：字段级显示降级提示，不阻塞其他可编辑字段。

### 11.2 风险

- 抽象过重：若第一版组件试图覆盖所有后台页面，会增加维护成本。
- 路由和抽屉状态分叉：旧 URL、查询参数和内部状态如果没有统一规则，会让后续 Codex 难以理解。
- 人员语义污染平台：`disable`、账号绑定、授权摘要等人员专属能力不能变成平台必备。
- 删除接口缺失：如果后端没有稳定删除能力，前端不应展示假删除按钮。

### 11.3 风险控制

- 第一版只服务 standard CRUD 样板，不扩展到复杂配置、树表、插件生命周期页面。
- `disable` 明确为可选动作。
- `delete` 先作为协议能力，人员中心是否启用按后端能力决定。
- 独立详情页保留为回滚和深链接出口。
- 每个状态机入口都补测试，尤其是旧路由自动打开抽屉。

## 12. 验证方案

最小验证命令：

```powershell
pnpm --dir frontend --filter @icp-platform/ui build
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud organization-person-center
pnpm --dir frontend --filter @icp-platform/shell build
pnpm --dir frontend --filter @icp-modules/organization build
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-17-person-center-crud-pro-sample-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

行为验证：

- 人员中心列表只出现一套标题、一套主操作和一套分页。
- 点击“新建人员”打开右侧表单抽屉，不默认跳转独立页面。
- 点击“查看”打开右侧详情抽屉。
- 详情抽屉可进入编辑抽屉。
- `/organization/persons/new` 回到列表并自动打开新建抽屉。
- 刷新不重置筛选条件。
- 资源声明 `delete` 后删除动作必须确认；若人员中心后端未支持删除，则按钮不展示。
- `disable` 作为人员资源可选动作展示，不影响其他 CRUD 资源。

## 13. 未决问题

- 人员中心是否已有稳定删除后端接口和独立删除权限；若没有，第一版只落平台删除协议，不展示人员删除按钮。
- 保存成功后表单抽屉默认关闭，还是切回详情抽屉；建议在实施计划中以测试明确。
- 详情抽屉 URL 是否使用查询参数作为真源，还是仅由旧路由适配写入内部状态；建议优先使用查询参数，便于调试和回放。

## 14. 实施拆分建议

1. 建立 UI CRUD Pro 组件骨架。
   - 最小交付：`CrudWorkspacePage`、`CrudTableSection`、`CrudDetailDrawer`、`CrudFormDrawer`、`CrudDangerAction` 可被测试渲染。
2. 升级 Shell standard CRUD Host。
   - 最小交付：列表、查看抽屉、新建抽屉、编辑抽屉、刷新状态机跑通。
3. 接入人员中心。
   - 最小交付：人员筛选改用 AntD 控件，查看/新增/编辑默认抽屉化，`disable` 作为可选动作保留。
4. 增加删除协议。
   - 最小交付：平台动作 runtime 支持 `delete` 确认、权限、loading、错误和刷新；人员中心按后端能力决定是否展示。
5. 增加旧路由兼容。
   - 最小交付：路由适配器生成 `/organization/persons/new` 到列表抽屉入口的兼容声明，并由 Shell 通用消费。
6. 验证和文档收口。
   - 最小交付：定向测试、构建、AI-first 文档校验通过，并记录风险与回滚路径。

## 15. AI-First 最小检查清单

- 入口在哪里：实现入口从 `frontend/packages/ui/src/pro-adapters/` 的 CRUD Pro 组件、`frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`、`frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`、`frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx` 和 `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx` 开始。
- 边界在哪里：允许修改 UI adapter、Shell standard CRUD Host、standard CRUD action runtime、路由兼容 adapter、人员资源注册和相关测试；禁止修改后端 API、认证权限、runtime metadata authoring、ModuleRegistry 事实源和 standard CRUD SDK 的破坏性契约。
- 如何验证：运行 `@icp-platform/ui build`、Shell standard CRUD 与人员中心定向测试、`@icp-platform/shell build`、`@icp-modules/organization build`，并执行 `validate-ai-first-design.ps1` 与 `check-ai-first-design-targets.ps1`。
- 失败信号是什么：出现双标题、双分页、主操作重复、列表点击仍默认跳转新建/编辑页、旧新建路由不能自动打开抽屉、资源声明删除后无确认、未声明删除时仍出现删除入口、`disable` 被平台强制要求、保存/删除/停用后列表未刷新、详情或表单错误导致列表白屏，均说明实现未按设计落地。
- 如何回滚：按三层回退。UI 层回退新增 CRUD Pro adapter；Shell 层回退 `StandardCrudListPageHost`、`StandardCrudDetailPageHost`、action runtime 和路由兼容；Organization 层回退人员资源注册中的抽屉化动作声明。独立详情页保留作为回滚出口，后端接口和数据层无需联动回滚。

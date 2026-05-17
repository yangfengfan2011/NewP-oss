# 前端 Ant Design 全量改造详细设计

## 1. 背景

当前前端已经具备平台型系统的基础结构：`frontend/packages/shell` 负责宿主 Shell，`frontend/packages/ui` 提供共享 UI 组件，`frontend/modules/*` 提供模块页面，模块通过 `ModuleRegistry`、runtime metadata、customPages、standardCrudResources 等契约接入。

现有 UI 仍处于过渡态：Shell、页面骨架和共享组件主要使用自研 `sh-*` 样式，视觉上更接近轻量产品或开发者工具风格，不符合企业客户对中后台系统的稳定、成熟、信息密度、表格驱动、表单驱动和权限清晰的预期。

本次设计以“全量 Ant Design 企业级中后台改造”为目标。这里的全量不是一次性推倒平台前端内核，而是明确旧 UI 体系不再作为长期方向：保留平台接入契约和人员中心业务资产，重建 Shell、页面模板和除人员中心外的过渡态界面。

## 2. 目标

- 建立 Ant Design 企业级中后台 Shell，包括侧边菜单、顶部栏、内容区、面包屑、页面容器和统一主题。
- 建立基于 Ant Design 的标准页面模板，包括工作台、查询表格页、详情页、表单页、树表页、主从页、配置页、权限页和日志审计页。
- 保留人员中心作为首个可用业务样板，将其标准 CRUD、lookup、详情、新增、停用动作和服务逻辑迁入新的 Ant Design 模板。
- 对权限、配置、审计、文件、插件运维、元数据配置等旧页面进行重做，而不是在旧 `sh-*` 页面上继续精修。
- 保留平台扩展内核，包括 runtime metadata、ModuleRegistry、权限 code、路由 fallback、standard CRUD contract 和模块注册方式。
- 建立可验证、可回退、可分阶段交付的改造路径。

## 3. 非目标

- 不重写后端接口。
- 不改变权限 code 语义。
- 不改变 runtime metadata 文档结构。
- 不改变 ModuleRegistry 的模块注册模型。
- 不改变 standard CRUD 的业务契约。
- 不把业务模块页面硬编码进 Shell。
- 不把 Shell、runtime metadata、ModuleRegistry、权限、路由机制或 standard CRUD contract 全量改造成 Pro Components 私有模型。
- 不在第一阶段引入 Ant Design Pro Components 作为强依赖；后续只通过平台 adapter 选择性使用 `ProTable`、`ProForm`、`PageContainer`、`ProDescriptions` 等 UI 承载组件。
- 不在没有独立设计的情况下大规模重组 metadata 菜单真源。
- 不把产品专属页面或流程直接固化进平台 Shell。

## 4. 范围

### 4.1 涉及目录

- `frontend/packages/shell`
  - 全局布局、主题、菜单适配、PageContainer、路由承载和工作台。
- `frontend/packages/ui`
  - 新 Ant Design 页面模板、状态展示、操作按钮、表格、表单、抽屉、弹窗等共享能力。
- `frontend/modules/organization`
  - 人员中心保留并迁移到 Ant Design 模板。
- `frontend/modules/authorization`
  - 权限中心页面重做。
- `frontend/modules/config-center`
  - 配置管理和值列表页面重做。
- `frontend/modules/audit`
  - 操作日志和访问日志页面重做。
- `frontend/modules/files`
  - 文件中心页面重做。
- `frontend/modules/plugin-ops`
  - 插件运维中心页面重做。
- `frontend/modules/metadata-config-center`
  - 元数据配置中心页面重做。
- `frontend/modules/demo` 与 `frontend/modules/module-template-sample`
  - 降级为开发示例或按新模板重建示例页。

### 4.2 不触碰范围

- 后端 API 路径和 DTO 契约。
- 认证和登录态恢复接口。
- 权限 code 和权限判断规则。
- runtime metadata authoring 结构。
- 模块加载、路由接管和 fallback 冲突处理规则。
- 与 Ant Design 无关的部署脚本、CI、密钥和基础设施配置。

## 5. 设计约束

### 5.1 架构约束

- Host/Shell 只负责布局、全局 Provider、认证上下文、菜单聚合、路由组合和模块加载。
- 模块前端必须继续通过 SDK、metadata 或受控注册点接入，不直接修改 Shell 内部结构实现业务接入。
- 新 UI 模板只能消费模块注册、metadata 和服务层，不能把执行流程下沉到 metadata 解释层。
- 人员中心作为保留资产时，保留其标准 CRUD resource 注册和服务层，不把人员业务逻辑搬入 Shell。
- Pro Components 只能作为 UI 层实现选择，不能成为菜单、路由、权限、metadata 或标准 CRUD 的事实源。
- 若使用 Pro Components，必须通过平台 adapter 将现有契约映射到 `ProTable`、`ProForm`、`PageContainer` 等组件，禁止让页面直接依赖 Pro Components 私有配置替代平台契约。

### 5.2 UI 约束

- 采用 Ant Design 企业级中后台视觉语言。
- 主色使用 `#1677ff`。
- 页面背景使用浅灰，内容区使用白色。
- 基准字号使用 14px。
- 圆角以 6px 为主，不使用过大圆角。
- 表格信息密度为中等或偏紧凑。
- 禁止大面积渐变、花哨阴影、过度动画和过度卡片化。

### 5.3 迁移约束

- 全量目标按阶段落地，阶段之间必须保持可运行。
- 每个阶段只聚焦一种能力或一组页面类型。
- 旧 `sh-*` 组件允许短期作为兼容层存在，但新建页面默认使用 Ant Design。
- 页面重做时必须先列出保留的接口、状态和权限边界，再替换 UI。

## 6. 总体方案

总体方案采用“平台契约保留 + UI 层全量重建 + Pro Components 受控适配”的路径。

### 6.1 分层方案

```text
App
└── AntdConfigProvider
    └── AppLayout
        ├── Header
        ├── Sidebar/Menu
        └── Content
            └── PageContainer
                └── Module Page
```

Shell 负责：

- Ant Design `ConfigProvider` 主题。
- Ant Design `Layout`、`Sider`、`Header`、`Content`。
- `MenuConfig` 到 Ant Design Menu 的展示适配。
- 根据当前路由推导面包屑和页面标题。
- 保留 `Outlet` 承载模块页面。

UI 包负责：

- 企业级页面模板。
- Ant Design wrapper 或组合组件。
- Pro Components adapter，例如标准 CRUD 到 `ProTable` / `ProForm` 的映射。
- 状态标签、操作按钮、搜索表单、表格、表单容器、详情容器。
- 新模块页面复制模板。

模块负责：

- 业务数据加载。
- 业务表单提交。
- 权限 code 声明。
- 标准 CRUD resource 注册。
- 模块页面内部业务状态。

### 6.2 Pro Components 使用决策

已确认的原则是：平台内核不 Pro Components 化，UI 层 Ant Design / Pro Components 化。

保留为平台内核的能力：

- runtime metadata：继续作为模块导航、页面、视图和模型的运行时契约。
- ModuleRegistry：继续作为模块注册和扩展入口。
- 权限体系：继续由平台权限 code、`PermissionGuard` 和 `usePermission` 判断。
- 路由机制：继续由 metadata routes、standard CRUD routes 和 fallback routes 组合。
- standard CRUD contract：继续作为 SDK 契约，不能改成 `ProTable` 或 `ProForm` 的私有配置。

允许使用 Pro Components 的位置：

- `PageContainer`：作为页面标题、面包屑、页签、操作区和内容容器的 UI 承载。
- `ProTable`：作为查询表格页、日志页、配置列表、权限列表、文件列表和插件状态列表的 UI 承载。
- `ProForm`：作为新增、编辑、配置表单的 UI 承载。
- `EditableProTable`：作为值列表、参数配置和明细配置的 UI 承载。
- `ProDescriptions`：作为人员详情、插件详情、审计详情等只读详情区的 UI 承载。

谨慎使用的位置：

- `ProLayout` 只允许作为视觉参考或受控布局承载，不允许接管菜单真源、路由生成、权限过滤或 shellView 过滤。

适配结构：

```text
runtime metadata / ModuleRegistry / permissions / routes / standard CRUD
                            ↓
                    平台 adapter 层
                            ↓
       PageContainer / ProTable / ProForm / ProDescriptions
```

### 6.3 为什么选择该方案

- 保留平台扩展内核，符合仓库“平台基础能力与扩展内核”的定位。
- Ant Design 改造集中在 UI 层，不破坏模块契约。
- Pro Components 提供成熟中后台页面模式，但通过 adapter 使用，避免 UI 库反向污染平台契约。
- 人员中心可作为样板先验证标准 CRUD 与 Ant Design 模板的结合方式。
- 其他页面当前多数是过渡态骨架，重做成本可控。

### 6.4 不选择的方案

不选择简单换皮：

- 旧 `sh-*` 体系无法自然获得 Ant Design 的表格、表单、弹窗、抽屉、消息、上传、菜单和主题能力。
- 简单换色会继续保留旧页面结构问题。

不选择一次性引入 Pro Components 全面接管：

- 当前已有 runtime metadata、ModuleRegistry 和标准 CRUD 宿主。
- Pro Layout/Pro Table 的默认组织方式可能与现有平台契约重叠。
- Pro Components 可以用于 UI 承载，但必须经过平台 adapter，不能成为平台事实源。

不选择推倒路由和 metadata：

- 路由和 metadata 是平台内核能力，不是 UI 组件问题。
- 推倒会增加业务回归风险，并破坏模块扩展边界。

## 7. 关键模型

### 7.1 Shell 菜单展示模型

输入仍为现有 `MenuConfig`：

```ts
interface MenuConfig {
  id: string;
  label: string;
  path?: string;
  icon?: string;
  order?: number;
  permissions?: string[];
  children?: MenuConfig[];
}
```

新增展示适配模型：

```ts
interface AntdMenuProjection {
  key: string;
  label: React.ReactNode;
  icon?: React.ReactNode;
  children?: AntdMenuProjection[];
}
```

规则：

- `MenuConfig` 是菜单真源。
- Ant Design Menu projection 只负责展示。
- 权限过滤和 shellView 过滤继续使用现有规则。
- 当前路由高亮通过 `path` 和 `location.pathname` 推导。

### 7.2 页面容器模型

```ts
interface PageContainerProps {
  title?: string;
  subtitle?: string;
  breadcrumbItems?: Array<{
    title: string;
    path?: string;
  }>;
  actions?: React.ReactNode;
  children: React.ReactNode;
}
```

规则：

- Shell 可以根据菜单路径提供默认标题和面包屑。
- 模块页面可以通过模板声明更准确的页面标题和操作区。
- Phase 1 允许页面内部旧标题短期存在；迁移到新模板后应避免双标题。

### 7.3 标准查询表格页模型

```ts
interface SearchTablePageProps<TFilter, TRow> {
  title: string;
  filters: TFilter;
  columns: ColumnsType<TRow>;
  rows: TRow[];
  loading: boolean;
  total: number;
  page: number;
  pageSize: number;
  onSearch: (filters: TFilter) => void;
  onReset: () => void;
  onPageChange: (page: number, pageSize: number) => void;
  toolbar?: React.ReactNode;
}
```

该模型覆盖：

- 权限列表。
- 配置列表。
- 值列表。
- 操作日志。
- 访问日志。
- 文件列表。
- 插件状态列表。

### 7.4 人员中心保留模型

人员中心继续使用：

- `personStandardCrudResource`
- `organizationService`
- `PersonCreatePage`
- `PersonDetailPage`
- standard CRUD list/detail host

Ant Design 改造应优先改造 standard CRUD host 和 UI renderer，使人员中心自然获得 Ant Design 表格、筛选、详情和表单能力。

### 7.5 Pro Components Adapter 模型

```ts
interface PlatformProTableAdapter<TFilter, TRow> {
  source: 'standard-crud' | 'custom-page';
  permissions: string[];
  buildColumns: () => ProColumns<TRow>[];
  buildRequest: (params: TFilter & { current?: number; pageSize?: number }) => Promise<{
    data: TRow[];
    total: number;
    success: boolean;
  }>;
  buildToolbar?: () => React.ReactNode;
}
```

规则：

- adapter 消费平台契约，再投影到 Pro Components。
- adapter 不保存业务真源，不绕过服务层，不改 metadata。
- 模块页面允许使用 adapter 输出的 props，但不直接把权限、路由、metadata 迁移进 Pro Components 配置。

## 8. 核心流程

### 8.1 Shell 渲染流程

1. `main.tsx` 注册模块。
2. `App.tsx` 加载 runtime metadata 和本地化清单。
3. `AppRoutes` 组合 metadata routes、standard CRUD detail routes 和 fallback routes。
4. 受保护页面进入 `AppLayout`。
5. `AppLayout` 从 runtime metadata 或 ModuleRegistry 取得菜单。
6. `Sidebar` 将 `MenuConfig` 投影为 Ant Design Menu。
7. `PageContainer` 根据当前路由显示标题、面包屑和内容区。

### 8.2 人员中心迁移流程

1. 保留 `organization.person` 标准 CRUD resource。
2. 将 `StandardCrudListPageHost` 的页面结构替换为 Ant Design 查询表格页。
3. 将筛选区映射为 Ant Design Form。
4. 将列表列映射为 Ant Design Table columns。
5. 将状态映射为 Ant Design Tag。
6. 将行操作映射为 Ant Design Button、Dropdown、Popconfirm。
7. 将详情页和新建页迁移到 Ant Design Form、Descriptions、Tabs、Drawer 或独立页面。

### 8.3 其他页面重做流程

1. 读取旧页面，列出必须保留的 API、状态、权限和错误处理。
2. 确认页面类型：查询表格页、表单页、详情页、树表页或复杂配置页。
3. 使用 Ant Design 模板重建页面。
4. 迁移必要的服务调用和状态机。
5. 删除旧页面内部 `sh-*` UI 结构。
6. 保留模块注册、路由和权限声明。

### 8.4 异常流程

- metadata 清单加载失败：继续回退到 ModuleRegistry fallback 菜单和路由。
- 当前用户无权限：继续由 `PermissionGuard` 和页面内权限判断处理。
- 页面 API 加载失败：Ant Design `Alert`、`Result` 或错误状态显示具体原因。
- 表单保存失败：Ant Design `message.error` 或表单错误区显示后端返回原因。
- 危险动作失败：保留弹窗关闭前后的明确状态，不吞掉错误。

## 9. 接口与集成点

### 9.1 前端接入点

- `frontend/packages/shell/src/App.tsx`
  - 注入 Ant Design `ConfigProvider`。
- `frontend/packages/shell/src/components/Layout.tsx`
  - 改造为 Ant Design Layout。
- `frontend/packages/shell/src/components/Header.tsx`
  - 改造为企业后台顶栏。
- `frontend/packages/shell/src/components/Sidebar.tsx`
  - 改造为 Ant Design Menu 适配器。
- `frontend/packages/shell/src/components/PageContainer.tsx`
  - 新增统一页面容器。
- `frontend/packages/shell/src/config/antdTheme.ts`
  - 新增主题 token。
- `frontend/packages/ui/src`
  - 新增 Ant Design 页面模板和组合组件。

### 9.2 模块集成点

- `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
  - 人员中心保留入口。
- `frontend/modules/*/src/index.ts`
  - 模块路由、菜单、权限和 customPages 注册仍保持。
- `modules/*/metadata/navigation/*.nav.json`
  - 菜单和页面 entry 真源继续保留。

### 9.3 后端接口

改造应保留以下已有接口语义：

- `/api/auth/current-user`
- `/api/auth/login`
- `/api/auth/logout`
- `/api/organization/persons`
- `/api/config/grouped`
- `/api/config/{id}`
- `/api/value-lists`
- `/api/audit/operation-logs`
- `/api/audit/access-logs`
- 文件模块 `fileService` 使用的上传、查询、下载、删除接口。
- 插件运维 `pluginOperationsClient` 使用的校验、安装、启用、禁用、回滚、卸载接口。
- 元数据配置中心服务使用的模块、settings、save、apply 接口。

## 10. 数据与状态

### 10.1 状态来源

- 登录态来自 `AuthProvider` 和后端 current-user 接口。
- 菜单和路由来自 runtime metadata，缺失时来自模块注册 fallback。
- 页面数据来自各模块服务层或现有 fetch 调用。
- 人员中心数据来自 `organizationService` 和标准 CRUD client。

### 10.2 生命周期

- Shell 级状态：布局折叠、当前视图、当前路由、面包屑。
- 页面级状态：筛选条件、分页、loading、error、selectedRows、drawer/modal open、form dirty。
- 模块级状态：人员中心 lookup、元数据草稿、插件运维忙碌动作、文件上传进度。

### 10.3 一致性要求

- 列表保存、删除、上传、生命周期动作后，必须回到后端权威数据刷新。
- 元数据配置中心必须保留“本地编辑 -> 已保存未应用 -> 已应用”的两段式状态。
- 插件运维中心必须保留同一时间只允许一个生命周期动作执行的边界。
- 人员中心 lookup 依赖关系必须保留公司、部门、岗位的联动加载。

## 11. 错误处理与风险

### 11.1 主要失败点

- Ant Design 依赖引入后构建失败。
- `ConfigProvider` 或全局样式导致旧页面样式冲突。
- Ant Design Menu 投影后菜单权限过滤或高亮错误。
- Pro Components adapter 绕过平台契约，导致页面配置与 runtime metadata、权限或标准 CRUD 分叉。
- PageContainer 与旧页面 PageHeader 形成双标题。
- 人员中心标准 CRUD host 替换后列表、详情或新建流程失效。
- 页面重做时遗漏旧 API、错误处理或权限判断。

### 11.2 风险控制方式

- 每阶段只改一个层级：Shell、模板、人员中心、单模块页面。
- 每个重做页面先列出保留接口和状态，再替换 UI。
- Ant Design 基础组件先落地，Pro Components 通过平台 adapter 选择性引入。
- 对 `ProTable`、`ProForm`、`PageContainer` 等组件的使用补 adapter 测试，证明权限、metadata、标准 CRUD 仍由平台契约驱动。
- 保留旧 `sh-*` 样式到迁移后期，避免未迁移页面白屏。
- 人员中心作为样板先验证标准 CRUD 宿主改造。

### 11.3 降级思路

- Shell 改造失败时，回退到旧 `Layout/Header/Sidebar/index.css`。
- 单模块页面重做失败时，仅回退该模块页面文件。
- Ant Design 模板有问题时，保留页面服务层并回退模板调用。
- 人员中心迁移失败时，恢复 standard CRUD host 的旧渲染路径。

## 12. 验证方案

### 12.1 文档验证

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-17-frontend-ant-design-rebuild-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

### 12.2 前端验证

每个实现阶段至少运行：

```powershell
pnpm --dir frontend --filter @icp-platform/shell test
pnpm --dir frontend --filter @icp-platform/shell build
```

涉及共享 UI 包时运行：

```powershell
pnpm --dir frontend --filter @icp-platform/ui build
```

涉及全 workspace 依赖或跨模块页面时运行：

```powershell
pnpm --dir frontend build
```

### 12.3 Smoke 验证

实现阶段应至少检查：

- 登录页可进入。
- 首页可进入。
- 左侧菜单显示正常。
- 当前路由菜单高亮正常。
- 人员中心列表可加载。
- 人员详情可进入。
- 人员新建入口可进入。
- 一个重做后的日志或配置页面可加载。
- 控制台无明显渲染错误。

## 13. 已确认问题与后续确认

已确认：

- 改造目标是全量 Ant Design 企业中后台改造。
- 人员中心是当前可保留的主要业务界面。
- 其他旧页面可以按 Ant Design 标准重做或抛弃。
- 平台契约、路由、权限、metadata 和 standard CRUD 不能因 UI 改造被推倒。
- 平台内核不 Pro Components 化；UI 层通过平台 adapter 使用 Pro Components。

后续进入实施计划前需要确认：

- 第一批重做页面是否选择“日志审计”作为低风险样板。
- Phase 1 只安装 `antd` 与 `@ant-design/icons`；`@ant-design/pro-components` 放到页面模板阶段，以 adapter 方式引入。
- 旧 demo/template/showcase 页面是删除入口、降级为开发入口，还是改成 Ant Design 示例入口。

## 14. 实施拆分建议

### Phase 1：Ant Design Shell

最小交付：

- 安装 `antd`、`@ant-design/icons`。
- 新建 Ant Design theme token。
- 改造 Shell Layout、Header、Sidebar、PageContainer。
- 保留现有路由和菜单来源。

验收：

- 所有受保护页面进入统一 Ant Design 外壳。
- 菜单、Header、内容区、面包屑和页面背景统一。

### Phase 2：Ant Design 页面模板

最小交付：

- 新建 SearchTablePage、SearchForm、ActionButtons、StatusTag、FormDrawer、FormModal、DetailPage。
- 引入 `@ant-design/pro-components`。
- 新模板优先通过平台 adapter 使用 `PageContainer`、`ProTable`、`ProForm`、`ProDescriptions`；基础操作仍可使用 Ant Design 组件。
- adapter 必须从平台契约生成 Pro Components props，不能把平台契约改写成 Pro Components 私有真源。

验收：

- 模板可在测试页或人员中心样板中使用。
- adapter 测试证明权限、metadata 和 standard CRUD contract 仍为输入真源。
- 旧 UI 包 API 暂不删除。

### Phase 3：人员中心 AntD 化

最小交付：

- 标准 CRUD 列表宿主使用 Ant Design Table/Form。
- 人员中心列表、筛选、状态和操作列迁移。
- 人员详情和新建流程迁移。

验收：

- 人员中心主要流程可用。
- lookup、提交、停用、刷新不破坏。

### Phase 4：低风险页面重做

最小交付：

- 重做操作日志和访问日志。
- 重做配置管理或值列表中的一个页面。

验收：

- 查询表格、分页、错误状态、loading 和空数据符合 Ant Design 模板。

### Phase 5：中高复杂页面重做

最小交付：

- 权限中心重做。
- 文件中心重做。
- 插件运维中心重做。

验收：

- 权限、上传、生命周期动作都有明确 loading、确认、错误和刷新。

### Phase 6：元数据配置中心与旧 UI 清理

最小交付：

- 元数据配置中心重做。
- 清理已无引用的 `sh-*` 样式和旧组件。
- 更新模块模板和开发文档。

验收：

- 全量前端 build 通过。
- Shell 测试通过。
- 关键页面 smoke 通过。

## 15. AI-First 最小检查清单

- 入口在哪里：正式实施入口从 `frontend/packages/shell/src/App.tsx`、`frontend/packages/shell/src/components/Layout.tsx`、`frontend/packages/shell/src/components/Header.tsx`、`frontend/packages/shell/src/components/Sidebar.tsx`、`frontend/packages/shell/src/config/antdTheme.ts` 和 `frontend/packages/ui/src` 的 Ant Design 模板开始；人员中心样板入口是 `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx` 与 standard CRUD host。
- 边界在哪里：允许改 Shell 外壳、共享 UI 模板和模块页面 UI；禁止改后端 API、权限 code、runtime metadata 文档结构、ModuleRegistry 注册模式、standard CRUD contract、认证恢复逻辑和路由 fallback 优先级。
- 如何验证：文档阶段运行 `scripts/validate-ai-first-design.ps1` 与 `scripts/check-ai-first-design-targets.ps1`；实现阶段按范围运行 `pnpm --dir frontend --filter @icp-platform/shell test`、`pnpm --dir frontend --filter @icp-platform/shell build`、`pnpm --dir frontend --filter @icp-platform/ui build` 和必要的页面 smoke。
- 失败信号是什么：构建或测试失败、菜单缺失或高亮错误、受保护路由无法进入、人员中心列表/详情/新增/停用流程失效、权限判断失效、页面出现双标题或白屏、控制台出现明显渲染错误、保存/删除/上传/插件动作后未回到后端权威状态。
- 如何回滚：Shell 阶段回退 Ant Design 依赖、theme、Layout、Header、Sidebar、PageContainer 和全局样式；模板阶段回退 `@ant-design/pro-components` 依赖与 `frontend/packages/ui` adapter / 新模板；模块阶段按单模块页面文件回退；人员中心阶段回退 standard CRUD host 或 `organization` 模块页面调用，保留服务层和平台契约不变。

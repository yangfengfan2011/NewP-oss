# nav metadata 统一入口与标准列表宿主页 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让当前前端导航入口从模块 `menus/routes` 迁移到 `nav metadata` 真源，并为 `standard-list` 引入 `StandardListPageHost` 运行时链路，最后用组织模块人员中心完成首个端到端接入。

**Architecture:** 本计划分四段推进。第一段先冻结 `nav metadata -> entryType/targetKey`、模块注册契约和 Shell 路由分流的最小行为；第二段在 Shell 内实现 `standard-list -> StandardListPageHost -> StandardListPage` 与 `custom -> pageKey registry` 两条运行时路径；第三段补齐现有模块的导航 authoring，并把组织模块人员中心从过渡态 `PersonListPage -> StandardListPage` 迁到 `viewKey -> host` 链路；第四段再收紧回退逻辑、完成验证并明确外部依赖。

**Tech Stack:** React 18 / TypeScript / Vite / React Router 6 / Vitest / `@icp-platform/sdk` / `@icp-platform/shell` / `@icp-platform/ui`

---

## 范围说明

本计划以以下设计文档为准：

- `docs/designs/platform/nav-metadata-page-entry-design.md`
- `docs/designs/platform/nav-metadata-page-entry-consistency-review.md`
- `docs/designs/platform/standard-list-page-host-design.md`
- `docs/designs/platform/standard-list-page-host-consistency-review.md`
- `docs/designs/platform/standard-list-page-template-design.md`

本计划**取代** `docs/superpowers/plans/2026-04-21-standard-list-page-template-pilot.md` 作为后续正式实施主线。  
`2026-04-21` 那份计划可保留为过渡实现记录，但不再作为目标架构执行依据。

## 文件结构与职责

**Create:**

- `frontend/packages/shell/src/navigation/pageEntryTypes.ts`  
  定义 `standard-list / custom`、运行时页面入口对象和最小校验帮助方法。
- `frontend/packages/shell/src/navigation/pageEntryAdapter.ts`  
  把 `navDocuments` 转成可供路由消费的页面入口清单。
- `frontend/packages/shell/src/navigation/pageRouteRenderer.tsx`  
  根据页面入口渲染 `StandardListPageHost` 或 custom page component。
- `frontend/packages/shell/src/pages/StandardListPageHost.tsx`  
  标准列表页宿主，只接收 `viewKey`，负责 metadata 装配与 registry 解析。
- `frontend/packages/shell/src/metadata/viewMetadataResolver.ts`  
  提供 `viewKey -> RuntimeViewDocument` 解析与类型校验。
- `frontend/packages/shell/src/registries/listViewRegistry.ts`  
  从模块注册信息生成 `viewKey -> standard-list definition` 映射。
- `frontend/packages/shell/src/registries/customPageRegistry.ts`  
  从模块注册信息生成 `pageKey -> React component` 映射。
- `frontend/packages/shell/src/__tests__/page-entry-routing.test.tsx`
- `frontend/packages/shell/src/__tests__/standard-list-page-host.test.tsx`
- `frontend/modules/organization/src/standard-list/PersonListStandardListEntry.tsx`  
  从原 `PersonListPage` 中抽出人员中心列表的标准列表注册组件。
- `modules/Platform/metadata/navigation/Platform.nav.json`  
  为当前平台侧导航入口补作者源，承接 `audit / authorization / demo / files / metadata-config-center / module-template-sample / plugin-ops` 等平台归口页面。
- `modules/ConfigCenter/metadata/navigation/ConfigCenter.nav.json`  
  为配置中心导航入口补作者源。

**Modify:**

- `frontend/packages/sdk/src/index.ts`
- `frontend/packages/shell/src/App.tsx`
- `frontend/packages/shell/src/components/Layout.tsx`
- `frontend/packages/shell/src/metadata/runtimeMetadataManifest.ts`
- `frontend/packages/shell/src/metadata/navigationAdapter.ts`
- `frontend/packages/shell/src/__tests__/metadata-navigation.test.tsx`
- `frontend/modules/audit/src/index.ts`
- `frontend/modules/authorization/src/index.ts`
- `frontend/modules/config-center/src/index.ts`
- `frontend/modules/demo/src/index.ts`
- `frontend/modules/files/src/index.ts`
- `frontend/modules/metadata-config-center/src/index.ts`
- `frontend/modules/module-template-sample/src/index.ts`
- `frontend/modules/organization/src/index.ts`
- `frontend/modules/organization/src/pages/PersonListPage.tsx`
- `frontend/modules/plugin-ops/src/index.ts`
- `modules/Organization/metadata/navigation/Organization.nav.json`
- `docs/superpowers/plans/2026-04-21-standard-list-page-template-pilot.md`

**Out of Scope in this plan:**

- `detail` 页面模板化
- `page / drawer / dialog` 统一编辑承载
- 非导航子路由的最终 authoring 模型（如 `/organization/persons/new`、`/organization/persons/:id`）
- `view metadata` 结构重写
- Shell 之外的运行时元数据打包系统重构

---

### Task 1：先用测试和契约冻结新的页面入口模型

**Files:**
- Create: `frontend/packages/shell/src/__tests__/page-entry-routing.test.tsx`
- Modify: `frontend/packages/sdk/src/index.ts`
- Modify: `frontend/packages/shell/src/metadata/runtimeMetadataManifest.ts`
- Modify: `frontend/packages/shell/src/__tests__/metadata-navigation.test.tsx`

- [ ] **Step 1: 先写 `nav metadata` 页面入口的失败测试**

```tsx
it('projects nav metadata items into runtime page entries', () => {
  const entries = buildPageEntriesFromNavDocuments([
    {
      type: 'nav',
      navigation: {
        items: [
          {
            key: 'organization.personCenter',
            path: '/organization/persons',
            entryType: 'standard-list',
            targetKey: 'organization.person.list',
          },
          {
            key: 'organization.tree',
            path: '/organization/tree',
            entryType: 'custom',
            targetKey: 'organization.tree.page',
          },
        ],
      },
    },
  ]);

  expect(entries).toEqual([
    { path: '/organization/persons', entryType: 'standard-list', targetKey: 'organization.person.list' },
    { path: '/organization/tree', entryType: 'custom', targetKey: 'organization.tree.page' },
  ]);
});
```

- [ ] **Step 2: 扩展运行时 `nav` 类型，显式支持 `entryType/targetKey`**

```ts
export type RuntimeNavEntryType = 'standard-list' | 'custom';

export interface RuntimeNavItem {
  key: string;
  titleKey?: string;
  path?: string;
  parentKey?: string;
  order?: number;
  permissions?: string[];
  shellViews?: string[];
  entryType?: RuntimeNavEntryType;
  targetKey?: string;
}
```

- [ ] **Step 3: 给 SDK 增加标准列表和自定义页面注册契约**

```ts
export interface StandardListPageDefinition {
  title: string;
  description?: string;
  headerActions?: ReactNode;
  filterBar?: ReactNode;
  content: ReactNode;
  footerLeft?: ReactNode;
  footerRight?: ReactNode;
  advancedFilterOpen?: boolean;
  advancedFilterContent?: ReactNode;
  onAdvancedFilterClose?: () => void;
}

export interface StandardListViewRendererProps {
  viewKey: string;
  viewMetadata: Record<string, unknown>;
  children: (definition: StandardListPageDefinition) => ReactNode;
}

export interface StandardListViewRegistration {
  viewKey: string;
  component: ComponentType<StandardListViewRendererProps>;
}

export interface CustomPageRegistration {
  pageKey: string;
  component: ComponentType;
}

export interface FrontendModule {
  metadata: ModuleMetadata;
  standardListViews?: StandardListViewRegistration[];
  customPages?: CustomPageRegistration[];
  routes?: RouteConfig[];
  menus?: MenuConfig[];
}
```

- [ ] **Step 4: 给 `metadata-navigation.test.tsx` 补上 `entryType/targetKey` 断言**

Run:

```powershell
cd frontend
pnpm --filter @icp-platform/shell test -- metadata-navigation
```

Expected:

```text
FAIL
```

- [ ] **Step 5: 构建 SDK，确认新增类型先可编译**

Run:

```powershell
cd frontend
pnpm --filter @icp-platform/sdk build
```

Expected:

```text
... build completed successfully ...
```

### Task 2：在 Shell 中建立 metadata 驱动的页面分流链路

**Files:**
- Create: `frontend/packages/shell/src/navigation/pageEntryTypes.ts`
- Create: `frontend/packages/shell/src/navigation/pageEntryAdapter.ts`
- Create: `frontend/packages/shell/src/navigation/pageRouteRenderer.tsx`
- Modify: `frontend/packages/shell/src/App.tsx`
- Modify: `frontend/packages/shell/src/components/Layout.tsx`

- [ ] **Step 1: 先实现页面入口适配器，让 `navDocuments` 同时产出菜单和页面入口**

```ts
export interface RuntimePageEntry {
  path: string;
  entryType: 'standard-list' | 'custom';
  targetKey: string;
  permissions?: string[];
}

export function buildPageEntriesFromNavDocuments(navDocuments: RuntimeNavItemDocument[]): RuntimePageEntry[] {
  return navDocuments
    .flatMap((document) => document.navigation.items)
    .filter((item) => item.path && item.entryType && item.targetKey)
    .map((item) => ({
      path: item.path as string,
      entryType: item.entryType as 'standard-list' | 'custom',
      targetKey: item.targetKey as string,
      permissions: item.permissions,
    }));
}
```

- [ ] **Step 2: 保持“metadata 优先、模块菜单回退”的菜单切换逻辑，直到全量 authoring 稳定**

```tsx
const metadataMenus = buildMenusFromNavDocuments(runtimeMetadata.navDocuments, (titleKey, fallback) =>
  t(titleKey ?? '', fallback),
);

const menus = metadataMenus.length > 0 ? metadataMenus : ModuleRegistry.getMenus();
return <Sidebar menus={menus} />;
```

- [ ] **Step 3: 在 `AppRoutes` 中引入页面入口分流，并保留模块路由回退**

```tsx
const metadataEntries = buildPageEntriesFromNavDocuments(runtimeMetadata.navDocuments);

{metadataEntries.map((entry) => (
  <Route
    key={entry.path}
    path={entry.path}
    element={renderPageEntry(entry)}
  />
))}
```

- [ ] **Step 4: 暂时保留 `ModuleRegistry.getRoutes()` 作为非导航子路由和无运行时清单场景的兼容回退**

```tsx
const fallbackRoutes = metadataEntries.length > 0
  ? ModuleRegistry.getRoutes().filter((route) =>
      !metadataEntries.some((entry) => entry.path === route.path),
    )
  : ModuleRegistry.getRoutes();
```

- [ ] **Step 5: 跑 Shell 定向测试，确认 `nav metadata` 既能出菜单也能出路由**

Run:

```powershell
cd frontend
pnpm --filter @icp-platform/shell test -- page-entry-routing
pnpm --filter @icp-platform/shell build
```

Expected:

```text
Passed
... build completed successfully ...
```

### Task 3：实现 `StandardListPageHost` 与两套注册表

**Files:**
- Create: `frontend/packages/shell/src/pages/StandardListPageHost.tsx`
- Create: `frontend/packages/shell/src/metadata/viewMetadataResolver.ts`
- Create: `frontend/packages/shell/src/registries/listViewRegistry.ts`
- Create: `frontend/packages/shell/src/registries/customPageRegistry.ts`
- Create: `frontend/packages/shell/src/__tests__/standard-list-page-host.test.tsx`
- Modify: `frontend/packages/shell/src/navigation/pageRouteRenderer.tsx`

- [ ] **Step 1: 先写宿主页失败测试，冻结缺元数据和缺注册时的行为**

```tsx
it('renders an explicit error block when view metadata is missing', () => {
  render(
    <RuntimeMetadataProvider value={{ manifest: { modules: [] }, navDocuments: [], viewDocuments: [], modelDocuments: [] }}>
      <StandardListPageHost viewKey="organization.person.list" />
    </RuntimeMetadataProvider>,
  );

  expect(screen.getByText('未找到列表视图元数据')).toBeInTheDocument();
});
```

- [ ] **Step 2: 实现 `viewKey -> view metadata` 解析器**

```ts
export function resolveListViewDocument(
  viewDocuments: RuntimeViewDocument[],
  viewKey: string,
): RuntimeViewDocument | null {
  return viewDocuments.find((document) => {
    const view = document.view as { key?: string; type?: string };
    return view.key === viewKey && (view.type === undefined || view.type === 'list');
  }) ?? null;
}
```

- [ ] **Step 3: 用模块注册信息生成两套运行时注册表**

```ts
export function buildListViewRegistry(modules: FrontendModule[]): Map<string, StandardListViewRegistration> {
  return new Map(
    modules.flatMap((module) => (module.standardListViews ?? []).map((entry) => [entry.viewKey, entry] as const)),
  );
}

export function buildCustomPageRegistry(modules: FrontendModule[]): Map<string, CustomPageRegistration> {
  return new Map(
    modules.flatMap((module) => (module.customPages ?? []).map((entry) => [entry.pageKey, entry] as const)),
  );
}
```

- [ ] **Step 4: 实现 `StandardListPageHost`，只接 `viewKey` 并渲染 `StandardListPage`**

```tsx
export default function StandardListPageHost({ viewKey }: { viewKey: string }) {
  const runtimeMetadata = useRuntimeMetadata();
  const viewDocument = resolveListViewDocument(runtimeMetadata.viewDocuments, viewKey);
  const registry = buildListViewRegistry(ModuleRegistry.getAllModules());
  const registration = registry.get(viewKey);

  if (!viewDocument) {
    return <div className="sh-error-state">未找到列表视图元数据</div>;
  }

  if (!registration) {
    return <div className="sh-error-state">未找到列表页注册项</div>;
  }

  const EntryComponent = registration.component;

  return (
    <EntryComponent
      viewKey={viewKey}
      viewMetadata={viewDocument.view as Record<string, unknown>}
    >
      {(definition) => <StandardListPage {...definition} />}
    </EntryComponent>
  );
}
```

- [ ] **Step 5: 让页面分流器真正落到宿主页和 custom registry**

```tsx
export function renderPageEntry(entry: RuntimePageEntry): React.ReactElement {
  if (entry.entryType === 'standard-list') {
    return <StandardListPageHost viewKey={entry.targetKey} />;
  }

  const component = buildCustomPageRegistry(ModuleRegistry.getAllModules()).get(entry.targetKey)?.component;
  return component ? React.createElement(component) : <div className="sh-error-state">未找到自定义页面注册项</div>;
}
```

- [ ] **Step 6: 跑宿主页与 Shell 测试**

Run:

```powershell
cd frontend
pnpm --filter @icp-platform/shell test -- standard-list-page-host
pnpm --filter @icp-platform/shell test
```

Expected:

```text
Passed
```

### Task 4：迁移现有模块注册信息，并补齐全量导航 authoring

**Files:**
- Modify: `frontend/modules/audit/src/index.ts`
- Modify: `frontend/modules/authorization/src/index.ts`
- Modify: `frontend/modules/config-center/src/index.ts`
- Modify: `frontend/modules/demo/src/index.ts`
- Modify: `frontend/modules/files/src/index.ts`
- Modify: `frontend/modules/metadata-config-center/src/index.ts`
- Modify: `frontend/modules/module-template-sample/src/index.ts`
- Modify: `frontend/modules/organization/src/index.ts`
- Modify: `frontend/modules/organization/src/pages/PersonListPage.tsx`
- Create: `frontend/modules/organization/src/standard-list/PersonListStandardListEntry.tsx`
- Modify: `frontend/modules/plugin-ops/src/index.ts`
- Create: `modules/Platform/metadata/navigation/Platform.nav.json`
- Create: `modules/ConfigCenter/metadata/navigation/ConfigCenter.nav.json`
- Modify: `modules/Organization/metadata/navigation/Organization.nav.json`

- [ ] **Step 1: 先给每个现有可导航页面冻结稳定 key 清单**

```ts
// standard-list
'organization.person.list'

// custom
'organization.tree.page'
'organization.positions.page'
'audit.operation-logs.page'
'authorization.roles.page'
'config-center.items.page'
'demo.home.page'
'demo.list.page'
'files.home.page'
'metadata-config.modules.page'
'module-template-sample.home.page'
'plugin-ops.home.page'
```

- [ ] **Step 2: 把各模块从 `menus/routes` 主注册改成 `standardListViews/customPages`**

```ts
export const organizationModule: FrontendModule = {
  metadata: { ... },
  standardListViews: [
    {
      viewKey: 'organization.person.list',
      component: PersonListStandardListEntry,
    },
  ],
  customPages: [
    { pageKey: 'organization.tree.page', component: OrganizationTreePage },
    { pageKey: 'organization.positions.page', component: PositionManagementPage },
  ],
  routes: [
    { path: '/organization/persons/new', component: PersonCreatePage, requireAuth: true, permissions: ['Organization.Person.View'] },
    { path: '/organization/persons/:id', component: PersonDetailPage, requireAuth: true, permissions: ['Organization.Person.View'] },
  ],
};
```

- [ ] **Step 3: 从原 `PersonListPage` 中抽出标准列表注册组件，让业务页不再承担运行时入口职责**

```tsx
export default function PersonListStandardListEntry({
  children,
}: StandardListViewRendererProps) {
  const definition = usePersonListDefinition();
  return <>{children(definition)}</>;
}

export default function PersonListPage() {
  // 仅保留为兼容包装页或现有测试入口，运行时主入口改由 nav metadata + host 承接。
  return <StandardListPageHost viewKey="organization.person.list" />;
}
```

- [ ] **Step 4: 为所有现有导航入口补 `nav metadata`，并按模块归属收敛到现有 metadata 目录**

```json
{
  "schemaVersion": "1.0",
  "type": "nav",
  "navigation": {
    "items": [
      {
        "key": "organization.personCenter",
        "titleKey": "Navigation.PersonCenter",
        "parentKey": "organization",
        "path": "/organization/persons",
        "order": 110,
        "entryType": "standard-list",
        "targetKey": "organization.person.list"
      },
      {
        "key": "plugin-ops.home",
        "titleKey": "Navigation.PluginOperations",
        "path": "/plugin-ops",
        "order": 30,
        "entryType": "custom",
        "targetKey": "plugin-ops.home.page"
      }
    ]
  }
}
```

作者源归属约定：

- `modules/Organization/metadata/navigation/Organization.nav.json`
  - 组织模块入口，如人员中心、组织树、岗位管理
- `modules/ConfigCenter/metadata/navigation/ConfigCenter.nav.json`
  - 配置中心入口
- `modules/Platform/metadata/navigation/Platform.nav.json`
  - 平台归口前端模块入口，如审计、权限、演示、文件、元数据配置中心、模块模板示例、插件运维

- [ ] **Step 5: 保留模块 `routes` 只承接当前未进入 `nav metadata` 的非导航子路由**

Run:

```powershell
cd frontend
pnpm --filter @icp-modules/organization build
pnpm --filter @icp-platform/shell test
pnpm build
```

Expected:

```text
... build completed successfully ...
Passed
```

### Task 5：验证切换结果，收紧回退并记录外部依赖

**Files:**
- Modify: `docs/superpowers/plans/2026-04-21-standard-list-page-template-pilot.md`
- Modify: `docs/superpowers/plans/2026-04-22-nav-metadata-standard-list-entry.md`

- [ ] **Step 1: 做一次运行时链路 smoke，确认在 `nav metadata` 存在时主导航优先走 metadata**

Run:

```powershell
cd frontend
pnpm --filter @icp-platform/shell test -- "metadata-navigation|page-entry-routing|standard-list-page-host|organization-person-center-list-layout"
```

Expected:

```text
Passed
```

- [ ] **Step 2: 做人员中心端到端 smoke，确认真实入口已经是 `standard-list -> host -> template`**

检查项：

- `/organization/persons` 从 `nav metadata` 进入
- Shell 路由命中 `StandardListPageHost(viewKey='organization.person.list')`
- `PersonListStandardListEntry` 提供列表数据、筛选、行动作和编辑抽屉
- `StandardListPage` 只负责渲染骨架

- [ ] **Step 3: 把旧计划标记为“过渡实现已完成，但不再作为后续执行主线”**

```md
## 状态

- `StandardListPage` 已落地为渲染内核
- `PersonListPage -> StandardListPage` 仅保留为过渡记录
- 后续正式实施请转到 `docs/superpowers/plans/2026-04-22-nav-metadata-standard-list-entry.md`
```

- [ ] **Step 4: 明确 repo 内外依赖边界**

记录项：

- 若 `/plugins/metadata-runtime-manifest.json` 的生成链路仍不在本仓库，本计划的 repo 内完成定义应止于 authoring 文件、Shell 消费与测试夹具
- 若后续补齐生成链路，需另起一份设计与计划，不得在本任务里静默塞入
- 在该外部链路未纳入前，本计划保留 `metadata 优先 + ModuleRegistry 回退` 的兼容路径

- [ ] **Step 5: 形成最终验证结论**

应明确输出：

- 改了什么
- 影响了哪些文件和模块
- 构建/测试结果
- 已知风险
- 回滚思路

## 当前执行记录

### 已完成实现核对

- 已确认 `frontend/modules/organization/src/index.ts` 通过 `standardListViews` 注册 `organization.person.list`，并通过 `customPages` 注册组织树、岗位管理页面。
- 已确认 `frontend/modules/organization/src/pages/PersonListPage.tsx` 保留兼容包装入口，主运行时入口已经转到 `nav metadata -> StandardListPageHost -> PersonListStandardListEntry -> StandardListPage`。
- 已确认 `frontend/modules/organization/src/standard-list/PersonListStandardListEntry.tsx` 承接原人员中心列表的业务状态、查询、动作和抽屉逻辑。
- 已确认平台侧与配置侧模块都补齐了 `customPages` 注册，并与 `modules/Platform/metadata/navigation/Platform.nav.json`、`modules/ConfigCenter/metadata/navigation/ConfigCenter.nav.json`、`modules/Organization/metadata/navigation/Organization.nav.json` 中的 `targetKey` 对齐。

### 已完成验证

- `cd frontend && pnpm --filter @icp-platform/shell test -- page-entry-routing standard-list-page-host metadata-navigation page-route-renderer`
  - 结果：通过，`4` 个测试文件、`15` 个测试用例全部通过。
- `cd frontend && pnpm --filter @icp-platform/shell test -- organization-person-center-list-layout`
  - 结果：通过，`1` 个测试文件、`2` 个测试用例全部通过，覆盖人员中心列表页骨架、动作区和高级筛选抽屉的页面级渲染行为。
- `cd frontend && pnpm --filter @icp-platform/shell build`
  - 结果：通过。
- `cd frontend && pnpm build`
  - 结果：通过，前端 workspace 各包均已构建成功。
- `dotnet build ICP-Platform.sln`
  - 结果：通过。
- `dotnet test tests/MetadataConfigCenter.Tests/MetadataConfigCenter.Tests.csproj --logger "console;verbosity=minimal"`
  - 结果：通过，`21` 个测试全部通过；已验证补齐 `nav` type mapping 后，元数据配置中心不会再把 `*.nav.json` 视为非法类型。
- `dotnet test tests/Kernel.Tests/Kernel.Tests.csproj --filter "FullyQualifiedName~MetadataRuntimeManifestBuilderTests|FullyQualifiedName~RuntimeViewMaterializerTests" --logger "console;verbosity=minimal"`
  - 结果：通过，`21` 个测试全部通过；已验证 runtime manifest 与运行时物化链路能识别新增 `navigation` authoring。
- `dotnet test tests/Organization.Tests/Organization.Tests.csproj --logger "console;verbosity=minimal"`
  - 结果：通过，`41` 个测试全部通过。
- 浏览器级 smoke：
  - 使用 `ASPNETCORE_ENVIRONMENT=Development` 启动 `HostApi.dll`（`--contentRoot apps/host-api`）后，`http://localhost:5000/swagger/index.html` 返回 `200`。
  - 使用 Edge headless + Selenium 登录 `http://localhost:3000/login`，凭据 `admin / admin123` 登录成功后访问 `http://localhost:3000/organization/persons`。
  - 已确认 live 页面包含“人员中心 / 关键字 / 新建”，并生成截图 `.codex-temp/organization-persons-smoke.png`。

### 已知验证限制

- `cd frontend && pnpm test`
  - 结果：失败，原因不是代码失败，而是根 workspace 未定义统一 `test` 脚本；当前前端测试需按包级命令执行。
- `dotnet test ICP-Platform.sln --no-build --logger "console;verbosity=minimal"`
  - 结果：失败，但当前剩余失败不再是本轮改动引入的回归。
  - `MetadataConfigCenter.Tests` 已在补齐 `nav` type mapping 后单独回归通过。
  - 剩余失败集中在 `Kernel.Tests` 的 `PluginDiscoveryTests` 与若干 PowerShell 脚本测试，表现为插件扫描结果不匹配，以及测试进程内拉起 `powershell.exe` 时出现“拒绝访问”。
  - 这些失败超出本轮 `nav metadata / standard-list host` 变更范围，因此未在本任务中继续扩面修复。
- live dev server 下的 `/plugins/metadata-runtime-manifest.json` 与 `/plugins/runtime/...` 当前返回的是 Vite `index.html` 回退页，而不是实际 JSON。
  - 因此真实浏览器 smoke 只能验证“兼容入口仍然可工作”，不能把它当作 `nav metadata -> host` 运行时清单链路已在 dev server 中生效的证据。
  - `nav metadata` 分流本身仍以 `page-entry-routing`、`standard-list-page-host`、`metadata-navigation`、`MetadataRuntimeManifestBuilderTests`、`RuntimeViewMaterializerTests` 这些仓库内测试为主证据。

### repo 内外依赖边界

- 当前仓库内已完成的范围，收口在 `nav metadata` authoring、Shell 运行时消费、模块注册表和测试夹具。
- `metadata runtime manifest` 的生成/发布链路若仍在仓库外，本轮不静默把该链路拉回仓库实现。
- 在外部链路未正式纳入前，继续保留“`metadata` 优先 + `ModuleRegistry` 回退”的兼容路径，避免把运行时切换建立在隐式顺序或外部状态之上。

### 当前结论

- 本轮已完成 `nav metadata` 统一入口、`standard-list` 宿主页链路和组织模块人员中心试点接入的仓库内实现。
- 影响范围主要在 `frontend/packages/shell`、`frontend/packages/sdk`、组织/平台相关前端模块入口，以及 `modules/*/metadata/navigation/*.nav.json`。
- 前端定向测试、前端整仓构建、相关后端构建/测试，以及一次真实浏览器登录与页面打开 smoke 均已给出通过证据。
- 当前主要风险不在实现代码，而在仓库外的 metadata 清单生成链路尚未纳入，导致 live dev server 不能直接产出 runtime metadata JSON；同时全量 `Kernel.Tests` 仍存在与本轮改动无关的既有/环境型失败。
- 若需回滚，可先回退 `App.tsx` / `Layout.tsx` 的 metadata 分流消费、删除新增注册表与宿主页接入，并恢复模块 `routes/menus` 作为唯一入口；该回滚不会要求改动既有 `view metadata` 结构。

---

## Self-Review

- **Spec coverage:** 本计划覆盖了当前已确认的三层目标：`nav metadata` 统一入口、`standard-list` 宿主页链路、人员中心首个试点接入，同时把现有模块导航 authoring 一并纳入。
- **Placeholder scan:** 计划中的新增文件、修改文件、测试文件和验证命令都给出了明确路径，没有使用 `TBD / TODO / 后续补充` 这类占位语句。
- **Type consistency:** 全文统一使用 `standard-list / custom / targetKey / viewKey / pageKey / StandardListPageHost / StandardListPage`，与现有设计文档保持一致。

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-22-nav-metadata-standard-list-entry.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?

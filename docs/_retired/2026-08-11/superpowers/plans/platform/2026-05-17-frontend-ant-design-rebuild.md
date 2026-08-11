# 前端 Ant Design 全量改造 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将前端 UI 层全量改造为 Ant Design / Pro Components 企业级中后台风格，同时保留 runtime metadata、ModuleRegistry、权限、路由、standard CRUD contract 和人员中心业务资产。

**Architecture:** 平台内核不 Pro Components 化，UI 层通过 adapter 使用 Ant Design / Pro Components。Shell 先用 Ant Design 基础组件重建全局布局，页面模板阶段再引入 `@ant-design/pro-components`，由平台 adapter 将 metadata、权限、standard CRUD 和服务层投影到 `PageContainer`、`ProTable`、`ProForm`、`ProDescriptions` 等 UI 承载组件。

**Tech Stack:** React 18 / TypeScript / Vite / pnpm workspace / Ant Design v5 / Ant Design Pro Components / Vitest

---

## 文件结构与职责

**Create**

- `frontend/packages/shell/src/config/antdTheme.ts`  
  定义 Ant Design 企业级主题 token，包括主色、状态色、圆角、字体、布局背景和组件 token。
- `frontend/packages/shell/src/navigation/menuProjection.tsx`  
  将平台 `MenuConfig` 投影为 Ant Design Menu items；只做展示适配，不改变菜单真源、权限、路由或 metadata。
- `frontend/packages/shell/src/navigation/pageChrome.ts`  
  根据当前路径和菜单树推导页面标题、面包屑、openKeys 和 selectedKeys。
- `frontend/packages/shell/src/components/PageContainer.tsx`  
  Shell 级页面容器，Phase 1 先基于 Ant Design 基础组件；Phase 2 再评估切换为 Pro Components `PageContainer` adapter。
- `frontend/packages/shell/src/__tests__/antd-shell-layout.test.tsx`  
  覆盖 Shell 布局、菜单投影、折叠、标题和面包屑。
- `frontend/packages/shell/src/__tests__/metadata-navigation.test.tsx`  
  Phase 1 更新。适配 Sidebar 新增的折叠受控参数，继续验证 metadata 菜单渲染。
- `frontend/packages/shell/src/__tests__/ui-showcase-navigation.test.tsx`  
  Phase 1 更新。适配 Sidebar 新增的折叠受控参数，继续验证 UI Showcase 兜底入口。
- `frontend/packages/ui/src/pro-adapters/SearchTablePage.tsx`  
  Phase 2 新增。平台查询表格页 adapter，消费平台契约并渲染 Pro Components。
- `frontend/packages/ui/src/pro-adapters/FormPage.tsx`  
  Phase 2 新增。平台表单页 adapter，消费字段、权限、提交状态和错误状态并渲染 Pro Components。
- `frontend/packages/ui/src/pro-adapters/DetailPage.tsx`  
  Phase 2 新增。平台详情页 adapter，消费详情 schema、summary block 和 actions 并渲染 Pro Components。
- `frontend/packages/ui/src/pro-adapters/ActionButtons.tsx`  
  Phase 2 新增。统一查看、编辑、删除、启用、禁用、发布、回滚和更多操作。
- `frontend/packages/ui/src/pro-adapters/StatusTag.tsx`  
  Phase 2 新增。统一状态展示。
- `frontend/packages/shell/src/__tests__/pro-components-adapter-contract.test.tsx`  
  Phase 2 新增。证明 Pro Components adapter 不替代平台契约。
- `docs/verification/frontend-ant-design-rebuild-verification.md`  
  记录每个阶段的验证命令、结果、风险和回滚口径。

**Modify**

- `frontend/packages/shell/package.json`  
  Phase 1 添加 `antd`、`@ant-design/icons`，用于 Shell 全局布局。
- `frontend/packages/ui/package.json`  
  Phase 2 添加 `antd`、`@ant-design/icons`、`@ant-design/pro-components`，因为 Pro adapter 位于 UI 包并直接 import 这些组件。
- `frontend/pnpm-lock.yaml`  
  随依赖变更更新。
- `frontend/packages/shell/src/App.tsx`  
  注入 Ant Design `ConfigProvider`。
- `frontend/packages/shell/src/components/Layout.tsx`  
  改造为 Ant Design `Layout` 结构，保留 `Outlet` 和 ErrorBoundary。
- `frontend/packages/shell/src/components/Header.tsx`  
  改造为企业后台 Header，保留视图切换、用户信息、退出登录。
- `frontend/packages/shell/src/components/Sidebar.tsx`  
  改造为受控 Ant Design `Sider` / `Menu`，使用平台菜单投影。
- `frontend/packages/shell/src/__tests__/metadata-navigation.test.tsx`  
  更新 `<Sidebar />` 调用，传入 `collapsed={false}` 和 `onCollapse={() => undefined}`。
- `frontend/packages/shell/src/__tests__/ui-showcase-navigation.test.tsx`  
  更新 `<Sidebar />` 调用，传入 `collapsed={false}` 和 `onCollapse={() => undefined}`。
- `frontend/packages/shell/src/index.css`  
  收敛全局背景、Shell 尺寸、布局和旧 `sh-*` 兼容样式。
- `frontend/packages/ui/src/index.ts`  
  Phase 2 导出新的 Pro adapter 和 Ant Design 模板。
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`  
  Phase 3 接入新的查询表格 adapter，人员中心首先验证。
- `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`  
  Phase 3 接入详情和表单 adapter。
- `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`  
  Phase 3 保留字段、lookup、动作和 mapper，按 adapter 需要做最小声明调整。
- `frontend/modules/audit/src/pages/OperationLogs.tsx`  
  Phase 4 重做为 Ant Design / Pro Components 查询表格页。
- `frontend/modules/audit/src/pages/AccessLogs.tsx`  
  Phase 4 重做为 Ant Design / Pro Components 查询表格页。
- `docs/superpowers/specs/platform/2026-05-17-frontend-ant-design-rebuild-design.md`  
  若实施中发现设计决策变化，先更新设计并 review。
- `docs/phases/current-handoff.md`  
  每个阶段完成后回填进展、验证快照、剩余风险和下一步。

**Do Not Modify**

- `server/**`
- `apps/host-api/**`
- `modules/*/metadata/**/*.json`，除非后续有独立菜单信息架构设计。
- `frontend/packages/sdk/src/**`，除非后续模板 adapter 发现 SDK 契约必须补充，并先更新设计。
- `frontend/packages/shell/src/registerModules.ts`
- `frontend/packages/shell/src/metadata/runtimeMetadataLoader.ts`
- `frontend/packages/shell/src/navigation/pageEntryAdapter.ts`
- `frontend/packages/shell/src/navigation/standardCrudRouteAdapter.ts`
- `scripts/build.ps1`
- `scripts/test.ps1`

---

### Task 0: 实施前一致性 Review

**Files:**
- Read: `AGENTS.md`
- Read: `docs/frontend-ant-design-rebuild-analysis.md`
- Read: `docs/superpowers/specs/platform/2026-05-17-frontend-ant-design-rebuild-design.md`
- Read: `docs/phases/current-handoff.md`
- Read: `frontend/packages/shell/src/App.tsx`
- Read: `frontend/packages/shell/src/components/Layout.tsx`
- Read: `frontend/packages/shell/src/components/Header.tsx`
- Read: `frontend/packages/shell/src/components/Sidebar.tsx`
- Read: `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`

- [x] **Step 1: 检查工作区状态**

Run:

```powershell
git status --short
```

Expected: 允许看到本设计和计划文档；若前端源码已有未提交改动，先阅读对应 diff，不覆盖用户改动。

- [x] **Step 2: 确认 Pro Components 决策**

Run:

```powershell
rg -n "平台内核不 Pro Components 化|adapter|ProTable|ProForm|ProDescriptions" docs/superpowers/specs/platform/2026-05-17-frontend-ant-design-rebuild-design.md
```

Expected: 输出显示平台内核不 Pro Components 化，UI 层通过 adapter 使用 Pro Components。

- [x] **Step 3: 确认 Phase 1 不触碰平台内核**

Run:

```powershell
rg -n "Do Not Modify|runtime metadata|ModuleRegistry|standard CRUD|权限" docs/superpowers/plans/platform/2026-05-17-frontend-ant-design-rebuild.md
```

Expected: 输出显示 Phase 1 禁止修改 runtime metadata、ModuleRegistry、权限、路由和 standard CRUD contract。

---

### Task 1: Phase 1 RED - Shell 布局测试

**Files:**
- Create: `frontend/packages/shell/src/__tests__/antd-shell-layout.test.tsx`
- Read: `frontend/packages/shell/src/components/Layout.tsx`
- Read: `frontend/packages/shell/src/components/Sidebar.tsx`
- Read: `frontend/packages/shell/src/components/Header.tsx`

- [x] **Step 1: 写失败测试**

Create `frontend/packages/shell/src/__tests__/antd-shell-layout.test.tsx`:

```tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { Sidebar } from '../components/Sidebar';
import { PageContainer } from '../components/PageContainer';

vi.mock('../contexts/PlatformContext', () => ({
  usePlatform: () => ({
    shellView: 'business',
  }),
}));

vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    hasAnyPermission: () => true,
  }),
}));

describe('Ant Design shell layout', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    if (container) {
      container.remove();
    }
  });

  it('renders an enterprise sidebar with collapse control and nested menus', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <MemoryRouter initialEntries={['/organization/persons']}>
          <Sidebar
            menus={[
              {
                id: 'organization',
                label: '组织用户',
                children: [
                  {
                    id: 'organization-persons',
                    label: '人员中心',
                    path: '/organization/persons',
                  },
                ],
              },
            ]}
            collapsed={false}
            onCollapse={() => undefined}
          />
        </MemoryRouter>,
      );
    });

    expect(container.textContent).toContain('ICP Platform');
    expect(container.textContent).toContain('组织用户');
    expect(container.textContent).toContain('人员中心');
    expect(container.querySelector('[aria-label="折叠菜单"]')).not.toBeNull();
  });

  it('renders title, breadcrumb and content in PageContainer', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <MemoryRouter>
          <PageContainer
            title="人员中心"
            breadcrumbItems={[
              { title: '工作台', path: '/' },
              { title: '组织用户' },
              { title: '人员中心' },
            ]}
          >
            <div>人员中心内容</div>
          </PageContainer>
        </MemoryRouter>,
      );
    });

    expect(container.textContent).toContain('工作台');
    expect(container.textContent).toContain('组织用户');
    expect(container.textContent).toContain('人员中心内容');
  });
});
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- src/__tests__/antd-shell-layout.test.tsx
```

Expected: FAIL，因为 `PageContainer` 尚不存在，且 `Sidebar` 尚未支持 `collapsed` / `onCollapse`。

---

### Task 2: Phase 1 GREEN - 引入 Ant Design 基础依赖

**Files:**
- Modify: `frontend/packages/shell/package.json`
- Modify: `frontend/pnpm-lock.yaml`

- [x] **Step 1: 安装 Phase 1 依赖**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell add antd @ant-design/icons
```

Expected: `frontend/packages/shell/package.json` 增加 `antd` 和 `@ant-design/icons`；`frontend/pnpm-lock.yaml` 更新。

- [x] **Step 2: 确认未引入 Pro Components**

Run:

```powershell
rg -n "@ant-design/pro-components" frontend/packages/shell/package.json frontend/pnpm-lock.yaml
```

Expected: 无输出。Phase 1 不引入 Pro Components。

---

### Task 3: Phase 1 GREEN - 主题与菜单投影

**Files:**
- Create: `frontend/packages/shell/src/config/antdTheme.ts`
- Create: `frontend/packages/shell/src/navigation/menuProjection.tsx`
- Create: `frontend/packages/shell/src/navigation/pageChrome.ts`

- [x] **Step 1: 创建 Ant Design theme**

Create `frontend/packages/shell/src/config/antdTheme.ts`:

```ts
import type { ThemeConfig } from 'antd';

export const shellAntdTheme: ThemeConfig = {
  token: {
    colorPrimary: '#1677ff',
    colorSuccess: '#52c41a',
    colorWarning: '#faad14',
    colorError: '#ff4d4f',
    colorInfo: '#1677ff',
    borderRadius: 6,
    fontSize: 14,
    colorBgLayout: '#f5f5f5',
    colorBgContainer: '#ffffff',
  },
  components: {
    Layout: {
      headerBg: '#ffffff',
      siderBg: '#001529',
      bodyBg: '#f5f5f5',
      headerHeight: 56,
    },
    Menu: {
      itemBorderRadius: 4,
      itemHeight: 40,
    },
    Table: {
      headerBg: '#fafafa',
      rowHoverBg: '#f5faff',
    },
  },
};
```

- [x] **Step 2: 创建菜单投影 adapter**

Create `frontend/packages/shell/src/navigation/menuProjection.tsx`:

```tsx
import type { MenuProps } from 'antd';
import type { MenuConfig } from '@icp-platform/sdk';

type AntdMenuItems = Required<MenuProps>['items'];

export function projectMenusToAntdItems(
  menus: MenuConfig[],
  canViewMenu: (menu: MenuConfig) => boolean,
): AntdMenuItems {
  return menus.flatMap((menu) => {
    if (!canViewMenu(menu)) {
      return [];
    }

    const children = menu.children
      ? projectMenusToAntdItems(menu.children, canViewMenu)
      : undefined;

    return [{
      key: menu.path ?? menu.id,
      label: menu.label,
      children: children && children.length > 0 ? children : undefined,
    }];
  });
}

export function collectMenuPaths(menus: MenuConfig[]): Array<{ id: string; label: string; path?: string; parentIds: string[] }> {
  const entries: Array<{ id: string; label: string; path?: string; parentIds: string[] }> = [];

  const visit = (items: MenuConfig[], parentIds: string[]) => {
    for (const item of items) {
      entries.push({
        id: item.id,
        label: item.label,
        path: item.path,
        parentIds,
      });

      if (item.children) {
        visit(item.children, [...parentIds, item.id]);
      }
    }
  };

  visit(menus, []);
  return entries;
}
```

- [x] **Step 3: 创建页面 chrome 推导工具**

Create `frontend/packages/shell/src/navigation/pageChrome.ts`:

```ts
import type { MenuConfig } from '@icp-platform/sdk';

export interface PageChrome {
  title: string;
  breadcrumbItems: Array<{ title: string; path?: string }>;
  selectedKeys: string[];
  openKeys: string[];
}

export function resolvePageChrome(menus: MenuConfig[], pathname: string): PageChrome {
  const trail = findTrail(menus, pathname) ?? [];
  const last = trail[trail.length - 1];

  return {
    title: last?.label ?? '工作台',
    breadcrumbItems: [
      { title: '工作台', path: '/' },
      ...trail.map((item) => ({ title: item.label, path: item.path })),
    ],
    selectedKeys: last ? [last.path ?? last.id] : [],
    openKeys: trail.slice(0, -1).map((item) => item.path ?? item.id),
  };
}

function findTrail(menus: MenuConfig[], pathname: string): MenuConfig[] | null {
  for (const menu of menus) {
    if (menu.path === pathname) {
      return [menu];
    }

    if (menu.children) {
      const childTrail = findTrail(menu.children, pathname);
      if (childTrail) {
        return [menu, ...childTrail];
      }
    }
  }

  return null;
}
```

- [x] **Step 4: 运行测试确认仍失败**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- src/__tests__/antd-shell-layout.test.tsx
```

Expected: 仍 FAIL，因为 Layout、Sidebar、PageContainer 尚未接入。

---

### Task 4: Phase 1 GREEN - Shell 外壳改造

**Files:**
- Modify: `frontend/packages/shell/src/App.tsx`
- Modify: `frontend/packages/shell/src/components/Layout.tsx`
- Modify: `frontend/packages/shell/src/components/Header.tsx`
- Modify: `frontend/packages/shell/src/components/Sidebar.tsx`
- Create: `frontend/packages/shell/src/components/PageContainer.tsx`
- Modify: `frontend/packages/shell/src/index.css`

- [x] **Step 1: 在 App 注入 ConfigProvider**

Modify `frontend/packages/shell/src/App.tsx`:

```tsx
import { ConfigProvider } from 'antd';
import { shellAntdTheme } from './config/antdTheme';
```

Wrap the existing providers inside:

```tsx
<ConfigProvider theme={shellAntdTheme}>
  <BrowserRouter>
    {/* existing LocalizationProvider / AuthProvider / PlatformProvider tree */}
  </BrowserRouter>
</ConfigProvider>
```

- [x] **Step 2: 新建 PageContainer**

Create `frontend/packages/shell/src/components/PageContainer.tsx`:

```tsx
import type { ReactNode } from 'react';
import { Breadcrumb, Typography } from 'antd';
import { Link } from 'react-router-dom';

const { Title, Paragraph } = Typography;

export interface PageContainerProps {
  title?: string;
  subtitle?: string;
  breadcrumbItems?: Array<{ title: string; path?: string }>;
  actions?: ReactNode;
  children: ReactNode;
}

export function PageContainer({
  title,
  subtitle,
  breadcrumbItems = [],
  actions,
  children,
}: PageContainerProps) {
  return (
    <section className="np-page-container">
      {breadcrumbItems.length > 0 ? (
        <Breadcrumb
          className="np-page-breadcrumb"
          items={breadcrumbItems.map((item) => ({
            title: item.path ? <Link to={item.path}>{item.title}</Link> : item.title,
          }))}
        />
      ) : null}
      <div className="np-page-header">
        <div>
          {title ? <Title level={3} className="np-page-title">{title}</Title> : null}
          {subtitle ? <Paragraph className="np-page-subtitle">{subtitle}</Paragraph> : null}
        </div>
        {actions ? <div className="np-page-actions">{actions}</div> : null}
      </div>
      <div className="np-page-content">{children}</div>
    </section>
  );
}
```

- [x] **Step 3: 改造 Header**

Modify `frontend/packages/shell/src/components/Header.tsx` to render Ant Design `Space`, `Button`, `Segmented`, `Dropdown` or `Avatar`, while preserving:

```tsx
const auth = useAuth();
const { shellView, setShellView, canSwitchShellView } = usePlatform();
```

Acceptance:

- 业务视图 / 平台视图仍可切换。
- 用户名仍显示。
- 退出登录仍调用 `auth.logout()`。

- [x] **Step 4: 改造 Sidebar**

Modify `frontend/packages/shell/src/components/Sidebar.tsx`:

- Props become:

```ts
interface SidebarProps {
  menus: MenuConfig[];
  collapsed: boolean;
  onCollapse: (collapsed: boolean) => void;
}
```

- Use Ant Design `Layout.Sider` and `Menu`.
- Continue using `useAuth()` permission filtering and `usePlatform()` shellView filtering.
- Keep `uiShowcaseMenu` fallback only until demo/showcase phase decides removal.
- Use `projectMenusToAntdItems` and `resolvePageChrome`.

- [x] **Step 5: 更新既有 Sidebar 测试调用**

Modify `frontend/packages/shell/src/__tests__/metadata-navigation.test.tsx` and `frontend/packages/shell/src/__tests__/ui-showcase-navigation.test.tsx` so every direct `<Sidebar menus={...} />` call becomes:

```tsx
<Sidebar menus={menus} collapsed={false} onCollapse={() => undefined} />
```

For the UI Showcase empty menu test, use:

```tsx
<Sidebar menus={[]} collapsed={false} onCollapse={() => undefined} />
```

Expected: Existing metadata navigation and UI Showcase fallback assertions still verify the same behavior; only the controlled collapse props are added.

- [x] **Step 6: 改造 Layout**

Modify `frontend/packages/shell/src/components/Layout.tsx`:

- Keep metadata menu resolution.
- Add local collapsed state.
- Render Ant Design Layout:

```tsx
<AntdLayout className="np-app-layout">
  <Sidebar menus={menus} collapsed={collapsed} onCollapse={setCollapsed} />
  <AntdLayout>
    <Header />
    <Content className="np-app-content">
      <PageContainer title={chrome.title} breadcrumbItems={chrome.breadcrumbItems}>
        <ErrorBoundary>
          <Outlet />
        </ErrorBoundary>
      </PageContainer>
    </Content>
  </AntdLayout>
</AntdLayout>
```

- [x] **Step 7: 调整 Shell CSS**

Modify `frontend/packages/shell/src/index.css`:

- Keep existing `sh-*` styles for compatibility.
- Add new `np-*` layout styles.
- Ensure body background is `#f5f5f5`.
- Avoid large gradients and excessive shadows.

- [x] **Step 8: 运行 Phase 1 测试**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- src/__tests__/antd-shell-layout.test.tsx
pnpm --dir frontend --filter @icp-platform/shell test -- src/__tests__/metadata-navigation.test.tsx src/__tests__/ui-showcase-navigation.test.tsx
```

Expected: PASS.

---

### Task 5: Phase 1 验证与记录

**Files:**
- Modify: `docs/verification/frontend-ant-design-rebuild-verification.md`
- Modify: `docs/phases/current-handoff.md`

- [x] **Step 1: 运行 Shell 测试**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test
```

Expected: PASS. If tests fail, fix Phase 1 regressions before proceeding.

- [x] **Step 2: 运行 Shell build**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell build
```

Expected: PASS.

- [x] **Step 3: 记录验证**

Create or update `docs/verification/frontend-ant-design-rebuild-verification.md`:

```markdown
# Frontend Ant Design Rebuild Verification

## Phase 1

- Scope: Shell Layout / Header / Sidebar / PageContainer / Ant Design theme.
- Commands:
  - `pnpm --dir frontend --filter @icp-platform/shell test`
  - `pnpm --dir frontend --filter @icp-platform/shell build`
- Result: 填写实际结果。
- Known risks: 填写剩余风险。
- Rollback: 回退 `frontend/packages/shell` 的 Ant Design 依赖、theme、Layout、Header、Sidebar、PageContainer 和 `index.css`。
```

- [x] **Step 4: 更新交接台账**

Update `docs/phases/current-handoff.md` with:

- 最近完成：Phase 1 Shell 改造。
- 验证快照：测试/build 命令和结果。
- 推荐下一步：Phase 2 页面模板与 Pro Components adapter。
- 回滚口径：回退 Shell 层改动和依赖。

---

### Task 6: Phase 2 RED - Pro Components Adapter 契约测试

**Files:**
- Create: `frontend/packages/shell/src/__tests__/pro-components-adapter-contract.test.tsx`
- Read: `docs/superpowers/specs/platform/2026-05-17-frontend-ant-design-rebuild-design.md`

- [x] **Step 1: 写失败测试**

Create `frontend/packages/shell/src/__tests__/pro-components-adapter-contract.test.tsx`:

```tsx
// @vitest-environment jsdom

import { describe, expect, it } from 'vitest';
import { createSearchTableRequest } from '@icp-platform/ui';

describe('Pro Components adapter contract', () => {
  it('keeps platform query builder as the request source of truth', async () => {
    const request = createSearchTableRequest({
      buildQuery: (filters: { keyword: string }) => ({ keyword: filters.keyword.trim() }),
      load: async (query) => ({
        items: [{ id: '1', name: `用户-${query.keyword}` }],
        total: 1,
      }),
      mapRow: (row: { id: string; name: string }) => row,
    });

    const result = await request({ keyword: ' Alice ', current: 1, pageSize: 20 });

    expect(result.success).toBe(true);
    expect(result.total).toBe(1);
    expect(result.data[0].name).toBe('用户-Alice');
  });
});
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- src/__tests__/pro-components-adapter-contract.test.tsx
```

Expected: FAIL，因为 `createSearchTableRequest` 尚未实现。

---

### Task 7: Phase 2 GREEN - 引入 Pro Components 与基础 adapter

**Files:**
- Modify: `frontend/packages/ui/package.json`
- Modify: `frontend/pnpm-lock.yaml`
- Create: `frontend/packages/ui/src/pro-adapters/SearchTablePage.tsx`
- Create: `frontend/packages/ui/src/pro-adapters/FormPage.tsx`
- Create: `frontend/packages/ui/src/pro-adapters/DetailPage.tsx`
- Create: `frontend/packages/ui/src/pro-adapters/ActionButtons.tsx`
- Create: `frontend/packages/ui/src/pro-adapters/StatusTag.tsx`
- Modify: `frontend/packages/ui/src/index.ts`

- [x] **Step 1: 安装 Pro Components**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/ui add antd @ant-design/icons @ant-design/pro-components
```

Expected: `frontend/packages/ui/package.json` 增加 `antd`、`@ant-design/icons` 和 `@ant-design/pro-components`；Shell 继续通过 Phase 1 的 `antd` 依赖承载全局布局。

- [x] **Step 2: 创建 SearchTable request adapter**

Create `frontend/packages/ui/src/pro-adapters/SearchTablePage.tsx`:

```tsx
import type { ReactNode } from 'react';
import type { ProColumns } from '@ant-design/pro-components';
import { ProTable } from '@ant-design/pro-components';

export interface SearchTableResult<TRow> {
  items: TRow[];
  total: number;
}

export interface SearchTableRequestConfig<TFilter, TSourceRow, TRow> {
  buildQuery: (filters: TFilter) => Record<string, unknown>;
  load: (query: Record<string, unknown>) => Promise<SearchTableResult<TSourceRow>>;
  mapRow: (row: TSourceRow) => TRow;
}

export function createSearchTableRequest<TFilter extends Record<string, unknown>, TSourceRow, TRow>(
  config: SearchTableRequestConfig<TFilter, TSourceRow, TRow>,
) {
  return async (params: TFilter & { current?: number; pageSize?: number }) => {
    const query = {
      ...config.buildQuery(params),
      pageIndex: params.current ?? 1,
      pageSize: params.pageSize ?? 20,
    };
    const result = await config.load(query);

    return {
      data: result.items.map(config.mapRow),
      total: result.total,
      success: true,
    };
  };
}

export interface SearchTablePageProps<TRow> {
  rowKey: string | ((row: TRow) => string);
  columns: ProColumns<TRow>[];
  request: (params: Record<string, unknown>) => Promise<{ data: TRow[]; total: number; success: boolean }>;
  toolbar?: ReactNode;
}

export function SearchTablePage<TRow extends Record<string, unknown>>({
  rowKey,
  columns,
  request,
  toolbar,
}: SearchTablePageProps<TRow>) {
  return (
    <ProTable<TRow>
      rowKey={rowKey}
      columns={columns}
      request={request}
      search={{ labelWidth: 96 }}
      options={{ density: true, fullScreen: false, reload: true, setting: true }}
      toolBarRender={toolbar ? () => [toolbar] : false}
      pagination={{ defaultPageSize: 20, showSizeChanger: true }}
    />
  );
}
```

- [x] **Step 3: 导出 adapter**

Modify `frontend/packages/ui/src/index.ts`:

```ts
export * from './pro-adapters/SearchTablePage';
export * from './pro-adapters/FormPage';
export * from './pro-adapters/DetailPage';
export * from './pro-adapters/ActionButtons';
export * from './pro-adapters/StatusTag';
```

- [x] **Step 4: 实现 FormPage / DetailPage / ActionButtons / StatusTag 最小骨架**

Implement minimal wrappers using `ProForm`, `ProDescriptions`, `Button`, `Dropdown`, `Popconfirm`, and `Tag`.

Expected:

- Wrappers do not import app-specific modules.
- Wrappers accept platform props.
- Wrappers do not read runtime metadata directly.

- [x] **Step 5: 运行 adapter 测试**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- src/__tests__/pro-components-adapter-contract.test.tsx
```

Expected: PASS.

---

### Task 8: Phase 3 - 人员中心 AntD / Pro Components 样板

**Files:**
- Modify: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- Modify: `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
- Modify: `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
- Test: `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx`
- Test: `frontend/packages/shell/src/__tests__/organization-person-center-detail-layout.test.tsx`
- Test: `frontend/packages/shell/src/__tests__/organization-person-create-flow.test.tsx`

- [x] **Step 1: 写或更新人员中心列表布局测试**

Run existing tests first:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- src/__tests__/organization-person-center-list-layout.test.tsx
```

Expected: Existing behavior baseline. If tests pass immediately, update assertions to require Ant Design / Pro Components table affordances such as query form labels, operation column, status tags, and pagination.

- [x] **Step 2: 改造 StandardCrudListPageHost**

Use `SearchTablePage` adapter for standard CRUD list rendering.

Must preserve:

- `registration.createEmptyFilters()`
- `registration.buildListQuery()`
- `registration.mapListItem()`
- `registration.getRowId()`
- `registration.buildColumns()`
- `registration.renderFilters()`
- `registration.listActions`
- `registration.actionHandlers`
- permissions

- [x] **Step 3: 改造 StandardCrudDetailPageHost**

Use `DetailPage` and `FormPage` adapter for details and edit/create behavior.

Must preserve:

- detail loading.
- summary renderers.
- lookup providers.
- required field validation.
- save and disable action behavior.

- [x] **Step 4: 运行人员中心测试**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- src/__tests__/organization-person-center-list-layout.test.tsx src/__tests__/organization-person-center-detail-layout.test.tsx src/__tests__/organization-person-create-flow.test.tsx
```

Expected: PASS.

---

### Task 9: Phase 4 - 日志审计低风险页面重做

**Files:**
- Modify: `frontend/modules/audit/src/pages/OperationLogs.tsx`
- Modify: `frontend/modules/audit/src/pages/AccessLogs.tsx`
- Test: `frontend/packages/shell/src/__tests__/audit-pages-ant-design.test.tsx`

- [x] **Step 1: 写日志页 AntD 测试**

Create `frontend/packages/shell/src/__tests__/audit-pages-ant-design.test.tsx` to verify:

- 操作日志页面显示查询表格。
- 访问日志页面显示查询表格。
- loading、empty、error 状态由 Ant Design / Pro Components 组件承载。
- API 路径仍为 `/api/audit/operation-logs` 和 `/api/audit/access-logs`。

- [x] **Step 2: 重做 OperationLogs**

Use `SearchTablePage` adapter and preserve:

- `pageIndex`
- `pageSize`
- `total`
- `/api/audit/operation-logs`
- result status mapping

- [x] **Step 3: 重做 AccessLogs**

Use `SearchTablePage` adapter and preserve:

- `pageIndex`
- `pageSize`
- `total`
- `/api/audit/access-logs`
- statusCode mapping
- responseTime display

- [x] **Step 4: 运行日志页测试**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- src/__tests__/audit-pages-ant-design.test.tsx
```

Expected: PASS.

---

### Task 10: 阶段验收与后续拆分

**Files:**
- Modify: `docs/verification/frontend-ant-design-rebuild-verification.md`
- Modify: `docs/phases/current-handoff.md`
- Optionally Modify: `docs/superpowers/plans/platform/2026-05-17-frontend-ant-design-rebuild.md`

- [x] **Step 1: 运行 Shell 测试**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test
```

Expected: PASS.

- [x] **Step 2: 运行 Shell build**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell build
```

Expected: PASS.

- [x] **Step 3: 涉及 UI 包时运行 UI build**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/ui build
```

Expected: PASS.

- [x] **Step 4: 记录验证和下一步**

Update `docs/verification/frontend-ant-design-rebuild-verification.md` and `docs/phases/current-handoff.md`.

Expected:

- 写明已完成阶段。
- 写明验证命令和结果。
- 写明未覆盖页面。
- 写明下一步页面重做顺序：配置管理、权限中心、文件中心、插件运维、元数据配置中心。

---

## Rollback Guide

- Phase 1 rollback: remove `antd` and `@ant-design/icons`, revert `frontend/packages/shell/src/config/antdTheme.ts`, `frontend/packages/shell/src/navigation/menuProjection.tsx`, `frontend/packages/shell/src/navigation/pageChrome.ts`, `frontend/packages/shell/src/components/Layout.tsx`, `frontend/packages/shell/src/components/Header.tsx`, `frontend/packages/shell/src/components/Sidebar.tsx`, `frontend/packages/shell/src/components/PageContainer.tsx`, and Shell CSS changes.
- Phase 2 rollback: remove UI package dependencies `antd`, `@ant-design/icons`, `@ant-design/pro-components`, revert `frontend/packages/ui/src/pro-adapters/*`, exports, and adapter tests.
- Phase 3 rollback: revert standard CRUD host changes and organization-specific minimal adjustments; keep `organizationService` untouched unless a dedicated design says otherwise.
- Phase 4 rollback: revert only `frontend/modules/audit/src/pages/OperationLogs.tsx`, `frontend/modules/audit/src/pages/AccessLogs.tsx`, and their tests.

## Completion Criteria

- Shell is Ant Design based.
- Pro Components are used only through platform adapter.
- runtime metadata, ModuleRegistry, permissions, routes, and standard CRUD remain platform facts.
- Personnel center remains usable.
- Audit pages are rebuilt as first low-risk Ant Design / Pro Components pages.
- Verification record and handoff are updated.

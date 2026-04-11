# Platform View Metadata Console Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first usable `platform` view shell as an AI-generated metadata review console with assembly-item entry points, a center review list, and a right-side configurator scaffold.

**Architecture:** Keep the existing `BrowserRouter + Layout + Home` shell intact and add a mode-aware platform console page that renders inside the current shell route. Represent V1 data with local typed fixtures and status metadata so the UI can exercise the full review flow without waiting on backend APIs. Reuse existing `@icp-platform/ui` patterns where possible and limit new shell code to platform-view-specific presentation and interaction state.

**Tech Stack:** React 18, TypeScript, Vite, Vitest, `@icp-platform/ui`, existing shell CSS tokens and page panel patterns.

---

## File Map

- Create: `frontend/packages/shell/src/platform/types.ts`
  - Defines typed platform assembly categories, review statuses, and object summary records used by the V1 console scaffold.
- Create: `frontend/packages/shell/src/platform/fixtures.ts`
  - Provides local seed data for `数据模型 / 菜单导航 / 权限策略 / 路由装配`, including status counts and per-object summaries.
- Create: `frontend/packages/shell/src/components/platform/PlatformPageChrome.tsx`
  - Renders the platform-only secondary context bar and three-column page chrome.
- Create: `frontend/packages/shell/src/components/platform/PlatformAssemblyNav.tsx`
  - Renders the left-side assembly navigation and active-state handling.
- Create: `frontend/packages/shell/src/components/platform/PlatformReviewList.tsx`
  - Renders the center review queue with filters, status summaries, and selectable rows.
- Create: `frontend/packages/shell/src/components/platform/PlatformConfiguratorPanel.tsx`
  - Renders the right-side configurator scaffold for the currently selected object.
- Create: `frontend/packages/shell/src/pages/PlatformConsoleHome.tsx`
  - Owns local UI state for selected assembly and object and composes the platform console page.
- Modify: `frontend/packages/shell/src/pages/Home.tsx`
  - Delegates to the business portal or platform console based on `shellView`.
- Modify: `frontend/packages/shell/src/index.css`
  - Adds platform console layout, queue row, context bar, and configurator styles while preserving existing shell tokens.
- Create: `frontend/packages/shell/src/__tests__/platform-console-home.test.tsx`
  - Verifies platform-mode rendering, assembly switching, object selection, and configurator updates.
- Modify: `frontend/packages/shell/src/__tests__/home-page-shortcuts.test.tsx`
  - Narrows existing assertions to business-mode behavior so platform-mode tests can own the new console expectations.

## Task 1: Split Home into business portal and platform console entry points

**Files:**
- Create: `frontend/packages/shell/src/pages/PlatformConsoleHome.tsx`
- Modify: `frontend/packages/shell/src/pages/Home.tsx`
- Modify: `frontend/packages/shell/src/__tests__/home-page-shortcuts.test.tsx`
- Create: `frontend/packages/shell/src/__tests__/platform-console-home.test.tsx`

- [ ] **Step 1: Write the failing platform-mode rendering test**

```tsx
// frontend/packages/shell/src/__tests__/platform-console-home.test.tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import Home from '../pages/Home';

vi.mock('../contexts/PlatformContext', () => ({
  usePlatform: () => ({
    shellView: 'platform',
    uiConfig: {
      shellExperienceMode: 'hybrid',
    },
  }),
}));

describe('Platform console home', () => {
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

  it('renders the platform metadata review console in platform mode', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <MemoryRouter>
          <Home />
        </MemoryRouter>,
      );
    });

    expect(container.textContent).toContain('平台元数据控制台');
    expect(container.textContent).toContain('数据模型');
    expect(container.textContent).toContain('待审查结果');
    expect(container.textContent).not.toContain('业务门户');
  });
});
```

- [ ] **Step 2: Run the new test to verify it fails**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/platform-console-home.test.tsx
```

Expected: FAIL because `Home` still renders the current portal-only landing page for both modes.

- [ ] **Step 3: Add a minimal platform console page component and mode-based branch in `Home.tsx`**

```tsx
// frontend/packages/shell/src/pages/PlatformConsoleHome.tsx
export default function PlatformConsoleHome() {
  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">平台视图</span>
          <h1 className="sh-page-title">平台元数据控制台</h1>
          <p className="sh-page-description">
            第一版优先承接 AI 生成元数据的审查、修正与发布。
          </p>
        </div>
      </section>
      <div>待审查结果</div>
      <div>数据模型</div>
    </div>
  );
}
```

```tsx
// frontend/packages/shell/src/pages/Home.tsx
import PlatformConsoleHome from './PlatformConsoleHome';

export default function Home() {
  const { shellView, uiConfig } = usePlatform();

  if (shellView === 'platform') {
    return <PlatformConsoleHome />;
  }

  const experienceMode = uiConfig.shellExperienceMode;
  const showBusiness = experienceMode === 'hybrid' || experienceMode === 'product';
  const showPlatform = experienceMode === 'hybrid' || experienceMode === 'platform';
  const currentViewLabel = shellView === 'business' ? '业务视图' : '平台视图';

  return (
    <div className="sh-page">
      {/* keep the current business / hybrid portal JSX here unchanged */}
      <span>{currentViewLabel}</span>
      {showBusiness && <section>业务门户</section>}
      {showPlatform && shellView !== 'platform' && <section>平台门户</section>}
    </div>
  );
}
```

- [ ] **Step 4: Adjust the existing business-home test so it explicitly runs in business mode**

```tsx
// frontend/packages/shell/src/__tests__/home-page-shortcuts.test.tsx
vi.mock('../contexts/PlatformContext', () => ({
  usePlatform: () => ({
    shellView: 'business',
    uiConfig: {
      shellExperienceMode: 'hybrid',
    },
  }),
}));
```

- [ ] **Step 5: Re-run the focused tests**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/home-page-shortcuts.test.tsx src/__tests__/platform-console-home.test.tsx
```

Expected: PASS for the business shortcut test and the new platform-mode smoke test.

- [ ] **Step 6: Commit the entry-point split**

```bash
git -C D:/Github/ICP-X/NewP add \
  frontend/packages/shell/src/pages/Home.tsx \
  frontend/packages/shell/src/pages/PlatformConsoleHome.tsx \
  frontend/packages/shell/src/__tests__/home-page-shortcuts.test.tsx \
  frontend/packages/shell/src/__tests__/platform-console-home.test.tsx
git -C D:/Github/ICP-X/NewP commit -m "feat: split shell home by platform view"
```

## Task 2: Add typed platform console fixtures and left-nav / queue scaffolding

**Files:**
- Create: `frontend/packages/shell/src/platform/types.ts`
- Create: `frontend/packages/shell/src/platform/fixtures.ts`
- Create: `frontend/packages/shell/src/components/platform/PlatformAssemblyNav.tsx`
- Create: `frontend/packages/shell/src/components/platform/PlatformReviewList.tsx`
- Modify: `frontend/packages/shell/src/pages/PlatformConsoleHome.tsx`
- Modify: `frontend/packages/shell/src/__tests__/platform-console-home.test.tsx`

- [ ] **Step 1: Write the failing interaction test for assembly switching**

```tsx
it('switches the review list when a different assembly item is selected', async () => {
  container = document.createElement('div');
  document.body.appendChild(container);

  await act(async () => {
    root = createRoot(container);
    root.render(
      <MemoryRouter>
        <Home />
      </MemoryRouter>,
    );
  });

  expect(container.textContent).toContain('Customer');

  const menuNavButton = Array.from(container.querySelectorAll('button')).find(
    (button) => button.textContent?.includes('菜单导航'),
  );
  expect(menuNavButton).toBeTruthy();

  await act(async () => {
    menuNavButton?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  });

  expect(container.textContent).toContain('侧边菜单');
  expect(container.textContent).not.toContain('Customer');
});
```

- [ ] **Step 2: Run the focused test to confirm it fails**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/platform-console-home.test.tsx
```

Expected: FAIL because the platform page has no assembly navigation or queue data yet.

- [ ] **Step 3: Add typed V1 fixture data**

```ts
// frontend/packages/shell/src/platform/types.ts
export type PlatformAssemblyKey = 'models' | 'menus' | 'permissions' | 'routes';
export type ReviewStatus = 'ai_draft' | 'pending_review' | 'needs_fix' | 'validation_failed' | 'ready_to_publish' | 'published';

export interface PlatformAssemblySummary {
  key: PlatformAssemblyKey;
  label: string;
  description: string;
  pendingCount: number;
}

export interface PlatformObjectSummary {
  id: string;
  assembly: PlatformAssemblyKey;
  title: string;
  moduleName: string;
  status: ReviewStatus;
  summary: string;
  updatedAt: string;
}
```

```ts
// frontend/packages/shell/src/platform/fixtures.ts
import type { PlatformAssemblySummary, PlatformObjectSummary } from './types';

export const platformAssemblies: PlatformAssemblySummary[] = [
  { key: 'models', label: '数据模型', description: '实体 / 字段 / 关系', pendingCount: 3 },
  { key: 'menus', label: '菜单导航', description: '菜单组 / 层级 / 可见性', pendingCount: 2 },
  { key: 'permissions', label: '权限策略', description: '角色 / 权限点 / 绑定', pendingCount: 2 },
  { key: 'routes', label: '路由装配', description: '路径 / 入口 / 模块挂载', pendingCount: 1 },
];

export const platformObjects: PlatformObjectSummary[] = [
  { id: 'customer', assembly: 'models', title: 'Customer', moduleName: 'CRM', status: 'pending_review', summary: '12 fields / 3 relations / AI generated', updatedAt: '2026-04-11 17:20' },
  { id: 'order', assembly: 'models', title: 'Order', moduleName: 'Sales', status: 'needs_fix', summary: '18 fields / missing relation check', updatedAt: '2026-04-11 17:42' },
  { id: 'side-menu', assembly: 'menus', title: '侧边菜单', moduleName: 'Shell', status: 'pending_review', summary: '8 nodes / 1 route conflict', updatedAt: '2026-04-11 18:02' },
];
```

- [ ] **Step 4: Add reusable assembly-nav and review-list components and wire them into the page**

```tsx
// frontend/packages/shell/src/components/platform/PlatformAssemblyNav.tsx
import type { PlatformAssemblyKey, PlatformAssemblySummary } from '../../platform/types';

interface PlatformAssemblyNavProps {
  items: PlatformAssemblySummary[];
  activeKey: PlatformAssemblyKey;
  onSelect: (key: PlatformAssemblyKey) => void;
}

export function PlatformAssemblyNav({ items, activeKey, onSelect }: PlatformAssemblyNavProps) {
  return (
    <aside className="sh-platform-nav">
      {items.map((item) => (
        <button
          key={item.key}
          type="button"
          className={`sh-platform-nav-item ${item.key === activeKey ? 'active' : ''}`}
          onClick={() => onSelect(item.key)}
        >
          <span>{item.label}</span>
          <span>{item.pendingCount}</span>
        </button>
      ))}
    </aside>
  );
}
```

```tsx
// frontend/packages/shell/src/components/platform/PlatformReviewList.tsx
import type { PlatformObjectSummary } from '../../platform/types';

interface PlatformReviewListProps {
  items: PlatformObjectSummary[];
  selectedId: string;
  onSelect: (id: string) => void;
}

export function PlatformReviewList({ items, selectedId, onSelect }: PlatformReviewListProps) {
  return (
    <section className="sh-platform-list">
      <div className="sh-platform-list-header">待审查结果</div>
      {items.map((item) => (
        <button
          key={item.id}
          type="button"
          className={`sh-platform-list-row ${item.id === selectedId ? 'active' : ''}`}
          onClick={() => onSelect(item.id)}
        >
          <strong>{item.title}</strong>
          <span>{item.summary}</span>
        </button>
      ))}
    </section>
  );
}
```

```tsx
// frontend/packages/shell/src/pages/PlatformConsoleHome.tsx
import { useMemo, useState } from 'react';
import { platformAssemblies, platformObjects } from '../platform/fixtures';
import type { PlatformAssemblyKey } from '../platform/types';
import { PlatformAssemblyNav } from '../components/platform/PlatformAssemblyNav';
import { PlatformReviewList } from '../components/platform/PlatformReviewList';

export default function PlatformConsoleHome() {
  const [activeAssembly, setActiveAssembly] = useState<PlatformAssemblyKey>('models');

  const visibleItems = useMemo(
    () => platformObjects.filter((item) => item.assembly === activeAssembly),
    [activeAssembly],
  );
  const [selectedId, setSelectedId] = useState('customer');

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">平台视图</span>
          <h1 className="sh-page-title">平台元数据控制台</h1>
          <p className="sh-page-description">
            第一版优先承接 AI 生成元数据的审查、修正与发布。
          </p>
        </div>
      </section>
      <div className="sh-platform-shell">
        <PlatformAssemblyNav items={platformAssemblies} activeKey={activeAssembly} onSelect={setActiveAssembly} />
        <PlatformReviewList items={visibleItems} selectedId={selectedId} onSelect={setSelectedId} />
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Re-run the platform console test**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/platform-console-home.test.tsx
```

Expected: PASS, including assembly switching from `数据模型` to `菜单导航`.

- [ ] **Step 6: Commit the fixture-backed platform review scaffold**

```bash
git -C D:/Github/ICP-X/NewP add \
  frontend/packages/shell/src/platform/types.ts \
  frontend/packages/shell/src/platform/fixtures.ts \
  frontend/packages/shell/src/components/platform/PlatformAssemblyNav.tsx \
  frontend/packages/shell/src/components/platform/PlatformReviewList.tsx \
  frontend/packages/shell/src/pages/PlatformConsoleHome.tsx \
  frontend/packages/shell/src/__tests__/platform-console-home.test.tsx
git -C D:/Github/ICP-X/NewP commit -m "feat: add platform console review queue scaffold"
```

## Task 3: Add the platform context bar and right-side configurator panel

**Files:**
- Create: `frontend/packages/shell/src/components/platform/PlatformPageChrome.tsx`
- Create: `frontend/packages/shell/src/components/platform/PlatformConfiguratorPanel.tsx`
- Modify: `frontend/packages/shell/src/pages/PlatformConsoleHome.tsx`
- Modify: `frontend/packages/shell/src/__tests__/platform-console-home.test.tsx`

- [ ] **Step 1: Write the failing object-selection test for the configurator panel**

```tsx
it('updates the configurator when a different object is selected', async () => {
  container = document.createElement('div');
  document.body.appendChild(container);

  await act(async () => {
    root = createRoot(container);
    root.render(
      <MemoryRouter>
        <Home />
      </MemoryRouter>,
    );
  });

  expect(container.textContent).toContain('Customer');
  expect(container.textContent).toContain('重新校验');

  const orderRow = Array.from(container.querySelectorAll('button')).find(
    (button) => button.textContent?.includes('Order'),
  );
  expect(orderRow).toBeTruthy();

  await act(async () => {
    orderRow?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  });

  expect(container.textContent).toContain('Order');
  expect(container.textContent).toContain('保存草稿');
  expect(container.textContent).toContain('发布');
});
```

- [ ] **Step 2: Run the test to confirm it fails**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/platform-console-home.test.tsx
```

Expected: FAIL because the page does not yet render a context bar or configurator details.

- [ ] **Step 3: Create the platform page chrome and configurator components**

```tsx
// frontend/packages/shell/src/components/platform/PlatformPageChrome.tsx
import type { ReactNode } from 'react';

interface PlatformPageChromeProps {
  eyebrow: string;
  title: string;
  description: string;
  context: ReactNode;
  nav: ReactNode;
  list: ReactNode;
  detail: ReactNode;
}

export function PlatformPageChrome({ eyebrow, title, description, context, nav, list, detail }: PlatformPageChromeProps) {
  return (
    <div className="sh-page sh-platform-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">{eyebrow}</span>
          <h1 className="sh-page-title">{title}</h1>
          <p className="sh-page-description">{description}</p>
        </div>
      </section>
      <div className="sh-platform-context-bar">{context}</div>
      <div className="sh-platform-shell">
        {nav}
        {list}
        {detail}
      </div>
    </div>
  );
}
```

```tsx
// frontend/packages/shell/src/components/platform/PlatformConfiguratorPanel.tsx
import type { PlatformObjectSummary } from '../../platform/types';

interface PlatformConfiguratorPanelProps {
  item: PlatformObjectSummary;
}

export function PlatformConfiguratorPanel({ item }: PlatformConfiguratorPanelProps) {
  return (
    <aside className="sh-platform-configurator">
      <h2>{item.title}</h2>
      <p>{item.summary}</p>
      <div className="sh-platform-configurator-section">基本信息</div>
      <div className="sh-platform-configurator-section">关键绑定</div>
      <div className="sh-platform-configurator-actions">
        <button type="button" className="sh-btn">保存草稿</button>
        <button type="button" className="sh-btn">重新校验</button>
        <button type="button" className="sh-btn sh-btn-primary">发布</button>
      </div>
    </aside>
  );
}
```

- [ ] **Step 4: Compose the full page in `PlatformConsoleHome.tsx`**

```tsx
const selectedItem = visibleItems.find((item) => item.id === selectedId) ?? visibleItems[0];
const activeAssemblyLabel = platformAssemblies.find((item) => item.key === activeAssembly)?.label ?? '数据模型';

return (
  <PlatformPageChrome
    eyebrow="平台视图"
    title="平台元数据控制台"
    description="第一版优先承接 AI 生成元数据的审查、修正与发布。"
    context={
      <>
        <span>当前装配项：{activeAssemblyLabel}</span>
        <span> / </span>
        <span>对象：{selectedItem.title}</span>
        <button type="button" className="sh-btn sh-btn-primary">发布配置</button>
      </>
    }
    nav={
      <PlatformAssemblyNav
        items={platformAssemblies}
        activeKey={activeAssembly}
        onSelect={setActiveAssembly}
      />
    }
    list={
      <PlatformReviewList
        items={visibleItems}
        selectedId={selectedItem.id}
        onSelect={setSelectedId}
      />
    }
    detail={<PlatformConfiguratorPanel item={selectedItem} />}
  />
);
```

- [ ] **Step 5: Re-run the platform console test suite**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/platform-console-home.test.tsx
```

Expected: PASS, including selecting `Order` and updating the right-side configurator content.

- [ ] **Step 6: Commit the page chrome and configurator**

```bash
git -C D:/Github/ICP-X/NewP add \
  frontend/packages/shell/src/components/platform/PlatformPageChrome.tsx \
  frontend/packages/shell/src/components/platform/PlatformConfiguratorPanel.tsx \
  frontend/packages/shell/src/pages/PlatformConsoleHome.tsx \
  frontend/packages/shell/src/__tests__/platform-console-home.test.tsx
git -C D:/Github/ICP-X/NewP commit -m "feat: add platform console configurator panel"
```

## Task 4: Add platform-console styling and status-driven summaries

**Files:**
- Modify: `frontend/packages/shell/src/platform/fixtures.ts`
- Modify: `frontend/packages/shell/src/components/platform/PlatformReviewList.tsx`
- Modify: `frontend/packages/shell/src/components/platform/PlatformConfiguratorPanel.tsx`
- Modify: `frontend/packages/shell/src/index.css`
- Modify: `frontend/packages/shell/src/__tests__/platform-console-home.test.tsx`

- [ ] **Step 1: Write the failing status-summary test**

```tsx
it('shows review statuses and publish gating cues in the platform console', async () => {
  container = document.createElement('div');
  document.body.appendChild(container);

  await act(async () => {
    root = createRoot(container);
    root.render(
      <MemoryRouter>
        <Home />
      </MemoryRouter>,
    );
  });

  expect(container.textContent).toContain('待审查');
  expect(container.textContent).toContain('待修正');
  expect(container.textContent).toContain('待发布');
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/platform-console-home.test.tsx
```

Expected: FAIL because the list and configurator do not yet expose status labels clearly.

- [ ] **Step 3: Add status metadata and surface it in the list + configurator**

```ts
// frontend/packages/shell/src/platform/fixtures.ts
export const reviewStatusLabels: Record<ReviewStatus, string> = {
  ai_draft: 'AI 草稿',
  pending_review: '待审查',
  needs_fix: '待修正',
  validation_failed: '校验失败',
  ready_to_publish: '待发布',
  published: '已发布',
};
```

```tsx
// frontend/packages/shell/src/components/platform/PlatformReviewList.tsx
<span className={`sh-platform-status sh-platform-status-${item.status}`}>
  {reviewStatusLabels[item.status]}
</span>
```

```tsx
// frontend/packages/shell/src/components/platform/PlatformConfiguratorPanel.tsx
<div className="sh-platform-configurator-meta">
  <span>{item.moduleName}</span>
  <span>{reviewStatusLabels[item.status]}</span>
</div>
```

- [ ] **Step 4: Add the platform-console CSS**

```css
/* frontend/packages/shell/src/index.css */
.sh-platform-context-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--sh-space-4);
  margin-bottom: var(--sh-space-4);
  padding: var(--sh-space-4) var(--sh-space-6);
  border: 1px solid var(--sh-border);
  border-radius: var(--sh-radius-lg);
  background: var(--sh-bg-surface);
}

.sh-platform-shell {
  display: grid;
  grid-template-columns: 220px minmax(0, 1fr) 320px;
  gap: var(--sh-space-4);
  align-items: start;
}

.sh-platform-nav-item,
.sh-platform-list-row {
  width: 100%;
  text-align: left;
}

.sh-platform-list-row.active,
.sh-platform-nav-item.active {
  border-color: var(--sh-primary);
  background: var(--sh-primary-bg);
}

.sh-platform-configurator,
.sh-platform-list,
.sh-platform-nav {
  border: 1px solid var(--sh-border);
  border-radius: var(--sh-radius-lg);
  background: var(--sh-bg-surface);
  box-shadow: var(--sh-shadow-sm);
}
```

- [ ] **Step 5: Run tests and build for the shell package**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell build
```

Expected:

- `vitest --run` completes with PASS for all shell tests.
- `tsc && vite build` completes successfully for `@icp-platform/shell`.

- [ ] **Step 6: Commit the styled V1 platform console**

```bash
git -C D:/Github/ICP-X/NewP add \
  frontend/packages/shell/src/platform/fixtures.ts \
  frontend/packages/shell/src/components/platform/PlatformReviewList.tsx \
  frontend/packages/shell/src/components/platform/PlatformConfiguratorPanel.tsx \
  frontend/packages/shell/src/index.css \
  frontend/packages/shell/src/__tests__/platform-console-home.test.tsx
git -C D:/Github/ICP-X/NewP commit -m "feat: style platform metadata review console"
```

## Task 5: Final verification and handoff notes

**Files:**
- Modify: `frontend/packages/shell/src/pages/PlatformConsoleHome.tsx` (only if final polish is needed)
- Modify: `docs/superpowers/specs/2026-04-11-platform-view-metadata-console-design.md` (only if implementation reveals a spec mismatch)

- [ ] **Step 1: Run the exact verification commands one more time**

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell build
```

- [ ] **Step 2: Smoke-check the expected UX in dev mode**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell dev
```

Expected manual checks:

- Switch to `platform` view and confirm the page title is `平台元数据控制台`.
- Confirm the left nav shows all four assembly items.
- Confirm clicking `菜单导航` changes the center list.
- Confirm selecting a row changes the right-side configurator.

- [ ] **Step 3: If implementation changed scope, update the spec inline**

```md
## Implementation Notes

- V1 ships local fixture-backed review data only.
- AI result APIs and publish actions remain future integration work.
```

- [ ] **Step 4: Prepare the handoff summary**

```md
- Implemented: platform-mode home split, assembly nav, review queue, configurator shell, platform styles
- Verified: shell Vitest suite, shell build, manual dev smoke-check
- Deferred: real AI result APIs, publish workflow backend, full manual metadata authoring
```

- [ ] **Step 5: Commit any final polish or spec alignment**

```bash
git -C D:/Github/ICP-X/NewP add \
  frontend/packages/shell/src/pages/PlatformConsoleHome.tsx \
  docs/superpowers/specs/2026-04-11-platform-view-metadata-console-design.md
git -C D:/Github/ICP-X/NewP commit -m "docs: align platform console implementation notes"
```

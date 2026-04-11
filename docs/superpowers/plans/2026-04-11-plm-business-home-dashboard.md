# PLM Business Home Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current business-side portal landing page with a task-driven PLM business dashboard that prioritizes `我的待办 / 待我审批 / 本周关键变更`, a central todo list, and a right-side flow/risk rail.

**Architecture:** Keep the existing shell route structure and `Home.tsx` entry point, but move business-home presentation into a dedicated dashboard page composed from focused shell components. Drive V1 with typed local fixture data so the team can validate layout, hierarchy, and interaction before any real PLM aggregation APIs exist. Preserve the current platform-facing logic and only swap the business-facing homepage experience.

**Tech Stack:** React 18, TypeScript, Vite, Vitest, `react-router-dom`, existing shell CSS tokens and `@icp-platform/ui` page patterns.

---

## File Map

- Create: `frontend/packages/shell/src/business-home/types.ts`
  - Defines typed dashboard card, todo item, flow summary, project summary, and quick-link records.
- Create: `frontend/packages/shell/src/business-home/fixtures.ts`
  - Provides local PLM dashboard fixture data for the top cards, todo list, flow rail, project list, and quick links.
- Create: `frontend/packages/shell/src/components/business-home/BusinessHeroCards.tsx`
  - Renders the three top action-summary cards.
- Create: `frontend/packages/shell/src/components/business-home/BusinessTodoPanel.tsx`
  - Renders the main “待办任务列表” panel.
- Create: `frontend/packages/shell/src/components/business-home/BusinessFlowRail.tsx`
  - Renders the right-side flow status, publish reminder, and risk reminder sections.
- Create: `frontend/packages/shell/src/components/business-home/BusinessProjectsPanel.tsx`
  - Renders the “我参与的项目” summary section.
- Create: `frontend/packages/shell/src/components/business-home/BusinessQuickLinksPanel.tsx`
  - Renders the curated quick links section.
- Create: `frontend/packages/shell/src/pages/BusinessDashboardHome.tsx`
  - Composes the dashboard layout and owns local selection state if needed.
- Modify: `frontend/packages/shell/src/pages/Home.tsx`
  - Delegates business-mode rendering to the new dashboard while preserving platform-mode branching.
- Modify: `frontend/packages/shell/src/index.css`
  - Adds business-dashboard-specific layout and card styles on top of existing shell tokens.
- Create: `frontend/packages/shell/src/__tests__/business-dashboard-home.test.tsx`
  - Verifies business-mode rendering, top-card hierarchy, todo list content, and right-rail content.
- Modify: `frontend/packages/shell/src/__tests__/home-page-shortcuts.test.tsx`
  - Re-targets assertions to the new business dashboard quick links or retires obsolete portal expectations.

## Task 1: Split business home into a dedicated dashboard page

**Files:**
- Create: `frontend/packages/shell/src/pages/BusinessDashboardHome.tsx`
- Modify: `frontend/packages/shell/src/pages/Home.tsx`
- Create: `frontend/packages/shell/src/__tests__/business-dashboard-home.test.tsx`
- Modify: `frontend/packages/shell/src/__tests__/home-page-shortcuts.test.tsx`

- [ ] **Step 1: Write the failing business-dashboard smoke test**

```tsx
// frontend/packages/shell/src/__tests__/business-dashboard-home.test.tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import Home from '../pages/Home';

vi.mock('../contexts/PlatformContext', () => ({
  usePlatform: () => ({
    shellView: 'business',
    uiConfig: {
      shellExperienceMode: 'hybrid',
    },
  }),
}));

describe('Business dashboard home', () => {
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

  it('renders the PLM business dashboard instead of the old dual-portal page', async () => {
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

    expect(container.textContent).toContain('PLM 业务概览');
    expect(container.textContent).toContain('我的待办');
    expect(container.textContent).toContain('待办任务列表');
    expect(container.textContent).not.toContain('业务门户');
  });
});
```

- [ ] **Step 2: Run the new test to verify it fails**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/business-dashboard-home.test.tsx
```

Expected: FAIL because `Home.tsx` still renders the current portal-style landing page.

- [ ] **Step 3: Add a minimal business dashboard page and branch `Home.tsx` to use it in business mode**

```tsx
// frontend/packages/shell/src/pages/BusinessDashboardHome.tsx
export default function BusinessDashboardHome() {
  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">业务视图</span>
          <h1 className="sh-page-title">PLM 业务概览</h1>
          <p className="sh-page-description">
            首页优先展示个人任务、待审批事项与本周关键变更。
          </p>
        </div>
      </section>
      <div>我的待办</div>
      <div>待办任务列表</div>
    </div>
  );
}
```

```tsx
// frontend/packages/shell/src/pages/Home.tsx
import { usePlatform } from '../contexts/PlatformContext';
import BusinessDashboardHome from './BusinessDashboardHome';

export default function Home() {
  const { shellView } = usePlatform();

  if (shellView === 'business') {
    return <BusinessDashboardHome />;
  }

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">平台视图</span>
          <h1 className="sh-page-title">欢迎使用 ICP Platform</h1>
        </div>
      </section>
    </div>
  );
}
```

- [ ] **Step 4: Update the old home shortcut test so it targets business dashboard quick links instead of portal wording**

```tsx
// frontend/packages/shell/src/__tests__/home-page-shortcuts.test.tsx
expect(container.textContent).toContain('快捷入口');
expect(container.textContent).not.toContain('业务门户');
```

- [ ] **Step 5: Re-run the focused tests**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/business-dashboard-home.test.tsx src/__tests__/home-page-shortcuts.test.tsx
```

Expected: PASS for the new business-dashboard smoke test; existing business-home test should now validate the new home shape.

- [ ] **Step 6: Commit the business-home page split**

```bash
git -C D:/Github/ICP-X/NewP add \
  frontend/packages/shell/src/pages/BusinessDashboardHome.tsx \
  frontend/packages/shell/src/pages/Home.tsx \
  frontend/packages/shell/src/__tests__/business-dashboard-home.test.tsx \
  frontend/packages/shell/src/__tests__/home-page-shortcuts.test.tsx
git -C D:/Github/ICP-X/NewP commit -m "feat: split business dashboard home"
```

## Task 2: Add typed PLM dashboard fixtures and top-card / todo content

**Files:**
- Create: `frontend/packages/shell/src/business-home/types.ts`
- Create: `frontend/packages/shell/src/business-home/fixtures.ts`
- Create: `frontend/packages/shell/src/components/business-home/BusinessHeroCards.tsx`
- Create: `frontend/packages/shell/src/components/business-home/BusinessTodoPanel.tsx`
- Modify: `frontend/packages/shell/src/pages/BusinessDashboardHome.tsx`
- Modify: `frontend/packages/shell/src/__tests__/business-dashboard-home.test.tsx`

- [ ] **Step 1: Write the failing content-hierarchy test**

```tsx
it('shows the three action-summary cards above the main todo list', async () => {
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

  expect(container.textContent).toContain('我的待办');
  expect(container.textContent).toContain('待我审批');
  expect(container.textContent).toContain('本周关键变更');
  expect(container.textContent).toContain('待办任务列表');
  expect(container.textContent).toContain('变更单评审');
});
```

- [ ] **Step 2: Run the focused test to confirm it fails**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/business-dashboard-home.test.tsx
```

Expected: FAIL because the page still renders placeholder text only.

- [ ] **Step 3: Add typed dashboard fixture data**

```ts
// frontend/packages/shell/src/business-home/types.ts
export interface BusinessHeroCard {
  id: 'my-todos' | 'my-approvals' | 'weekly-changes';
  title: string;
  value: string;
  note: string;
}

export interface BusinessTodoItem {
  id: string;
  title: string;
  projectName: string;
  dueHint: string;
  priority: 'high' | 'medium' | 'low';
}
```

```ts
// frontend/packages/shell/src/business-home/fixtures.ts
import type { BusinessHeroCard, BusinessTodoItem } from './types';

export const businessHeroCards: BusinessHeroCard[] = [
  { id: 'my-todos', title: '我的待办', value: '12', note: '3 项需要今天处理' },
  { id: 'my-approvals', title: '待我审批', value: '5', note: '2 项已接近超时' },
  { id: 'weekly-changes', title: '本周关键变更', value: '4', note: '含 1 项发布前风险' },
];

export const businessTodoItems: BusinessTodoItem[] = [
  { id: 'eco-review', title: '变更单评审', projectName: '电控总成', dueHint: '今天 16:00 前', priority: 'high' },
  { id: 'bom-confirm', title: 'BOM 差异确认', projectName: '新平台样机', dueHint: '明天 10:00 前', priority: 'high' },
  { id: 'doc-sync', title: '设计文档同步', projectName: '结构件规范', dueHint: '本周内', priority: 'medium' },
];
```

- [ ] **Step 4: Add the hero-card and todo-panel components and compose them into the page**

```tsx
// frontend/packages/shell/src/components/business-home/BusinessHeroCards.tsx
import type { BusinessHeroCard } from '../../business-home/types';

interface BusinessHeroCardsProps {
  items: BusinessHeroCard[];
}

export function BusinessHeroCards({ items }: BusinessHeroCardsProps) {
  return (
    <section className="sh-business-hero-grid">
      {items.map((item) => (
        <article key={item.id} className="sh-business-hero-card">
          <span className="sh-summary-card-label">{item.title}</span>
          <strong className="sh-summary-card-value">{item.value}</strong>
          <span className="sh-summary-card-meta">{item.note}</span>
        </article>
      ))}
    </section>
  );
}
```

```tsx
// frontend/packages/shell/src/components/business-home/BusinessTodoPanel.tsx
import type { BusinessTodoItem } from '../../business-home/types';

interface BusinessTodoPanelProps {
  items: BusinessTodoItem[];
}

export function BusinessTodoPanel({ items }: BusinessTodoPanelProps) {
  return (
    <section className="sh-page-panel">
      <div className="sh-page-panel-header">
        <h2 className="sh-page-panel-title">待办任务列表</h2>
      </div>
      <div className="sh-page-panel-body">
        <ul className="sh-business-todo-list">
          {items.map((item) => (
            <li key={item.id} className="sh-business-todo-item">
              <strong>{item.title}</strong>
              <span>{item.projectName}</span>
              <span>{item.dueHint}</span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  );
}
```

```tsx
// frontend/packages/shell/src/pages/BusinessDashboardHome.tsx
import { businessHeroCards, businessTodoItems } from '../business-home/fixtures';
import { BusinessHeroCards } from '../components/business-home/BusinessHeroCards';
import { BusinessTodoPanel } from '../components/business-home/BusinessTodoPanel';

export default function BusinessDashboardHome() {
  return (
    <div className="sh-page sh-business-dashboard">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">业务视图</span>
          <h1 className="sh-page-title">PLM 业务概览</h1>
          <p className="sh-page-description">
            首页优先展示个人任务、待审批事项与本周关键变更。
          </p>
        </div>
      </section>
      <BusinessHeroCards items={businessHeroCards} />
      <BusinessTodoPanel items={businessTodoItems} />
    </div>
  );
}
```

- [ ] **Step 5: Re-run the business-dashboard test**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/business-dashboard-home.test.tsx
```

Expected: PASS with the three top cards shown above the main todo panel and `变更单评审` visible in the list.

- [ ] **Step 6: Commit the top-card and todo sections**

```bash
git -C D:/Github/ICP-X/NewP add \
  frontend/packages/shell/src/business-home/types.ts \
  frontend/packages/shell/src/business-home/fixtures.ts \
  frontend/packages/shell/src/components/business-home/BusinessHeroCards.tsx \
  frontend/packages/shell/src/components/business-home/BusinessTodoPanel.tsx \
  frontend/packages/shell/src/pages/BusinessDashboardHome.tsx \
  frontend/packages/shell/src/__tests__/business-dashboard-home.test.tsx
git -C D:/Github/ICP-X/NewP commit -m "feat: add PLM dashboard hero cards and todo panel"
```

## Task 3: Add the right-side flow rail and lower collaboration sections

**Files:**
- Create: `frontend/packages/shell/src/components/business-home/BusinessFlowRail.tsx`
- Create: `frontend/packages/shell/src/components/business-home/BusinessProjectsPanel.tsx`
- Create: `frontend/packages/shell/src/components/business-home/BusinessQuickLinksPanel.tsx`
- Modify: `frontend/packages/shell/src/business-home/types.ts`
- Modify: `frontend/packages/shell/src/business-home/fixtures.ts`
- Modify: `frontend/packages/shell/src/pages/BusinessDashboardHome.tsx`
- Modify: `frontend/packages/shell/src/__tests__/business-dashboard-home.test.tsx`
- Modify: `frontend/packages/shell/src/__tests__/home-page-shortcuts.test.tsx`

- [ ] **Step 1: Write the failing right-rail and lower-sections test**

```tsx
it('renders flow status on the right and collaboration sections below the main task area', async () => {
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

  expect(container.textContent).toContain('变更流程状态');
  expect(container.textContent).toContain('发布提醒');
  expect(container.textContent).toContain('风险提示');
  expect(container.textContent).toContain('我参与的项目');
  expect(container.textContent).toContain('快捷入口');
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/business-dashboard-home.test.tsx
```

Expected: FAIL because the dashboard still has no right rail or lower collaboration sections.

- [ ] **Step 3: Extend fixture types and data for the flow rail, projects, and quick links**

```ts
// frontend/packages/shell/src/business-home/types.ts
export interface BusinessFlowItem {
  id: string;
  title: string;
  value: string;
  note: string;
}

export interface BusinessProjectItem {
  id: string;
  title: string;
  roleLabel: string;
  note: string;
}

export interface BusinessQuickLink {
  id: string;
  label: string;
  to: string;
  note: string;
}
```

```ts
// frontend/packages/shell/src/business-home/fixtures.ts
export const businessFlowItems: BusinessFlowItem[] = [
  { id: 'flow-status', title: '变更流程状态', value: '8', note: '处理中变更单' },
  { id: 'publish-alerts', title: '发布提醒', value: '2', note: '本周待发布' },
  { id: 'risk-alerts', title: '风险提示', value: '1', note: '存在发布阻断项' },
];

export const businessProjects: BusinessProjectItem[] = [
  { id: 'proj-a', title: '电控总成重构', roleLabel: '评审人', note: '本周 2 个待跟进事项' },
  { id: 'proj-b', title: '样机 BOM 冻结', roleLabel: '参与人', note: '等待变更确认' },
];

export const businessQuickLinks: BusinessQuickLink[] = [
  { id: 'change-center', label: '进入变更中心', to: '/demo', note: '查看当前变更任务' },
  { id: 'pending-approvals', label: '查看待审批', to: '/demo/list', note: '聚焦审批事项' },
  { id: 'document-hub', label: '打开文档中心', to: '/files', note: '处理相关文档' },
];
```

- [ ] **Step 4: Implement the flow-rail, projects, and quick-links components and compose the full dashboard**

```tsx
// frontend/packages/shell/src/components/business-home/BusinessFlowRail.tsx
import type { BusinessFlowItem } from '../../business-home/types';

interface BusinessFlowRailProps {
  items: BusinessFlowItem[];
}

export function BusinessFlowRail({ items }: BusinessFlowRailProps) {
  return (
    <aside className="sh-business-flow-rail">
      {items.map((item) => (
        <section key={item.id} className="sh-page-panel">
          <div className="sh-page-panel-header">
            <h3 className="sh-page-panel-title">{item.title}</h3>
          </div>
          <div className="sh-page-panel-body">
            <strong className="sh-summary-card-value">{item.value}</strong>
            <div className="sh-page-panel-meta">{item.note}</div>
          </div>
        </section>
      ))}
    </aside>
  );
}
```

```tsx
// frontend/packages/shell/src/components/business-home/BusinessProjectsPanel.tsx
import type { BusinessProjectItem } from '../../business-home/types';

interface BusinessProjectsPanelProps {
  items: BusinessProjectItem[];
}

export function BusinessProjectsPanel({ items }: BusinessProjectsPanelProps) {
  return (
    <section className="sh-page-panel">
      <div className="sh-page-panel-header">
        <h2 className="sh-page-panel-title">我参与的项目</h2>
      </div>
      <div className="sh-page-panel-body">
        {items.map((item) => (
          <div key={item.id} className="sh-business-project-item">
            <strong>{item.title}</strong>
            <span>{item.roleLabel}</span>
            <span>{item.note}</span>
          </div>
        ))}
      </div>
    </section>
  );
}
```

```tsx
// frontend/packages/shell/src/components/business-home/BusinessQuickLinksPanel.tsx
import { Link } from 'react-router-dom';
import type { BusinessQuickLink } from '../../business-home/types';

interface BusinessQuickLinksPanelProps {
  items: BusinessQuickLink[];
}

export function BusinessQuickLinksPanel({ items }: BusinessQuickLinksPanelProps) {
  return (
    <section className="sh-page-panel">
      <div className="sh-page-panel-header">
        <h2 className="sh-page-panel-title">快捷入口</h2>
      </div>
      <div className="sh-page-panel-body">
        {items.map((item) => (
          <div key={item.id} className="sh-business-link-item">
            <Link to={item.to}>{item.label}</Link>
            <div className="sh-page-panel-meta">{item.note}</div>
          </div>
        ))}
      </div>
    </section>
  );
}
```

```tsx
// frontend/packages/shell/src/pages/BusinessDashboardHome.tsx
import {
  businessFlowItems,
  businessHeroCards,
  businessProjects,
  businessQuickLinks,
  businessTodoItems,
} from '../business-home/fixtures';
import { BusinessFlowRail } from '../components/business-home/BusinessFlowRail';
import { BusinessProjectsPanel } from '../components/business-home/BusinessProjectsPanel';
import { BusinessQuickLinksPanel } from '../components/business-home/BusinessQuickLinksPanel';

export default function BusinessDashboardHome() {
  return (
    <div className="sh-page sh-business-dashboard">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">业务视图</span>
          <h1 className="sh-page-title">PLM 业务概览</h1>
          <p className="sh-page-description">
            首页优先展示个人任务、待审批事项与本周关键变更。
          </p>
        </div>
      </section>
      <BusinessHeroCards items={businessHeroCards} />
      <div className="sh-business-main-grid">
        <BusinessTodoPanel items={businessTodoItems} />
        <BusinessFlowRail items={businessFlowItems} />
      </div>
      <div className="sh-section-grid sh-section-grid-2">
        <BusinessProjectsPanel items={businessProjects} />
        <BusinessQuickLinksPanel items={businessQuickLinks} />
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Re-run the business-home tests**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/business-dashboard-home.test.tsx src/__tests__/home-page-shortcuts.test.tsx
```

Expected: PASS with the right rail and both lower sections present; `home-page-shortcuts.test.tsx` should now validate dashboard quick links instead of the retired portal list.

- [ ] **Step 6: Commit the full dashboard content scaffold**

```bash
git -C D:/Github/ICP-X/NewP add \
  frontend/packages/shell/src/components/business-home/BusinessFlowRail.tsx \
  frontend/packages/shell/src/components/business-home/BusinessProjectsPanel.tsx \
  frontend/packages/shell/src/components/business-home/BusinessQuickLinksPanel.tsx \
  frontend/packages/shell/src/business-home/types.ts \
  frontend/packages/shell/src/business-home/fixtures.ts \
  frontend/packages/shell/src/pages/BusinessDashboardHome.tsx \
  frontend/packages/shell/src/__tests__/business-dashboard-home.test.tsx \
  frontend/packages/shell/src/__tests__/home-page-shortcuts.test.tsx
git -C D:/Github/ICP-X/NewP commit -m "feat: add PLM dashboard flow rail and collaboration sections"
```

## Task 4: Add dashboard-specific styling and responsive layout behavior

**Files:**
- Modify: `frontend/packages/shell/src/index.css`
- Modify: `frontend/packages/shell/src/pages/BusinessDashboardHome.tsx`
- Modify: `frontend/packages/shell/src/__tests__/business-dashboard-home.test.tsx`

- [ ] **Step 1: Write the failing semantic-layout test**

```tsx
it('marks the business dashboard container and main grid with dedicated class names', async () => {
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

  expect(container.querySelector('.sh-business-dashboard')).toBeTruthy();
  expect(container.querySelector('.sh-business-main-grid')).toBeTruthy();
  expect(container.querySelector('.sh-business-flow-rail')).toBeTruthy();
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test -- src/__tests__/business-dashboard-home.test.tsx
```

Expected: FAIL until the layout classes and final structure are wired consistently.

- [ ] **Step 3: Add the business dashboard CSS**

```css
/* frontend/packages/shell/src/index.css */
.sh-business-dashboard {
  display: flex;
  flex-direction: column;
  gap: var(--sh-space-6);
}

.sh-business-hero-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--sh-space-4);
}

.sh-business-hero-card {
  display: flex;
  flex-direction: column;
  gap: var(--sh-space-2);
  padding: var(--sh-space-6);
  border: 1px solid var(--sh-border);
  border-radius: var(--sh-radius-lg);
  background: linear-gradient(180deg, #fff 0%, #f8fbff 100%);
  box-shadow: var(--sh-shadow-sm);
}

.sh-business-main-grid {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 320px;
  gap: var(--sh-space-4);
  align-items: start;
}

.sh-business-flow-rail {
  display: grid;
  gap: var(--sh-space-4);
}

.sh-business-todo-list,
.sh-business-project-item,
.sh-business-link-item {
  display: grid;
  gap: var(--sh-space-2);
}

@media (max-width: 1200px) {
  .sh-business-hero-grid {
    grid-template-columns: 1fr;
  }

  .sh-business-main-grid {
    grid-template-columns: 1fr;
  }
}
```

- [ ] **Step 4: Ensure `BusinessDashboardHome.tsx` uses the final class names consistently**

```tsx
// frontend/packages/shell/src/pages/BusinessDashboardHome.tsx
return (
  <div className="sh-page sh-business-dashboard">
    <section className="sh-page-header">
      <div className="sh-page-header-main">
        <span className="sh-page-eyebrow">业务视图</span>
        <h1 className="sh-page-title">PLM 业务概览</h1>
        <p className="sh-page-description">
          首页优先展示个人任务、待审批事项与本周关键变更。
        </p>
      </div>
    </section>
    <BusinessHeroCards items={businessHeroCards} />
    <div className="sh-business-main-grid">
      <BusinessTodoPanel items={businessTodoItems} />
      <BusinessFlowRail items={businessFlowItems} />
    </div>
    <div className="sh-section-grid sh-section-grid-2">
      <BusinessProjectsPanel items={businessProjects} />
      <BusinessQuickLinksPanel items={businessQuickLinks} />
    </div>
  </div>
);
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

- [ ] **Step 6: Commit the styled dashboard layout**

```bash
git -C D:/Github/ICP-X/NewP add \
  frontend/packages/shell/src/index.css \
  frontend/packages/shell/src/pages/BusinessDashboardHome.tsx \
  frontend/packages/shell/src/__tests__/business-dashboard-home.test.tsx
git -C D:/Github/ICP-X/NewP commit -m "feat: style PLM business dashboard layout"
```

## Task 5: Final verification and implementation notes

**Files:**
- Modify: `docs/superpowers/specs/2026-04-11-plm-business-home-dashboard-design.md` (only if implementation reveals a mismatch)
- Modify: `frontend/packages/shell/src/pages/BusinessDashboardHome.tsx` (only if final polish is needed)

- [ ] **Step 1: Re-run the exact verification commands**

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell test
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell build
```

- [ ] **Step 2: Smoke-check the dashboard in dev mode**

Run:

```bash
pnpm --dir D:/Github/ICP-X/NewP/frontend --filter @icp-platform/shell dev
```

Expected manual checks:

- The header reads `PLM 业务概览`.
- The first row contains the three top cards.
- The central column is visually larger than the right rail.
- The right rail contains `变更流程状态 / 发布提醒 / 风险提示`.
- The bottom row contains `我参与的项目` and `快捷入口`.

- [ ] **Step 3: If the implementation exposed a spec mismatch, append a short implementation note to the spec**

```md
## Implementation Notes

- V1 ships with local fixture-backed dashboard data only.
- Real task aggregation, approval queues, and PLM change metrics remain future integration work.
```

- [ ] **Step 4: Prepare the handoff summary**

```md
- Implemented: business dashboard page split, hero cards, todo panel, flow rail, projects panel, quick links panel, dashboard styles
- Verified: shell Vitest suite, shell build, manual dashboard smoke-check
- Deferred: real PLM aggregation APIs, live approval counts, live project summaries
```

- [ ] **Step 5: Commit any final spec alignment or polish**

```bash
git -C D:/Github/ICP-X/NewP add \
  docs/superpowers/specs/2026-04-11-plm-business-home-dashboard-design.md \
  frontend/packages/shell/src/pages/BusinessDashboardHome.tsx
git -C D:/Github/ICP-X/NewP commit -m "docs: align PLM dashboard implementation notes"
```

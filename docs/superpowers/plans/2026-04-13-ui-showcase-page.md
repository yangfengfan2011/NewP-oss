# UI Showcase Page Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `frontend/packages/shell` 中新增一个带左侧导航入口的控件展示页，集中展示 `@icp-platform/ui` 当前已有控件并提供最小交互示例。

**Architecture:** 方案收敛在 Shell 内部，新增一个 `UiShowcase` 页面并直接注册到应用路由。页面使用 `@icp-platform/ui` 组件自身搭建演示内容，所有交互都由页面本地 state 和 mock 数据驱动，不接真实业务数据。测试先覆盖页面可达性与关键内容渲染，再实现最小代码使其通过。

**Tech Stack:** React 18、TypeScript、React Router、Vitest、`@icp-platform/ui`

---

## 文件结构与职责

- `frontend/packages/shell/src/pages/UiShowcase.tsx`
  - 新增页面组件，负责展示四类控件及其最小交互示例。
- `frontend/packages/shell/src/App.tsx`
  - 注册 `/ui-showcase` 路由。
- `frontend/packages/shell/src/components/Sidebar.tsx`
  - 增加“控件展示”导航入口。
- `frontend/packages/shell/src/__tests__/ui-showcase-page.test.tsx`
  - 新增页面渲染测试，覆盖标题、分组和代表控件展示。
- `frontend/packages/shell/src/__tests__/ui-showcase-navigation.test.tsx`
  - 新增导航测试，覆盖侧栏入口与链接目标。

## Task 1: 补导航入口测试

**Files:**
- Create: `frontend/packages/shell/src/__tests__/ui-showcase-navigation.test.tsx`
- Modify: none
- Test: `frontend/packages/shell/src/__tests__/ui-showcase-navigation.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import Sidebar from '../components/Sidebar';

describe('UI showcase navigation', () => {
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

  it('shows ui showcase link in sidebar', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <MemoryRouter>
          <Sidebar />
        </MemoryRouter>,
      );
    });

    expect(container.textContent).toContain('控件展示');
    const link = Array.from(container.querySelectorAll('a')).find((item) =>
      item.textContent?.includes('控件展示'),
    );
    expect(link?.getAttribute('href')).toBe('/ui-showcase');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/ui-showcase-navigation.test.tsx`

Expected: FAIL，因为当前侧栏还没有“控件展示”入口。

- [ ] **Step 3: Write minimal implementation**

在 `frontend/packages/shell/src/components/Sidebar.tsx` 中，按现有导航声明方式新增一项：

```tsx
{
  label: '控件展示',
  to: '/ui-showcase',
}
```

只新增导航项，不顺带重构现有导航结构。

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/ui-showcase-navigation.test.tsx`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add frontend/packages/shell/src/components/Sidebar.tsx frontend/packages/shell/src/__tests__/ui-showcase-navigation.test.tsx
git commit -m "测试：补充控件展示页导航入口"
```

## Task 2: 补展示页渲染测试

**Files:**
- Create: `frontend/packages/shell/src/__tests__/ui-showcase-page.test.tsx`
- Modify: none
- Test: `frontend/packages/shell/src/__tests__/ui-showcase-page.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import UiShowcase from '../pages/UiShowcase';

describe('UiShowcase page', () => {
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

  it('renders title, sections, and representative controls', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(<UiShowcase />);
    });

    expect(container.textContent).toContain('控件展示');
    expect(container.textContent).toContain('基础控件');
    expect(container.textContent).toContain('结构型页面块');
    expect(container.textContent).toContain('复合控件');
    expect(container.textContent).toContain('候选控件');
    expect(container.textContent).toContain('Button');
    expect(container.textContent).toContain('PageHeader');
    expect(container.textContent).toContain('Modal');
    expect(container.textContent).toContain('Tree');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/ui-showcase-page.test.tsx`

Expected: FAIL，因为 `UiShowcase.tsx` 还不存在。

- [ ] **Step 3: Write minimal implementation**

先新增一个最小页面骨架 `frontend/packages/shell/src/pages/UiShowcase.tsx`：

```tsx
export default function UiShowcase() {
  return (
    <div>
      <h1>控件展示</h1>
      <section>
        <h2>基础控件</h2>
        <div>Button</div>
      </section>
      <section>
        <h2>结构型页面块</h2>
        <div>PageHeader</div>
      </section>
      <section>
        <h2>复合控件</h2>
        <div>Modal</div>
      </section>
      <section>
        <h2>候选控件</h2>
        <div>Tree</div>
      </section>
    </div>
  );
}
```

这里只写最小骨架，让测试先转绿。

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/ui-showcase-page.test.tsx`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add frontend/packages/shell/src/pages/UiShowcase.tsx frontend/packages/shell/src/__tests__/ui-showcase-page.test.tsx
git commit -m "测试：补充控件展示页基础渲染"
```

## Task 3: 接入页面路由

**Files:**
- Modify: `frontend/packages/shell/src/App.tsx`
- Test: `frontend/packages/shell/src/__tests__/ui-showcase-page.test.tsx`

- [ ] **Step 1: Write the failing test**

在 `frontend/packages/shell/src/__tests__/ui-showcase-page.test.tsx` 增加一条路由可达性测试：

```tsx
import { MemoryRouter, Routes, Route } from 'react-router-dom';

it('renders through ui-showcase route', async () => {
  container = document.createElement('div');
  document.body.appendChild(container);

  await act(async () => {
    root = createRoot(container);
    root.render(
      <MemoryRouter initialEntries={['/ui-showcase']}>
        <Routes>
          <Route path="/ui-showcase" element={<UiShowcase />} />
        </Routes>
      </MemoryRouter>,
    );
  });

  expect(container.textContent).toContain('控件展示');
});
```

如果测试已经通过，则补一条针对 `App.tsx` 的轻量接线验证，确保实现后不遗漏正式路由注册。

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/ui-showcase-page.test.tsx`

Expected: FAIL，如果测试改为依赖真实 `AppRoutes` 接线，则在路由未注册前失败。

- [ ] **Step 3: Write minimal implementation**

在 `frontend/packages/shell/src/App.tsx` 中：

```tsx
import UiShowcase from './pages/UiShowcase';
```

并在受保护路由下增加：

```tsx
<Route path="ui-showcase" element={<UiShowcase />} />
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/ui-showcase-page.test.tsx`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add frontend/packages/shell/src/App.tsx frontend/packages/shell/src/__tests__/ui-showcase-page.test.tsx
git commit -m "功能：接入控件展示页路由"
```

## Task 4: 用真实组件替换展示页骨架

**Files:**
- Modify: `frontend/packages/shell/src/pages/UiShowcase.tsx`
- Test: `frontend/packages/shell/src/__tests__/ui-showcase-page.test.tsx`

- [ ] **Step 1: Write the failing test**

把页面测试补充为更具体的断言：

```tsx
expect(container.textContent).toContain('Button');
expect(container.textContent).toContain('Input');
expect(container.textContent).toContain('PageHeader');
expect(container.textContent).toContain('SummaryCard');
expect(container.textContent).toContain('ConfirmDialog');
expect(container.textContent).toContain('候选能力');
```

这样能驱动页面从占位文本升级为真实展示结构。

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/ui-showcase-page.test.tsx`

Expected: FAIL，因为当前页面仍是最小骨架。

- [ ] **Step 3: Write minimal implementation**

在 `frontend/packages/shell/src/pages/UiShowcase.tsx` 中：

- 从 `@icp-platform/ui` 导入所有要展示的组件。
- 用页面内 `useState` 管理最小交互状态。
- 建立四个分组区块：
  - 基础控件
  - 结构型页面块
  - 复合控件
  - 候选控件
- 每个区块中为代表控件提供最小示例。

实现约束：

```tsx
// 约束示例
const [inputValue, setInputValue] = useState('');
const [selectedTab, setSelectedTab] = useState('overview');
const [modalOpen, setModalOpen] = useState(false);
```

```tsx
<PageHeader
  title="控件展示"
  description="集中展示 @icp-platform/ui 当前已有控件与最小交互示例。"
/>
```

```tsx
<Panel title="基础控件">
  <Button variant="primary">主按钮</Button>
  <Input value={inputValue} onChange={(event) => setInputValue(event.target.value)} />
</Panel>
```

```tsx
<Panel title="候选控件">
  <Badge variant="warning">候选能力</Badge>
  <Tree data={treeData} />
</Panel>
```

只实现设计文档要求的最小演示，不额外抽象为通用数据模型。

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/ui-showcase-page.test.tsx`

Expected: PASS。

- [ ] **Step 5: Commit**

```bash
git add frontend/packages/shell/src/pages/UiShowcase.tsx frontend/packages/shell/src/__tests__/ui-showcase-page.test.tsx
git commit -m "功能：实现控件展示页最小交互示例"
```

## Task 5: 运行定向验证

**Files:**
- Modify: none
- Test: `frontend/packages/shell/src/__tests__/ui-showcase-navigation.test.tsx`
- Test: `frontend/packages/shell/src/__tests__/ui-showcase-page.test.tsx`

- [ ] **Step 1: Run navigation test**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/ui-showcase-navigation.test.tsx`

Expected: PASS。

- [ ] **Step 2: Run page test**

Run: `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/ui-showcase-page.test.tsx`

Expected: PASS。

- [ ] **Step 3: Run full shell test suite**

Run: `pnpm --dir .\frontend\packages\shell test`

Expected: PASS；如果失败，先修复与本次改动直接相关的问题。

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "验证：完成控件展示页测试验证"
```

## Task 6: 运行前端构建验证

**Files:**
- Modify: none
- Test: workspace build output

- [ ] **Step 1: Run frontend build**

Run: `pnpm --dir .\frontend build`

Expected: exit code 0，前端工作区构建通过。

- [ ] **Step 2: Review output for warnings or failures**

检查是否有：

- TypeScript 编译错误
- Vite 构建失败
- 新页面导入导致的路径或类型问题

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "验证：确认控件展示页前端构建通过"
```

## 自检结论

- 规格覆盖：
  - 正式页面：Task 2、Task 3、Task 4
  - 左侧导航入口：Task 1
  - 四类控件分区：Task 4
  - 最小交互示例：Task 4
  - 测试与构建验证：Task 5、Task 6
  - 回滚边界无需单独实现代码，已由文件边界天然保证
- 占位符检查：
  - 无 `TODO` / `TBD`
  - 每个任务都有明确文件、命令与期望结果
- 一致性检查：
  - 页面名统一为 `UiShowcase`
  - 路由统一为 `/ui-showcase`
  - 导航名称统一为“控件展示”

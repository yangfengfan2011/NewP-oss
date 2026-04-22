# 标准列表页模板能力与人员中心试点 Implementation Plan

## 状态

- `StandardListPage` 已落地为标准列表页渲染内核。
- `PersonListPage -> StandardListPage` 现仅保留为过渡兼容记录，不再作为后续主线架构继续扩散。
- 后续正式实施主线已切换到 `docs/superpowers/plans/2026-04-22-nav-metadata-standard-list-entry.md`。
- 本计划保留为“模板能力落地 + 人员中心过渡试点”的历史执行记录。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 先沉淀平台级标准列表页模板能力，再让组织模块人员中心列表页迁移到该模板上，验证后续同类功能可以通过调整模板层获得统一样式收益。

**Architecture:** 本计划分两阶段推进。第一阶段在 `frontend/packages/ui` 收口标准列表页模板代码内核，只统一页面骨架与布局，不把行为下沉到 metadata；第二阶段把组织模块人员中心列表页迁移到模板上，保留其数据查询、字段渲染、列渲染与 `drawer` 编辑承载，从而验证模板能力可以在真实模块落地。

**Tech Stack:** React 18 / TypeScript / Vite / `@icp-platform/ui` / 组织模块前端包 / 现有 metadata runtime 消费链

**Preconditions:** 本计划默认 `modules/Organization/metadata/views/PersonList.view.json` 及其前端运行时读取链路已经可用；当前计划只调整模板消费方式，不重新实现 metadata consumer alignment。

---

## 文件结构与职责

**Create:**

- `frontend/packages/ui/src/patterns/StandardListPage.tsx`  
  平台级标准列表页模板组件，承接头部、轻筛选条、表格区、底部分页/导出与高级筛选抽屉骨架。
- `frontend/packages/ui/src/patterns/StandardListPage.types.ts`  
  若模板 props 需要独立收口，可拆出单独类型文件。
- `docs/designs/platform/standard-list-page-template-design.md`  
  平台级设计文档。
- `docs/designs/platform/standard-list-page-template-consistency-review.md`  
  模板能力一致性 review。

**Modify:**

- `frontend/packages/ui/src/index.ts`
- `frontend/modules/organization/src/pages/PersonListPage.tsx`
- `frontend/modules/organization/src/runtime/personCenterRuntime.ts`
- `modules/Organization/metadata/views/PersonList.view.json`
  仅在模板接入需要补稳定声明时最小调整；不得把执行逻辑塞回 metadata。
- `docs/designs/modules/organization-person-center-pilot-design.md`
  最小回填“列表页优先落到平台模板”这一新实现路径。

**Out of Scope in this plan:**

- `frontend/modules/organization/src/pages/PersonDetailPage.tsx`
- `frontend/modules/organization/src/components/PersonEditorForm.tsx`
- `frontend/modules/organization/src/components/PersonUserLinkCard.tsx`
- `frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx`
- 详情页模板化
- `page / drawer / dialog` 统一编辑模板
- Shell 内部结构改造

---

### Task 1: 为标准列表页模板能力写前端设计与回填试点设计

**Files:**
- Create: `docs/designs/platform/standard-list-page-template-design.md`
- Create: `docs/designs/platform/standard-list-page-template-consistency-review.md`
- Modify: `docs/designs/modules/organization-person-center-pilot-design.md`

- [x] **Step 1: 写平台级模板设计文档**

文档应覆盖：

- 为什么当前需要模板代码内核，而不是继续在业务页拼结构
- 模板与 metadata、业务页、Shell 的边界
- 首轮只做列表页模板，不做详情页模板
- 人员中心列表页作为首个试点的接入方式

- [x] **Step 2: 写一致性 review 文档**

review 至少要显式检查：

- 是否仍遵守三层元数据边界
- 模板是否放在共享层，而不是 Shell
- 首轮范围是否仍只聚焦列表页模板

- [x] **Step 3: 回填人员中心试点设计**

在 `organization-person-center-pilot-design.md` 中补最小文字，明确：

- 人员中心仍是试点主线
- 但列表页实现路径应优先接入平台标准列表页模板

- [x] **Step 4: 自检文档一致性**

检查以下文档之间是否存在冲突：

- `docs/designs/platform/metadata-driven-backoffice-ui-design.md`
- `docs/designs/platform/list-management-layout-demo-design.md`
- `docs/designs/platform/standard-list-page-template-design.md`
- `docs/designs/modules/organization-person-center-pilot-design.md`

- [ ] **Step 5: 提交文档变更**

```bash
git add docs/designs/platform/standard-list-page-template-design.md docs/designs/platform/standard-list-page-template-consistency-review.md docs/designs/modules/organization-person-center-pilot-design.md
git commit -m "设计：补充标准列表页模板能力设计"
```

### Task 2: 先为模板组件补最小可构建入口

**Files:**
- Create: `frontend/packages/ui/src/patterns/StandardListPage.tsx`
- Create: `frontend/packages/ui/src/patterns/StandardListPage.types.ts`
- Modify: `frontend/packages/ui/src/index.ts`

- [x] **Step 1: 为模板组件写最小类型定义**

```ts
import type { ReactNode } from 'react';

export interface StandardListPageProps {
  title: string;
  description?: string;
  headerActions?: ReactNode;
  filterBar?: ReactNode;
  content: ReactNode;
  footerLeft?: ReactNode;
  footerRight?: ReactNode;
  advancedFilterTrigger?: ReactNode;
  advancedFilterOpen?: boolean;
  advancedFilterContent?: ReactNode;
  onAdvancedFilterClose?: () => void;
}
```

- [x] **Step 2: 先写最小模板组件实现**

```tsx
import { Drawer, PageHeader } from '../index';
import type { StandardListPageProps } from './StandardListPage.types';

export default function StandardListPage({
  title,
  description,
  headerActions,
  filterBar,
  content,
  footerLeft,
  footerRight,
  advancedFilterOpen = false,
  advancedFilterContent,
  onAdvancedFilterClose,
}: StandardListPageProps) {
  return (
    <div className="sh-page">
      <PageHeader title={title} description={description} actions={headerActions} />
      {filterBar}
      <section className="sh-page-stack">{content}</section>
      {(footerLeft || footerRight) ? (
        <section className="sh-panel">
          <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, flexWrap: 'wrap' }}>
            <div>{footerLeft}</div>
            <div>{footerRight}</div>
          </div>
        </section>
      ) : null}
      <Drawer open={advancedFilterOpen} onClose={onAdvancedFilterClose ?? (() => {})} title="高级筛选">
        {advancedFilterContent}
      </Drawer>
    </div>
  );
}
```

- [x] **Step 3: 在 `ui` 导出模板组件**

```ts
export { default as StandardListPage } from './patterns/StandardListPage';
export type { StandardListPageProps } from './patterns/StandardListPage.types';
```

- [x] **Step 4: 运行 `ui` 包构建**

Run:

```powershell
cd frontend
pnpm --filter @icp-platform/ui build
```

Expected:

```text
... build completed successfully ...
```

- [ ] **Step 5: 提交模板组件骨架**

```bash
git add frontend/packages/ui/src/patterns/StandardListPage.tsx frontend/packages/ui/src/patterns/StandardListPage.types.ts frontend/packages/ui/src/index.ts
git commit -m "功能：新增标准列表页模板骨架"
```

### Task 3: 将人员中心列表页迁移到标准模板骨架

**Files:**
- Modify: `frontend/modules/organization/src/pages/PersonListPage.tsx`
- Modify: `frontend/modules/organization/src/runtime/personCenterRuntime.ts`

- [x] **Step 1: 为人员中心列表页写迁移前的构建验证**

Run:

```powershell
cd frontend
pnpm --filter @icp-modules/organization build
```

Expected:

```text
... build completed successfully ...
```

- [x] **Step 2: 将页面头部、筛选区、表格区、底部区迁入 `StandardListPage`**

迁移后 `PersonListPage.tsx` 应保留：

- schema 读取
- 公司/部门/人员数据加载
- filters 与 appliedFilters 状态
- 列定义
- 动作处理

并改成：

```tsx
return (
  <StandardListPage
    title={resolveListText(listSchema.titleKey, '人员中心')}
    description={resolveListText(listSchema.descriptionKey, '人员列表维护')}
    headerActions={headerActions}
    filterBar={filterBar}
    content={tableContent}
    footerLeft={footerLeft}
    footerRight={footerRight}
    advancedFilterOpen={advancedFilterOpen}
    advancedFilterContent={advancedFilterContent}
    onAdvancedFilterClose={() => setAdvancedFilterOpen(false)}
  />
);
```

- [x] **Step 3: 保持高级筛选抽屉与 `drawer` 编辑逻辑不变**

要求：

- 高级筛选仍由 `advancedFilterOpen` 控制
- 人员新建/查看/编辑仍沿用当前 `Drawer`
- 不在本任务中引入统一编辑模板

- [x] **Step 4: 若模板接入暴露 metadata 缺口，仅补最小 runtime 对齐**

允许调整：

- `descriptionKey` 的 fallback 行为
- `advancedFilter` 标题 fallback

不允许调整：

- 把查询动作逻辑写回 metadata
- 为模板接入扩展新的行为型 metadata 字段

执行记录：

- 本轮接入未暴露新的 metadata 缺口。
- `frontend/modules/organization/src/runtime/personCenterRuntime.ts` 与 `modules/Organization/metadata/views/PersonList.view.json` 本轮未改动。

- [x] **Step 5: 运行组织模块构建验证**

Run:

```powershell
cd frontend
pnpm --filter @icp-modules/organization build
pnpm build
```

Expected:

```text
... build completed successfully ...
```

- [ ] **Step 6: 提交人员中心列表页迁移**

```bash
git add frontend/modules/organization/src/pages/PersonListPage.tsx frontend/modules/organization/src/runtime/personCenterRuntime.ts modules/Organization/metadata/views/PersonList.view.json
git commit -m "重构：人员中心列表页接入标准模板"
```

### Task 4: 做试点验证并记录风险

**Files:**
- Modify: `docs/superpowers/plans/2026-04-21-standard-list-page-template-pilot.md`
  仅勾选执行记录；若仓库已有专门验证文档，也可追加到对应验证文档。

- [ ] **Step 1: 本地启动或构建后确认列表页结构**

至少确认：

- 页面头部仍显示标题与主动作
- 轻筛选条仍显示关键字、公司、部门、状态
- 表格区仍能显示加载态/空态/数据态
- 底部分页/导出仍在统一底部区域
- 高级筛选抽屉仍可打开与关闭

执行记录：

- 已完成构建级验证：
  - `pnpm --filter @icp-platform/ui build`
  - `pnpm --filter @icp-modules/organization build`
  - `pnpm build`
- 已补充浏览器级 smoke：
  - 启动本地 Host API 与前端 dev server 后，使用 Edge headless 登录 `admin / admin123`。
  - 已验证 `http://localhost:3000/organization/persons` 能正常打开，并确认页面包含“人员中心 / 关键字 / 新建”。
  - 当前 live dev server 不直接产出 runtime metadata JSON，因此该 smoke 验证的是兼容入口可用性；`nav metadata -> host` 链路以后续计划中的专项测试为主证据。

- [x] **Step 2: 记录已知风险**

至少记录：

- 首轮模板仍偏轻量，字段与列渲染未完全模板化
- 详情页仍未模板化
- 后续模块接入前还需要再验证一个非组织模块样本

当前记录：

- 首轮模板仍是轻量骨架，查询字段和表格列尚未进入统一 renderer registry。
- 高级筛选抽屉与人员编辑抽屉仍由业务页自己提供内容，当前只统一外层结构。
- 详情页仍未模板化。

- [x] **Step 3: 形成验证结论**

应明确输出：

- 改了什么
- 影响了哪些文件/模块
- 构建验证结果
- 已知风险
- 回滚思路

当前结论：

- 已新增 `StandardListPage` 模板组件并导出到 `@icp-platform/ui`
- 已将 `PersonListPage` 迁移为“业务容器页 + 标准模板”结构
- 已完成 `ui` 包、组织模块包和前端整仓构建验证
- 已补齐浏览器级手动 smoke；当前仍未做本轮提交

- [ ] **Step 4: 提交验证记录**

```bash
git add docs/superpowers/plans/2026-04-21-standard-list-page-template-pilot.md
git commit -m "验证：确认标准列表页模板试点结果"
```

---

## Self-Review

- 本计划已把首轮范围收口为“标准列表页模板能力 + 人员中心列表页试点”。
- 本计划没有把详情页模板、统一编辑模板、Shell 改造重新拉回范围。
- 本计划中的文件落点均为仓库相对路径，符合仓库文档约束。

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-21-standard-list-page-template-pilot.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?

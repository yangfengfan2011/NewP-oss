# 人员中心前端 Pro 风格布局重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改业务链路的前提下，把人员中心重构为更接近 Ant Design Pro 的企业后台页面，并消除重复标题、重复新建按钮和重复分页。

**Architecture:** Shell 继续提供统一顶栏、侧边栏、面包屑和内容容器；标准 CRUD 列表宿主收口页面标题区、列表卡片和分页职责；人员模块继续通过资源注册提供筛选、列和行操作声明。视觉统一优先走 Ant Design theme token，页面特定结构差异只补少量受控样式。

**Tech Stack:** React 18 / TypeScript / Vite / Ant Design v5 / ProTable / Vitest

---

## 文件结构与职责

**Modify**

- `frontend/packages/shell/src/components/Layout.tsx`
  - 停止向人员中心内容区注入重复页面标题，只保留 breadcrumb chrome。
- `frontend/packages/shell/src/components/Header.tsx`
  - 重构为紧凑浅色顶栏。
- `frontend/packages/shell/src/components/Sidebar.tsx`
  - 重构为浅色侧边栏和轻量选中态。
- `frontend/packages/shell/src/components/PageContainer.tsx`
  - 保留面包屑和内容区包装，支持更轻的页面 chrome。
- `frontend/packages/shell/src/config/antdTheme.ts`
  - 统一 Shell、Menu、Button、Input、Select、Table token。
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
  - 收口页面标题区、筛选工具条、表格工具栏和分页职责。
- `frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx`
  - 如有必要，收紧通用动作展示风格，避免与新页面风格冲突。
- `frontend/packages/shell/src/index.css`
  - 补 Shell 布局、浅色侧栏、顶栏、人员中心 Pro 风格样式。
- `frontend/packages/ui/src/pro-adapters/SearchTablePage.tsx`
  - 允许宿主自定义 headerTitle、工具栏和分页展示细节。
- `frontend/packages/ui/src/pro-adapters/StatusTag.tsx`
  - 允许模块传入 className，支持低饱和状态样式。
- `frontend/packages/ui/src/styles/components.css`
  - 调整标准列表页、PageHeader、Panel 等基础样式，配合浅色企业后台结构。
- `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
  - 重写筛选工具条和行操作，保留原有查询、查看、编辑、停用逻辑。
- `frontend/packages/shell/src/__tests__/antd-shell-layout.test.tsx`
  - 更新浅色 Shell 结构断言。
- `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx`
  - 更新人员中心标题、工具栏、分页和链接操作断言。

**Do Not Modify**

- `server/**`
- `apps/**`
- `frontend/packages/sdk/src/**`
- `frontend/packages/shell/src/registerModules.ts`
- `frontend/packages/shell/src/metadata/**`
- `frontend/modules/organization/src/services/organizationService.ts`

---

### Task 0: 一致性 Review

**Files:**
- Read: `AGENTS.md`
- Read: `docs/superpowers/specs/platform/2026-05-17-person-center-frontend-pro-layout-design.md`
- Read: `frontend/packages/shell/src/components/Layout.tsx`
- Read: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- Read: `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`

- [ ] 检查工作区状态，确认没有需要避让的未提交代码。
- [ ] 对照设计确认本轮只做布局和样式收口，不扩散到服务层和 SDK。

### Task 1: Shell 主题与全局布局收口

**Files:**
- Modify: `frontend/packages/shell/src/components/Layout.tsx`
- Modify: `frontend/packages/shell/src/components/Header.tsx`
- Modify: `frontend/packages/shell/src/components/Sidebar.tsx`
- Modify: `frontend/packages/shell/src/components/PageContainer.tsx`
- Modify: `frontend/packages/shell/src/config/antdTheme.ts`
- Modify: `frontend/packages/shell/src/index.css`

- [ ] 将顶栏改为 52px 左右的浅色企业后台头部。
- [ ] 将侧边栏改为浅色背景、低干扰选中态和 216px 左右宽度。
- [ ] 让内容区背景变为浅灰，移除内容容器默认白卡片，避免与列表卡片职责冲突。
- [ ] 停止由 Shell 给人员中心内容区输出重复标题。

### Task 2: 列表宿主职责收口

**Files:**
- Modify: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- Modify: `frontend/packages/ui/src/pro-adapters/SearchTablePage.tsx`
- Modify: `frontend/packages/ui/src/styles/components.css`

- [ ] 页面标题区只保留“人员中心 + 描述 + 新建人员”一套结构。
- [ ] 表格卡片头只保留“人员列表 / 共 N 条 / 刷新 / 密度 / 列设置”。
- [ ] 删除额外 footer 分页，只保留 ProTable 底部分页。
- [ ] 统一 ProTable 表头、hover、分页、工具栏和卡片间距。

### Task 3: 人员中心筛选条与行操作重构

**Files:**
- Modify: `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
- Modify: `frontend/packages/ui/src/pro-adapters/StatusTag.tsx`
- Modify: `frontend/packages/shell/src/index.css`

- [ ] 将筛选区改为轻量工具条，字段顺序固定为关键字、公司、部门、在职状态、搜索、重置、更多筛选。
- [ ] 保留公司/部门联动和错误兜底，不改查询参数结构。
- [ ] 将行操作改为文字链接，查看/编辑为蓝色，停用为红色并保留二次确认。
- [ ] 将状态标签改为低饱和蓝/绿风格。

### Task 4: 测试与验证

**Files:**
- Modify: `frontend/packages/shell/src/__tests__/antd-shell-layout.test.tsx`
- Modify: `frontend/packages/shell/src/__tests__/organization-person-center-list-layout.test.tsx`

- [ ] 更新 Shell 测试，验证浅色布局和保留折叠入口。
- [ ] 更新人员中心列表测试，验证只保留一处主标题、一处新建入口和一处分页。
- [ ] 运行定向测试。
- [ ] 运行 `frontend/packages/shell` 构建。

## 验证命令

- `git status --short`
- `powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-05-17-person-center-frontend-pro-layout-design.md`
- `powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1`
- `pnpm --dir frontend/packages/shell test -- --run src/__tests__/antd-shell-layout.test.tsx src/__tests__/organization-person-center-list-layout.test.tsx`
- `pnpm --dir frontend/packages/shell build`

## 回滚提示

- 若 Shell 风格回归异常，先回滚 `Header.tsx`、`Sidebar.tsx`、`antdTheme.ts`、`index.css`。
- 若人员中心结构回归异常，回滚 `StandardCrudListPageHost.tsx`、`SearchTablePage.tsx`、`personStandardCrudResource.tsx`。
- 测试文件和文档可随实现一并回滚，不影响业务数据和接口。

# Frontend UI Component Strategy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `frontend/packages/ui` 中建立第一批可复用前端公共控件与结构块，并让 `Shell` 与至少两个现有模块页面切换到这套公共层，形成“先基础、后复合、按复用升级”的可执行路线。

**Architecture:** 本次实现不追求一次性做全量组件库，而是先把 `@icp-platform/ui` 作为真实可消费的 workspace 包立起来，承接 design token、基础控件和高复用页面结构块，然后用 `Login`、`Home`、`ConfigManagement`、`RoleManagement`、`PermissionList` 等现有页面验证抽象是否成立。复杂控件如 `Tree`、复杂 `Table`、权限矩阵和配置编辑器暂不进入第一批实现，只在后续真实功能需要时按第二批路线补充。

**Tech Stack:** React 18、TypeScript、Vite workspace、现有 `Shell`/模块页面 CSS 约定、pnpm workspace

---

## 文件结构

### 需要修改的现有文件

- `D:\Github\ICP-X\NewP\frontend\packages\ui\README.md`
- `D:\Github\ICP-X\NewP\frontend\packages\shell\package.json`
- `D:\Github\ICP-X\NewP\frontend\modules\config-center\package.json`
- `D:\Github\ICP-X\NewP\frontend\modules\authorization\package.json`
- `D:\Github\ICP-X\NewP\frontend\packages\shell\src\index.css`
- `D:\Github\ICP-X\NewP\frontend\packages\shell\src\main.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\shell\src\pages\Login.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\shell\src\pages\Home.tsx`
- `D:\Github\ICP-X\NewP\frontend\modules\config-center\src\pages\ConfigManagement.tsx`
- `D:\Github\ICP-X\NewP\frontend\modules\authorization\src\pages\RoleManagement.tsx`
- `D:\Github\ICP-X\NewP\frontend\modules\authorization\src\pages\PermissionList.tsx`

### 需要新增的文件

- `D:\Github\ICP-X\NewP\frontend\packages\ui\package.json`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\tsconfig.json`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\index.ts`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\styles\tokens.css`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\styles\base.css`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\Button.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\Input.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\TextArea.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\StatusBadge.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\LoadingState.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\EmptyState.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\ErrorState.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\PageHeader.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\Panel.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\SummaryCard.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\HelperBlock.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\MetaBlock.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\TableWrap.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\FormInline.tsx`
- `D:\Github\ICP-X\NewP\frontend\packages\ui\src\examples\UiKitchenSink.tsx`
- `D:\Github\ICP-X\NewP\docs\verification\frontend-ui-component-phase-a-verification.md`

---

### Task 1: 建立 `@icp-platform/ui` 包骨架

**Files:**
- Modify: `D:\Github\ICP-X\NewP\frontend\packages\ui\README.md`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\package.json`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\tsconfig.json`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\index.ts`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\styles\tokens.css`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\styles\base.css`

- [ ] **Step 1: 新建 UI 包项目文件**

目标：

- 让 `frontend/packages/ui` 从空目录说明变成真实 workspace 包。

关键结果：

- 可被 `pnpm --filter @icp-platform/ui build` 构建
- 有 `src/index.ts` 作为导出入口
- 有公共 token 与基础样式入口

- [ ] **Step 2: 升级 README**

README 至少要说明：

- 第一批范围
- 当前不包含的复杂控件
- 二次复用升级规则

- [ ] **Step 3: 验证骨架存在**

Run:

```powershell
Get-ChildItem .\frontend\packages\ui -Recurse
```

Expected:

- `package.json`、`tsconfig.json`、`src/index.ts`、`styles/tokens.css`、`styles/base.css` 都存在

---

### Task 2: 落地第一批基础控件与结构块

**Files:**
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\Button.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\Input.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\TextArea.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\StatusBadge.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\LoadingState.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\EmptyState.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\components\ErrorState.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\PageHeader.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\Panel.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\SummaryCard.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\HelperBlock.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\MetaBlock.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\TableWrap.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\patterns\FormInline.tsx`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\ui\src\examples\UiKitchenSink.tsx`

- [ ] **Step 1: 实现第一批基础控件**

最小范围：

- `Button`
- `Input`
- `TextArea`
- `StatusBadge`
- `LoadingState`
- `EmptyState`
- `ErrorState`

要求：

- API 保持小而稳定
- 不预埋复杂业务语义

- [ ] **Step 2: 实现第一批结构块**

最小范围：

- `PageHeader`
- `Panel`
- `SummaryCard`
- `HelperBlock`
- `MetaBlock`
- `TableWrap`
- `FormInline`

- [ ] **Step 3: 写极简示例页**

要求：

- 至少展示 `Button`、`Input`、`StatusBadge`
- 作为第一批公共控件的活示例

- [ ] **Step 4: 构建 UI 包**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/ui build
```

Expected:

- `dist/` 生成成功
- TypeScript 构建通过

---

### Task 3: 接入 `Shell` 与模块页面

**Files:**
- Modify: `D:\Github\ICP-X\NewP\frontend\packages\shell\package.json`
- Modify: `D:\Github\ICP-X\NewP\frontend\modules\config-center\package.json`
- Modify: `D:\Github\ICP-X\NewP\frontend\modules\authorization\package.json`
- Modify: `D:\Github\ICP-X\NewP\frontend\packages\shell\src\main.tsx`
- Modify: `D:\Github\ICP-X\NewP\frontend\packages\shell\src\index.css`
- Modify: `D:\Github\ICP-X\NewP\frontend\packages\shell\src\pages\Login.tsx`
- Modify: `D:\Github\ICP-X\NewP\frontend\packages\shell\src\pages\Home.tsx`
- Modify: `D:\Github\ICP-X\NewP\frontend\modules\config-center\src\pages\ConfigManagement.tsx`
- Modify: `D:\Github\ICP-X\NewP\frontend\modules\authorization\src\pages\RoleManagement.tsx`
- Modify: `D:\Github\ICP-X\NewP\frontend\modules\authorization\src\pages\PermissionList.tsx`

- [ ] **Step 1: 给消费方包增加依赖**

在以下包里加入：

- `@icp-platform/ui`

目标文件：

- `frontend/packages/shell/package.json`
- `frontend/modules/config-center/package.json`
- `frontend/modules/authorization/package.json`

- [ ] **Step 2: 让 Shell 引入 UI 包样式**

在 `frontend/packages/shell/src/main.tsx` 中引入 `@icp-platform/ui`。

- [ ] **Step 3: 收窄 `index.css` 职责**

把公共 token、基础控件样式、结构块样式迁移到 UI 包，仅保留：

- 宿主布局
- 顶部栏
- 侧边栏
- 登录品牌区特有样式

- [ ] **Step 4: 改造现有页面**

优先页面：

- `Login.tsx`
- `Home.tsx`
- `ConfigManagement.tsx`
- `RoleManagement.tsx`
- `PermissionList.tsx`

要求：

- 至少使用 `PageHeader`、`Panel`、`SummaryCard`、`StatusBadge`、`Button`、`Input` 等第一批公共控件
- 去掉对应的重复结构实现

- [ ] **Step 5: 构建前端 workspace**

Run:

```powershell
pnpm --dir .\frontend install --frozen-lockfile
pnpm --dir .\frontend build
```

Expected:

- 所有 workspace 包构建通过
- `@icp-platform/ui`、`@icp-platform/shell`、`@icp-platform/module-config-center`、`@icp-platform/module-authorization` 构建通过

---

### Task 4: 验证与文档收口

**Files:**
- Create: `D:\Github\ICP-X\NewP\docs\verification\frontend-ui-component-phase-a-verification.md`
- Modify: `D:\Github\ICP-X\NewP\frontend\packages\ui\README.md`
- Modify: `D:\Github\ICP-X\NewP\docs\current-status.md`

- [ ] **Step 1: 更新 UI README**

补充第一批已落地控件与结构块清单。

- [ ] **Step 2: 写验证记录**

记录：

- UI 包可独立构建
- Shell 可消费 UI 包
- `ConfigCenter` / `Authorization` 模块页面完成第一批替换

- [ ] **Step 3: 更新当前状态**

在 `docs/current-status.md` 中补充：

- 前端公共 UI 第一批基础控件与结构块已落地
- `frontend/packages/ui` 已成为真实可构建包
- Shell / ConfigCenter / Authorization 已完成第一批接入验证

- [ ] **Step 4: 运行最终核验**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend build
```

Expected:

- UI 包与前端 workspace 构建全部通过

---

## 自检

- 规格覆盖：计划覆盖了 UI 包初始化、第一批基础控件、结构块、页面替换、构建验证和文档收口。
- 占位项扫描：没有 `TODO`、`TBD` 或“后续自行补充”类占位。
- 类型一致性：第一批组件统一从 `@icp-platform/ui` 导出，结构块统一采用 `PageHeader`、`Panel`、`SummaryCard`、`HelperBlock`、`MetaBlock`、`TableWrap`、`FormInline` 命名。

## 执行交接

计划已保存到 `docs/superpowers/plans/2026-04-06-frontend-ui-component-strategy.md`。

Two execution options:

1. Subagent-Driven (recommended) - 我按任务边界逐个实施并在每一段后回归验证
2. Inline Execution - 在当前会话里连续实现这四个任务，再统一做构建与文档收口

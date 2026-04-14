# Phase 6 前端组件化模板第一版 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不引入端到端示例模块和共享 UI 组件库建设的前提下，落地一套“正式规范 + 仓库内模板骨架 + 最小验证链路”的前端组件化模板第一版。

**Architecture:** 本次实施以当前真实前端模式为基准，围绕 `frontend/modules/*` 的模块契约、`frontend/packages/shell/src/registerModules.ts` 的集中接入点，以及 `FrontendModule` 的声明式聚合机制展开。实施内容不改动宿主运行时行为，只补齐模板文档、workspace 模板包、复制说明和验证记录，让后续端到端示例模块与脚手架建立在已稳定模板之上。

**Tech Stack:** React 18、TypeScript、pnpm workspace、Vite、现有 `@icp-platform/sdk` / `@icp-platform/shell` / `@icp-platform/core`。

---

## 文件结构

### 需要修改的现有文件

- `frontend\package.json`
  为根 workspace 配置补充 `templates/*`，确保模板骨架能像普通包一样参与构建。
- `frontend\pnpm-workspace.yaml`
  与根 `package.json` 保持一致，把模板目录纳入 pnpm workspace。
- `frontend\docs\module-integration-guide.md`
  从“快速接入指南”升级为“前端模板规范 + 复制指南”，明确契约、目录、必改项、可删项和禁改项。
- `frontend\README.md`
  补充 `templates/` 目录说明，避免后续只看前端根 README 时遗漏模板位置。
- `docs\current-status.md`
  在模板实际落地后更新 Phase 6 状态和可用能力。

### 需要新增的文件

- `frontend\templates\frontend-module-template\README.md`
  模板骨架使用说明，明确复制方式、必改项、可删项、验证命令。
- `frontend\templates\frontend-module-template\package.json`
  模板包定义，使用最小依赖集合。
- `frontend\templates\frontend-module-template\tsconfig.json`
  模板包 TypeScript 构建配置。
- `frontend\templates\frontend-module-template\src\index.ts`
  模板模块入口，演示 `FrontendModule` 的标准声明方式。
- `frontend\templates\frontend-module-template\src\pages\TemplatePage.tsx`
  模板最小页面。
- `docs\verification\phase-6-frontend-component-template-verification.md`
  第六阶段前端模板第一版验证记录。

### 命名与边界约束

- 模板骨架目录固定为 `frontend/templates/frontend-module-template`
- 模板包名固定为 `@icp-templates/frontend-module-template`
- 模板入口文件固定为 `src/index.ts`
- 模板默认只依赖 `@icp-platform/sdk` 和 `react`
- 模板不直接引用 `shell/src/*`、`packages/ui` 或真实业务模块代码

---

### Task 1: 固化前端模板规范与 workspace 边界

**Files:**
- Modify: `frontend\package.json`
- Modify: `frontend\pnpm-workspace.yaml`
- Modify: `frontend\docs\module-integration-guide.md`
- Modify: `frontend\README.md`

- [ ] **Step 1: 更新前端根 package 的 workspace 配置**

把 `frontend/package.json` 中的 `workspaces` 从：

```json
[
  "packages/*",
  "modules/*"
]
```

改成：

```json
[
  "packages/*",
  "modules/*",
  "templates/*"
]
```

- [ ] **Step 2: 更新 pnpm workspace 范围**

把 `frontend/pnpm-workspace.yaml` 改成：

```yaml
packages:
  - 'packages/*'
  - 'modules/*'
  - 'templates/*'
```

- [ ] **Step 3: 把模块接入指南升级成正式模板规范**

在 `frontend/docs/module-integration-guide.md` 中重写为以下结构：

```markdown
# 前端模块模板规范

## 目标

- 让新增前端模块按 `FrontendModule` 契约接入宿主
- 固化目录结构、复制方式和验证命令
- 避免模块直接依赖宿主内部实现

## 标准目录

```text
frontend/templates/frontend-module-template/
├── src/
│   ├── index.ts
│   └── pages/
│       └── TemplatePage.tsx
├── package.json
├── tsconfig.json
└── README.md
```

## 必改项

- 包名
- `moduleId`
- `displayName`
- 路由前缀
- 权限编码
- 菜单 `id`

## 可选目录

- `src/components/`
- `src/services/`
- `src/assets/`

## 禁止事项

- 直接引用 `shell/src/*`
- 把权限、菜单、路由散写到页面文件
- 默认依赖 `packages/ui`

## 宿主接入点

- `frontend/packages/shell/src/registerModules.ts`
```

- [ ] **Step 4: 更新前端根 README 的目录说明**

在 `frontend/README.md` 的目录结构中加入模板目录说明：

```markdown
- `templates/`：仓库内前端模板骨架，供复制新模块时使用
```

并补一段说明：

```markdown
当前前端模板第一版聚焦模块契约与最小构建链路，不依赖 `packages/ui`。
```

- [ ] **Step 5: 检查规范文件已按预期更新**

Run:

```powershell
Get-Content .\frontend\package.json
Get-Content .\frontend\pnpm-workspace.yaml
Get-Content .\frontend\docs\module-integration-guide.md
Get-Content .\frontend\README.md
```

Expected:

- `templates/*` 已进入 workspace 配置
- 指南中已出现“必改项 / 可选目录 / 禁止事项 / 宿主接入点”
- README 中已出现 `templates/` 目录说明

- [ ] **Step 6: 提交规范与 workspace 边界**

```powershell
git add .\frontend\package.json .\frontend\pnpm-workspace.yaml .\frontend\docs\module-integration-guide.md .\frontend\README.md
git commit -m "docs: define frontend module template contract"
```

---

### Task 2: 创建仓库内前端模板骨架包

**Files:**
- Create: `frontend\templates\frontend-module-template\README.md`
- Create: `frontend\templates\frontend-module-template\package.json`
- Create: `frontend\templates\frontend-module-template\tsconfig.json`
- Create: `frontend\templates\frontend-module-template\src\index.ts`
- Create: `frontend\templates\frontend-module-template\src\pages\TemplatePage.tsx`

- [ ] **Step 1: 创建模板包 README**

写入：

```markdown
# 前端模块模板骨架

本目录用于提供仓库内可复制的前端模块模板骨架。

## 必改项

- `package.json` 中的包名
- `metadata.moduleId`
- `metadata.displayName`
- 路由路径
- 权限编码
- 菜单 `id`

## 可删项

- `permissions`
- `initialize`
- `cleanup`
- `src/components/`
- `src/services/`

## 禁止事项

- 不要直接依赖 `shell/src/*`
- 不要默认依赖 `packages/ui`
- 不要把所有逻辑都堆进 `src/index.ts`

## 验证命令

```bash
pnpm --filter @icp-templates/frontend-module-template build
```
```

- [ ] **Step 2: 创建模板包 package.json**

写入：

```json
{
  "name": "@icp-templates/frontend-module-template",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "main": "./dist/index.js",
  "types": "./dist/index.d.ts",
  "scripts": {
    "build": "tsc",
    "dev": "tsc --watch"
  },
  "dependencies": {
    "@icp-platform/sdk": "workspace:*",
    "react": "^18.3.1"
  },
  "devDependencies": {
    "@types/react": "^18.3.1",
    "typescript": "^5.4.2"
  }
}
```

- [ ] **Step 3: 创建模板包 tsconfig**

写入：

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "jsx": "react-jsx",
    "declaration": true,
    "outDir": "./dist",
    "rootDir": "./src",
    "strict": true,
    "moduleResolution": "bundler",
    "esModuleInterop": true,
    "skipLibCheck": true,
    "forceConsistentCasingInFileNames": true
  },
  "include": ["src"],
  "exclude": ["node_modules", "dist"]
}
```

- [ ] **Step 4: 创建模板页面**

写入 `src/pages/TemplatePage.tsx`：

```tsx
export default function TemplatePage() {
  return (
    <div style={{ padding: 24 }}>
      <h2>模板模块页面</h2>
      <p>这里用于演示前端模块模板的最小页面结构。</p>
    </div>
  );
}
```

- [ ] **Step 5: 创建模板模块入口**

写入 `src/index.ts`：

```ts
import { FrontendModule } from '@icp-platform/sdk';
import TemplatePage from './pages/TemplatePage';

export const templateModule: FrontendModule = {
  metadata: {
    moduleId: 'TemplateModule',
    displayName: '模板模块',
    version: '0.1.0',
    description: '前端组件化模板骨架'
  },
  routes: [
    {
      path: '/template-module',
      component: TemplatePage,
      requireAuth: true,
      permissions: ['TemplateModule.View']
    }
  ],
  menus: [
    {
      id: 'template-module',
      label: '模板模块',
      path: '/template-module',
      order: 100,
      permissions: ['TemplateModule.View']
    }
  ],
  permissions: [
    {
      code: 'TemplateModule.View',
      name: '查看模板模块',
      category: '模板模块'
    }
  ],
  initialize: async () => {
    console.log('模板模块初始化完成');
  },
  cleanup: async () => {
    console.log('模板模块清理完成');
  }
};

export default templateModule;
```

- [ ] **Step 6: 构建模板包并确认输出**

Run:

```powershell
cd .\frontend
pnpm --filter @icp-templates/frontend-module-template build
```

Expected:

- 模板包构建成功
- `frontend/templates/frontend-module-template/dist` 生成类型声明和入口产物

- [ ] **Step 7: 提交模板骨架包**

```powershell
git add .\frontend\templates\frontend-module-template
git commit -m "feat: add frontend module template skeleton"
```

---

### Task 3: 完成验证记录与项目状态收口

**Files:**
- Create: `docs\verification\phase-6-frontend-component-template-verification.md`
- Modify: `docs\current-status.md`

- [ ] **Step 1: 编写前端模板验证记录**

写入 `docs/verification/phase-6-frontend-component-template-verification.md`：

```markdown
# Phase 6 前端组件化模板第一版验证记录

## 验证命令

- `pnpm --filter @icp-templates/frontend-module-template build`
- `pnpm build`

## 预期

- 前端模板骨架目录存在
- 模板包可独立构建
- 前端工作区整体构建通过
- 模块接入规范与模板说明已落地

## 结果摘要

- 前端模块模板规范已升级
- 仓库内模板骨架已落地
- 模板包可通过 workspace 独立构建
- 整体前端工作区构建通过
```

- [ ] **Step 2: 运行最终验证命令**

Run:

```powershell
cd .\frontend
pnpm --filter @icp-templates/frontend-module-template build
pnpm build
```

Expected:

- 模板包构建通过
- 前端工作区整体构建通过

- [ ] **Step 3: 更新当前状态文档**

在 `docs/current-status.md` 的 `Phase 6` 段落补充：

```markdown
- 前端组件化模板设计与仓库内模板骨架已落地
- 前端模板验证记录已补齐
```

在“当前可用能力”中补充：

```markdown
- 前端模板骨架可复制并独立构建
```

并保持“端到端示例模块、可执行脚手架”仍为后续工作。

- [ ] **Step 4: 检查文档与验证结果一致**

Run:

```powershell
Get-Content .\docs\verification\phase-6-frontend-component-template-verification.md
Get-Content .\docs\current-status.md
```

Expected:

- 文档中已记录真实验证命令
- `Phase 6` 状态与已落地内容一致
- 没有把端到端示例模块或 `packages/ui` 误写成已完成

- [ ] **Step 5: 提交验证记录与状态收口**

```powershell
git add .\docs\verification\phase-6-frontend-component-template-verification.md .\docs\current-status.md
git commit -m "docs: record frontend component template closeout"
```

---

## 自检

- 规格覆盖：本计划覆盖了模板规范、workspace 范围、模板骨架包、验证记录和状态收口
- 占位项扫描：计划中没有 `TODO`、`TBD` 或“稍后补细节”类占位
- 类型一致性：模板包名统一为 `@icp-templates/frontend-module-template`，模板入口统一为 `templateModule`，权限编码统一为 `TemplateModule.View`

## 执行交接

计划已保存到 `docs/superpowers/plans/2026-04-06-phase-6-frontend-component-template.md`。

后续执行方式已确定：**继续使用 Codex 统筹 + Claude 实施**。实现时按任务边界逐个派发给 Claude，Codex 负责审查、fresh 验证和文档收口。

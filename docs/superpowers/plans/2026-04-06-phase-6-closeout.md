# Phase 6 Closeout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 补齐 Phase 6 的端到端示例模块与可执行脚手架，让仓库满足“可以端到端脚手架出一个示例模块”的阶段验收标准。

**Architecture:** 在现有前后端模板之上补齐最小联通能力，不新增额外基础设施层。通过一个真实前端示例模块承接 `ModuleTemplateSample` 后端接口，再用脚手架把相同结构自动化生成，并把宿主接入固定为显式更新 `HostApi.csproj` 与 `registerModules.ts`。

**Tech Stack:** .NET 10、ASP.NET Core、TypeScript、React、pnpm、PowerShell、Shell Script

---

### Task 1: 补齐模板与示例范围映射

**Files:**
- Modify: `server/templates/backend-module-template/README.md`
- Modify: `frontend/templates/frontend-module-template/README.md`
- Create: `server/templates/backend-module-template/TemplateModule.csproj`

- [ ] **Step 1: 补齐后端模板项目文件**

创建一个与 `ModuleTemplateSample` 对齐的模板项目文件，确保脚手架可以直接生成可构建的后端模块项目。

- [ ] **Step 2: 更新前后端模板说明**

把模板说明更新为“默认包含最小状态接口闭环”，明确前端模板会请求状态接口、后端模板提供状态接口。

- [ ] **Step 3: 构建校验模板相关变更**

Run: `dotnet build .\server\modules\ModuleTemplateSample\ModuleTemplateSample.csproj`
Expected: Build succeeded, 0 errors

### Task 2: 落地端到端前端示例模块

**Files:**
- Create: `frontend/modules/module-template-sample/package.json`
- Create: `frontend/modules/module-template-sample/tsconfig.json`
- Create: `frontend/modules/module-template-sample/src/index.ts`
- Create: `frontend/modules/module-template-sample/src/pages/ModuleTemplateSamplePage.tsx`
- Create: `frontend/modules/module-template-sample/src/services/moduleTemplateSampleService.ts`
- Modify: `frontend/packages/shell/src/registerModules.ts`

- [ ] **Step 1: 先写最小前端服务与页面**

前端页面需要请求 `/api/module-template-sample/status`，并渲染加载中、成功、失败三种状态。

- [ ] **Step 2: 注册前端示例模块**

在 `registerModules.ts` 中添加导入和注册项，保持现有显式注册模式。

- [ ] **Step 3: 验证前端示例模块构建**

Run: `pnpm --filter @icp-modules/module-template-sample build`
Expected: Build succeeded

### Task 3: 升级前端模板为最小联通模板

**Files:**
- Modify: `frontend/templates/frontend-module-template/src/index.ts`
- Modify: `frontend/templates/frontend-module-template/src/pages/TemplatePage.tsx`
- Create: `frontend/templates/frontend-module-template/src/services/templateStatusService.ts`

- [ ] **Step 1: 把模板页面改为接口驱动页面**

保留模板可编译特性，同时让默认页面能请求 `/api/template-module/status`。

- [ ] **Step 2: 保持模板目录可独立构建**

Run: `pnpm --filter @icp-templates/frontend-module-template build`
Expected: Build succeeded

### Task 4: 实现可执行脚手架

**Files:**
- Create: `scripts/new-module.ps1`
- Create: `scripts/new-module.sh`
- Modify: `frontend/docs/module-integration-guide.md`
- Modify: `frontend/README.md`

- [ ] **Step 1: 实现 PowerShell 脚手架**

支持输入模块名并自动复制模板、替换标识、更新 `HostApi.csproj` 与 `registerModules.ts`。

- [ ] **Step 2: 实现 Shell 脚手架**

提供与 PowerShell 基本等价的最小能力版本。

- [ ] **Step 3: 更新中文文档**

把脚手架使用方式写入前端接入指南与前端 README。

### Task 5: 执行脚手架 smoke 验证并收口文档

**Files:**
- Modify: `docs/verification/phase-6-frontend-component-template-verification.md`
- Modify: `docs/current-status.md`
- Create or Modify: `docs/verification/phase-6-closeout-verification.md`

- [ ] **Step 1: 执行 fresh 构建验证**

Run:
`dotnet build .\server\modules\ModuleTemplateSample\ModuleTemplateSample.csproj`
`dotnet build .\apps\host-api\HostApi.csproj`
`pnpm --filter @icp-templates/frontend-module-template build`
`pnpm --filter @icp-modules/module-template-sample build`
`pnpm build`

Expected:
- 全部命令 exit code 为 0

- [ ] **Step 2: 执行脚手架 smoke**

Run:
`.\scripts\new-module.ps1 -ModuleName Phase6ScaffoldSmoke -SkipIntegration`
`dotnet build .\server\modules\Phase6ScaffoldSmoke\Phase6ScaffoldSmoke.csproj`
`pnpm --filter @icp-modules/phase6-scaffold-smoke build`

Expected:
- 生成目录存在
- 生成后的后端项目可构建
- 生成后的前端模块可构建

- [ ] **Step 3: 用真实结果更新验证文档与状态文档**

把脚手架验证结果、是否验证 `.sh` 版本、Phase 6 完成状态同步到验证文档和 `current-status.md`。

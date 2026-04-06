# Componentization Guardrails Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 收紧组件化护栏、模板说明和受控接入机制，让后续新增模块默认更符合组件化与 AI-first 目标。

**Architecture:** 本轮不改现有业务模块实现，只改主文档、模块契约、前端接入指南、Shell 受控注册入口说明、前后端模板 README 与脚手架提示。前端仍保留集中注册方式，但明确 `registerModules.ts` 是唯一允许的 Shell 级模块接入清单。

**Tech Stack:** Markdown, PowerShell, Bash, TypeScript, existing frontend/backend module templates

---

### Task 1: 补齐组件化护栏文档与项目入口口径

**Files:**
- Create: `D:\Github\ICP-X\NewP\docs\architecture\componentization-guardrails.md`
- Modify: `D:\Github\ICP-X\NewP\AGENTS.md`
- Modify: `D:\Github\ICP-X\NewP\README.md`
- Modify: `D:\Github\ICP-X\NewP\docs\architecture\module-contract.md`

- [ ] **Step 1: 先写组件化护栏文档草案**

```markdown
# 组件化护栏

## 允许直接进入 Kernel / SDK / Contracts 的公共能力
- 模块元数据契约
- 菜单、路由、权限、配置声明契约
- 迁移/导入钩子契约
- 通用注册帮助器

## 可以留在模块内部的能力
- 模块私有 DTO
- 模块私有服务
- 模块私有页面与映射逻辑

## 禁止直接进入 Host / Shell 的内容
- 业务规则
- 模块私有状态
- 模块私有数据访问细节
- 临时迁移逻辑
```

- [ ] **Step 2: 把项目级组件化约束同步到主文档**

```markdown
## 组件化护栏

- 新增模块优先走 Contracts / SDK / 受控接入点。
- Host / Shell 不承载模块私有业务规则。
- 组件化护栏入口见 `docs/architecture/componentization-guardrails.md`。
```

- [ ] **Step 3: 在模块契约文档中加入受控接入点说明**

```markdown
## Guardrails

- Host composes, modules declare.
- `frontend/packages/shell/src/registerModules.ts` is the only shell-level module registration list.
- Modules must not modify shell internals directly for normal integration tasks.
```

- [ ] **Step 4: 用 `Select-String` 检查占位词**

Run:
```powershell
Select-String -Path .\docs\architecture\componentization-guardrails.md,.\AGENTS.md,.\README.md,.\docs\architecture\module-contract.md -Pattern 'TODO|TBD'
```

Expected: no output

### Task 2: 收紧前端受控接入说明

**Files:**
- Modify: `D:\Github\ICP-X\NewP\frontend\docs\module-integration-guide.md`
- Modify: `D:\Github\ICP-X\NewP\frontend\packages\shell\src\registerModules.ts`

- [ ] **Step 1: 先补“唯一受控接入点”和“禁改区”说明**

```markdown
## 受控接入点

- 允许修改：`frontend/packages/shell/src/registerModules.ts`
- 默认不允许修改：Shell 布局、Provider 结构、内部路由装配实现
- 如需超出以上范围，先补设计文档
```

- [ ] **Step 2: 在 `registerModules.ts` 顶部加入受控清单注释**

```ts
/**
 * Shell 级模块受控注册清单。
 * 新模块接入仅允许更新此文件，不应直接修改 Shell 其他内部结构。
 */
```

- [ ] **Step 3: 明确最小验证命令**

```markdown
## 最小验证

- `pnpm --filter @icp-modules/<module> build`
- `pnpm build`
```

- [ ] **Step 4: 读取文件检查口径是否一致**

Run:
```powershell
Get-Content .\frontend\docs\module-integration-guide.md
Get-Content .\frontend\packages\shell\src\registerModules.ts
```

Expected: both mention controlled integration and shell guardrails

### Task 3: 收紧模板 README 与脚手架提示

**Files:**
- Modify: `D:\Github\ICP-X\NewP\server\templates\backend-module-template\README.md`
- Modify: `D:\Github\ICP-X\NewP\frontend\templates\frontend-module-template\README.md`
- Modify: `D:\Github\ICP-X\NewP\scripts\new-module.ps1`
- Modify: `D:\Github\ICP-X\NewP\scripts\new-module.sh`

- [ ] **Step 1: 在前后端模板 README 中补边界、禁改区、最小验证路径**

```markdown
## 边界

- `*Module` / `src/index.ts` 只负责模块声明
- `Controllers` / `src/pages` 只负责接口与页面入口
- `Services` / `src/services` 只负责模块内部服务与请求

## 禁改区

- 不要直接侵入 Host / Shell 其他内部结构
```

- [ ] **Step 2: 在脚手架输出中补充受控接入提示**

```powershell
Write-Host 'Allowed shell integration file: frontend/packages/shell/src/registerModules.ts'
Write-Host 'Do not modify other shell internals unless a separate design approves it.'
```

```bash
echo "Allowed shell integration file: frontend/packages/shell/src/registerModules.ts"
echo "Do not modify other shell internals unless a separate design approves it."
```

- [ ] **Step 3: 保持脚手架只更新允许的接入点**

Run:
```powershell
Select-String -Path .\scripts\new-module.ps1 -Pattern 'registerModules.ts|HostApi.csproj'
```

Expected: only approved integration targets are referenced for auto-integration

- [ ] **Step 4: 用现有 README 文本检查模板口径**

Run:
```powershell
Get-Content .\server\templates\backend-module-template\README.md
Get-Content .\frontend\templates\frontend-module-template\README.md
```

Expected: both explain boundaries, guardrails, and verification

### Task 4: 机制级最小验证

**Files:**
- Verify only

- [ ] **Step 1: 检查新增护栏文档存在**

Run:
```powershell
Get-Item .\docs\architecture\componentization-guardrails.md
```

Expected: file exists

- [ ] **Step 2: 检查主文档与接入指南是否都引用护栏**

Run:
```powershell
Select-String -Path .\README.md,.\AGENTS.md,.\docs\architecture\module-contract.md,.\frontend\docs\module-integration-guide.md -Pattern '组件化护栏|受控接入|registerModules.ts'
```

Expected: matching lines found in multiple files

- [ ] **Step 3: 汇总结果**

Record:
```markdown
- 新增了统一组件化护栏文档
- Shell 受控接入点已明确
- 前后端模板 README 已补边界与禁改区
- 脚手架提示已补充，不再鼓励侵入 Shell 内部
```

# Post-Phase 6 Governance And AI-First Roadmap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Phase 6 之后的后续路线落成可执行工作包，优先补平台治理和 AI-first 约束，并保持最小迁移连续性。

**Architecture:** 本计划不直接扩大战线做新实现，而是先完成三类路线文档与状态收口，再补一份最小迁移准备设计。执行上按 `WP1 -> WP2 -> WP3 -> WP4` 串行推进，每个工作包单独收口，避免并发多个大主题。

**Tech Stack:** Markdown, PowerShell, ripgrep, existing docs under `docs/designs`, `docs/verification`, `docs/current-status.md`, `Plan.md`

---

### Task 1: 落地 WP1 插件治理增强设计

**Files:**
- Create: `f:/Github/NewP/NewP/docs/designs/plugin-governance-enhancement-design.md`
- Read: `f:/Github/NewP/NewP/docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md`
- Read: `f:/Github/NewP/NewP/docs/architecture/plugin-package-format.md`
- Read: `f:/Github/NewP/NewP/docs/architecture/plugin-runtime-loading.md`
- Read: `f:/Github/NewP/NewP/docs/architecture/plugin-compatibility-policy.md`
- Read: `f:/Github/NewP/NewP/docs/guides/unified-plugin-package-operations.md`
- Read: `f:/Github/NewP/NewP/docs/current-status.md`

- [ ] **Step 1: 盘点插件治理现状与缺口**

Run:

```powershell
Get-Content 'docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md' -TotalCount 260
```

```powershell
Get-Content 'docs/architecture/plugin-package-format.md' -TotalCount 220
```

```powershell
Get-Content 'docs/architecture/plugin-runtime-loading.md' -TotalCount 220
```

```powershell
Get-Content 'docs/architecture/plugin-compatibility-policy.md' -TotalCount 220
```

Expected: 能明确列出当前已具备的 `install / enable / disable / rollback` 能力，以及尚未收口的依赖治理、卸载语义、失败信号和兼容约束。

- [ ] **Step 2: 起草 WP1 设计文档**

Use this structure in `docs/designs/plugin-governance-enhancement-design.md`:

```md
# 插件治理增强设计

## 1. 背景
## 2. 目标
## 3. 非目标
## 4. 范围
## 5. 设计约束
## 6. 总体方案
## 7. 生命周期与状态模型
## 8. 依赖治理模型
## 9. 卸载、清理与回滚语义
## 10. 失败信号与验证
## 11. 风险与回滚思路
## 12. 分阶段实施建议
```
```

Required content:

```md
- Host 和 Shell 不新增模块私有接入特例
- 依赖冲突、版本不兼容、宿主不兼容必须有明确失败信号
- 卸载必须定义对安装目录、状态目录、运行时投影的影响
- 回滚必须说明边界：仅回滚插件状态，还是回滚安装内容与投影
- 外部插件开发仍通过 Contracts / SDK / templates / controlled entry points 进入
```

- [ ] **Step 3: 自检设计文档没有占位项**

Run:

```powershell
rg -n "TODO|TBD|待补|占位" docs/designs/plugin-governance-enhancement-design.md -S
```

Expected: 无输出。

- [ ] **Step 4: 记录计划内验证入口**

Append this section to the design doc:

```md
## 最小验证入口

- 现有 `install / enable / disable / rollback` 路径能够映射到新状态模型
- 至少一个依赖冲突案例能被文档化表达
- 至少一个卸载案例能被文档化表达
- 至少一个宿主兼容失败案例能被文档化表达
```

- [ ] **Step 5: Commit**

```bash
git add docs/designs/plugin-governance-enhancement-design.md
git commit -m "docs: define plugin governance enhancement design"
```

### Task 2: 落地 WP2 AI-First 执行标准设计

**Files:**
- Create: `f:/Github/NewP/NewP/docs/designs/ai-first-execution-standard-design.md`
- Read: `f:/Github/NewP/NewP/AGENTS.md`
- Read: `f:/Github/NewP/NewP/docs/architecture/componentization-guardrails.md`
- Read: `f:/Github/NewP/NewP/docs/architecture/ai-metadata-authoring-guide.md`
- Read: `f:/Github/NewP/NewP/docs/designs/detailed-design-template.md`
- Read: `f:/Github/NewP/NewP/docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md`

- [ ] **Step 1: 汇总当前 AI-first 约束来源**

Run:

```powershell
Get-Content 'AGENTS.md' -TotalCount 260
```

```powershell
Get-Content 'docs/architecture/componentization-guardrails.md' -TotalCount 220
```

```powershell
Get-Content 'docs/architecture/ai-metadata-authoring-guide.md' -TotalCount 220
```

Expected: 能整理出当前已有但分散的 AI-first 要求，包括小上下文、单一任务、入口清晰、可验证、可回退、受控接入点等。

- [ ] **Step 2: 起草 AI-first 执行标准设计**

Use this structure in `docs/designs/ai-first-execution-standard-design.md`:

```md
# AI-First 执行标准设计

## 1. 背景
## 2. 目标
## 3. 非目标
## 4. 适用范围
## 5. 核心判断标准
## 6. 文档标准
## 7. 模块与文件边界标准
## 8. 验证标准
## 9. 失败信号与回滚标准
## 10. 与现有 guardrails 的关系
## 11. 落地步骤
```
```

Required rules:

```md
- 新任务默认只聚焦一个模块或一种能力
- 新设计必须包含入口、边界、验证方式、失败信号、回滚思路
- 没有详细设计文档时，不进入正式实现
- 如果某项实现依赖口口相传才能继续，则视为不符合 AI-first
- 正常模块接入优先通过 Contracts / SDK / templates / controlled entry points
```

- [ ] **Step 3: 补齐设计模板要求**

Modify the plan section inside `docs/designs/ai-first-execution-standard-design.md` so it explicitly requires this checklist:

```md
每份新设计文档至少回答：

1. 入口在哪里
2. 边界在哪里
3. 如何验证
4. 失败信号是什么
5. 如何回滚
```

- [ ] **Step 4: 自检与现有 guardrails 不冲突**

Run:

```powershell
rg -n "Contracts|SDK|controlled entry points|入口|边界|回滚" docs/designs/ai-first-execution-standard-design.md docs/architecture/componentization-guardrails.md AGENTS.md -S
```

Expected: 新设计文档中的口径与现有约束一致，不引入新的冲突表述。

- [ ] **Step 5: Commit**

```bash
git add docs/designs/ai-first-execution-standard-design.md
git commit -m "docs: define ai-first execution standard"
```

### Task 3: 落地 WP3 状态与文档一致性收口设计

**Files:**
- Create: `f:/Github/NewP/NewP/docs/designs/post-phase-6-status-alignment-design.md`
- Modify: `f:/Github/NewP/NewP/docs/current-status.md`
- Modify: `f:/Github/NewP/NewP/Plan.md`
- Read: `f:/Github/NewP/NewP/docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md`
- Read: `f:/Github/NewP/NewP/docs/current-status.md`
- Read: `f:/Github/NewP/NewP/Plan.md`

- [ ] **Step 1: 盘点当前状态文档与计划文档的差异**

Run:

```powershell
Get-Content 'docs/current-status.md' -TotalCount 460
```

```powershell
Get-Content 'Plan.md' -TotalCount 260
```

Expected: 能识别哪些阶段名称、优先级、后续建议与新路线不一致。

- [ ] **Step 2: 起草状态收口设计文档**

Use this structure in `docs/designs/post-phase-6-status-alignment-design.md`:

```md
# Post-Phase 6 状态与计划对齐设计

## 1. 背景
## 2. 目标
## 3. 非目标
## 4. 范围
## 5. 对齐原则
## 6. current-status 收口方案
## 7. Plan 收口方案
## 8. design / implementation / verification 链路核对方案
## 9. 风险
## 10. 回滚思路
```
```

- [ ] **Step 3: 更新 `docs/current-status.md` 的下一步建议**

Replace the “当前推荐下一步”部分，使其表达以下顺序：

```md
1. 平台治理收口
2. AI-first 规范落地
3. 文档与状态一致性治理
4. 最小迁移连续性
```

Run:

```powershell
rg -n "当前推荐下一步|下一步" docs/current-status.md -S
```

Expected: 能定位到被更新的段落。

- [ ] **Step 4: 更新 `Plan.md` 的后续路线描述**

Add a section that mirrors the same 4-work-package order, with one short paragraph per work package.

Run:

```powershell
rg -n "治理|AI-first|迁移|插件" Plan.md -S
```

Expected: `Plan.md` 中能直接看到新的后续路线口径。

- [ ] **Step 5: 核对至少三项核心能力的文档链路**

Create a checklist in the design doc for:

```md
- Plugin platform
- Multi-db persistence
- Plugin operations center
```

For each item, confirm whether these documents exist:

```md
- design
- implementation or guide
- verification
- current status mention
```

- [ ] **Step 6: Commit**

```bash
git add docs/designs/post-phase-6-status-alignment-design.md docs/current-status.md Plan.md
git commit -m "docs: align post-phase-6 status and plan"
```

### Task 4: 落地 WP4 最小迁移连续性准备

**Files:**
- Create: `f:/Github/NewP/NewP/docs/designs/dictionary-enumeration-migration-preparation-design.md`
- Read: `f:/Github/NewP/NewP/docs/migration/metadata-migration-strategy.md`
- Read: `f:/Github/NewP/NewP/docs/migration/oldp-asset-disposition-v1.md`
- Read: `f:/Github/NewP/NewP/docs/migration/setting-import-poc.md`
- Read: `f:/Github/NewP/NewP/docs/verification/setting-import-poc-verification.md`

- [ ] **Step 1: 复盘现有迁移模板与约束**

Run:

```powershell
Get-Content 'docs/migration/metadata-migration-strategy.md' -TotalCount 260
```

```powershell
Get-Content 'docs/migration/setting-import-poc.md' -TotalCount 260
```

```powershell
Get-Content 'docs/verification/setting-import-poc-verification.md' -TotalCount 260
```

Expected: 能明确 Phase 5 已经验证的模板动作，以及下一波迁移仍需坚持的冻结方式。

- [ ] **Step 2: 起草最小迁移准备设计**

Use this structure in `docs/designs/dictionary-enumeration-migration-preparation-design.md`:

```md
# Dictionary / Enumeration 迁移准备设计

## 1. 背景
## 2. 目标
## 3. 非目标
## 4. 范围
## 5. 样本冻结原则
## 6. 映射规则草案
## 7. 执行路径草案
## 8. 验证草案
## 9. 正式实施准入条件
## 10. 风险与回滚思路
```
```

- [ ] **Step 3: 把正式实施前置条件写清楚**

Required conditions:

```md
- WP1 已收口
- WP2 已收口
- WP3 已收口
- 样本已冻结
- 映射规则已冻结
- 执行路径已冻结
- 验证路径已冻结
```

- [ ] **Step 4: 自检没有扩大迁移范围**

Run:

```powershell
rg -n "\\.page|\\.view|\\.generator|大规模迁移|全面实现" docs/designs/dictionary-enumeration-migration-preparation-design.md -S
```

Expected: 若出现这些内容，只能作为明确排除项，不能作为当前阶段实施目标。

- [ ] **Step 5: Commit**

```bash
git add docs/designs/dictionary-enumeration-migration-preparation-design.md
git commit -m "docs: prepare dictionary and enumeration migration"
```

### Task 5: 总体收口与路线确认

**Files:**
- Read: `f:/Github/NewP/NewP/docs/designs/plugin-governance-enhancement-design.md`
- Read: `f:/Github/NewP/NewP/docs/designs/ai-first-execution-standard-design.md`
- Read: `f:/Github/NewP/NewP/docs/designs/post-phase-6-status-alignment-design.md`
- Read: `f:/Github/NewP/NewP/docs/designs/dictionary-enumeration-migration-preparation-design.md`
- Read: `f:/Github/NewP/NewP/docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md`

- [ ] **Step 1: 交叉检查四份文档的顺序一致性**

Run:

```powershell
rg -n "WP1|WP2|WP3|WP4|平台治理|AI-first|状态|迁移" docs/designs/*.md -S
```

Expected: 四份文档与总路线文档使用同一顺序和同一术语。

- [ ] **Step 2: 交叉检查占位与歧义**

Run:

```powershell
rg -n "TODO|TBD|待补|后续再定|占位" docs/designs/plugin-governance-enhancement-design.md docs/designs/ai-first-execution-standard-design.md docs/designs/post-phase-6-status-alignment-design.md docs/designs/dictionary-enumeration-migration-preparation-design.md -S
```

Expected: 无输出。

- [ ] **Step 3: 形成路线交付清单**

Write this summary into your final review notes:

```md
- 插件治理增强设计已完成
- AI-first 执行标准设计已完成
- 状态与计划对齐设计已完成
- 最小迁移连续性准备设计已完成
```

- [ ] **Step 4: Commit**

```bash
git add docs/designs
git commit -m "docs: complete post-phase-6 governance roadmap package"
```

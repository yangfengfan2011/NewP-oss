# 当前协作模式说明

本文档用于说明 `NewP` 仓库在当前阶段的正式协作方式。

当前已不再以 `GitHub Copilot` 作为主执行线程，仓库默认按 **Codex-only** 模式推进；如后续需要参考历史双工具协作材料，应转到历史文档区查看。

## 1. 适用阶段

当前适用范围：

- 当前主线已切换到 post-Phase-6 的平台治理与 AI-first 收口
- 当前默认使用 `Codex` 作为主线程与主执行单元
- `WP1`、`WP2`、`WP3` 已完成并收口
- 当前主线已完成 `WP4 Stage 8` 准入复核，下一包建议进入 `Stage 9`

## 2. 当前结论

当前应以本文档作为正式协作入口，而不是继续沿用面向 `Copilot` 或历史阶段的说明文档。

当前最重要的工作纪律是：

- 主线程统一负责范围、计划、设计、实现、验证和收口
- 子代理或并行线程只在边界明确时作为辅助执行单元
- 不再按 “Codex + Copilot” 的双工具固定分工组织任务

## 3. 当前推荐工作方式

### 3.1 Codex 主线程

当前默认由 Codex 负责：

- 阅读仓库现状
- 收敛方案
- 维护计划和当前状态文档
- 更新设计文档
- 决定是否需要子代理或独立 worktree
- 运行验证并给出最终结论

### 3.2 辅助执行单元

如果任务天然可并行，仍可使用：

- 子代理
- worktree 中的独立线程
- 同一次会话中的只读探索线程

但这些执行单元只负责：

- 局部探索
- 局部验证
- 文件边界明确的小范围实现

关键判断仍由主线程负责，不外包架构结论、阶段结论和主线状态口径。

## 4. 文件规则

以下文件默认由主线程统一维护：

- `Plan.md`
- `docs/current-status.md`
- `AGENTS.md`
- `docs/designs/*`
- `docs/phases/post-phase-6-stage-map.md`

下列内容更适合交给辅助执行单元：

- 只读摸底笔记
- 验证记录草稿
- 样本核对结果
- 契约审查意见

## 5. 新增设计文档的默认入口

如果当前任务需要新增或更新设计文档，默认按下面的 AI-first 闭环执行：

1. 先阅读 [../architecture/ai-first-execution-standard.md](../architecture/ai-first-execution-standard.md) 和 [../designs/README.md](../designs/README.md)
2. 使用 `scripts/new-design-doc.ps1` / `scripts/new-design-doc.sh` 按模板创建设计文档，而不是手工复制旧文档
3. 在提交前至少运行一次 `scripts/validate-ai-first-design.ps1`
4. 如果当前设计已进入显式目标清单，或工作区存在新增 `docs/designs/*-design.md`，再运行一次 `scripts/check-ai-first-design-targets.ps1`

## 6. 当前有效入口

现阶段建议优先阅读：

1. [../../AGENTS.md](../../AGENTS.md)
2. [../../Plan.md](../../Plan.md)
3. [../current-status.md](../current-status.md)
4. [../architecture/ai-first-execution-standard.md](../architecture/ai-first-execution-standard.md)
5. [current-collaboration-mode.md](current-collaboration-mode.md)
6. [../phases/post-phase-6-stage-map.md](../phases/post-phase-6-stage-map.md)
7. 当前任务直接相关的设计、实现说明、验证记录和工作包文档

## 7. 历史文档位置

以下文档已转入历史区，仅保留参考价值，不再作为当前正式入口：

- [../history/collaboration/github-copilot-working-mode.md](../history/collaboration/github-copilot-working-mode.md)
- [../history/collaboration/claude-code-第二阶段执行手册.md](../history/collaboration/claude-code-第二阶段执行手册.md)

## 8. 当前推荐下一包

当前路线以 [../phases/post-phase-6-stage-map.md](../phases/post-phase-6-stage-map.md) 为准。

当前建议的下一包是：

- `Stage 9：Dictionary 静态资源转换与最小 i18n 基座`

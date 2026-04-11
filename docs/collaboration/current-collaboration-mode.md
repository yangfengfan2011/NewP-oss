# 当前协作模式说明

本文档用于说明 `NewP` 仓库在当前阶段如何使用 `Codex + Claude Code` 协同工作，以及后续切换到“仅使用 Codex”时如何保持流程不断档。

## 1. 适用阶段

当前适用范围：

- 当前主线已切换到 post-Phase-6 的平台治理与 AI-first 收口
- 当前仍使用 `Codex + Claude Code` 协同推进
- 后续仍可能切换为“仅使用 Codex”
- `WP1` 已完成并收口，`WP2` 已形成正式执行标准与设计创建闭环，后续主线将转入 `WP3 / WP4`

因此，本说明关注的不是“如何从零启动项目”，而是：

- 如何在现有仓库上稳定协作
- 如何减少多人/多工具并行时的冲突
- 如何保证后续切换工具模式时，文档、计划、AI-first 设计入口和交接方式仍然成立

## 2. 当前结论

当前仓库已有的协作文档可以支撑一部分工作，但不能单独完整支撑现阶段协作模式。

原因主要有三点：

- 现有 `claude-code-第二阶段执行手册.md` 明显偏“第二阶段”语境，已经不能完整覆盖当前 Phase 3 / Phase 4 状态。
- `docs/bootstrap/` 与 `docs/prompts/` 更偏历史启动材料和执行素材，不是当前双工具协作的正式操作说明。
- 现有文档没有明确写清：Codex 与 Claude Code 在当前阶段分别承担什么职责，以及切换到 Codex-only 时哪些规则保持不变。

因此，当前应以本文档作为“现阶段协作模式”的正式入口。

## 3. 当前推荐分工

### Codex

当前默认由 Codex 负责主线程工作，包括：

- 阅读仓库现状
- 收敛方案
- 维护计划和当前状态文档
- 补齐详细设计
- 决定是否亲自实现，还是调度其他执行线程
- 跑验证并给出最终结论

### Claude Code

当前推荐由 Claude Code 承担以下两类角色之一：

#### 角色 A：辅助线程

在未明确要求 Claude 直接实施代码时，Claude Code 更适合做：

- 只读探索
- 文档核对
- 范围检查
- 契约审查
- 辅助验证
- 长文说明整理

#### 角色 B：实施线程

如果用户明确要求“由 Claude Code 来实现代码”，那么 Claude Code 可以成为实际实施线程，负责：

- 阅读任务范围内文件
- 修改获准范围内的代码或文档
- 运行任务内要求的验证
- 汇报改动文件、验证结果和剩余问题

在这种模式下，Codex 应保持 lead / reviewer / coordinator 角色，不直接手改目标业务文件。

### 不推荐的做法

- 不要让 Codex 和 Claude Code 同时修改同一批核心文件。
- 不要把“架构判断”和“实际主实现”长期分散到两个活跃线程里。
- 不要在没有详细设计文档的情况下，让任一工具直接开始正式实现。

## 4. 当前推荐工作方式

### 模式 A：Codex 主线程，Claude Code 辅助

这是当前最推荐的模式。

做法：

1. 由 Codex 负责读取相关文档、收敛方案、判断边界。
2. 如需并行，由 Claude Code 只做探索、核对、审查、验证辅助。
3. 最终由 Codex 统一落改动、统一校验、统一收口。

适用场景：

- 需要改代码
- 需要改文档
- 需要同时维护计划、设计和实现
- 需要保证输出口径统一

### 模式 B：Codex 协调，Claude Code 实施

适用于用户明确要求 Claude Code 负责实现，或者需要 Codex 保持在规划与评审位的场景。

做法：

1. 由 Codex 写清单个边界明确的任务包。
2. 明确允许修改的文件范围、禁止触碰的范围、验收标准和验证命令。
3. Claude Code 在该范围内实施改动并运行任务内验证。
4. Codex 对结果进行复核，并再次做独立验证后再判断是否完成。

此模式下，Claude Code 不是辅助研究员，而是明确授权下的实施线程。

### 模式 C：Claude Code 做历史材料消化，Codex 负责正式落地

适用于：

- 旧文档很多
- 启动包、prompts、历史提纲较多
- 需要先把历史材料整理成当前可执行结论

此模式下，Claude Code 更像辅助研究员，而不是主实现线程。

## 5. 必须固定下来的交接材料

无论是 Codex 交给 Claude Code，还是 Claude Code 把结果交回 Codex，都必须至少带上这些信息：

- 当前任务目标
- 当前阶段归属
- 对应详细设计文档
- 影响范围目录
- 明确不做什么
- 推荐验证命令
- 当前已知风险
- 是否允许写文件，还是只读探索

如果这些信息不完整，就容易在协同过程中出现范围漂移。

## 6. 新增设计文档的默认入口

如果当前任务需要新增设计文档，默认按下面的 AI-first 闭环执行：

1. 先阅读 [../architecture/ai-first-execution-standard.md](../architecture/ai-first-execution-standard.md) 和 [../designs/README.md](../designs/README.md)
2. 使用 `scripts/new-design-doc.ps1` / `scripts/new-design-doc.sh` 按模板创建设计文档，而不是手工复制旧文档
3. 在交回主线程前，至少运行一次 `scripts/validate-ai-first-design.ps1`
4. 如果当前新增设计已进入显式目标清单，或工作区存在新增 `docs/designs/*-design.md`，再运行一次 `scripts/check-ai-first-design-targets.ps1`

这条路径的目标不是增加步骤，而是让“设计先行”真正具备统一入口、统一校验和统一失败信号。

## 7. 当前协同的核心不是“固定双人分工”

当前更准确的理解应是：

- 主线程统一负责范围、计划、任务拆分、评审和验收
- 执行单元根据任务选择
- 执行单元可以是 Claude Code，也可以是其他子代理、worktree 中的独立线程，或者 Codex 自己

因此，协同工作的核心不是“固定谁永远写代码、谁永远只读”，而是：

- 是否有明确边界
- 是否有清晰 acceptance criteria
- 是否有文件范围约束
- 是否有验证闭环
- 是否有主线程最终复核

## 8. 双工具协作时的文件规则

- `Plan.md`
- `docs/current-status.md`
- `AGENTS.md`
- `CLAUDE.md`
- `docs/designs/*`

以上文件默认应由主线程统一维护，不建议两个工具并行编辑。

下列内容更适合作为辅助线程产出：

- 摸底笔记
- 验证记录草稿
- 历史文档比对结果
- 契约审查意见

## 9. 与现有文档的关系

当前建议这样使用现有协作文档：

- [../../AGENTS.md](../../AGENTS.md)
  - 作为全局规则文档
- [../../CLAUDE.md](../../CLAUDE.md)
  - 作为 Claude Code 的项目级工作约束
- [../architecture/ai-first-execution-standard.md](../architecture/ai-first-execution-standard.md)
  - 作为新增设计、校验与阶段验收的 AI-first 正式执行入口
- [../guides/subagents-guide.md](../guides/subagents-guide.md)
  - 作为并行协作与子代理调度的基本规则
- [../guides/worktree-guide.md](../guides/worktree-guide.md)
  - 作为任务隔离和并行开发指南
- [claude-code-第二阶段执行手册.md](claude-code-第二阶段执行手册.md)
  - 作为历史阶段手册保留参考，不再视为当前全局主手册
- [../bootstrap/README_使用说明.md](../bootstrap/README_使用说明.md)
  - 作为历史启动包说明
- [../prompts/](../prompts/)
  - 作为执行素材库，而不是当前协作总规范

## 10. 切换到仅使用 Codex 时如何处理

如果一周后切换到仅使用 Codex，当前文档体系不需要推翻，只需要调整理解方式：

- 本文档继续保留，作为“从双工具到单工具”的过渡说明。
- `Codex 主线程` 的规则保持不变。
- 原本给 Claude Code 的“辅助线程职责”改由 Codex 的子代理、worktree 或单线程额外轮次承担。
- 详细设计先行、计划/状态同步更新、验证收口这些规则全部保持不变。

也就是说，后续变化的是“工具数量”，不是“工作纪律”。

## 11. 现阶段是否满足工作需要

结论：**部分满足，但补完本文档后，才算基本满足。**

当前已经具备的部分：

- 有全局规则
- 有项目状态
- 有计划
- 有 subagents / worktree 指南
- 有历史启动包与 prompt 素材

此前缺少的部分：

- 现阶段正式协作模式说明
- Codex / Claude Code 当前职责边界说明
- 切换到 Codex-only 时的连续性说明

本文档就是用来补这三个缺口的。

## 12. 当前推荐入口

现阶段协作时，建议优先阅读：

1. [../../AGENTS.md](../../AGENTS.md)
2. [../../Plan.md](../../Plan.md)
3. [../current-status.md](../current-status.md)
4. [../architecture/ai-first-execution-standard.md](../architecture/ai-first-execution-standard.md)
5. [current-collaboration-mode.md](current-collaboration-mode.md)
6. [../designs/README.md](../designs/README.md)
7. 与当前任务直接相关的模块边界、详细设计、实现说明和验证文档

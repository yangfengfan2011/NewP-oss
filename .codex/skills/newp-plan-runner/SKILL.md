---
name: newp-plan-runner
description: Use when executing an existing NewP implementation plan in docs/superpowers/plans, especially for unattended automation that must run only the first unfinished Task, use a plan lock, persist checkpoints, stop on BLOCKED state, protect dirty worktrees, run task-local verification, use read-only two-stage subagent review before commit, and avoid branch switching or worktree creation.
---

# NewP Plan Runner

## 目的

每轮只执行一个既有 NewP 实施计划 Task。该 skill 面向长时间无人值守 automation，重点处理会话上下文被压缩或丢失后的恢复问题。

核心规则：计划文档是长期真源；`.codex-temp/plan-runs/*.json` 只是恢复检查点。

默认 automation 调度频率为每 20 分钟执行一次。低于 10 分钟的频率默认不建议使用；短任务或高优先级计划可以显式配置为每 10 分钟触发一次。如果用户或外部调度器显式覆盖频率，仍必须保留锁、检查点和 `BLOCKED` 启动拦截规则。

## 必要输入

- 计划路径，通常是 `docs/superpowers/plans/<name>.md`。
- 可选锁路径；未提供时，从计划文件名推导 `.codex/automation-locks/<plan-id>.lock`。

## 强边界

- 不创建 git worktree。
- 不切换分支。
- 无人值守执行时不使用交互式 MCP 工具、浏览器、Figma、GitHub connector 或 node_repl。
- 不自动删除 stale lock。
- 不执行超过第一个未完成任务 的范围。
- 检查点状态为 `BLOCKED` 时不得继续。
- 成功提交前不得跳过只读两阶段 subagent review。
- `git add` 不得包含锁文件或 `.codex-temp/plan-runs/*.json`。

## 启动检查

1. 读取 `AGENTS.md`。
2. 读取 `docs/superpowers/README.md`。
3. 读取目标计划文件。
4. 读取计划引用的相关设计文档。
5. 确认当前 automation 的调度频率；默认应为每 20 分钟执行一次。
6. 从计划文件名解析 `planId`。
7. 解析状态路径 `.codex-temp/plan-runs/<planId>.json`。
8. 解析锁路径 `.codex/automation-locks/<planId>.lock`；如果用户提供了具体锁路径，则使用用户提供的路径。

## 启动门禁

1. 如果状态文件存在且 `status` 为 `BLOCKED`，立即停止：
   - 报告 `计划存在阻塞检查点，需要人工处理或暂停 automation`。
   - 不扫描或执行未完成任务。
2. 如果锁文件存在且更新时间距离现在小于 1 小时，立即停止：
   - 报告 `已有任务运行中`。
3. 如果锁文件存在且更新时间距离现在大于等于 1 小时，立即停止：
   - 报告 `疑似 stale lock，需要人工确认`。
   - 不删除锁文件。
4. 运行 `git status --short`。
5. 记录已有未提交改动，并在初始化检查点后写入相关线索。
6. 识别计划中的第一个未完成任务。
7. 如果没有未完成任务：
   - 写入 `PLAN_COMPLETED` 检查点。
   - 不修改代码。
   - 如果可以暂停 automation，则暂停；否则报告 `计划已完成，请人工暂停 automation`。

## 锁与检查点

编辑文件前必须先创建锁。锁内容至少包含：

- 当前时间。
- 计划路径。
- 目标任务 标题。
- 当前 run id 或进程 id，如果可用。

初始化检查点时写入：

- `status: RUNNING`
- `planPath`
- `lockPath`
- `targetTaskTitle`
- `targetTaskIndex`
- `targetTaskHeadingHash`
- `startedAt`
- `updatedAt`
- `verificationFailureCount: 0`
- `reviewFailureCount: 0`

每完成一个步骤，都运行状态脚本更新检查点，并刷新锁文件更新时间。

## 执行循环

仅针对目标任务：

1. 按顺序执行 checkbox 步骤。
2. 每完成一个步骤 后更新检查点。
3. 运行计划中该 Task 要求的验证命令。
4. 如果验证失败：
   - 递增 `verificationFailureCount`。
   - 记录 `lastFailedCommand`。
   - 如果仍在 Task 范围内，只做一次聚焦修复。
   - 重新运行验证。
   - 两轮修复后仍失败则以 `BLOCKED` 停止。
5. 验证通过后，启动只读 spec compliance review subagent。
6. 如果无法启动 spec review，以 `BLOCKED` 停止。
7. 如果 spec review 发现问题，递增 `reviewFailureCount`，由主 agent 修复、重新验证并重新 review。
8. 启动只读 code quality review subagent。
9. 如果无法启动 quality review，以 `BLOCKED` 停止。
10. 如果 quality review 发现问题，递增 `reviewFailureCount`，由主 agent 修复、重新验证并重新 review。
11. 只勾选目标任务 和实际完成步骤 的 checkbox。
12. 自检最终 diff，包括计划 checkbox 改动。
13. 使用简体中文提交信息创建 commit。
14. 将检查点标记为 `COMPLETED_TASK`。
15. 只删除本轮创建的锁。

## 只读 Review Prompt 形状

两次 review 都使用以下形状：

```text
你是只读 reviewer。不得编辑文件、stage 文件、commit、切换分支、创建 worktree 或运行破坏性命令。

工作区：<repo root>
计划：<plan path>
设计：<design path>
目标任务：<task title>
Review 类型：spec compliance | code quality

只读范围：
- AGENTS.md
- 目标计划
- 引用的设计文档
- git diff
- 相关源码和测试文件
- 主 agent 提供的验证输出

返回：
- STATUS: APPROVED | CHANGES_REQUESTED | BLOCKED
- Findings
- 涉及文件/行号，如果适用
- 验证风险
```

## 阻塞处理

以下情况进入 `BLOCKED`：

- 既有检查点已经是 `BLOCKED`。
- 当前任务 与代码现状冲突。
- 两轮聚焦修复后验证仍失败。
- 修复需要范围外架构决策。
- 无法启动只读 review subagent。
- review finding 无法在当前任务 范围内修复。
- 当前未提交改动会被覆盖。
- 计划指令不清楚。

阻塞时：

1. 写入检查点 `status: BLOCKED`。
2. 记录 `blockerReason`、`lastFailedCommand`、相关文件和 `nextAction`。
3. 不勾选当前任务。
4. 不执行后续任务。
5. 不创建成功 commit。
6. 只删除本轮创建的锁。
7. 报告 automation 应暂停或等待人工处理。

## 完成输出

使用 `AGENTS.md` 要求的输出形状：

- 改了什么
- 影响了哪些文件或模块
- 验证结果
- 已知风险
- 回滚思路

# NewP 计划自动执行 Skill 详细设计

## 1. 背景

当前仓库已经形成“设计文档 -> 实施计划 -> 验证记录 -> 状态收口”的 AI-first 工作方式。长计划通常放在 `docs/superpowers/plans/`，并用 checkbox 记录 Task / Step 进度。实际执行时，如果计划很长、会话多次压缩，任务状态容易只留在聊天上下文里，导致后续 Codex 难以判断已经完成到哪里、哪一步验证失败、哪些文件属于本轮改动。

已有自动化任务已经验证了一个可行方向：以计划文件为入口，配合计划级互斥锁，每轮只执行第一个未完成 Task，完成后提交；但也暴露了关键失败模式：当某个 Task 阻塞后，automation 仍可能继续循环执行，既浪费运行次数，也可能反复触碰同一个失败点。

本设计的目的，是把该经验固化为项目级 Codex skill：让 Codex 在已有计划的情况下，可以无人值守地推进计划任务，同时把状态、阻塞和验证证据落到仓库内可读文件中，避免依赖易丢失的会话上下文。

## 2. 目标

- 新增项目级 skill `newp-plan-runner`，用于执行已有实施计划中的第一个未完成 Task。
- 通过计划级锁、检查点状态文件和阻塞停止规则，支持长任务在会话压缩或恢复后继续判断状态。
- 明确自动化只推进一个 Task，不自动跨 Task 扩范围。
- 明确默认调度频率，避免无人值守 automation 因触发过密或触发口径不一致反复打到同一阻塞点。
- 明确阻塞后必须停止本轮、记录原因、释放本轮锁，并禁止继续循环推进。
- 保持与 `AGENTS.md`、`docs/superpowers/README.md`、现有计划文档和验证矩阵一致。

## 3. 非目标

- 不自动生成新设计或新实施计划；本 skill 只消费已经存在并通过 review 的计划。
- 不替代计划作者对架构边界、实现范围和验证命令的判断。
- 不自动切换分支，不创建 git worktree，不清理用户或其他 agent 的未提交改动。
- 不在阻塞后自动删除 stale lock，不自动跳过失败 Task。
- 不引入需要交互授权的 MCP 工具作为无人值守执行依赖。
- 不把多份计划合并成一次执行。

## 4. 范围

涉及文件与目录：

- `.codex/skills/newp-plan-runner/SKILL.md`
- `.codex/skills/newp-plan-runner/agents/openai.yaml`
- `.codex/skills/newp-plan-runner/scripts/plan-run-state.ps1`
- `.codex/automation-locks/*.lock`
- `.codex-temp/plan-runs/*.json`
- `docs/superpowers/plans/**/*.md`
- `docs/superpowers/specs/**/*.md`
- `docs/verification/**/*.md`

当前设计只定义 skill 与其本地脚本，不改造平台后端、前端 Shell、模块契约、SDK 或 CI。

## 5. 设计约束

- 计划文件仍是任务真源，检查点文件只记录执行状态和恢复线索，不承载长期计划 authoring。
- 每轮运行必须先读 `AGENTS.md` 和目标计划文件，再判断允许范围。
- 每轮运行在检查锁之前，必须先读取同一计划的检查点文件；如果状态为 `BLOCKED`，直接退出并报告需要人工处理，不得重新识别未完成 Task。
- 每轮只能选择第一个未完成 Task；如果该 Task 阻塞，不能扫描或执行后续 Task。
- 锁文件只用于互斥，不参与 git add / commit。
- 运行中每次更新检查点时，必须同步刷新本轮锁文件更新时间，避免长 Task 被后续 automation 误判为 stale lock。
- 检查点文件默认不提交，避免把一次无人值守运行的临时状态混入长期文档；计划勾选、代码、测试、文档和验证记录可以提交。
- 如果 git 工作区已有未提交改动，必须识别并保护，不得回退、覆盖或清理非本轮产生的改动。
- Task 完成后，提交前必须做两阶段只读 review：先做 spec compliance review，再做 code quality review。
- review agent 只能读取计划、设计、git diff、相关源码和测试输出，不得编辑、stage、commit、切换分支或创建 worktree。
- 如果当前环境无法启动只读 review subagent，本轮必须进入 `BLOCKED`，不得跳过 review 后提交。
- 第一版不采用完整 `superpowers:subagent-driven-development` 执行模式；只采用其中“两阶段 review”的质量门禁。原因是完整模式会派 implementer subagent 修改文件，且其集成规则要求先建立隔离 worktree，这与本自动化“当前分支、当前工作区、不创建 worktree、review subagents 只读”的运行约束冲突。

## 6. 总体方案

采用“检查点驱动的单 Task 自动执行”方案。

`newp-plan-runner` skill 负责指导 Codex 执行以下固定流程：读取计划、获取锁、识别第一个未完成 Task、创建检查点、执行 Step、运行验证、做两阶段 review、提交、释放锁。辅助脚本 `plan-run-state.ps1` 负责创建和更新 JSON 状态，降低 Codex 每轮手写状态文件的出错概率。

本方案可以理解为“主 agent 执行 + 只读 subagent review”的受控模式，而不是完整 subagent-driven implementation 模式。主 agent 是唯一允许编辑、验证、勾选计划和提交的人；subagent 只在提交前读取上下文并返回 review 结论。这样可以保留 subagent-driven 的规格符合性审查和代码质量审查收益，同时避免多个执行者在同一工作区里写文件，导致检查点、锁和 git diff 归属变得不可靠。

选择该方案的原因：

- 比纯说明型 skill 更适合长任务，因为关键状态写入文件，压缩后仍可恢复。
- 比多 agent 全量并行更稳，因为本仓库计划通常会修改共享代码和计划文件，串行单 Task 能减少冲突。
- 与已有自动化经验一致，可直接吸收 lock、Task 粒度、简体中文 commit 和只读 review 规则。

不选择全自动多 Task 连续执行，是因为一旦某个 Task 的验证失败或范围判断错误，后续 Task 很可能建立在不可靠状态上。每轮只推进一个 Task 能让失败边界更清楚，也更符合当前仓库“单目标、可验证、可回滚”的要求。

## 7. 关键模型

### 7.1 PlanRunState

检查点文件位于 `.codex-temp/plan-runs/<plan-id>.json`。

字段：

- `planPath`：仓库相对计划文件路径。
- `lockPath`：仓库相对锁文件路径。
- `status`：`RUNNING`、`COMPLETED_TASK`、`BLOCKED`、`PLAN_COMPLETED`。
- `targetTaskTitle`：本轮目标 Task 标题。
- `targetTaskIndex`：本轮目标 Task 在计划中的顺序。
- `targetTaskHeadingHash`：目标 Task 标题与首段内容的稳定摘要，用于计划被编辑后辅助确认 Task 身份。
- `startedAt`：本轮开始时间。
- `updatedAt`：最近一次检查点更新时间。
- `completedSteps`：本轮已完成 Step 标题列表。
- `changedFiles`：本轮确认产生或修改的文件列表。
- `verificationCommands`：已运行验证命令及结果摘要。
- `verificationFailureCount`：本轮验证失败并进入修复循环的次数，跨会话恢复时继续沿用。
- `reviewFailureCount`：本轮 review 未通过并进入修复循环的次数，跨会话恢复时继续沿用。
- `lastFailedCommand`：最近一次失败的验证或检查命令。
- `reviewResults`：两阶段 review 结论。
- `blockerReason`：阻塞时的原因。
- `nextAction`：恢复时应执行的下一步。

### 7.2 LockFile

锁文件位于 `.codex/automation-locks/<plan-id>.lock`。

内容使用可读文本或 JSON，至少包含：

- 当前时间。
- 计划文件路径。
- 本轮目标 Task。
- 当前进程或运行标识。

锁文件只表示互斥状态，不作为任务完成依据。

### 7.3 Task Completion Marker

Task 完成仍以计划文档中的 checkbox 为准。只有当代码/文档改动、验证、review 和 commit 都完成后，才能勾选 Task 和相关 Step。

### 7.4 Automation Schedule

默认调度频率为每 20 分钟执行一次。

原因：

- 明显短于 1 小时 stale lock 阈值：正常长任务运行中，后续触发会因新鲜锁直接退出；异常退出超过 1 小时后才进入人工确认路径。
- 避免频率过高时反复创建无人值守运行，尤其是在 `BLOCKED` 检查点尚未人工处理前。
- 每轮只执行一个 Task，20 分钟节奏能兼顾及时推进和避免空跑过多。

如果使用 heartbeat、外部调度器或临时手工触发，可以覆盖频率；但必须保留同一组锁、检查点和 `BLOCKED` 启动拦截规则。默认不建议低于 10 分钟触发一次；短任务或高优先级计划可以显式配置为每 10 分钟触发一次。

## 8. 核心流程

### 8.1 启动流程

1. 读取 `AGENTS.md`、`docs/superpowers/README.md` 和目标计划文件。
2. 读取同一计划的检查点文件。
3. 如果检查点状态为 `BLOCKED`，直接退出并报告“计划存在阻塞检查点，需要人工处理或暂停 automation”。
4. 检查计划级锁文件。
5. 如果锁文件存在且更新时间小于 1 小时，直接退出并报告“已有任务运行中”。
6. 如果锁文件存在且更新时间大于等于 1 小时，直接退出并报告“疑似 stale lock，需要人工确认”。
7. 如果锁文件不存在，识别第一个未完成 Task，创建锁文件并写入本轮目标。
8. 检查 `git status --short`，记录已有未提交改动。
9. 创建或更新检查点文件，状态设为 `RUNNING`。

### 8.2 执行流程

1. 只执行本轮目标 Task。
2. 按 Task 下 checkbox Step 顺序推进。
3. 每完成一个 Step，更新检查点文件，并同步刷新锁文件更新时间。
4. 如需编辑文件，遵守计划允许范围和 AGENTS.md 模块边界。
5. 运行 Task 要求的验证命令。
6. 如果验证失败，更新 `verificationFailureCount` 和 `lastFailedCommand`，最多进行 2 轮定向修复和重新验证。
7. 验证通过后，运行只读 spec compliance review。
8. spec review 通过后，运行只读 code quality review。
9. 如果无法启动只读 review subagent，立即进入阻塞流程。
10. review 如发现问题，更新 `reviewFailureCount`，由主 agent 修复、重新验证并重新 review。
11. review 通过后，勾选计划中完成的 checkbox。
12. 对包含计划 checkbox 的最终 diff 做一次轻量自检，确认只勾选本轮目标 Task 和实际完成 Step。
13. 创建一条简体中文 commit。
14. 更新检查点状态为 `COMPLETED_TASK`。
15. 删除本轮创建的锁文件。

### 8.3 阻塞流程

遇到以下任一情况，必须停止本轮：

- 当前 Task 与代码现状冲突。
- 验证连续修复 2 次仍失败。
- 需要范围外架构决策。
- review 指出的问题无法在当前 Task 范围内修复。
- 当前环境无法启动只读 review subagent。
- git 工作区存在会被本轮改动覆盖的非本轮变更。
- 计划指令不清楚，无法可靠判断允许范围或验证标准。

阻塞处理步骤：

1. 将检查点状态写为 `BLOCKED`。
2. 写入 `blockerReason`、失败命令、相关文件和建议人工处理点。
3. 不勾选当前 Task。
4. 不执行后续 Task。
5. 不创建成功 commit；如已产生可保留但未提交的诊断改动，必须在最终报告中列出。
6. 删除本轮创建的锁文件。
7. 如果该 skill 由 automation 调用，报告 automation 应暂停或等待人工处理，禁止继续循环。

### 8.4 计划完成流程

如果计划中已经没有未完成 Task：

1. 不执行代码改动。
2. 将检查点状态写为 `PLAN_COMPLETED`。
3. 如果当前运行环境允许更新 automation 状态，则将 automation 更新为 `PAUSED`。
4. 如果无法自行暂停 automation，则报告“计划已完成，请人工暂停 automation”。
5. 按 `AGENTS.md` 输出改动、影响文件、验证结果、已知风险和回滚思路。

## 9. 接口与集成点

### 9.1 Skill 入口

用户触发示例：

```text
使用 newp-plan-runner 执行 docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md 的第一个未完成 Task。
```

skill 必须支持以下输入：

- 计划文件路径。
- 可选锁文件路径；未提供时按计划文件名推导。
- 可选最终验证条件；未提供时以计划文档内容为准。

### 9.2 脚本入口

`plan-run-state.ps1` 提供最小命令：

```powershell
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 init -PlanPath <plan>
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 update -PlanPath <plan>
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 block -PlanPath <plan> -Reason <reason>
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 complete-task -PlanPath <plan>
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 plan-completed -PlanPath <plan>
```

脚本只管理检查点 JSON，不解析或修改计划 checkbox，避免脚本误改长期计划。

### 9.3 Automation 集成

如果本 skill 被 Codex automation 调用，prompt 必须包含：

- 计划路径。
- 调度频率：默认每 20 分钟执行一次；如覆盖频率，必须在 prompt 中显式说明。
- 每轮只执行第一个未完成 Task。
- 阻塞后停止并请求暂停 automation。
- 不使用需要交互授权的工具。

## 10. 数据与状态

长期状态：

- 计划文档 checkbox。
- 相关代码、测试和文档 diff。
- 验证记录。
- git commit。

短期状态：

- `.codex/automation-locks/*.lock`
- `.codex-temp/plan-runs/*.json`

一致性规则：

- 检查点显示 `COMPLETED_TASK` 但计划 checkbox 未勾选时，以计划文档为准，需要人工复核。
- 计划 checkbox 已勾选但无 commit 或验证证据时，不应继续自动执行后续 Task，应记录阻塞。
- 锁文件存在时不以检查点覆盖锁判断。
- 检查点状态为 `BLOCKED` 时，即使没有锁文件，也不允许继续自动执行；必须先由人工处理阻塞或重置检查点。

## 11. 错误处理与风险

主要风险：

- 阻塞后 automation 继续循环执行。
- stale lock 被误删，导致两个运行并发修改同一计划。
- 长 Task 未刷新锁，导致后续运行误判为 stale lock。
- Codex 把非本轮改动一起提交。
- review agent 越权编辑文件。
- review subagent 不可用时被主 agent 静默跳过。
- 验证命令耗时过长或依赖环境不稳定。

控制方式：

- `BLOCKED` 是终止态；本轮进入后只能报告和退出，不能继续执行。
- 启动时先检查 `BLOCKED` 检查点，再检查锁；阻塞状态优先于未完成 Task 扫描。
- stale lock 永远不自动删除。
- 每次 checkpoint 更新同步 touch 本轮锁文件。
- 每次 git add 前必须用 `git diff --name-only` 和初始 `git status` 比对本轮文件。
- review agent prompt 明确只读，主 agent 复核 git diff 后再继续。
- review subagent 不可用时必须阻塞，不能降级为无 review 提交。
- 验证失败最多修复 2 轮，之后必须阻塞。

## 12. 验证方案

设计落地后需要验证：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\tooling\2026-05-05-newp-plan-runner-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

skill 实现后需要补充：

- 对 `plan-run-state.ps1 init/update/block` 的脚本级 smoke。
- 使用一份临时计划样本验证：无锁启动、已有新鲜锁退出、stale lock 退出、启动时遇到 `BLOCKED` 检查点直接退出、阻塞写入 `BLOCKED`、计划完成写入 `PLAN_COMPLETED`。
- 使用长任务样本验证：每次 checkpoint 更新都会刷新锁文件更新时间。
- 使用失败样本验证：`verificationFailureCount`、`reviewFailureCount` 和 `lastFailedCommand` 会写入检查点，并在恢复后继续参与停止判断。
- 使用只读 review prompt dry run，确认 subagent 指令不允许编辑、stage、commit、切换分支或创建 worktree。

## 13. 未决问题

当前无影响第一版设计落地的未决问题。

可在实现计划中继续细化：

- automation 暂停能力在不同运行环境中的可用性检测方式。
- 是否为常用计划提供固定 lock 命名映射。
- 检查点 JSON 是否需要保留最近 N 次历史运行摘要。

## 14. 实施拆分建议

1. 新增 `.codex/skills/newp-plan-runner/SKILL.md` 和 `agents/openai.yaml`，先固化执行流程、停止条件和 review 约束。
2. 新增 `plan-run-state.ps1`，只负责检查点 JSON 的初始化、更新和阻塞记录。
3. 用临时样本计划做脚本 smoke，验证状态文件和锁判断。
4. 用一份真实已完成计划做只读演练，不改代码，只确认 Task 识别、锁策略和完成态判断。
5. 形成验证记录，说明无人值守执行的入口、阻塞信号和人工恢复方式。

## 15. AI-First 最小检查清单

- 入口在哪里：`.codex/skills/newp-plan-runner/SKILL.md` 是 skill 入口；`.codex/skills/newp-plan-runner/scripts/plan-run-state.ps1` 是检查点脚本入口；计划文件默认来自 `docs/superpowers/plans/`。
- 边界在哪里：只执行已有计划的第一个未完成 Task；只允许修改该 Task 范围内的代码、测试、文档和计划 checkbox；不切换分支、不创建 worktree、不触碰交互授权工具、不自动删除 stale lock。
- 如何验证：先运行 AI-first 设计校验；实现后运行检查点脚本 smoke、锁状态 smoke、阻塞态 smoke、失败计数 smoke、锁刷新 smoke、计划完成态 smoke，并按目标 Task 运行计划指定 build/test。
- 失败信号是什么：启动时存在 `BLOCKED` 检查点、验证连续两轮修复后仍失败、锁文件新鲜或疑似 stale、review subagent 不可用、review 未通过、git diff 包含非本轮文件、计划完成但 automation 未能暂停，均表示不能继续自动推进。
- 如何回滚：删除新增 skill 目录和检查点脚本，移除设计目标清单中的本设计路径；运行期临时状态可删除 `.codex-temp/plan-runs/<plan-id>.json` 和人工确认后的对应 lock 文件；代码或文档改动按本轮 commit 回滚。


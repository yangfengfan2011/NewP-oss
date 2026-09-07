# 任务跟踪器：GitHub

本仓库以 GitHub Issues 保存需要持久化的规格与执行状态。GitHub 操作使用 `gh` CLI，并从当前仓库的 `git remote` 自动识别目标仓库；写入授权遵循 `AGENTS.md`。

## 约定

- 创建 map issue：`gh issue create --title "..." --body-file <file>`
- 创建 child issue：`gh issue create --title "..." --body-file <file> --parent <map-number> --blocked-by <numbers>`；无 blocker 时省略 `--blocked-by`。
- 读取 issue：`gh issue view <number> --comments`
- 列出 issue：`gh issue list --state open --json number,title,body,labels,comments`
- 添加评论：`gh issue comment <number> --body "..."`
- 添加或移除标签：`gh issue edit <number> --add-label "..."` 或 `--remove-label "..."`
- 关闭 issue：`gh issue close <number> --comment "..."`
- 处理多行正文时，在 PowerShell 中优先使用临时正文文件配合 `--body-file`，避免复杂的命令行转义。

需要筛选标签、评论或状态时，使用 `gh` 的 `--json` 与 `--jq` 参数，只保留当前任务需要的字段。

## 规格到交付

1. 实施前明确目标、范围、非目标与验收条件，优先复用已确认的对话或 Issue 规格。局部缺陷和纯文档修正可在当前任务中直接实施；复杂工作需要已确认规格，但不因此自动调用 `$to-spec` 或发布新 Issue。用户要求发布规格或显式调用 `$to-spec` 时，按对应技能发布一个带 `ready-for-agent` 的 Spec Issue；已有规格不重复发布。
2. 选择最小交付形态：一个 owner、一个回滚边界且可在一个执行上下文完成时，保留单任务并按纵向检查点实施。只有存在多个可独立验收的增量、真实 blocker 或不同 owner 时，才考虑拆 child；用户授权拆票或显式调用 `$to-tickets` 后创建。每张 child 必须独立可验证，不能按前端、后端或测试水平拆分。
3. 既有 GitHub child 图继续使用原生关系与 Issue 状态；已有规格通过链接引用，避免复制正文或另建执行状态副本。
4. 新公共 seam 在扩大实施前需要一次真实纵向验证，规则见 `AGENTS.md` 的「设计与交付」。实施层级和验证分工见 [quick-verification.md](../verification/quick-verification.md)；不为满足流程单独创建可行性 child。

## 是否将 Pull Request 作为分流入口

**PRs as a request surface: no.**

如果以后希望把外部 Pull Request 也纳入 `/triage`，可将上面的值改为 `yes`。启用后：

- 读取 PR：`gh pr view <number> --comments`，并按需执行 `gh pr diff <number>`
- 列出外部 PR：`gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments`
- 仅保留 `CONTRIBUTOR`、`FIRST_TIME_CONTRIBUTOR` 或 `NONE`
- 评论、标签和关闭分别使用 `gh pr comment`、`gh pr edit` 和 `gh pr close`

GitHub Issues 与 Pull Requests 共用编号空间。遇到裸编号 `#42` 时，先执行 `gh pr view 42`；若不是 PR，再执行 `gh issue view 42`。

## 技能操作映射

当技能要求“发布到任务跟踪器”时，创建 GitHub issue。

当技能要求“获取相关 ticket”时，执行：

`gh issue view <number> --comments`

## Wayfinder 操作

`/wayfinder` 使用一个 map issue 管理多个 child issue：

- Map：单个带有 `wayfinder:map` 标签的 issue，正文保存 Notes、Decisions-so-far 和 Fog。
- Child ticket：创建时使用 `--parent` 建立 GitHub sub-issue 关联；正文链接只用于导航。
- Child 标签：使用 `wayfinder:<type>`，类型包括 `research`、`prototype`、`grilling` 和 `task`。
- 阻塞关系：创建时使用 `--blocked-by` 建立 GitHub 原生 issue dependencies；关系未建立时先修复该操作，再选择依赖它的执行前沿，不能把正文备注当成已建立关系。
- Frontier：先按下文“执行前沿与中断恢复”核对续作；领取新任务时，从 map 的未关闭 child 中排除仍被阻塞或已有负责人者，再按 map 顺序选择第一个。
- 认领：`gh issue edit <n> --add-assignee '@me'`。这是执行会话的第一次 GitHub 写操作。
- 完成：向 child 写入结论、关闭 child，并在 map 的 Decisions-so-far 中追加上下文链接。

## 执行前沿与中断恢复

开始或恢复 Issue 执行时，先读取本次授权范围内的 Issue 状态、负责人、最新评论及原生依赖，再选择下一步。

- **恢复优先**：依据当前任务历史、Issue 执行记录及对应工作区，核对此前认领但未完成的 Issue；检查已有改动、提交和验证结果，确认依赖已满足后从尚未完成的步骤继续。此时已有负责人不构成跳过原任务的理由。
- **确认会话归属**：相同 GitHub 负责人不能单独证明是本会话的任务。结合可用的会话状态、交接记录和工作区归属确认没有其他会话正在执行；归属仍不明确时，先澄清该任务的接手安排，不覆盖改动或解除他人的认领。授权范围内有明确独立的执行前沿时可继续其他任务。
- **复用已完成工作**：核对现有实现与当前验收条件，保留有效提交和验证证据，只补缺失的实现、验证或完成回写。已有实现但 Issue 仍开放时，先核实完成条件，再按授权关闭并同步父 Map；不因状态未关闭而重复实现。
- **区分无前沿的原因**：所有 child 已关闭时进入父级收尾，父级验收通过后才算整体完成；仍有未完成项时，分别报告依赖阻塞、其他会话正在执行或归属待确认的任务及恢复入口。没有可领取的新任务不代表整体完成。每个 child 完成后重新读取状态与依赖，再选择下一前沿。

## 依赖关系

GitHub 原生 sub-issue（`--parent`）与 blocked-by（`--blocked-by`）是依赖事实源，issue 正文不替代原生关系。创建后读取回 issue 核对关系是否建立。

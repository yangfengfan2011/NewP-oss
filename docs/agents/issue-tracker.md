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
2. 选择最小交付形态：一个 owner、一个回滚边界且可在一个执行上下文完成时，保留单任务，由同一 owner 连续完成纵向检查点。只有存在多个可独立验收的增量、真实 blocker 或不同 owner 时，才考虑拆 child；用户授权拆票或显式调用 `$to-tickets` 后创建。每张 child 必须独立可验证，不能按前端、后端或测试水平拆分。
3. 既有 GitHub child 图继续使用原生关系与 Issue 状态，按下文“执行前沿与中断恢复”调度 owner 和 ready child；已有规格通过链接引用，避免复制正文或另建执行状态副本。
4. 新公共 seam 在扩大实施前需要一次真实纵向验证，规则见 `AGENTS.md` 的「设计与交付」。实施层级和验证分工见 [quick-verification.md](../verification/quick-verification.md)；不为满足流程单独创建可行性 child。

### 交付提示词

以下模板的授权仅在用户复制并发出时生效；本文不自行授予技能调用、提交或远端写入权限。按本次任务替换仓库和 Issue，调整模型偏好及授权差异即可。

```text
在 <仓库路径> 完整交付父 Issue #<编号>。

遵循 AGENTS.md、docs/agents/issue-tracker.md 和 docs/verification/quick-verification.md。先核对父子 Issue、评论、原生依赖及现有实现；首次将 HEAD 作为审查 baseline 写入父 Issue 评论，续作沿用该 baseline，并保留已有改动与有效证据。

本次授权按 issue-tracker 的“技能操作映射”选择实施入口：依赖图中存在可独立并行的 child 时，使用 $implement-spec 作为唯一父级编排入口；小任务或紧密相关的串行增量使用 $implement，由同一 owner 持续完成。按本页依赖与资源规则调度。子代理默认使用 GPT-6.1 SOL（model: gpt-6.1-sol）。每次创建时，由主会话按子任务难度显式设置 reasoning_effort，使用 medium 及以上，并简记理由；通过实际创建参数设置，不仅写入任务描述。按 quick-verification 前置关键反例，各 Issue 分别验证和提交，集成并核对完成证据后及时回写、关闭及同步父 Map。

全部 child 完成并集成后，以原 baseline 为 fixed point、父子 Issue 及已确认评论为 Spec，由独立代理并行执行一次 $code-review 的 Standards 与 Spec；implement-spec 的收尾审查计入这一次。自主修复本次引入且有稳定证据的 P0/P1（数据损坏或丢失、权限或安全边界扩大、事务一致性破坏、主路径不可用），以及范围内有规格依据和缺口证据的显式验收遗漏；其他 finding 只记录。按 quick-verification 完成原 owner 返修、原轴聚焦复核，汇总 child 有效证据的覆盖与候选归属；只对真实跨 child 集成缺口新增或补跑最小场景。全仓 lint/build/unit verify 仅按明确交付约定或实际影响执行，不附带整模块或全部验收；覆盖齐备后按授权关闭父 Issue。

授权本任务范围内的本地提交及父子 Issue 生命周期写入；不推送、不创建 PR、新 Issue 或 tickets.md，不修改无关内容。仅在权限或凭据缺失、任务归属不明、重大决策未定或需要未授权操作时暂停，其余自主推进。
```

## 是否将 Pull Request 作为分流入口

**PRs as a request surface: no.**

如果以后希望把外部 Pull Request 也纳入 `/triage`，可将上面的值改为 `yes`。启用后：

- 读取 PR：`gh pr view <number> --comments`，并按需执行 `gh pr diff <number>`
- 列出外部 PR：`gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments`
- 仅保留 `CONTRIBUTOR`、`FIRST_TIME_CONTRIBUTOR` 或 `NONE`
- 评论、标签和关闭分别使用 `gh pr comment`、`gh pr edit` 和 `gh pr close`

GitHub Issues 与 Pull Requests 共用编号空间。遇到裸编号 `#42` 时，先执行 `gh pr view 42`；若不是 PR，再执行 `gh issue view 42`。

## 技能操作映射

技能要求发布到跟踪器时，仅在本次授权包含创建时新建 GitHub Issue；获取 ticket 使用 `gh issue view <number> --comments`。已有规格与 child 直接复用。

小任务或紧密相关的串行增量使用 `$implement`，按下文保持 owner 连续。已有确认 Spec、原生 child 依赖图且存在可独立并行的工作时，可由用户明确调用或通过交付提示词授权 `$implement-spec`，作为唯一父级编排入口；不为调用技能拆票，也不另叠一套协调流程。它采用任务图和简短上下文引用，具体 owner、worktree 与集成安排由本页调度规则决定，不固定每票新建 implementer、worktree 或 merger。

`implement-spec` 的集成目标沿用本次确认的工作区／分支；需要隔离时创建 worktree，不能按技能默认 reset 覆盖已有修改。worktree 不自动带入被忽略的配置、历史发行物或凭据，也不隔离本机 SQL 与端口；必需验证缺环境时按验证指南报告未完成，不能以跳过冒充通过。child 的适用验证、提交及集成证据齐备后即按授权关闭并同步 Map、刷新原生 frontier，不拖到父级结尾，也不另建依赖状态副本。审查修复沿用任务约定的级别和显式验收范围，由原 owner 处理、原轴聚焦复核；技能中的“修复全部 finding”不扩大范围，其收尾按验证指南汇总覆盖与归属，不追加整模块总验收；仓库 verify 按明确交付约定或实际影响选择。清理 worktree 前确认改动已集成且需保留资产已保存。

用户请求会话复盘或明确调用 `$retro` 时使用该技能，重点用于异常耗时、反复返修和工具／检查失效；不把它加入每个 child 或父级的固定收尾。取证同时覆盖协调者与实际实施 owner 的记录，区分新增范围、等待和返工，按验证指南复用已有指标。优先定位现有本机检查的漏接／失效及工具成本，不能仅因没有 CI 或 hook 就判为缺陷或自动新增它们。标准沿用 [code-quality-standard.md](../developer/code-quality-standard.md) 及其引用，不另建 `CODING_STANDARDS.md`；实施前的规范读取要求仍由 `AGENTS.md` 规定。复盘先交付有证据的改进建议，落实修改及远端操作遵循本次授权。

## Wayfinder 操作

`/wayfinder` 使用一个 map issue 管理多个 child issue：

- Map：单个带有 `wayfinder:map` 标签的 issue，正文保存 Notes、Decisions-so-far 和 Fog。
- Child ticket：创建时使用 `--parent` 建立 GitHub sub-issue 关联；正文链接只用于导航。
- Child 标签：使用 `wayfinder:<type>`，类型包括 `research`、`prototype`、`grilling` 和 `task`。
- 阻塞关系：创建时使用 `--blocked-by` 建立 GitHub 原生 issue dependencies；关系未建立时先修复该操作，再选择依赖它的执行前沿，不能把正文备注当成已建立关系。
- Frontier：按下文“执行前沿与中断恢复”核对续作并调度 ready 集合，map 顺序只表示选择优先级。
- 认领：`gh issue edit <n> --add-assignee '@me'`。这是执行会话的第一次 GitHub 写操作。
- 完成：向 child 写入结论、关闭 child，并在 map 的 Decisions-so-far 中追加上下文链接。

## 执行前沿与中断恢复

开始或恢复 Issue 执行时，先读取本次授权范围内的 Issue 状态、负责人、最新评论及原生依赖，再确定当前可推进的集合。

- **恢复优先**：依据当前任务历史、Issue 执行记录及对应工作区，核对此前认领但未完成的 Issue；检查已有改动、提交和验证结果，确认依赖已满足后从尚未完成的步骤继续。此时已有负责人不构成跳过原任务的理由。
- **确认会话归属**：相同 GitHub 负责人不能单独证明是本会话的任务。结合可用的会话状态、交接记录和工作区归属确认没有其他会话正在执行；归属仍不明确时，先澄清该任务的接手安排，不覆盖改动或解除他人的认领。授权范围内有明确独立的执行前沿时可继续其他任务。
- **调度 ready 集合**：新领取的 child 必须未关闭、原生 blocker 均已完成且未被其他会话认领；map 顺序只决定选择优先级。写入文件互斥且运行资源独立的 child 可并行推进；共享文件、构建产物、状态文件、数据库或端口的冲突操作串行，Gate 按验证指南串行。验证期间所有 owner 保持相关候选不变；共享工作树的暂存、提交及候选切分窗口由协调者串行安排，避免混入其他 owner 的差异。独立 worktree 也须核对 SQL catalog、端口与发行目录是否隔离；边界未明时先并行只读调查，写入与验证待边界明确，不跳过 blocked-by 或他人的认领。
- **保持 owner 连续**：相关小任务及接入同一能力的后续 child 优先复用原 owner，返修也回到原 owner；按独立边界需要并行或需要独立审查时再分配不同 owner。复用 owner 不合并各 Issue 的验收条件、完成证据或生命周期；按实际交付层级交回证据，不为换 child 重复调研和验证仍有效的工作。
- **复用已完成工作**：核对现有实现与当前验收条件，保留有效提交和验证证据，只补缺失的实现、验证或完成回写。已有实现但 Issue 仍开放时，先核实完成条件，再按授权关闭并同步父 Map；不因状态未关闭而重复实现。
- **区分无前沿的原因**：所有 child 已关闭时进入父级收尾，核对覆盖与候选归属、补齐真实集成缺口后才算整体完成；仍有未完成项时，分别报告依赖阻塞、其他会话正在执行或归属待确认的任务及恢复入口。没有可领取的新任务不代表整体完成。child 完成或依赖变化后重新读取状态与依赖，更新 ready 集合。

## 依赖关系

GitHub 原生 sub-issue（`--parent`）与 blocked-by（`--blocked-by`）是依赖事实源，issue 正文不替代原生关系。创建后读取回 issue 核对关系是否建立。

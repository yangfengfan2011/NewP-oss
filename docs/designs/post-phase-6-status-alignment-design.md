# Post-Phase 6 状态与计划对齐设计

## 1. 背景

仓库已完成 `WP1` 插件平台治理增强与 `WP2` AI-first 执行标准落地，当前主线正式转入 `WP3`：文档与状态一致性治理。

但与 `WP3` 直接相关的几类入口文档仍存在口径错位：

- `docs/designs/post-phase-6-status-alignment-design.md` 仍停留在“`WP1`、`WP2` 只有设计、尚未实现”的旧前提。
- `docs/phases/post-phase-6-stage-map.md` 中 `Stage 3` 与 `Stage 7` 都使用了“WP3 状态对齐”表述，容易把“设计完成”和“第一轮落地”混成同一阶段。
- `docs/collaboration/work-packages/wp3-post-phase-6-status-alignment.md` 仍把范围限制在“新增设计 + 修改 `Plan.md` + 修改 `docs/current-status.md`”，没有覆盖本轮真正需要同步的协作入口与验证入口。
- `docs/verification/verification-matrix.md` 尚未收录 `WP2` 与 `WP3` 的验证入口。
- `docs/current-status.md` 虽然已经把主线切到 `WP3`，但尚未记录 `WP3` 第一轮落地结果，且“文档链路已知缺口”遗漏了 `Phase 6.6`。

这些冲突都属于“入口口径不一致、阶段顺序滞后、引用链路不统一”，而且可以在不扩成全仓清理的前提下完成最小闭环。

## 2. 目标

- 收敛 `WP3` 第一轮的最小闭环范围，只处理当前最影响后续协作入口的一组冲突。
- 统一 `docs/current-status.md`、`docs/phases/post-phase-6-stage-map.md`、`docs/collaboration/work-packages/wp3-post-phase-6-status-alignment.md` 与 `docs/verification/verification-matrix.md` 的当前口径。
- 为本轮新增正式验证记录，避免 `WP3` 自己继续缺少 verification 入口。

## 3. 非目标

- 不补写 `Phase 6.5`、`Phase 6.6`、`Phase 6.7` 缺失的独立实现说明或独立验证文档本体。
- 不把 `WP3` 扩成历史文档全量重写任务。
- 不修改任何代码、脚本、测试或 `AGENTS.md`。
- 不为了“每轮都要碰一下”而强行改动 `Plan.md`；只有发现路线层口径直接冲突时才调整。

## 4. 入口在哪里

本轮 `WP3` 第一轮收口的正式入口如下：

- 设计入口：`docs/designs/post-phase-6-status-alignment-design.md`
- 状态入口：`docs/current-status.md`
- 协作入口：`docs/phases/post-phase-6-stage-map.md`
- 工作包入口：`docs/collaboration/work-packages/wp3-post-phase-6-status-alignment.md`
- 验证入口：`docs/verification/verification-matrix.md`
- 验证记录：`docs/verification/post-phase-6-status-alignment-phase-1-verification.md`

`Plan.md` 在本轮只作为路线核对输入，不作为默认修改目标。

## 5. 边界在哪里

- 只处理 `WP3` 第一轮直接相关的状态、协作和验证入口，不新增业务实现。
- 保留 `Phase 0` 到 `Phase 6.7`、`WP1`、`WP2` 的既有事实描述，不靠删减事实制造一致性。
- 对 `Phase 6.5`、`Phase 6.6`、`Phase 6.7` 的链路缺口只做记录，不在本轮直接补齐缺口文档本体。
- `Plan.md` 已在路线层与 `WP1`–`WP4` 顺序对齐，本轮若未发现新的直接冲突，则保持不动。

## 6. 第一轮冲突选择与取舍

### 6.1 本轮只处理的冲突

1. `WP3` 设计文档仍基于旧前提，未反映 `WP1`、`WP2` 已收口的现实。
2. `Stage map` 没有清楚区分“`WP3` 设计完成”和“`WP3` 第一轮落地完成”。
3. `WP3` 工作包与验证矩阵仍停留在更早的三文件收口口径。
4. `docs/current-status.md` 尚未记录 `WP3` 第一轮收口结果，且缺口表遗漏 `Phase 6.6`。

### 6.2 本轮不处理的冲突

- 不在本轮补 `Phase 6.5`、`Phase 6.6`、`Phase 6.7` 的独立验证文档。
- 不在本轮补 `Plugin Ops` 独立实现说明。
- 不在本轮展开 `Plan.md` 的二次细化，只保留路线层语义。

### 6.3 取舍理由

这组冲突同时满足三点：

- 直接影响后续协作入口是否继续引用过时主线。
- 可控制在少量文档内完成，不会扩大为历史文档大清理。
- 做完之后，`WP4` 的准入复核可以在更稳定的入口上继续推进。

## 7. 收口方案

### 7.1 `docs/current-status.md`

- 新增 `WP3` 第一轮收口段落，明确本轮已落地的文档入口统一动作。
- 将“当前推荐下一步”更新为：先进入 `WP4` 的 `Stage 8` 准入复核，再把 `WP3` 历史缺口补链作为后续专项处理。
- 将“文档链路已知缺口”补齐 `Phase 6.6`，避免状态入口继续遗漏核心缺口。

### 7.2 协作入口

- 在 `docs/phases/post-phase-6-stage-map.md` 中显式区分：
  - `Stage 3` = `WP3` 设计完成
  - `Stage 7` = `WP3` 第一轮落地完成
- 将下一推荐阶段切换为 `Stage 8`，避免协作入口仍把主线停留在已完成的 `Stage 7`。
- 更新 `docs/collaboration/work-packages/wp3-post-phase-6-status-alignment.md`，使其反映本轮真实允许修改文件、验证方式和边界。

### 7.3 验证入口

- 在 `docs/verification/verification-matrix.md` 中补入 `WP2` 与 `WP3` 的定向验证入口。
- 新增 `docs/verification/post-phase-6-status-alignment-phase-1-verification.md`，作为 `WP3` 第一轮的正式验证记录。

### 7.4 `Plan.md`

本轮核对后，`Plan.md` 在路线层已经与 `WP1`–`WP4` 顺序保持一致，没有新的直接冲突，因此不作为第一轮强制改动对象。

## 8. 如何验证

本轮验证只做与 `WP3` 直接相关的最小定向检查：

1. 设计校验

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\post-phase-6-status-alignment-design.md
```

2. 关键口径对齐检查

```powershell
powershell -NoProfile -Command "Select-String -Path '.\docs\current-status.md','.\docs\phases\post-phase-6-stage-map.md','.\docs\collaboration\work-packages\wp3-post-phase-6-status-alignment.md','.\docs\verification\verification-matrix.md' -Pattern 'WP3|Stage 7|Stage 8|WP4|Phase 6.6|第一轮'"
```

3. 变更范围检查

```powershell
git status --short -- docs/current-status.md docs/designs/post-phase-6-status-alignment-design.md docs/phases/post-phase-6-stage-map.md docs/collaboration/work-packages/wp3-post-phase-6-status-alignment.md docs/verification/verification-matrix.md docs/verification/post-phase-6-status-alignment-phase-1-verification.md Plan.md
```

## 9. 失败信号是什么

若出现以下任一情况，说明本轮收口未成功：

- `docs/current-status.md` 仍未记录 `WP3` 第一轮收口结果。
- `docs/current-status.md` 的缺口表仍遗漏 `Phase 6.6`。
- `docs/phases/post-phase-6-stage-map.md` 仍把 `Stage 7` 标成待拆分，或仍把下一包指向 `Stage 7`。
- `docs/verification/verification-matrix.md` 仍找不到 `WP2` / `WP3` 验证入口。
- `WP3` 工作包仍只允许修改旧版三文件范围。

## 10. 风险

- 本轮只做第一轮入口统一，历史链路缺口仍会保留。控制方式：在 `docs/current-status.md` 中继续显式列出缺口，不把它们写成“已解决”。
- `Plan.md` 本轮不改，个别读者可能误以为遗漏。控制方式：在本设计中明确记录“路线层已对齐，因此本轮不强改”。
- `WP3` 第一轮完成后，团队可能过早认为所有文档治理都结束了。控制方式：把后续缺口补链明确标成 `WP3` 第二轮专项，而不是“已收口”。

## 11. 如何回滚

- 所有改动均为文档级变更，可直接通过 git 回退本轮相关文件。
- 若只需局部回滚，可单独回退 `docs/current-status.md`、`docs/collaboration/*` 或 `docs/verification/*` 中的本轮修改。
- 不涉及代码、数据库或运行时状态变更，无需额外数据回滚。

# WP3：文档与状态一致性治理（第一轮）

## 1. 目标

在最小范围内完成 `WP3` 第一轮落地：统一 `docs/current-status.md`、协作入口、工作包入口和验证入口对当前主线的说法，并补一份正式验证记录。

本工作包只处理文档，不做代码实现。

## 2. 当前阶段归属

- 后续路线中的 `WP3`
- 对应 `docs/phases/post-phase-6-stage-map.md` 的 `Stage 7`
- 处于 `WP1`、`WP2` 已完成收口之后，`Stage 8` 之前

## 3. 必读上下文

执行前请先阅读：

- `AGENTS.md`
- `Plan.md`
- `docs/current-status.md`
- `docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md`
- `docs/designs/post-phase-6-status-alignment-design.md`
- `docs/architecture/ai-first-execution-standard.md`
- `docs/history/collaboration/github-copilot-working-mode.md`
- `docs/phases/post-phase-6-stage-map.md`

如需核对缺口与验证入口，再阅读：

- `docs/verification/verification-matrix.md`
- `docs/verification/ai-first-execution-standard-phase-7-verification.md`

## 4. 允许修改的文件

- 修改：`docs/designs/post-phase-6-status-alignment-design.md`
- 修改：`docs/current-status.md`
- 修改：`docs/phases/post-phase-6-stage-map.md`
- 修改：`docs/collaboration/work-packages/wp3-post-phase-6-status-alignment.md`
- 修改：`docs/verification/verification-matrix.md`
- 新增：`docs/verification/post-phase-6-status-alignment-phase-1-verification.md`

条件修改：

- `Plan.md`
  - 仅当发现路线层表述与当前主线直接冲突时才改；如果只是细节可补充而非直接冲突，本轮不改。

## 5. 禁止触碰的范围

- 不修改任何后端代码
- 不修改任何前端代码
- 不修改 `AGENTS.md`
- 不修改 `CLAUDE.md`
- 不修改 `docs/architecture/*`
- 不补 `Phase 6.5`、`Phase 6.6`、`Phase 6.7` 的独立实现说明或独立验证文档本体
- 不把 `WP3` 扩成历史文档全量重写
- 不把 `WP4` 的实施代码或实施准入结论提前并入本包

## 6. 工作包必须覆盖的内容

### 6.1 设计文档必须收口

- 改写旧前提：不再把 `WP1`、`WP2` 写成“只有设计、尚未实现”
- 明确本轮入口、边界、验证方式、失败信号和回滚思路
- 明确本轮只处理哪组冲突，以及为什么先处理它们

### 6.2 `docs/current-status.md` 必须收口

- 新增 `WP3` 第一轮收口段落
- “当前推荐下一步”更新为先进入 `WP4 Stage 8`，再处理 `WP3` 第二轮专项补链
- “文档链路已知缺口”补齐 `Phase 6.6`

### 6.3 协作入口必须收口

- `Stage map` 中显式区分 `Stage 3 = WP3 设计` 与 `Stage 7 = WP3 第一轮落地`
- `Stage 7` 不再保持“待拆分”
- 当前推荐下一包切换到 `Stage 8`

### 6.4 验证入口必须收口

- `docs/verification/verification-matrix.md` 增加 `WP2` 与 `WP3` 条目
- 新增 `WP3` 第一轮验证记录，并把本轮实际执行的检查写清

### 6.5 `Plan.md` 的处理规则

- 先核对是否存在路线层直接冲突
- 如果没有直接冲突，应在交回汇报中明确说明“本轮未改 `Plan.md` 的理由”

## 7. 必须写清的约束

文档中必须明确表达这些规则：

- 状态文档反映的是“当前统一口径”，不是会话历史堆积
- `Stage map` 必须区分“设计完成”和“第一轮落地完成”
- 验证矩阵要能给出当前工作包的最小定向验证入口
- 缺口仍然只是“文档链路缺口”，不能误写成“功能未完成”
- 本轮只做第一轮入口统一，不展开大范围历史补链

## 8. 执行要求

- 优先保留仓库内已有事实，再调整阶段命名、引用路径和验证入口
- 不写常见占位词
- 不引入仓库中没有依据的新阶段名、新能力名或新完成状态
- 如果核对后确认 `Plan.md` 已对齐，应明确写出“不改动”的理由，而不是默认遗漏
- 文档应可直接推送到 GitHub，不依赖本机路径、IDE 状态或会话上下文

## 9. 验收标准

满足以下条件才算完成：

1. `docs/designs/post-phase-6-status-alignment-design.md` 已更新为当前轮口径
2. `docs/current-status.md` 已记录 `WP3` 第一轮结果，并补齐 `Phase 6.6` 缺口
3. `docs/phases/post-phase-6-stage-map.md` 已把下一推荐阶段切到 `Stage 8`
4. `docs/verification/verification-matrix.md` 已有 `WP2` 与 `WP3` 入口
5. `docs/verification/post-phase-6-status-alignment-phase-1-verification.md` 已存在
6. 没有把未完成事项误写成已完成
7. 没有临时占位表达，没有依赖单机环境的描述

## 10. 建议验证方式

至少做这些自检：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\post-phase-6-status-alignment-design.md`
- `powershell -NoProfile -Command "Select-String -Path '.\docs\current-status.md','.\docs\phases\post-phase-6-stage-map.md','.\docs\collaboration\work-packages\wp3-post-phase-6-status-alignment.md','.\docs\verification\verification-matrix.md' -Pattern 'WP3|Stage 7|Stage 8|WP4|Phase 6.6|第一轮'"`
- `git status --short -- docs/current-status.md docs/designs/post-phase-6-status-alignment-design.md docs/phases/post-phase-6-stage-map.md docs/collaboration/work-packages/wp3-post-phase-6-status-alignment.md docs/verification/verification-matrix.md docs/verification/post-phase-6-status-alignment-phase-1-verification.md Plan.md`

## 11. 交回 Codex 时必须汇报

完成后请用简短结构汇报：

- 改了什么
- 哪些入口文档已经统一
- 为什么这轮没有扩成历史全量补链
- `Plan.md` 是否改动；如果没改，理由是什么
- 做了哪些定向验证
- 仍有哪些缺口留给 `WP3` 第二轮或后续专项

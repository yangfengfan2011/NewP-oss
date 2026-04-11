# Post-Phase 6 Copilot 阶段总控文档

## 1. 目的

这份文档用于把 Post-Phase 6 阶段的协作路线收敛到一个地方，作为后续给 GitHub Copilot 拆分工作包的母文档。

它回答三件事：

- 当前已经完成了哪些阶段
- 接下来推荐按什么顺序推进
- 后续每个阶段拆给 Copilot 时应保持什么边界

本文件是阶段总览，不替代具体设计文档，也不替代后续单个工作包文档。

## 2. 使用方式

推荐顺序：

1. 先阅读 `docs/collaboration/github-copilot-working-mode.md`
2. 再阅读 `docs/architecture/ai-first-execution-standard.md`，确认当前阶段是否涉及新增设计或阶段验收入口
3. 再阅读本文件，确认当前所在阶段和下一阶段
4. GitHub Copilot 只执行当前工作包
5. 由 Codex 负责审查、返修和决定是否进入下一阶段

## 3. 总体原则

- 已完成阶段默认只读，不因为顺手优化而重新扩写
- 未完成阶段先看对应设计文档，再拆具体工作包
- 一次只拆一个阶段或一个子阶段给 Copilot
- 设计、实现、状态收口、迁移实施不要混在同一个工作包里
- 如果某阶段存在明确前置条件，前置条件未满足前不拆实现包
- 如果某阶段需要新增设计或补写阶段验收口径，必须显式引用 `docs/architecture/ai-first-execution-standard.md`

## 4. 阶段总览

| 阶段 | 归属 | 当前状态 | 主要目标 | 主要依据 |
|------|------|----------|----------|----------|
| Stage 0 | 协作基线 | 已完成 | 建立 Copilot 工作模式、审查清单和基础工作包结构 | `docs/collaboration/github-copilot-working-mode.md`、`docs/collaboration/codex-review-checklist.md` |
| Stage 1 | WP1 设计 | 已完成 | 完成插件治理增强设计 | `docs/designs/plugin-governance-enhancement-design.md` |
| Stage 2 | WP2 设计 | 已完成 | 完成 AI-first 执行标准设计 | `docs/designs/ai-first-execution-standard-design.md` |
| Stage 3 | WP3 状态对齐 | 已完成 | 完成路线、状态与链路对齐设计 | `docs/designs/post-phase-6-status-alignment-design.md`、`Plan.md`、`docs/current-status.md` |
| Stage 4 | WP4 迁移准备 | 已完成 | 完成 Dictionary / Enumeration 迁移准备设计 | `docs/designs/dictionary-enumeration-migration-preparation-design.md` |
| Stage 5 | WP1 实施收口 | 已完成 | 插件治理增强已完成实现、验证与状态收口 | `docs/current-status.md`、`docs/verification/plugin-governance-enhancement-verification.md` |
| Stage 6 | WP2 执行标准落地 | 已完成 | AI-first 执行标准已形成正式标准、校验、登记、创建与协作入口闭环 | `docs/architecture/ai-first-execution-standard.md`、`docs/current-status.md` |
| Stage 7 | WP3 状态对齐 | 待拆分 | 统一状态文档、计划文档、设计文档和验证记录口径 | `docs/designs/post-phase-6-status-alignment-design.md`、`docs/current-status.md` |
| Stage 8 | WP4 实施准入复核 | 待拆分 | 复核迁移正式实施准入条件是否满足 | `docs/designs/dictionary-enumeration-migration-preparation-design.md` 第 8 节 |
| Stage 9 | WP4 Dictionary 实施 | 暂不拆分 | 首批 Dictionary 导入实现 | 仅在 Stage 8 通过后拆分 |
| Stage 10 | WP4 Enumeration 实施 | 暂不拆分 | 首批 Enumeration 导入实现 | 仅在 Stage 8 通过后拆分 |

## 5. 已完成阶段

### 5.1 Stage 0 到 Stage 6 的处理规则

- 这些阶段的核心文档已经存在，不应再作为“开放式写作任务”重新交给 Copilot
- 如果后续审查发现问题，只按具体 review finding 做小范围返修
- 不因为新的实现阶段启动，就回头重写这些设计文档的主结构

### 5.2 已完成阶段对应文档

- Stage 0：`docs/collaboration/github-copilot-working-mode.md`、`docs/collaboration/codex-review-checklist.md`
- Stage 1：`docs/designs/plugin-governance-enhancement-design.md`
- Stage 2：`docs/designs/ai-first-execution-standard-design.md`
- Stage 3：`docs/designs/post-phase-6-status-alignment-design.md`
- Stage 4：`docs/designs/dictionary-enumeration-migration-preparation-design.md`
- Stage 5：`docs/verification/plugin-governance-enhancement-verification.md`、`docs/current-status.md`
- Stage 6：`docs/architecture/ai-first-execution-standard.md`、`docs/current-status.md`

## 6. 待拆分阶段

### 6.1 Stage 7：WP3 状态对齐

推荐作为下一个 Copilot 工作包。

目标：

- 对齐 `Plan.md`、`docs/current-status.md`、相关设计文档和验证记录中的阶段口径
- 清理当前主线与历史协作文档之间的冲突引用
- 让后续工作包入口统一引用当前真实主线，而不是历史阶段顺序

边界：

- 只做状态、口径和链接对齐，不写新的业务实现
- 不顺手重写已经完成的设计正文
- 不把 WP4 的实施代码提前并入本包

### 6.2 Stage 8：WP4 实施准入复核

目标：

- 逐条复核 `Dictionary / Enumeration` 正式实施准入条件
- 确认是否仍有模型兼容、多语言策略、样本审阅或治理收口前置缺口

边界：

- 只做准入复核，不写导入代码
- 如果准入条件不满足，应输出阻塞项而不是继续实施

### 6.3 Stage 9：WP4 Dictionary 实施

目标：

- 仅在 Stage 8 通过后，按冻结样本实现首批 Dictionary 导入

边界：

- 首批只做已冻结样本
- 不扩到全部 `.dic` 文件

### 6.4 Stage 10：WP4 Enumeration 实施

目标：

- 仅在 Stage 8 通过后，按冻结样本实现首批 Enumeration 导入

边界：

- 只做静态 `.enum` + `.dic` 样本
- 不处理 DB Enumeration

## 7. 推荐推进顺序

建议按以下顺序继续：

1. Stage 7：WP3 状态对齐
2. Stage 8：WP4 实施准入复核
3. Stage 9：WP4 Dictionary 实施
4. Stage 10：WP4 Enumeration 实施

原因：

- `WP1` 与 `WP2` 已完成收口，不应再把主线拉回已完成工作包
- Stage 7 负责统一当前主线口径，避免后续协作继续引用过时阶段顺序
- Stage 8 负责控制迁移实施前置条件，避免重新越过治理主线
- Stage 9 和 Stage 10 应在治理和准入都稳定后再拆给 Copilot

## 8. 从总控文档拆成 Copilot 工作包的规则

后续每次拆工作包时，子工作包至少应写清：

- 当前要执行的是哪个 Stage
- 该 Stage 的唯一目标
- 允许修改哪些文件
- 明确禁止碰哪些文件
- 最小交付物是什么
- 必须运行哪些验证
- 还有哪些验证明确不在本轮运行
- 完成后要如何向 Codex 汇报

拆分时应遵守：

- 一个工作包只对应一个 Stage；如果阶段过大，只拆它的一个子阶段
- 不把设计补写、代码实现、状态文档收口混在同一个工作包
- 不把“顺手优化”写进工作包
- 如果工作包涉及新增设计文档，必须显式写出 `new-design-doc`、`validate-ai-first-design` 与必要的 `check-ai-first-design-targets` 用法

## 9. 当前推荐入口

如果现在开始下一轮 Copilot 协作，建议从这里进入：

1. `docs/collaboration/github-copilot-working-mode.md`
2. `docs/architecture/ai-first-execution-standard.md`
3. `docs/collaboration/post-phase-6-copilot-stage-map.md`
4. 后续新增的 `Stage 7` 对应工作包文档

当前推荐的下一包是：

- Stage 7：WP3 状态对齐

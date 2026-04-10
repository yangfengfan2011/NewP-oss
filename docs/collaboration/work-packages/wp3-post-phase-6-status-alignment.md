# WP3：文档与状态一致性治理

## 1. 目标

为当前阶段补一份正式的“Post-Phase 6 状态与计划对齐设计”，并在受控范围内同步收口 `docs/current-status.md` 与 `Plan.md`，让当前路线、阶段完成度和核心能力链路形成统一口径。

本工作包只处理文档，不做代码实现。

## 2. 当前阶段归属

- 后续路线中的 `WP3`
- 属于“文档与状态一致性治理”
- 应在 `WP1`、`WP2` 两份设计文档已具备后推进

## 3. 必读上下文

执行前请先阅读：

- `AGENTS.md`
- `Plan.md`
- `docs/current-status.md`
- `docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md`
- `docs/designs/plugin-governance-enhancement-design.md`
- `docs/designs/ai-first-execution-standard-design.md`
- `docs/collaboration/github-copilot-working-mode.md`
- `docs/collaboration/codex-review-checklist.md`

如需核对文档链路，再阅读：

- `docs/architecture/plugin-runtime-loading.md`
- `docs/developer/persistence-public-api.md`
- `docs/verification/phase-3-contract-verification.md`

## 4. 允许修改的文件

- 新增：`docs/designs/post-phase-6-status-alignment-design.md`
- 修改：`docs/current-status.md`
- 修改：`Plan.md`

本工作包默认不修改其他文件。

## 5. 禁止触碰的范围

- 不修改任何后端代码
- 不修改任何前端代码
- 不修改 `AGENTS.md`
- 不修改 `CLAUDE.md`
- 不修改 `docs/architecture/*`
- 不修改 `docs/verification/*`
- 不修改现有 `WP1`、`WP2` 工作包文档
- 不把“建议路线”写成“已完成阶段”
- 不为了对齐口径而抹掉已有阶段成果

## 6. 工作包必须覆盖的内容

### 6.1 新设计文档必须覆盖

- 背景
- 目标
- 非目标
- 范围
- 对齐原则
- `docs/current-status.md` 收口方案
- `Plan.md` 收口方案
- design / implementation / verification 链路核对方案
- 风险
- 回滚思路

### 6.2 `docs/current-status.md` 必须收口的内容

- “当前推荐下一步”顺序调整为：
  1. 平台治理收口
  2. AI-first 规范落地
  3. 文档与状态一致性治理
  4. 最小迁移连续性
- 不把 `WP2` 之外的新工作误写成已完成
- 不删除已经有文档和验证支撑的阶段成果

### 6.3 `Plan.md` 必须补充的内容

- 增加与上述四个工作包一致的后续路线说明
- 每个工作包只用一小段说明目标，不扩写成新的大阶段
- 口径必须与 `docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md` 一致

### 6.4 文档链路核对必须覆盖的能力

至少核对这三项能力是否同时具备以下链路：

- design
- implementation or guide
- verification
- current status mention

建议优先核对：

- Plugin platform
- Multi-db persistence
- Plugin operations center

## 7. 必须写清的约束

文档中必须明确表达这些规则：

- 状态文档要反映“当前统一口径”，不是会话历史堆积
- `Plan.md` 负责表达路线和优先级，不负责替代设计文档细节
- 已有实现但缺少文档或验证收口的事项，不能被写成“完全完成”
- 设计、实现说明、验证记录之间出现缺口时，应记录为链路缺口，而不是靠口头假设补齐
- 文档收口不能通过删减事实来制造一致性

## 8. 执行要求

- 目标是“统一口径”，不是重写整个计划体系。
- 优先保留仓库内已有事实，再调整顺序和表述。
- 术语尽量对齐 `Plan.md`、`docs/current-status.md` 和路线设计文档。
- 不写 `TODO`、`TBD`、`待补`、`占位`。
- 不引入仓库中没有依据的新阶段名、新能力名或新完成状态。
- 文档应可直接推送到 GitHub，不依赖本机路径、IDE 状态或会话上下文。

## 9. 验收标准

满足以下条件才算完成：

1. `docs/designs/post-phase-6-status-alignment-design.md` 已创建
2. 新设计文档结构完整，覆盖第 6.1 节要求
3. `docs/current-status.md` 的“当前推荐下一步”顺序已与路线设计一致
4. `Plan.md` 已补入与四个工作包一致的后续路线说明
5. 文档链路核对至少覆盖三项核心能力
6. 没有把未完成事项误写成已完成
7. 没有占位词，没有依赖单机环境的描述

## 10. 建议验证方式

至少做这些自检：

- 搜索占位词：`TODO` / `TBD` / `待补` / `占位`
- 自查是否误改了允许范围之外的文件
- 自查 `Plan.md` 与 `docs/current-status.md` 的工作包顺序是否一致
- 自查是否把“推荐下一步”写成“已完成状态”
- 自查三项核心能力是否都能在文档中找到 design / implementation or guide / verification / current status mention

## 11. 交回 Codex 时必须汇报

完成后请用简短结构汇报：

- 改了什么
- 新增了哪个文件
- `docs/current-status.md` 和 `Plan.md` 分别收口了什么
- 核对了哪三项核心能力的文档链路
- 没有改哪些本不该动的文件
- 做了哪些自检
- 仍有哪些需要 Codex 复核的点

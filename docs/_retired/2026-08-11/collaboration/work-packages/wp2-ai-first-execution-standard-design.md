# WP2：AI-First 执行标准设计

## 1. 目标

为当前阶段补一份正式的“AI-First 执行标准设计”，把仓库里已经存在但分散在 `AGENTS.md`、架构文档和设计模板中的 AI-first 约束收敛成一份统一标准。

本工作包只处理设计文档，不做代码实现，不修改阶段完成状态。

## 2. 当前阶段归属

- 后续路线中的 `WP2`
- 属于“AI-first 规范落地”
- 应在 `WP1` 插件治理增强设计完成后推进

## 3. 必读上下文

执行前请先阅读：

- `AGENTS.md`
- `Plan.md`
- `docs/current-status.md`
- `docs/architecture/componentization-guardrails.md`
- `docs/architecture/ai-metadata-authoring-guide.md`
- `docs/designs/detailed-design-template.md`
- `docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md`
- `docs/designs/plugin-governance-enhancement-design.md`
- `docs/collaboration/current-collaboration-mode.md`

## 4. 允许修改的文件

- 新增：`docs/designs/ai-first-execution-standard-design.md`

本工作包默认不修改其他文件。

## 5. 禁止触碰的范围

- 不修改任何后端代码
- 不修改任何前端代码
- 不修改 `Plan.md`
- 不修改 `docs/current-status.md`
- 不修改 `AGENTS.md`
- 不修改 `docs/architecture/*`
- 不修改现有验证文档
- 不把“当前已有约束”误写成“新引入的实现能力”

## 6. 设计必须覆盖的内容

新设计文档至少要覆盖以下主题：

- 背景
- 目标
- 非目标
- 适用范围
- 核心判断标准
- 文档标准
- 模块与文件边界标准
- 验证标准
- 失败信号与回滚标准
- 与现有 guardrails 的关系
- 落地步骤

## 7. 必须写清的规则

文档中必须明确表达这些规则：

- 新任务默认只聚焦一个模块或一种能力
- 新设计必须包含入口、边界、验证方式、失败信号、回滚思路
- 没有详细设计文档时，不进入正式实现
- 如果某项实现依赖口口相传才能继续，则视为不符合 AI-first
- 正常模块接入优先通过 `Contracts / SDK / templates / controlled entry points`

并且必须明确写出这份最小检查清单：

```md
每份新设计文档至少回答：

1. 入口在哪里
2. 边界在哪里
3. 如何验证
4. 失败信号是什么
5. 如何回滚
```

## 8. 执行要求

- 目标是“统一标准”，不是重写现有所有规则文档。
- 应优先归纳已有约束，不凭空发明新的大范围制度。
- 术语尽量对齐 `AGENTS.md`、`componentization-guardrails.md` 和现有设计文档。
- 不写 `TODO`、`TBD`、`待补` 之类占位。
- 如果某项规则目前只是推荐，不要写成已经被全仓严格执行。
- 设计应能被推送到 GitHub 后直接复用，不能依赖你当前机器或 IDE 会话。

## 9. 验收标准

满足以下条件才算完成：

1. `docs/designs/ai-first-execution-standard-design.md` 已创建
2. 文档结构完整，覆盖第 6 节要求
3. 文档明确写出第 7 节中的所有规则
4. 文档包含“每份新设计文档至少回答”的 5 项检查清单
5. 文档口径与 `AGENTS.md` 和现有 guardrails 不冲突
6. 文档没有占位词，没有把建议写成已落地事实

## 10. 建议验证方式

至少做这些自检：

- 搜索占位词：`TODO` / `TBD` / `待补` / `占位`
- 自查是否误改了允许范围之外的文件
- 自查是否与 `AGENTS.md`、`docs/architecture/componentization-guardrails.md` 冲突
- 自查是否把“规范”写成了“已经落地的实现”
- 自查是否出现依赖本机路径或单机环境的描述

## 11. 交回 Codex 时必须汇报

完成后请用简短结构汇报：

- 改了什么
- 新增了哪个文件
- 文档统一了哪些 AI-first 规则来源
- 没有改哪些本不该动的文件
- 做了哪些自检
- 仍有哪些需要 Codex 复核的点

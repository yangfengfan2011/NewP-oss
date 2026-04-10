# WP1：插件治理增强设计

## 1. 目标

为当前阶段补一份正式的“插件治理增强设计”，把现有插件平台首版闭环之后仍缺失的治理约束写清楚。

本工作包只处理设计文档，不做代码实现，不修改阶段完成状态。

## 2. 当前阶段归属

- 后续路线中的 `WP1`
- 属于“平台治理收口”
- 优先级高于继续扩迁移波次

## 3. 必读上下文

执行前请先阅读：

- `AGENTS.md`
- `Plan.md`
- `docs/current-status.md`
- `docs/designs/post-phase-6-foundation-and-migration-design.md`
- `docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md`
- `docs/architecture/plugin-package-format.md`
- `docs/architecture/plugin-runtime-loading.md`
- `docs/architecture/plugin-compatibility-policy.md`
- `docs/guides/unified-plugin-package-operations.md`
- `docs/collaboration/github-copilot-working-mode.md`

## 4. 允许修改的文件

- 新增：`docs/designs/plugin-governance-enhancement-design.md`

本工作包默认不修改其他文件。

## 5. 禁止触碰的范围

- 不修改任何后端代码
- 不修改任何前端代码
- 不修改 `Plan.md`
- 不修改 `docs/current-status.md`
- 不修改现有验证文档
- 不把“建议路线”改写成“已完成状态”

## 6. 设计必须覆盖的内容

新设计文档至少要覆盖以下主题：

- 背景
- 目标
- 非目标
- 范围
- 设计约束
- 总体方案
- 生命周期与状态模型
- 依赖治理模型
- 卸载、清理与回滚语义
- 失败信号与验证
- 风险与回滚思路
- 分阶段实施建议

## 7. 必须写清的约束

文档中必须明确表达这些规则：

- Host 和 Shell 不新增模块私有接入特例
- 外部插件开发仍通过 `Contracts / SDK / templates / controlled entry points`
- 依赖冲突、版本不兼容、宿主不兼容都必须有明确失败信号
- 卸载要说明对安装目录、状态目录、运行时投影的影响
- 回滚要说明回滚的是“状态”、还是“安装内容 + 投影 + 状态”
- 现有 `install / enable / disable / rollback` 是基础，不等于治理已完成

## 8. 执行要求

- 只写最小必要设计，不扩写无关平台愿景。
- 术语尽量对齐现有插件文档，不随意发明新名词。
- 不写 `TODO`、`TBD`、`待补` 之类占位。
- 如果某项能力当前尚未实现，要写成“当前边界”或“后续建议”，不要写成“已具备”。
- 尽量用仓库内已有事实来支撑设计，不凭空设定不存在的运行时机制。

## 9. 验收标准

满足以下条件才算完成：

1. `docs/designs/plugin-governance-enhancement-design.md` 已创建
2. 文档结构完整，覆盖第 6 节要求
3. 文档明确写出第 7 节中的所有约束
4. 文档没有占位词
5. 文档没有把未完成能力写成已完成
6. 文档口径与现有插件架构文档不冲突

## 10. 建议验证方式

至少做这些自检：

- 搜索占位词：`TODO` / `TBD` / `待补` / `占位`
- 自查是否误改了允许范围之外的文件
- 自查是否把建议路线写成当前状态
- 自查是否出现依赖本机路径或单机环境的描述

## 11. 交回 Codex 时必须汇报

完成后请用简短结构汇报：

- 改了什么
- 新增了哪个文件
- 文档覆盖了哪些治理主题
- 没有改哪些本不该动的文件
- 做了哪些自检
- 仍有哪些需要 Codex 复核的点

# 当前文档入口

本目录只把任务路由到仍有效的规则、Context、ADR 和操作指南。当前事实仍以源码、配置、测试及已确认的 GitHub issue/spec 为准；文档与它们冲突时先核实，不用文档覆盖现状。

## 任务路由

- Issue、规格和分流：[issue-tracker.md](agents/issue-tracker.md)、[triage-labels.md](agents/triage-labels.md)
- 领域术语与 Context：[domain.md](agents/domain.md)、[../CONTEXT-MAP.md](../CONTEXT-MAP.md)
- 交付和验收：[delivery-gates.md](agents/delivery-gates.md)、[quick-verification.md](verification/quick-verification.md)
- Host、Shell、Contracts、SDK 或模块接入：[componentization-guardrails.md](architecture/componentization-guardrails.md)
- 架构导航：[architecture.md](architecture/architecture.md)
- 本地初始化、连接和启动：[local-environment.md](guides/local-environment.md)

## 当前开发指南

- 内建模块：[componentized-development.md](developer/componentized-development.md)
- 五分钟接入：[componentized-development-quickstart.md](developer/componentized-development-quickstart.md)
- 前端 binding：[module-integration-guide.md](../frontend/docs/module-integration-guide.md)
- 外部声明式插件：[external-plugin-development.md](developer/external-plugin-development.md)
- 持久化与 migration stream：[persistence-development-workflow.md](developer/persistence-development-workflow.md)
- 模型 DTO 同步：[model-dto-sync-usage-guide.md](developer/model-dto-sync-usage-guide.md)
- AI 代码生成：[code-quality-standard.md](developer/code-quality-standard.md)
- 代码注释：[code-commenting-guidelines.md](developer/code-commenting-guidelines.md)
- 审查清单：[codex-review-checklist.md](collaboration/codex-review-checklist.md)

## 稳定架构契约

- [module-contract.md](architecture/module-contract.md)
- [module-template.md](architecture/module-template.md)
- [frontend-plugin-loading.md](architecture/frontend-plugin-loading.md)
- [componentization-guardrails.md](architecture/componentization-guardrails.md)
- [CONTEXT-MAP.md](../CONTEXT-MAP.md) 下的 Context 文档
- [adr/](adr/) 下未被后续 ADR 取代的决策

## 文档状态边界

`docs/_retired/` 集中了旧阶段设计、计划、提示词、迁移材料、验证记录和已失效指南。该目录可以整体删除，不是当前开发依据；普通检索、评审和上下文收集应排除它。

新增内容按以下规则放置：

- 任务目标与验收进入 GitHub issue/spec，或进入已调用 skill 明确要求的产物。
- 长期架构决策进入 Context/ADR；只在确有稳定导航价值时新增架构说明。
- 可复跑操作进入短指南，并优先引用脚本、配置和 `--help`，不复制可直接读取的实现清单。
- 一次性计划、阶段总结和验证流水不再进入当前文档树。

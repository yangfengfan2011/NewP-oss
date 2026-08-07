# 文档总览

`docs/` 已按职责重组为以下分类：

## 1. architecture

长期有效的架构、范围、契约与模板文档。

- [architecture/architecture.md](architecture/architecture.md)
- [architecture/scope-v1.md](architecture/scope-v1.md)
- [architecture/module-contract.md](architecture/module-contract.md)
- [architecture/module-boundaries-v1.md](architecture/module-boundaries-v1.md)
- [architecture/platform-sdk.md](architecture/platform-sdk.md)
- [architecture/module-template.md](architecture/module-template.md)
- [architecture/ai-first-execution-standard.md](architecture/ai-first-execution-standard.md)
- [architecture/ai-first-design-check-targets.txt](architecture/ai-first-design-check-targets.txt)

## 2. phases（历史）

旧阶段沉淀的计划、总结和实施材料，不再表示当前路线图。

- [phases/phase-2-minimum-runnable-plan.md](phases/phase-2-minimum-runnable-plan.md)
- [phases/phase-3-extension-contracts-summary.md](phases/phase-3-extension-contracts-summary.md)
- [phases/scaffold-summary.md](phases/scaffold-summary.md)
- [phases/post-phase-6-stage-map.md](phases/post-phase-6-stage-map.md)（历史阶段图）

## 3. modules

各模块边界、实现说明与模块级文档。

- [modules/identity-lite-module-boundary-v1.md](modules/identity-lite-module-boundary-v1.md)
- [modules/authorization-module-boundary-v1.md](modules/authorization-module-boundary-v1.md)
- [modules/configcenter-module-boundary-v1.md](modules/configcenter-module-boundary-v1.md)
- [modules/audit-module-boundary-v1.md](modules/audit-module-boundary-v1.md)
- [modules/authorization-implementation.md](modules/authorization-implementation.md)
- [modules/configcenter-implementation.md](modules/configcenter-implementation.md)
- [modules/audit-implementation.md](modules/audit-implementation.md)
- [modules/files-implementation.md](modules/files-implementation.md)

## 4. designs（历史）

既有历史详细设计文档与设计模板。新任务的设计产物遵守 `AGENTS.md`、当前 GitHub issue/spec 或已调用 skill 的契约。

- [designs/README.md](designs/README.md)
- [designs/detailed-design-template.md](designs/detailed-design-template.md)

## 5. migration（历史）

旧平台资产盘点、迁移策略与模板，仅用于明确迁移或兼容任务的历史取证，不作为 NewP 的设计来源。

- [migration/oldp-asset-inventory-v1.md](migration/oldp-asset-inventory-v1.md)
- [migration/oldp-asset-disposition-v1.md](migration/oldp-asset-disposition-v1.md)
- [migration/migration-strategy.md](migration/migration-strategy.md)
- [migration/metadata-migration-strategy.md](migration/metadata-migration-strategy.md)
- [migration/asset-inventory-template.md](migration/asset-inventory-template.md)
- [migration/asset-disposition-matrix.md](migration/asset-disposition-matrix.md)

## 6. verification

构建、阶段验收、快速验证与 Demo 贯通验证。

- [verification/quick-verification.md](verification/quick-verification.md)
- [verification/scaffold-verification.md](verification/scaffold-verification.md)
- [verification/demo-module-verification.md](verification/demo-module-verification.md)
- [verification/phase-3-contract-verification.md](verification/phase-3-contract-verification.md)
- [verification/ai-first-execution-standard-phase-1-verification.md](verification/ai-first-execution-standard-phase-1-verification.md)
- [verification/ai-first-execution-standard-phase-2-verification.md](verification/ai-first-execution-standard-phase-2-verification.md)
- [verification/ai-first-execution-standard-phase-3-verification.md](verification/ai-first-execution-standard-phase-3-verification.md)
- [verification/ai-first-execution-standard-phase-4-verification.md](verification/ai-first-execution-standard-phase-4-verification.md)
- [verification/ai-first-execution-standard-phase-5-verification.md](verification/ai-first-execution-standard-phase-5-verification.md)
- [verification/ai-first-execution-standard-phase-6-verification.md](verification/ai-first-execution-standard-phase-6-verification.md)
- [verification/ai-first-execution-standard-phase-7-verification.md](verification/ai-first-execution-standard-phase-7-verification.md)
- [verification/verification-matrix.md](verification/verification-matrix.md)（历史验证索引）

## 7. developer

面向开发者的开发、接入与扩展指南。

- [developer/componentized-development-quickstart.md](developer/componentized-development-quickstart.md)
- [developer/componentized-development.md](developer/componentized-development.md)
- [developer/componentized-example-module.md](developer/componentized-example-module.md)
- [developer/code-commenting-guidelines.md](developer/code-commenting-guidelines.md)
- [developer/external-plugin-development.md](developer/external-plugin-development.md)
- [developer/frontend-plugin-integration.md](developer/frontend-plugin-integration.md)
- [developer/persistence-public-api.md](developer/persistence-public-api.md)
- [developer/persistence-usage-guide.md](developer/persistence-usage-guide.md)
- [developer/persistence-development-workflow.md](developer/persistence-development-workflow.md)

## 8. guides

面向运行、环境、运维与协作辅助说明。

- [guides/local-environment.md](guides/local-environment.md)
- [guides/local-multi-db-container-setup.md](guides/local-multi-db-container-setup.md)
- [guides/2026-04-09-local-multi-db-runtime-record.md](guides/2026-04-09-local-multi-db-runtime-record.md)
- [guides/plugin-packaging.md](guides/plugin-packaging.md)
- [guides/plugin-upgrade-and-rollback.md](guides/plugin-upgrade-and-rollback.md)
- [guides/unified-plugin-package-operations.md](guides/unified-plugin-package-operations.md)
- [guides/external-plugin-debugging.md](guides/external-plugin-debugging.md)
- [guides/subagents-guide.md](guides/subagents-guide.md)
- [guides/worktree-guide.md](guides/worktree-guide.md)

## 9. collaboration

面向审查清单、历史工作包和协作资料的文档区。

- [collaboration/codex-review-checklist.md](collaboration/codex-review-checklist.md)
- [collaboration/work-packages/wp1-plugin-governance-design.md](collaboration/work-packages/wp1-plugin-governance-design.md)
- [collaboration/work-packages/wp2-ai-first-execution-standard-design.md](collaboration/work-packages/wp2-ai-first-execution-standard-design.md)
- [collaboration/work-packages/wp2-ai-first-execution-standard-closeout.md](collaboration/work-packages/wp2-ai-first-execution-standard-closeout.md)
- [collaboration/work-packages/wp3-post-phase-6-status-alignment.md](collaboration/work-packages/wp3-post-phase-6-status-alignment.md)
- [collaboration/work-packages/wp4-stage-8-admission-review.md](collaboration/work-packages/wp4-stage-8-admission-review.md)
- [prompts/](prompts/)
- [bootstrap/](bootstrap/)

历史协作资料已移入：

- [history/collaboration/README.md](history/collaboration/README.md)

## 10. superpowers（历史）

旧 Superpowers 工作流生成的设计规格与实施计划，仅用于追溯历史决策和验证证据，不再接收新任务产物。

- [superpowers/specs/](superpowers/specs/)
- [superpowers/plans/](superpowers/plans/)

## 11. 当前推荐入口

- 仓库入口：[../README.md](../README.md)
- 仓库规则：[../AGENTS.md](../AGENTS.md)
- Issue 与规格约定：[agents/issue-tracker.md](agents/issue-tracker.md)
- 分流标签：[agents/triage-labels.md](agents/triage-labels.md)
- 领域文档约定：[agents/domain.md](agents/domain.md)
- 组件化开发超短版：[developer/componentized-development-quickstart.md](developer/componentized-development-quickstart.md)
- 代码注释规则：[developer/code-commenting-guidelines.md](developer/code-commenting-guidelines.md)
- 架构总览：[architecture/architecture.md](architecture/architecture.md)
- 组件化开发入口：[developer/componentized-development.md](developer/componentized-development.md)
- 组件化 Example：[developer/componentized-example-module.md](developer/componentized-example-module.md)
- 本地开发环境：[guides/local-environment.md](guides/local-environment.md)
- 历史状态归档：[current-status.md](current-status.md)
- 历史阶段图：[phases/post-phase-6-stage-map.md](phases/post-phase-6-stage-map.md)
- 历史 Superpowers 文档：[superpowers/README.md](superpowers/README.md)

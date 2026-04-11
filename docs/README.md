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

## 2. phases

按阶段沉淀的计划、总结、阶段实施材料。

- [phases/phase-2-minimum-runnable-plan.md](phases/phase-2-minimum-runnable-plan.md)
- [phases/phase-3-extension-contracts-summary.md](phases/phase-3-extension-contracts-summary.md)
- [phases/scaffold-summary.md](phases/scaffold-summary.md)

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

## 4. designs

实施前必须存在的详细设计文档。

- [designs/README.md](designs/README.md)
- [designs/detailed-design-template.md](designs/detailed-design-template.md)

## 5. migration

旧平台资产盘点、迁移策略与模板。

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
- [verification/verification-matrix.md](verification/verification-matrix.md)

## 7. developer

面向开发者的开发、接入与扩展指南。

- [developer/componentized-development-quickstart.md](developer/componentized-development-quickstart.md)
- [developer/componentized-development.md](developer/componentized-development.md)
- [developer/componentized-example-module.md](developer/componentized-example-module.md)
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

面向 Codex / Claude / GitHub Copilot 协作流程的执行手册。

- [collaboration/current-collaboration-mode.md](collaboration/current-collaboration-mode.md)
- [collaboration/claude-code-第二阶段执行手册.md](collaboration/claude-code-第二阶段执行手册.md)
- [collaboration/github-copilot-working-mode.md](collaboration/github-copilot-working-mode.md)
- [collaboration/post-phase-6-copilot-stage-map.md](collaboration/post-phase-6-copilot-stage-map.md)
- [collaboration/codex-review-checklist.md](collaboration/codex-review-checklist.md)
- [collaboration/work-packages/wp1-plugin-governance-design.md](collaboration/work-packages/wp1-plugin-governance-design.md)
- [collaboration/work-packages/wp2-ai-first-execution-standard-design.md](collaboration/work-packages/wp2-ai-first-execution-standard-design.md)
- [collaboration/work-packages/wp2-ai-first-execution-standard-closeout.md](collaboration/work-packages/wp2-ai-first-execution-standard-closeout.md)
- [collaboration/work-packages/wp3-post-phase-6-status-alignment.md](collaboration/work-packages/wp3-post-phase-6-status-alignment.md)
- [prompts/](prompts/)
- [bootstrap/](bootstrap/)

## 10. superpowers

设计规格与实施计划沉淀。

- [superpowers/specs/](superpowers/specs/)
- [superpowers/plans/](superpowers/plans/)

## 11. 当前推荐入口

- 项目现状：[current-status.md](current-status.md)
- 仓库入口：[../README.md](../README.md)
- 组件化开发超短版：[developer/componentized-development-quickstart.md](developer/componentized-development-quickstart.md)
- 架构总览：[architecture/architecture.md](architecture/architecture.md)
- 组件化开发入口：[developer/componentized-development.md](developer/componentized-development.md)
- 组件化 Example：[developer/componentized-example-module.md](developer/componentized-example-module.md)
- 详细设计入口：[designs/README.md](designs/README.md)
- 本地开发环境：[guides/local-environment.md](guides/local-environment.md)
- 当前协作模式：[collaboration/current-collaboration-mode.md](collaboration/current-collaboration-mode.md)
- GitHub Copilot 协作入口：[collaboration/github-copilot-working-mode.md](collaboration/github-copilot-working-mode.md)

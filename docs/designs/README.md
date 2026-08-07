# 历史详细设计文档

本目录保留既有详细设计与模板，供追溯历史决策。新任务不再向本目录或 `docs/superpowers/` 写入设计与计划，当前产物遵守 `AGENTS.md`、GitHub issue/spec、用户指令或已调用 skill 的契约。

## 目录分层

本目录中的既有设计文档按以下子目录分类：

- `platform/`
  - 跨模块、跨能力的平台级详细设计
- `modules/`
  - 具体模块或试点主线的详细设计
- `tooling/`
  - 工具链、脚本、CI/CD、工程韧性相关设计
- `roadmap/`
  - 阶段规划、阶段收口、阶段路线图相关设计

## 使用边界

- 本目录中的文档描述当时的目标、约束和取舍，不自动代表当前源码状态或当前产品方向。
- 追溯历史时应同时核对对应提交、当前源码与测试；发生冲突时，以 `AGENTS.md` 定义的当前事实源和决策源为准。
- 新的长期架构或开发规范应进入 `docs/architecture/`、`docs/developer/` 或上下文 ADR，不通过修改历史设计来建立当前规则。
- [detailed-design-template.md](detailed-design-template.md) 仅用于理解旧文档结构，不再是新任务的默认模板。

## 历史设计文档

- [identity-lite-design.md](identity-lite-design.md)
- [authorization-design.md](authorization-design.md)
- [configcenter-design.md](configcenter-design.md)
- [metadata-config-center-design.md](metadata-config-center-design.md)
- [frontend-ui-component-strategy-design.md](frontend-ui-component-strategy-design.md)
- [post-phase-6-foundation-and-migration-design.md](post-phase-6-foundation-and-migration-design.md)
- [user-visible-text-localization-foundation-design.md](user-visible-text-localization-foundation-design.md)
- [platform/metadata-driven-backoffice-ui-design.md](platform/metadata-driven-backoffice-ui-design.md)
- [platform/dic-localization-manifest-alignment-design.md](platform/dic-localization-manifest-alignment-design.md)
- [platform/dic-local-key-dotless-consistency-review.md](platform/dic-local-key-dotless-consistency-review.md)
- [platform/standard-crud-contract-design.md](platform/standard-crud-contract-design.md)
- [platform/form-action-area-comparison-design.md](platform/form-action-area-comparison-design.md)
- [platform/list-management-layout-demo-design.md](platform/list-management-layout-demo-design.md)
- [platform/model-truth-development-schema-sync-design.md](platform/model-truth-development-schema-sync-design.md)
- [modules/organization-person-center-pilot-design.md](modules/organization-person-center-pilot-design.md)

# 详细设计文档

本目录用于存放“实施前必须存在”的详细设计文档。

## 目录分层

为避免 `docs/designs/` 长期平铺失控，后续新增设计文档默认按以下子目录分类：

- `platform/`
  - 跨模块、跨能力的平台级详细设计
- `modules/`
  - 具体模块或试点主线的详细设计
- `tooling/`
  - 工具链、脚本、CI/CD、工程韧性相关设计
- `roadmap/`
  - 阶段规划、阶段收口、阶段路线图相关设计

默认规则：

- 详细设计仍然统一放在 `docs/designs/` 之下，不新增新的 `docs/` 顶层目录。
- 新文档从现在开始按子目录分类落位，旧文档不要求一次性迁移。
- 旧文档在后续被修改时，可顺手迁移到更合适的子目录。
- 当某份设计稳定为长期规范后，可再提炼到 `docs/architecture/` 或 `docs/developer/`。

## 规则

- 任何新的功能、模块、阶段性改造、行为变更，在正式实现前都必须先有详细设计文档。
- 没有详细设计文档时，只允许做信息收集、问题排查、方案讨论和设计草稿，不进入正式实现。
- 详细设计应在方案达成一致后落盘，并在实现过程中持续维护。

## 推荐命名

- 模块设计：`<module-name>-design.md`
- 能力设计：`<capability-name>-design.md`
- 阶段设计：`phase-<n>-<topic>-design.md`

示例：

- `identity-lite-design.md`
- `authorization-design.md`
- `configcenter-import-design.md`
- `platform/metadata-driven-backoffice-ui-design.md`
- `modules/organization-person-center-pilot-design.md`

## 应包含的内容

- 背景与目标
- 范围与非目标
- 设计约束
- 关键模型
- 核心流程
- 接口与集成点
- 数据与状态
- 错误处理与风险
- 验证方案
- 未决问题

## 模板

请优先从模板开始：

- [detailed-design-template.md](detailed-design-template.md)

如果需要快速创建一份新设计文档，可使用：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\new-design-doc.ps1 -DesignName <your-design-name>
```

新增设计文档还应同时满足：

- 明确回答 AI-first 最小检查清单中的 5 项必答项：入口、边界、验证、失败信号、回滚
- 不保留 `TODO`、`TBD`、`待补`、`占位` 这类占位词

建议在提交前运行定向校验：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\<subdir>\<your-design>.md
```

如果这份设计文档需要进入 AI-first 受控门禁，可登记到显式目标清单：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\register-ai-first-design-target.ps1 -DesignPath .\docs\designs\<subdir>\<your-design>.md
```

## 当前设计文档

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

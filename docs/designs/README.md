# 详细设计文档

本目录用于存放“实施前必须存在”的详细设计文档。

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

## 当前设计文档

- [identity-lite-design.md](identity-lite-design.md)
- [authorization-design.md](authorization-design.md)
- [configcenter-design.md](configcenter-design.md)
- [frontend-ui-component-strategy-design.md](frontend-ui-component-strategy-design.md)
- [post-phase-6-foundation-and-migration-design.md](post-phase-6-foundation-and-migration-design.md)

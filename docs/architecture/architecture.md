# 架构导航

本文只描述当前稳定边界和进一步阅读入口，不维护阶段状态或模块完成清单。

## 组合关系

- Host 负责启动、认证接入、模块加载、数据库 migration 唯一执行入口和宿主级横切能力。
- Shell 负责布局、全局 Provider，以及从活动模块运行时目录装配菜单、路由和前端实现。
- Module Capsule 拥有模块身份、版本和依赖；metadata 拥有静态声明；C# 与 TypeScript 只提供 behavior binding。
- Kernel 编译、校验并投影 Canonical Module Plan；冲突或引用缺失会拒绝整份候选，不按加载顺序覆盖。
- Plugin Platform 管理外部插件的校验、期望状态、启动期激活和活动快照，但不直接执行数据库 migration。
- Platform Persistence 把同一候选计划编译为共享运行时模型和模块自有 migration streams，并通过 Host 维护统一 EF Migration History、执行完整 Up 和完成只读一致性检查。
- 内建模块通过 Contracts、SDK 和平台抽象协作，不依赖其他模块的私有实现。

## 事实入口

- 领域关系与术语：[CONTEXT-MAP.md](../../CONTEXT-MAP.md)
- 组件化边界：[componentization-guardrails.md](componentization-guardrails.md)
- Module Capsule 契约：[module-contract.md](module-contract.md)
- 模块模板：[module-template.md](module-template.md)
- 前端装配：[frontend-plugin-loading.md](frontend-plugin-loading.md)
- ADR：[../adr/](../adr/)
- 当前开发指南：[../developer/](../developer/)

源码位置：

```text
apps/host-api/                       Host 组合与启动
server/kernel/                       模块计划、包和运行时投影内核
server/plugin-platform/              外部插件生命周期
server/platform-persistence*/        持久化计划、EF 与 provider adapter
modules/<ModuleId>/                  Capsule 与 metadata authoring 真源
server/modules/<ModuleId>/           后端行为实现
frontend/packages/shell/             Shell 与活动目录消费
frontend/modules/<module-id>/        前端行为实现
```

架构或公共契约变化必须先有已确认设计工件；实现完成后同步更新唯一对应的 Context 或 ADR，不另写阶段性“现状副本”。

---
status: accepted
---

# Host 是完整 Up Migration 的唯一执行入口

> 实现状态：决策已确认。Provider Target/Transition 精确校验、数据前置检查、阻断性 history/frontier reconciliation 与完整 Up 执行已实施；移除 DevSchemaSync 写入口仍在后续子 Issue 中实施。在父 Issue 完成前，当前运行行为仍以源码和配置为准。

NewP 当前尚未接入需要独立发布保护的真实生产数据库。继续同时维护 `safe-expand` 限制、开发态直连结构同步工具和 Host 启动迁移，会形成多个数据库结构写入入口，并过早引入环境分支。平台决定由 Host 作为唯一受支持的 Migration 执行入口：所有环境默认沿同一条 Host 启动链路，根据已编译持久化计划和统一 EF Migration History 执行尚未应用的完整 `Up`，包括 Drop、Rename、Alter 等删除、重命名和结构修改操作，不再按开发/发布环境或操作风险类别切换默认行为。

## 边界与保留不变量

- 模块继续拥有自己的 model 元数据和不可变线性 migration stream；现有 model 元数据结构及其 authoring 真源地位不变。Host 不拥有模块业务模型，也不自行发现、补写或排序模块迁移。
- “完整 `Up`”表示执行已确认迁移中面向目标 frontier 的全部 EF Up 操作。Migration 可以定义 `Down`，但 Host 不自动执行 `Down`，也不通过 metadata diff 或临时 SQL 修复数据库。
- 完整操作集不绕过平台校验。Provider、模块归属、全局唯一 Migration ID、线性迁移链、bundle/content/model/from-to digest、期望 frontier 和程序集完整性必须在 DDL 前通过；无法证明归属的操作仍然拒绝。
- 所有模块迁移流和 Platform Foundation Stream 共用一份由 Platform Persistence 维护的 EF Migration History。模块不得创建私有 history，也不得绕过 Host 回写迁移事实。
- `dbo.__EFMigrationsHistory` 继续记录全局执行顺序；每个 migration 同一事务写入 `dbo.__NewPMigrationHistory` 的版本化 owner、step-scoped bundle membership、content、model/from-to、全局前缀、Provider Target/Transition 事实。SQL Server 只接受空 catalog 或由当前已编译计划创建的有效 history 前缀；旧 history 和预建业务结构在新增 DDL 前拒绝，不回填或改写。
- history `BundleDigest` 是从 owner/provider 与不可变 migration step 链输入计算的稳定 membership fact，不是会随 assembly 追加 migration 而变化的 whole-assembly `BundleChecksum`；后者仍在候选计划编译及连接前严格校验当前 artifact 完整性。
- history 的 Provider Target/Transition fact 沿用 #43 的 v1 canonical 摘要算法，但只投影 migration owner 的完整表结构；owner 表上的列、key、index、FK 及 FK principal 物理引用全部保留。该投影仅保证追加其他 owner 时旧历史稳定，不替代连接前对当前候选 full Provider Target/Transition 的精确 operation 校验。
- 每个 migration 的 DDL 与对应 history 写入必须处于同一事务；不能参与事务的命令不得进入该执行链。全局迁移锁继续串行化并发 Host 的 schema 写入。
- Plugin Platform 可以贡献候选模型和 migration stream，但不能直接执行迁移。迁移成功也不等于候选插件已经激活，活动快照仍由 Plugin Platform 原子提交。

## 一致性检查与环境策略

`DevSchemaSync` CLI 及其直接 SQL apply 路径在实施时移除。Host 只保留启动期必要的只读一致性检查，用于核对统一 history、迁移 frontier、模型摘要和实际 schema；该检查在所有环境遵循同一语义，不生成或执行修复 DDL，也不是第二个迁移入口。

当前不区分开发与发布迁移策略。首个需要受支持运维的真实生产数据库出现时，必须通过新的已确认设计增加显式安全模式，并写清启用条件、备份/恢复前提、授权流程和失败处理；不得通过环境名隐式改变迁移语义。

## 失败、恢复与验收

- history 不是已编译迁移链的有效前缀、数据库超前于目标 frontier、摘要或归属不一致时，Host 必须在新增 DDL 前失败并保持 Not Ready，不自动降级、回退或改写 history。
- 某个 migration 失败时，该 migration 的 DDL 与 history 写入一并回滚；此前已经提交的 migration 不自动回退。
- 如果迁移已经提交而后续候选激活失败，Host 不得假定旧活动快照仍与数据库兼容。系统保持 Not Ready，恢复方式是发布修正后的前向 migration，或使用已验证的数据库恢复流程，而不是自动执行 `Down`。
- 实施验收必须覆盖空数据库、由当前计划创建的有效 history 前缀、包含 Drop/Rename/Alter 的待执行 Up、并发迁移锁、失败重试、history/frontier/digest 不一致、Host 重启，以及只读一致性检查不会产生 schema 写入。

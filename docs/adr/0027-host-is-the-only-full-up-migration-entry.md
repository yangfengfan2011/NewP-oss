---
status: accepted
---

# Host 是完整 Up Migration 的唯一执行入口

> 实现状态：Provider Target/Transition 精确校验、数据前置检查、阻断性 history/frontier reconciliation、完整 Up 执行以及 migration 后只读一致性检查均已实施；父 Issue 的终局真实验收仍由后续 Acceptance Gate 完成。

NewP 当前尚未接入需要独立发布保护的真实生产数据库。继续同时维护 `safe-expand` 限制、开发态直连结构同步工具和 Host 启动迁移，会形成多个数据库结构写入入口，并过早引入环境分支。平台决定由 Host 作为唯一受支持的 Migration 执行入口：所有环境默认沿同一条 Host 启动链路，根据已编译持久化计划和统一 EF Migration History 执行尚未应用的完整 `Up`，包括 Drop、Rename、Alter 等删除、重命名和结构修改操作，不再按开发/发布环境或操作风险类别切换默认行为。

## 边界与保留不变量

- 模块继续拥有自己的 model 元数据和线性 migration stream；同一已确认候选中的迁移事实不可变，兼容基线冻结后的已发布 migration 不可改写。实验基线期的整条 authoring 基线替换由 [ADR-0042](0042-development-uses-a-resettable-migration-baseline.md) 约束，不允许 Host 自行发现、补写或排序模块迁移。
- “完整 `Up`”表示执行已确认迁移中面向目标 frontier 的全部 EF Up 操作。Migration 可以定义 `Down`，但 Host 不自动执行 `Down`，也不通过 metadata diff 或临时 SQL 修复数据库。
- 完整操作集不绕过平台校验。Provider、模块归属、全局唯一 Migration ID、线性迁移链、bundle/content/model/from-to digest、期望 frontier 和程序集完整性必须在 DDL 前通过；无法证明归属的操作仍然拒绝。
- 所有模块迁移流和 Platform Foundation Stream 共用一份由 Platform Persistence 维护的 EF Migration History。模块不得创建私有 history，也不得绕过 Host 回写迁移事实。
- `dbo.__EFMigrationsHistory` 保存全局唯一的已应用 MigrationId 集合；每个 migration 同一事务写入 `dbo.__NewPMigrationHistory` 的版本化 owner、step-scoped bundle membership、content、model/from-to、实际提交 Position/前缀 PlanDigest、Provider Target/Transition 事实。按 [ADR-0055](0055-component-composition-separates-model-identity-and-deployment.md)，各 owner 的已应用步骤必须是自身 manifest Sequence 的线性前缀，统一账本按 Position 保存实际提交顺序，当前候选不得按 ID 重排旧事实。候选在各 owner 的队列头之间按 ID 确定执行顺序，不反转 owner 内 Sequence；首次启用或恢复的较早 ID 可在其他 owner 已提交步骤后追加。SQL Server 只接受空 catalog 或当前计划可严格验证的 owner 前缀；旧基线、未知 history 和预建业务结构在新增业务 DDL 前拒绝，不补写、放宽摘要或改写旧事实。业务行的一次性 Required 字段回填必须另行满足 [ADR-0043](0043-required-fields-use-explicit-write-and-migration-lifecycles.md)。
- history `BundleDigest` 是从 owner/provider 与不可变 migration step 链输入计算的稳定 membership fact，不是会随 assembly 追加 migration 而变化的 whole-assembly `BundleChecksum`；后者仍在候选计划编译及连接前严格校验当前 artifact 完整性。
- history 的 Provider Target/Transition fact 沿用 #43 的 v1 canonical 摘要算法，但只投影 migration owner 的完整表结构；owner 表上的列、key、index、FK 及 FK principal 物理引用全部保留。该投影仅保证追加其他 owner 时旧历史稳定，不替代连接前对当前候选 full Provider Target/Transition 的精确 operation 校验。
- 每个 migration 的结构或受限数据操作与对应 history 写入必须处于同一事务；不能参与事务的命令不得进入该执行链。全局迁移锁继续串行化并发 Host 的迁移写入。
- Plugin Platform 可以贡献候选模型和 migration stream，但不能直接执行迁移。迁移成功也不等于候选插件已经激活，活动快照仍由 Plugin Platform 原子提交。

## 一致性检查与环境策略

Host 外结构同步工具及直接 SQL apply 路径已移除。Host 只保留启动期必要的只读一致性检查，用于核对统一 history、迁移 frontier、模型摘要、Provider Target 和实际 schema；该检查在所有环境遵循同一语义，不生成或执行修复 DDL，也不是第二个迁移入口。

迁移执行语义不因开发或发布环境而改变；所有环境仍通过同一 Host seam 执行并验证完整 `Up`。当前实验基线期与未来兼容基线冻结后的差异只属于 authoring 和数据库生命周期，按 [ADR-0042](0042-development-uses-a-resettable-migration-baseline.md) 显式决定，不得通过环境名隐式放松迁移校验。首个需要受支持运维的真实数据库进入范围前，还必须用新的已确认设计写清备份/恢复前提、发布授权和失败处理。

## 失败、恢复与验收

- 任一 owner 的 history 不是其已编译迁移链的有效前缀、实际 Position 不连续、实际前缀摘要被改写、数据库超前于目标 frontier、摘要或归属不一致时，Host 必须在新增业务 DDL 前失败并保持 Not Ready，不自动降级、回退或改写 history。
- 某个 migration 失败时，该 migration 的 DDL 与 history 写入一并回滚；此前已经提交的 migration 不自动回退。
- 如果迁移已经提交而后续候选激活失败，Host 不得假定旧活动快照仍与数据库兼容。系统保持 Not Ready，恢复方式是发布修正后的前向 migration，或使用已验证的数据库恢复流程，而不是自动执行 `Down`。
- 实施验收必须覆盖空数据库、由当前计划创建的有效 history 前缀、包含 Drop/Rename/Alter 的待执行 Up、并发迁移锁、失败重试、history/frontier/digest 不一致、Host 重启，以及只读一致性检查不会产生 schema 写入。

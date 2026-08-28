---
status: accepted
---

# 开发期使用可重建迁移基线直到兼容基线冻结

NewP 尚未接入必须保留数据与 migration history 的受支持数据库，当前处于“实验基线期”。在兼容基线明确冻结前，仓库只承诺当前 SQL Server 空数据库基线；当 Model、持久化不变量或安全语义变化时，可以把受影响模块的现行 migration、manifest、摘要、snapshot 与 frontier 作为同一个候选整体替换。使用被替换基线的开发 catalog 不做原位升级，必须在确认数据可丢弃后显式重建为空 catalog，再由 Host 从零执行当前完整 `Up`。

实验基线期的默认选择仍是重建无保留价值的数据，但这不禁止当前有效 history 前缀通过追加迁移演进。任务明确需要保留当前有效前缀中的数据，且模块能给出语义正确的旧行处理方式时，可以按 [ADR-0043](0043-required-fields-use-explicit-write-and-migration-lifecycles.md) 使用 Required 字段的有序前向迁移；这不构成对已被替换基线或任意旧开发数据的兼容承诺。不得只为保存实验数据扩张通用 migration data-transform 或 table-replacement interface。

基线替换是 authoring 与数据库生命周期策略，不是迁移执行绕过。Host 仍是唯一受支持的 schema migration 执行入口，模块归属、线性顺序、Provider Target/Transition、摘要、事务、迁移锁、统一 history 与只读一致性检查保持严格；Host 不得因为 `Development` 环境或任何迁移失败自动删除 catalog。开发库重建必须针对精确 catalog，先验证 NewP ownership marker 与 repository/worktree identity，并排除显式保护、验收证据或来源不明的数据库；重建完成后的账号与测试数据通过正式 bootstrap 或 fixture 恢复。

“兼容基线冻结”是一个必须由独立设计工件明确确认的发布里程碑，不从环境名、分支名或首次部署自动推断。冻结后，已发布 migration ID、内容与历史事实不可改写，数据库只能通过追加的前向 migration 演进；届时还必须另行确认数据保留、备份恢复、发布授权和失败处理。无论是否冻结，当前都不支持数据库永久 `DEFAULT`；一次性迁移回填不能转化为新记录的长期数据库兜底。

---
status: accepted
---

# SQL Server 从空数据库重建当前迁移基线

NewP 当前没有需要保留的受支持数据库、业务数据或 migration history，因此实验基线期只支持从空 SQL Server catalog 启动。空 catalog 可以包含 NewP ownership marker，但不得包含业务表或 migration history。仓库可以按 [ADR-0042](0042-development-uses-a-resettable-migration-baseline.md) 把既有 SQL Server migration 文件、ID、摘要、snapshot 与 frontier 作为当前基线整体替换，不提供被替换旧基线的开发数据库升级兼容；发现旧 history、已有平台业务表或其他非空业务结构时，Host 必须在新增 DDL 前以稳定失败码拒绝，并要求显式删除、重建数据库，不得自动删除、迁移或回填。

Host 仍是唯一 schema migration 写入口，使用完整 EF `Up`、统一 history、事务和全局迁移锁创建当前 Provider Target。`platform.foundation` 是 SQL Server 的平台基础迁移流，从零摘要创建当前尚无模块 migration bundle 的 Authorization 与 ConfigCenter；Organization、Identity 和 Authentication 各自拥有从零到当前 target 的初始迁移。各 migration stream 的 ID、content/model/from-to digest、bundle 与 frontier 必须确定且精确，warm start 只能发生在当前基线的有效 history 前缀上；已被替换的基线必须稳定要求重建。

基线重建本身不得携带业务数据回填、原始 DML、`SqlOperation`、自动 `Down` 或 Host 外 schema migration 写入口。若任务选择保留当前有效 history 前缀并追加 Required 字段迁移，则按 [ADR-0043](0043-required-fields-use-explicit-write-and-migration-lifecycles.md) 处理，不属于本 ADR 的基线重建。当前两条路径都不允许数据库永久 `DEFAULT`。catalog 删除与重建属于显式数据库生命周期操作，必须核对归属并排除受保护数据库，不能由 Host 启动或环境名隐式触发。业务 audit 列的可空性必须与当前 EF/read 契约一致。Oracle/MySQL 基线重建不属于本决策范围。

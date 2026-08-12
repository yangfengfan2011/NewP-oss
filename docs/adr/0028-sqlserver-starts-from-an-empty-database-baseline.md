---
status: accepted
---

# SQL Server 从空数据库重建当前迁移基线

NewP 当前没有需要保留的实际数据库、业务数据或 migration history，因此本阶段只支持从空 SQL Server catalog 启动。空 catalog 可以包含 NewP ownership marker，但不得包含业务表或 migration history。仓库直接替换既有 SQL Server migration 文件、ID、摘要、snapshot 与旧切换清单，不提供旧数据库升级兼容；发现旧 history、已有平台业务表或其他非空业务结构时，Host 必须在新增 DDL 前以稳定失败码拒绝，并要求删除、重建数据库，不得自动删除、迁移或回填。

Host 仍是唯一结构写入口，使用完整 EF `Up`、统一 history、事务和全局迁移锁创建当前 Provider Target。`platform.foundation` 是 SQL Server 的平台基础迁移流，从零摘要创建当前尚无模块 migration bundle 的 Authorization 与 ConfigCenter；Organization、Identity 和 Authentication 各自拥有从零到当前 target 的初始迁移。各 migration stream 的 ID、content/model/from-to digest、bundle 与 frontier 必须确定且精确，warm start 只能是当前有效 history 前缀上的 no-op 或继续前进。

不得借重建基线引入业务数据库 `DEFAULT`、数据回填、原始 DML、`SqlOperation`、自动 `Down` 或 Host 外 schema 写入口。业务 audit 列的可空性必须与当前 EF/read 契约一致。Oracle/MySQL 基线重建不属于本决策范围。

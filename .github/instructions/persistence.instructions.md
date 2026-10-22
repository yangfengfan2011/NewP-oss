---
description: "Use when modifying persistence layer code including EfPlatformDbContext, EfModelProjector, SQL dialects, persistence abstractions, or model definitions."
applyTo: "server/platform-persistence*/**"
---
# 持久化层开发规则

## 分层结构

```
platform-modeling           → 模型语义（FieldKind, ModelKind, FieldValueType）
platform-persistence        → 持久化抽象（ISqlQueryExecutor, IPersistenceMigrationRunner, ISqlDialect）
platform-persistence-efcore → EF Core 实现（EfPlatformDbContext, EfModelProjector, 方言实现）
platform-persistence-composition → 模型聚合（PlatformPersistenceModelCatalog）
platform-persistence-efcore.migrations.* → 各数据库 Migration 程序集
```

## 核心原则

- 模块不直接依赖 EF Core，通过抽象层交互
- 不在业务层判断数据库方言
- 模型语义层不包含数据库特定信息
- `EfModelProjector` 负责将 `ModelDefinition` 投射为 EF Core 模型

## ModelDefinition 定义规则

- 命名：Name 用 PascalCase，TableName 用 snake_case
- 每个表必须有主键
- `ModelKind.Entity` 用于普通实体，`ModelKind.Link` 用于关系表
- 审计字段（CreatedBy/CreatedAt/UpdatedBy/UpdatedAt）设 `IsAuditField = true`
- `StoreType` 填语义类型（string/bool/datetime），不填数据库方言类型

## 多数据库支持

- 三个 Migration 程序集独立维护：SqlServer / Oracle / MySQL
- 每个程序集有自己的 `IDesignTimeDbContextFactory` 实现
- 运行时通过 `PersistenceOptions.Provider` 配置选择

## 修改 DbContext 或 Projector 后

1. 确认 `dotnet build` 通过
2. 确认 `tests/Persistence.Tests` 通过
3. 如果影响了模型投射，需要为三个数据库重新生成 Migration

## 参考

- 开发流程：`docs/developer/persistence-development-workflow.md`
- Migration 规则：`docs/developer/ef-migration-ai-rules.md`
- 元数据编写：`docs/architecture/ai-metadata-authoring-guide.md`

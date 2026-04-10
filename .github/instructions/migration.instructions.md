---
description: "Use when generating EF Core migrations, modifying ModelDefinitions, updating PlatformPersistenceModelCatalog, or working with database schema changes. Covers multi-database migration generation and validation."
---
# 数据库迁移操作规则

## 何时需要生成 Migration

- 新增或修改了 `ModelDefinition`
- 在 `PlatformPersistenceModelCatalog.GetDefinitions()` 中添加/移除了模型

## 必须三库同步

每次 Migration 必须为 SQL Server、Oracle、MySQL 三个数据库分别生成，Migration 名称保持一致。

## 生成命令（在仓库根目录执行）

```powershell
# 先确保工具已安装
dotnet tool restore

# SQL Server
dotnet ef migrations add <Name> --project server/platform-persistence-efcore.migrations.sqlserver/PlatformPersistence.EfCore.SqlServerMigrations.csproj --startup-project server/platform-persistence-efcore.migrations.sqlserver/PlatformPersistence.EfCore.SqlServerMigrations.csproj --context ICP.Platform.Persistence.EfCore.EfPlatformDbContext

# Oracle
dotnet ef migrations add <Name> --project server/platform-persistence-efcore.migrations.oracle/PlatformPersistence.EfCore.OracleMigrations.csproj --startup-project server/platform-persistence-efcore.migrations.oracle/PlatformPersistence.EfCore.OracleMigrations.csproj --context ICP.Platform.Persistence.EfCore.EfPlatformDbContext

# MySQL
dotnet ef migrations add <Name> --project server/platform-persistence-efcore.migrations.mysql/PlatformPersistence.EfCore.MySqlMigrations.csproj --startup-project server/platform-persistence-efcore.migrations.mysql/PlatformPersistence.EfCore.MySqlMigrations.csproj --context ICP.Platform.Persistence.EfCore.EfPlatformDbContext
```

## 命名规则

- PascalCase，动词开头：`AddWorkflowInstance`、`RemoveObsoleteField`
- 不要用 `Update1`、`Fix2` 等无意义名称

## 禁止事项

- 不要手动编辑 `.Designer.cs` 文件
- 不要在 `Up()` / `Down()` 中写数据库方言特定 SQL
- 不要跳过某个数据库
- 不要把连接字符串硬编码到代码中

## 验证

```powershell
dotnet build ICP-Platform.sln
dotnet test ICP-Platform.sln
```

## 详细规则

完整文档见 `docs/developer/ef-migration-ai-rules.md`。

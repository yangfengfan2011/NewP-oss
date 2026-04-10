# EF Core Migration AI 作业规则

本文档面向 AI Agent，说明在本项目中何时以及如何生成 EF Core Migration。

---

## 1. 何时需要生成 Migration

以下情况**必须**生成新的 Migration：

- 新增了 `ModelDefinition`（例如新模块定义了新表）
- 修改了现有 `ModelDefinition`（例如新增字段、修改字段属性、新增索引）
- 在 `PlatformPersistenceModelCatalog.GetDefinitions()` 中添加或移除了模型

以下情况**不需要**生成 Migration：

- 仅修改了应用层代码（Service / Controller / DTO）
- 仅修改了前端代码
- 仅修改了不涉及持久化的模块逻辑

## 2. 核心流程概览

```
1. 定义/修改 ModelDefinition（在模块内）
2. 确保 PlatformPersistenceModelCatalog 注册了该定义
3. 为三个数据库分别生成 Migration
4. 验证生成的 Migration 文件
5. 运行测试
```

## 3. 前提条件

```powershell
# 在仓库根目录执行，确保 dotnet-ef 工具已安装
dotnet tool restore
```

当前项目 `dotnet-ef` 版本：**10.0.1**（锁定在 `dotnet-tools.json` 中，不可随意升级）。

## 4. Migration 生成命令

**必须为三个数据库分别生成 Migration**，命名保持一致。

### SQL Server

```powershell
dotnet ef migrations add <MigrationName> `
    --project server/platform-persistence-efcore.migrations.sqlserver/PlatformPersistence.EfCore.SqlServerMigrations.csproj `
    --startup-project server/platform-persistence-efcore.migrations.sqlserver/PlatformPersistence.EfCore.SqlServerMigrations.csproj `
    --context ICP.Platform.Persistence.EfCore.EfPlatformDbContext
```

### Oracle

```powershell
dotnet ef migrations add <MigrationName> `
    --project server/platform-persistence-efcore.migrations.oracle/PlatformPersistence.EfCore.OracleMigrations.csproj `
    --startup-project server/platform-persistence-efcore.migrations.oracle/PlatformPersistence.EfCore.OracleMigrations.csproj `
    --context ICP.Platform.Persistence.EfCore.EfPlatformDbContext
```

### MySQL

```powershell
dotnet ef migrations add <MigrationName> `
    --project server/platform-persistence-efcore.migrations.mysql/PlatformPersistence.EfCore.MySqlMigrations.csproj `
    --startup-project server/platform-persistence-efcore.migrations.mysql/PlatformPersistence.EfCore.MySqlMigrations.csproj `
    --context ICP.Platform.Persistence.EfCore.EfPlatformDbContext
```

> **注意**：以上命令均在仓库根目录执行。如果在 migration 项目目录内执行，去掉 `--project` 和 `--startup-project` 中的路径前缀。

## 5. Migration 命名规则

| 规则 | 说明 |
|------|------|
| **PascalCase** | 例如 `AddUserProfile`、`AddValueListItem` |
| **动词开头** | `Add...`、`Remove...`、`Alter...`、`Rename...` |
| **描述变更内容** | 不要用 `Update1`、`Fix2` 等无意义名称 |
| **三个数据库使用相同名称** | 时间戳可以不同，但 Migration 名称必须一致 |

## 6. 定义 ModelDefinition 的规则

### 6.1 在哪里定义

每个模块在自己的 `Persistence/Descriptors/` 目录下定义 `*ModelDefinitions.cs`：

```
server/modules/<Module>/Persistence/Descriptors/<Module>ModelDefinitions.cs
```

### 6.2 命名约定

| 层面 | 命名风格 | 示例 |
|------|---------|------|
| ModelDefinition.Name | PascalCase | `ConfigItem` |
| ModelDefinition.TableName | snake_case | `config_items` |
| FieldDefinition.Name | PascalCase | `CreatedAt` |
| FieldDefinition.StoreName | snake_case | `created_at` |
| IndexDefinition.Name | 前缀 + 表名 + 字段 | `UX_ConfigItem_Key`（唯一）/ `IX_ConfigItem_Module`（普通） |

### 6.3 字段类型映射

| FieldValueType | CLR 类型 | 典型用途 |
|----------------|---------|---------|
| `ObjectId` | `string` (max 128) | 主键、外键 |
| `Text` | `string` (max 256) | 普通文本字段 |
| `Description` | `string` (无限制) | 长文本、备注 |
| `Boolean` | `bool` | 布尔标志 |
| `DateTime` | `DateTime` | 时间戳 |
| `Enum` | `string` (max 256) | 枚举值存储 |
| `User` | `string` (max 128) | 用户标识引用 |
| `Group` | `string` (max 128) | 组/租户标识引用 |

### 6.4 必须遵守的约束

- **每个表必须有主键**：至少一个 `IsPrimaryKey = true` 的字段
- **ModelKind 区分**：普通实体用 `Entity`，多对多关系表用 `Link`
- **审计字段标记**：`CreatedBy`、`CreatedAt`、`UpdatedBy`、`UpdatedAt` 应设 `IsAuditField = true`
- **不要在 ModelDefinition 中写数据库方言特定的类型**：`StoreType` 统一填 `"string"`、`"bool"`、`"datetime"` 等语义类型，EfModelProjector 负责转换为具体数据库类型
- **索引名唯一**：整个平台范围内索引名不可重复

## 7. 注册到 ModelCatalog

新增模块定义后，必须在 `PlatformPersistenceModelCatalog` 中注册：

**文件**：`server/platform-persistence-composition/PlatformPersistenceModelCatalog.cs`

```csharp
public static IReadOnlyList<ModelDefinition> GetDefinitions() =>
    ConfigCenterModelDefinitions.All
        .Concat(AuthorizationModelDefinitions.All)
        .Concat(NewModuleModelDefinitions.All)   // ← 新增这一行
        .ToArray();
```

> **注意**：注册顺序不影响 Migration 结果，但保持按字母序既方便阅读又避免合并冲突。

## 8. 验证清单

生成 Migration 后，按以下顺序验证：

```powershell
# 1. 确保后端可构建
dotnet build ICP-Platform.sln

# 2. 确保所有测试通过
dotnet test ICP-Platform.sln

# 3. 检查生成的 Migration 文件
#    - 三个数据库的 Migration 都已生成
#    - Migration 名称一致
#    - Up() 和 Down() 方法都存在
#    - 不包含硬编码的数据库方言（EF Core 自动处理）

# 4. 如果有可用的数据库实例，运行 smoke 测试
#    （可选，视环境决定）
tests/integration/persistence/Run-SqlServerSmoke.ps1
```

## 9. 常见错误与应对

| 错误 | 原因 | 处理方式 |
|------|------|---------|
| `No migrations configuration was found` | 未找到 Design-time Factory | 确认 migration 项目中有 `IDesignTimeDbContextFactory` 实现 |
| `The model has changed since the last migration` | 有未生成的 ModelDefinition 变更 | 生成新的 Migration |
| `Unable to create an object of type 'EfPlatformDbContext'` | Design-time Factory 构造失败 | 检查 `PlatformPersistenceModelCatalog.GetDefinitions()` 是否编译通过 |
| `Table already exists` | Migration 重复应用 | 检查 `__EFMigrationsHistory` 表，确认 Migration 历史 |
| 生成的 Migration 为空 | ModelDefinition 未注册到 Catalog | 检查 `PlatformPersistenceModelCatalog` |

## 10. 禁止事项

- **不要手动编辑生成的 Migration `.Designer.cs` 文件**
- **不要在 Migration 的 `Up()` / `Down()` 中写数据库方言特定的 SQL**（除非绝对必要并注明原因）
- **不要跳过某个数据库的 Migration 生成**（三个数据库必须同步）
- **不要修改 `__EFMigrationsHistory` 表**
- **不要在非 Migration 项目中引用 `Microsoft.EntityFrameworkCore.Design`**
- **不要把连接字符串硬编码到代码中**（使用环境变量或 appsettings）

## 11. 回滚 Migration

如果需要撤销最近一次 Migration（尚未应用到数据库）：

```powershell
# 移除最后一个 Migration（三个数据库分别执行）
dotnet ef migrations remove `
    --project server/platform-persistence-efcore.migrations.sqlserver/PlatformPersistence.EfCore.SqlServerMigrations.csproj `
    --startup-project server/platform-persistence-efcore.migrations.sqlserver/PlatformPersistence.EfCore.SqlServerMigrations.csproj `
    --context ICP.Platform.Persistence.EfCore.EfPlatformDbContext
```

如果 Migration 已经应用到数据库：

```powershell
# 回退到指定 Migration
dotnet ef database update <PreviousMigrationName> `
    --project server/platform-persistence-efcore.migrations.sqlserver/PlatformPersistence.EfCore.SqlServerMigrations.csproj `
    --startup-project server/platform-persistence-efcore.migrations.sqlserver/PlatformPersistence.EfCore.SqlServerMigrations.csproj `
    --context ICP.Platform.Persistence.EfCore.EfPlatformDbContext

# 然后移除 Migration 文件
dotnet ef migrations remove ...
```

## 12. 完整工作流示例

假设要为新模块 `Workflow` 添加一个 `WorkflowInstance` 表：

```
步骤 1: 创建 server/modules/Workflow/Persistence/Descriptors/WorkflowModelDefinitions.cs
步骤 2: 定义 WorkflowInstance ModelDefinition（含字段和索引）
步骤 3: 在 PlatformPersistenceModelCatalog.GetDefinitions() 中 Concat(WorkflowModelDefinitions.All)
步骤 4: dotnet build ICP-Platform.sln（确保编译通过）
步骤 5: 为 SqlServer 生成 Migration: dotnet ef migrations add AddWorkflowInstance ...
步骤 6: 为 Oracle 生成 Migration: dotnet ef migrations add AddWorkflowInstance ...
步骤 7: 为 MySQL 生成 Migration: dotnet ef migrations add AddWorkflowInstance ...
步骤 8: dotnet build ICP-Platform.sln（确认 Migration 文件编译通过）
步骤 9: dotnet test ICP-Platform.sln（确认测试通过）
步骤 10: 提交所有变更
```

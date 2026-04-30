# 持久化使用指南

本文档面向仓库内部开发者，说明当前仓库里已经落地的多数据库持久化能力应该如何使用。

如果你想先看 API 边界，请先看 [`persistence-public-api.md`](./persistence-public-api.md)。

## 1. 当前范围

`Phase 6.5` 当前首批真实落库范围是：

- `ConfigCenter`
- `Authorization`

当前不在首批范围内的模块：

- `Audit`
- `Files`

当前实现已经具备：

- 宿主统一持久化装配入口
- `SqlServer` / `Oracle` / `MySql` 三 Provider 切换
- 三套独立 migration 项目
- 本地 smoke 脚本

## 2. 宿主如何启用平台持久化

当前宿主入口在：

- `apps/host-api/Program.cs`
- `apps/host-api/Extensions/PersistenceBootstrapExtensions.cs`

关键调用只有两步：

```csharp
builder.Services.AddPlatformPersistence(builder.Configuration);
```

```csharp
await app.ApplyPlatformPersistenceAsync();
```

含义分别是：

- `AddPlatformPersistence`
  - 注册平台持久化服务
- `ApplyPlatformPersistenceAsync`
  - 在应用启动时执行迁移入口

如果你是在补宿主接入，优先沿用这两步，不要在 `Program.cs` 里直接拼 Provider 分支和迁移逻辑。

## 3. 关键配置项

当前默认配置位于：

- `apps/host-api/appsettings.json`
- `apps/host-api/appsettings.Development.json`

### 3.1 `Persistence` 配置段

当前使用：

```json
"Persistence": {
  "Adapter": "EfCore",
  "Provider": "SqlServer",
  "AutoMigrate": true,
  "AutoSeed": true
}
```

说明：

- `Adapter`
  - 当前固定为 `EfCore`
- `Provider`
  - 可切换 `SqlServer` / `Oracle` / `MySql`
- `AutoMigrate`
  - 宿主启动时是否执行迁移
- `AutoSeed`
  - 当前配置模型已保留，但首批没有统一数据库 seed 执行器直接消费它

### 3.2 `ConnectionStrings`

当前约定的连接串名：

- `SqlServer`
- `Oracle`
- `MySql`

`appsettings.Development.json` 已给出本地 Docker 容器可用的开发样例。

## 4. 如何切换 Provider

最直接的切换方式是修改：

- `Persistence:Provider`

并保证对应的连接串可用：

- `ConnectionStrings:SqlServer`
- `ConnectionStrings:Oracle`
- `ConnectionStrings:MySql`

在代码层，Provider 会影响：

- 连接串选择
- SQL 方言选择
- 默认 migration 程序集选择

正常使用时，不需要在业务模块里自己写 Provider 分支。

## 5. 如何用环境变量覆盖配置

当前脚本和宿主都支持使用环境变量覆盖配置。

最常用的是：

- `Persistence__Adapter`
- `Persistence__Provider`
- `Persistence__AutoMigrate`
- `ConnectionStrings__SqlServer`
- `ConnectionStrings__Oracle`
- `ConnectionStrings__MySql`

例如在 PowerShell 中可以这样覆盖：

```powershell
$env:Persistence__Adapter = "EfCore"
$env:Persistence__Provider = "SqlServer"
$env:Persistence__AutoMigrate = "false"
$env:ConnectionStrings__SqlServer = "Server=localhost,1433;Database=ICP_Platform;User Id=sa;Password=REDACTED_FOR_OPEN_SOURCE;TrustServerCertificate=True;"
```

这也是 smoke 脚本当前采用的方式。

## 6. 三套 migration 项目怎么用

当前 migration 项目位置：

- `server/platform-persistence-efcore.migrations.sqlserver`
- `server/platform-persistence-efcore.migrations.oracle`
- `server/platform-persistence-efcore.migrations.mysql`

它们的职责是：

- 复用同一套模型语义定义
- 为不同数据库 Provider 提供各自独立的迁移集

不要把“三库独立迁移集”理解成“三套业务模型”。当前仍然是：

- 同一套业务边界
- 同一套模型语义
- 三套 Provider 投影后的迁移产物

## 7. 如何执行迁移

执行迁移前，先确保本地工具存在：

```powershell
dotnet tool restore
```

然后选择对应 migration 项目执行：

```powershell
dotnet tool run dotnet-ef database update `
  --project .\server\platform-persistence-efcore.migrations.sqlserver\PlatformPersistence.EfCore.SqlServerMigrations.csproj `
  --startup-project .\server\platform-persistence-efcore.migrations.sqlserver\PlatformPersistence.EfCore.SqlServerMigrations.csproj `
  --context ICP.Platform.Persistence.EfCore.EfPlatformDbContext
```

```powershell
dotnet tool run dotnet-ef database update `
  --project .\server\platform-persistence-efcore.migrations.oracle\PlatformPersistence.EfCore.OracleMigrations.csproj `
  --startup-project .\server\platform-persistence-efcore.migrations.oracle\PlatformPersistence.EfCore.OracleMigrations.csproj `
  --context ICP.Platform.Persistence.EfCore.EfPlatformDbContext
```

```powershell
dotnet tool run dotnet-ef database update `
  --project .\server\platform-persistence-efcore.migrations.mysql\PlatformPersistence.EfCore.MySqlMigrations.csproj `
  --startup-project .\server\platform-persistence-efcore.migrations.mysql\PlatformPersistence.EfCore.MySqlMigrations.csproj `
  --context ICP.Platform.Persistence.EfCore.EfPlatformDbContext
```

如果只是本地验证完整链路，优先直接跑 smoke，而不是手工拼所有步骤。

## 8. 如何执行 smoke

当前提供三套快捷入口：

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-SqlServerSmoke.ps1
powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-OracleSmoke.ps1
powershell -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-MySqlSmoke.ps1
```

通用主脚本位于：

- `tests/integration/persistence/Invoke-PersistenceSmoke.ps1`

当前 smoke 会执行：

1. `dotnet tool restore`
2. 构建 `HostApi`
3. 运行 `ConfigCenter.Tests`
4. 运行 `Authorization.Tests`
5. 对应 migration 项目执行 `dotnet-ef database update`
6. 拉起 `HostApi`
7. 通过 `swagger/v1/swagger.json` 探活

当前脚本还会：

- 设置 `Persistence__Adapter`
- 设置 `Persistence__Provider`
- 临时覆盖对应连接串
- 在启动宿主前控制 `Persistence__AutoMigrate`

## 9. 开发期结构同步工具怎么用

如果只是让本地 SQL Server 开发库跟上当前 `*.model.json`，可以使用开发期结构同步工具：

```powershell
dotnet build .\server\tools\dev-schema-sync\DevSchemaSync.csproj -v minimal
```

构建后可直接执行 exe：

```powershell
.\server\tools\dev-schema-sync\bin\Debug\net10.0\dev-schema-sync.exe
```

也可以不显式构建，直接通过 `dotnet run` 执行：

```powershell
dotnet run --project .\server\tools\dev-schema-sync\DevSchemaSync.csproj --
```

### 9.1 常用命令

只看计划，不执行：

```powershell
.\server\tools\dev-schema-sync\bin\Debug\net10.0\dev-schema-sync.exe
```

看计划和 SQL，不执行：

```powershell
.\server\tools\dev-schema-sync\bin\Debug\net10.0\dev-schema-sync.exe --sql
```

看计划和 SQL，然后确认执行：

```powershell
.\server\tools\dev-schema-sync\bin\Debug\net10.0\dev-schema-sync.exe --sql --apply
```

输出 JSON 计划，供 agent 或脚本读取，不执行：

```powershell
.\server\tools\dev-schema-sync\bin\Debug\net10.0\dev-schema-sync.exe --json
```

`--json --apply` 不支持，因为 JSON 模式不提供交互确认。

### 9.2 确认流程

`--sql --apply` 会先输出结构计划和 SQL Preview，然后进入确认：

```text
是否继续执行本次同步计划？输入 y 继续，其他任意输入取消 [y/N]:
```

输入 `y` 后，如果计划只包含 Safe 操作，会直接执行。

如果计划包含 `Confirm` 级高风险操作，工具会继续输出高风险摘要，并要求输入固定短语：

```text
请输入 APPLY RISK 继续执行高风险操作，其他任意输入取消:
```

只有完整输入 `APPLY RISK` 才会继续执行高风险 SQL。输入 `y`、`yes` 或其他内容都会取消，不执行任何 SQL。

### 9.3 高风险操作有哪些

当前常见 `Confirm` 操作包括：

- 删除列
- 表重命名
- 列重命名
- 列类型变更
- 可空性从可空收紧为非空
- 需要重建或影响唯一性的索引调整

执行前应仔细检查 SQL Preview。这个工具不做自动数据备份和自动回滚。

### 9.4 和 Host 自动启动的区别

Host 在 `DevelopmentSchemaSync:Enabled=true`、`Persistence:Provider=SqlServer`、开发环境下会自动执行 Safe 结构同步。

但 Host 自动启动不会执行 `Confirm` 高风险计划；遇到高风险计划时仍会阻断启动。需要执行高风险计划时，应回到 CLI，使用：

```powershell
.\server\tools\dev-schema-sync\bin\Debug\net10.0\dev-schema-sync.exe --sql --apply
```

这样可以先看 SQL，再经过整体确认和高风险二次确认。

### 9.5 边界

- 当前只支持 SQL Server。
- 工具只读取仓库中的 `modules/**/metadata/models/*.model.json`。
- 工具不生成 migration 文件。
- 工具不修改 `ModelSnapshot`。
- 工具不写 `__EFMigrationsHistory`。
- 工具第一版不自动删表；数据库中多出的表只作为未托管对象提示。

正式发布和可审计回滚仍应使用 EF Core migration 链路。

## 10. 本地数据库准备入口

如果你还没有准备本地三库容器，请先看：

- [`../guides/local-multi-db-container-setup.md`](../guides/local-multi-db-container-setup.md)

如果你想知道当前仓库最近一次真实联调使用了哪些容器、端口和连接串，请看：

- [`../guides/2026-04-09-local-multi-db-runtime-record.md`](../guides/2026-04-09-local-multi-db-runtime-record.md)

## 11. 模块作者通常需要改哪些位置

以首批模块为例：

- `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelDefinitions.cs`
- `server/modules/ConfigCenter/Persistence/Ef/EfConfigRepository.cs`
- `server/modules/Authorization/Persistence/Descriptors/AuthorizationModelDefinitions.cs`
- `server/modules/Authorization/Persistence/Ef/`

正常情况下：

- 模型语义改动进入 `Descriptors`
- EF 仓储改动进入 `Ef`
- 宿主装配和 migration 配置留在平台层

## 12. 当前边界与注意事项

- 当前默认适配器只有 `EfCore`
- 当前 `AutoMigrate` 已实际生效
- 当前 `AutoSeed` 只是配置模型中的保留位，不等于已具备统一数据库 seed 管线
- 当前 smoke 依赖本机数据库容器和连接串可用
- 更换环境后，应重新跑 smoke 确认

## 13. 进一步阅读

- API 边界：[`persistence-public-api.md`](./persistence-public-api.md)
- 开发顺序：[`persistence-development-workflow.md`](./persistence-development-workflow.md)
- 开发期结构同步高风险确认设计：[`../designs/dev-schema-sync-risk-confirm-apply-design.md`](../designs/dev-schema-sync-risk-confirm-apply-design.md)
- 测试入口：[`../../tests/README.md`](../../tests/README.md)

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

## 9. 本地数据库准备入口

如果你还没有准备本地三库容器，请先看：

- [`../guides/local-multi-db-container-setup.md`](../guides/local-multi-db-container-setup.md)

如果你想知道当前仓库最近一次真实联调使用了哪些容器、端口和连接串，请看：

- [`../guides/2026-04-09-local-multi-db-runtime-record.md`](../guides/2026-04-09-local-multi-db-runtime-record.md)

## 10. 模块作者通常需要改哪些位置

以首批模块为例：

- `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelDefinitions.cs`
- `server/modules/ConfigCenter/Persistence/Ef/EfConfigRepository.cs`
- `server/modules/Authorization/Persistence/Descriptors/AuthorizationModelDefinitions.cs`
- `server/modules/Authorization/Persistence/Ef/`

正常情况下：

- 模型语义改动进入 `Descriptors`
- EF 仓储改动进入 `Ef`
- 宿主装配和 migration 配置留在平台层

## 11. 当前边界与注意事项

- 当前默认适配器只有 `EfCore`
- 当前 `AutoMigrate` 已实际生效
- 当前 `AutoSeed` 只是配置模型中的保留位，不等于已具备统一数据库 seed 管线
- 当前 smoke 依赖本机数据库容器和连接串可用
- 更换环境后，应重新跑 smoke 确认

## 12. 进一步阅读

- API 边界：[`persistence-public-api.md`](./persistence-public-api.md)
- 开发顺序：[`persistence-development-workflow.md`](./persistence-development-workflow.md)
- 测试入口：[`../../tests/README.md`](../../tests/README.md)

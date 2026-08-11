# 开发环境数据库结构同步工具验证记录

## 1. 验证范围

- 后端构建：`ICP-Platform.sln`
- Host 开发启动链路：`apps/host-api`
- Host 持久化启动测试：`tests/Persistence.Tests/Persistence.Tests.csproj`
- 模块回归测试：`tests/Authorization.Tests/Authorization.Tests.csproj`、`tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj`、`tests/Organization.Tests/Organization.Tests.csproj`
- 工具回归测试：`tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj`
- 命令入口历史验证：`--json`、`--sql`、`--apply`
- 正式链路隔离验证：migration 目录、`ModelSnapshot`

## 2. Host 接入行为

- `apps/host-api/appsettings.Development.json` 启用 `DevelopmentSchemaSync:Enabled=true`。
- `AddPlatformPersistence` 在 `DevelopmentSchemaSync:Enabled=true` 且 `Persistence:Provider=SqlServer` 时注册 `DevelopmentSchemaSyncOptions` 与 `DevelopmentSchemaSyncCoordinator`。
- `ApplyPlatformPersistenceAsync` 在开发环境、开关启用、SqlServer provider 同时满足时优先执行 coordinator。
- Host 自动启动路径使用 `ExecuteAsync` 的非交互同步模式，默认 `ApplyChanges=true` 且 `ConfirmApply=_ => true`。
- Confirm 风险会在 coordinator 的 `PrepareAsync` 阶段阻断，不会进入 apply；Safe plan 会在启动期间自动 apply。
- 开发态检查失败、Confirm 风险、apply cancelled/failed/throws 时会抛出 `InvalidOperationException(result.BuildMessage())` 并阻断启动；Safe apply 通过后不再进入 EF migration runner。
- Repository root 不依赖当前工作目录：Host 从 `AppContext.BaseDirectory` 向上查找包含 `apps`、`server`、`modules` 的仓库根；找不到时回退到 AppBase，并由 coordinator 暴露缺失 authoring/DTO 依赖的失败信号。
- `HostApi.csproj` 将 `modules/**/metadata/models/*.model.json` 与 `server/platform-modeling/metadata/storage-types.json` 视为运行时模型真源产物，所有配置都复制；`server/modules/**/DTOs/Generated/*Dto.g.cs` 与 `server/modules/**/DTOs/Generated/*BaseDtoSignatureProvider.g.cs` 仍只在 `EnableDevelopmentSchemaSyncAssets=true` 时复制。
- `PlatformPersistence.DevSync` 项目引用保留无条件引用，因为 `PersistenceBootstrapExtensions.cs` 的运行时代码直接引用 coordinator/options 类型；Release 输出应包含模型真源与 storage-types，不默认包含 generated DTO/signature 源文件。
- 本任务未修改 `apps/host-api/Program.cs`；现有 `await app.ApplyPlatformPersistenceAsync()` 注册顺序可满足接入要求。

## 3. TDD 红/绿记录

- 红灯：`dotnet test tests/Persistence.Tests/Persistence.Tests.csproj --filter DevelopmentSchemaSyncBootstrapTests -v minimal`
  - 结果：失败，`Assert.Contains() Failure: Filter not matched in collection`
  - 原因：`AddPlatformPersistence` 尚未注册 `DevelopmentSchemaSyncCoordinator`
- 绿灯：`dotnet test tests/Persistence.Tests/Persistence.Tests.csproj --filter DevelopmentSchemaSyncBootstrapTests -v minimal`
  - 结果：通过，`1/1` 用例通过
- Review 补测：`dotnet test tests/Persistence.Tests/Persistence.Tests.csproj --filter DevelopmentSchemaSyncBootstrapTests -v minimal`
  - 结果：通过，`9/9` 用例通过
  - 覆盖：真实 DI 解析 coordinator/options、devsync artifact 复制条件、`Development + Enabled + SqlServer` 下 Safe plan 自动 apply、Confirm 风险阻断且不 apply、apply 失败阻断、`Production`/disabled/non-SqlServer 回落 migration runner
- 条件复制红灯：`dotnet test tests/Persistence.Tests/Persistence.Tests.csproj --filter DevelopmentSchemaSyncBootstrapTests -v minimal`
  - 结果：失败，`HostApiProject_DevelopmentSchemaSyncAssets_AreCopiedOnlyForDebugOrExplicitOptIn` 检出 devsync artifacts 的 `Content` 缺少 `EnableDevelopmentSchemaSyncAssets` 条件
- 条件复制绿灯：同命令
  - 结果：通过，`8/8` 用例通过
- Release 模型真源红灯：`dotnet test tests/Persistence.Tests/Persistence.Tests.csproj --filter "RuntimeModelTruthAssets|GeneratedDevSyncAssets" -v minimal`
  - 结果：失败，`HostApiProject_RuntimeModelTruthAssets_AreCopiedForAllConfigurations` 检出模型真源仍受 `EnableDevelopmentSchemaSyncAssets` 条件限制
- Release 模型真源绿灯：同命令
  - 结果：通过，`2/2` 用例通过
- Safe apply 红灯：`dotnet test tests/Persistence.Tests/Persistence.Tests.csproj --filter DevelopmentSchemaSyncBootstrapTests -v minimal`
  - 结果：失败，`AddPlatformPersistence_WhenDevelopmentSchemaSyncEnabled_ResolvesCoordinatorAndOptions` 检出 Host devsync options 的 `ApplyChanges=false`
- Safe apply 绿灯：同命令
  - 结果：通过，`9/9` 用例通过

## 4. 本次实际执行过的命令

- `dotnet build apps/host-api/HostApi.csproj -v minimal`
  - 结果：通过，`0` 错误，`0` 警告
  - 备注：验证 `HostApi.csproj` Debug 默认复制项无 MSBuild 冲突
- `Test-Path apps/host-api/bin/Debug/net10.0/server/platform-modeling/metadata/storage-types.json; Get-ChildItem apps/host-api/bin/Debug/net10.0/server/modules -Recurse -Include *Dto.g.cs,*BaseDtoSignatureProvider.g.cs | Select-Object -First 8 FullName`
  - 结果：通过，`storage-types.json` 存在，输出目录存在 generated DTO 与 signature provider
- `Get-ChildItem apps/host-api/bin/Debug/net10.0/modules -Recurse -Filter *.model.json | Select-Object -First 8 FullName`
  - 结果：通过，输出目录存在模块 `metadata/models/*.model.json`
- `dotnet build apps/host-api/HostApi.csproj -c Release -v minimal`
  - 结果：通过，`0` 错误，`0` 警告
- `Release 输出 smoke`
  - 结果：`ModelJson=11`、`StorageTypes=1`、`GeneratedDtos=0`
  - 备注：Release 需要复制运行时模型真源与 storage type map；generated DTO/signature 源文件默认仍不复制
- `Debug 输出 smoke`
  - 结果：`ModelJson=11`、`StorageTypes=1`、`GeneratedDtos=14`
- `dotnet build ICP-Platform.sln -v minimal`
  - 结果：通过，`0` 错误，`0` 警告
- `dotnet test tests/Persistence.Tests/Persistence.Tests.csproj -v minimal`
  - 结果：通过，`15/15` 用例通过
- `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj -v minimal`
  - 结果：通过，`20/20` 用例通过
- `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj -v minimal`
  - 结果：通过，`38/38` 用例通过
- `dotnet test tests/Organization.Tests/Organization.Tests.csproj -v minimal`
  - 结果：通过，`51/51` 用例通过
- `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj -v minimal`
  - 结果：通过，`41/41` 用例通过
- `dotnet test tests/Modeling.Tests/Modeling.Tests.csproj -v minimal`
  - 结果：通过，`33/33` 用例通过；作为 Host 输出复制项涉及 modeling metadata 的补充回归
- `git diff --check -- apps/host-api tests/Persistence.Tests docs/verification/dev-schema-sync-tool-verification.md server/platform-persistence-devsync server/tools/dev-schema-sync server/tools/model-dto-sync`
  - 结果：通过，无 whitespace error；Git 提示部分工作区文件下次触碰时会从 LF 转为 CRLF

## 5. 历史命令入口验证

- `dotnet run --project server/tools/dev-schema-sync -- --json`
  - 结果：通过
  - 摘要：输出结构化计划 JSON，包含 `summary`、`managedObjects`、`operations`、`unmanagedTables`
- `dotnet run --project server/tools/dev-schema-sync -- --sql`
  - 结果：通过
  - 摘要：输出文本计划四个区块与 SQL Preview
- `@('y','n') | dotnet run --project server/tools/dev-schema-sync -- --sql --apply`
  - 结果：按预期安全取消，进程退出码为 `1`
  - 摘要：先出现第一次整体确认，再出现第二次高风险确认；第二次输入 `n` 后输出“已取消本次同步，未执行任何数据库变更。”

## 6. 已知限制

- 第一版只支持 SQL Server。
- Host 开发启动链路会自动 apply Safe 计划；Confirm 风险、前置检查失败和执行失败都会阻断启动。
- 只接管 `model` 管理对象，不兼容手写 `ModelDefinition`。
- 当前 SQL 生成对泛型 store type 仅做了 SQL Server 最小必要映射，足以覆盖当前真实计划和常见新增表/列场景，但还不是完整类型系统。
- 第一版不自动删表；数据库多出的表只进入 `Unmanaged Objects Summary`。
- 工具不生成 migration、不修改 `ModelSnapshot`、不写 `__EFMigrationsHistory`。

## 7. 回滚思路

- 关闭开发启动检查：将 `apps/host-api/appsettings.Development.json` 中 `DevelopmentSchemaSync:Enabled` 改为 `false`。
- 回退 Host 接入：移除 `apps/host-api/Extensions/PersistenceBootstrapExtensions.cs` 中 devsync 注册与开发态分支，Host 会重新只走 `IPersistenceMigrationRunner`。
- 回退输出复制：移除 `apps/host-api/HostApi.csproj` 中无条件复制的 `modules/**/metadata/models/*.model.json`、`server/platform-modeling/metadata/storage-types.json`，以及受 `EnableDevelopmentSchemaSyncAssets` 控制的 `server/modules/**/DTOs/Generated/*Dto.g.cs`、`server/modules/**/DTOs/Generated/*BaseDtoSignatureProvider.g.cs` 的 `Content Include`。
- 回退依赖：移除 `HostApi.csproj` 对 `server/platform-persistence-devsync/PlatformPersistence.DevSync.csproj` 的引用。

## 8. 正式链路隔离验证结论

- 本任务未修改正式 migration 目录、`ModelSnapshot` 或 `__EFMigrationsHistory`。
- Host 接入仅在 `Development` 环境、开关启用、SqlServer provider 下生效。
- 非开发环境或非 SqlServer provider 仍回落到原有 EF migration runner。
- 结论：任务 8 已将开发态 schema sync 检查接入 Host 启动链路，同时保持正式 migration 发布链路隔离。

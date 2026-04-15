# 开发环境数据库结构同步工具验证记录

## 1. 验证范围

- 后端构建：`ICP-Platform.sln`
- 工具测试：`tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj`
- 建模回归测试：`tests/Modeling.Tests/Modeling.Tests.csproj`
- 命令入口验证：`--json`、`--sql`、`--apply`
- 正式链路隔离验证：migration 目录、`ModelSnapshot`、`apps/host-api`

## 2. 实际执行过的命令

- `dotnet build ICP-Platform.sln -v minimal`
  - 结果：通过，`0` 错误，`1` 个警告
  - 备注：警告为测试输出目录文件占用导致的重试复制，不影响构建成功
- `dotnet test tests/DevSchemaSync.Tests/DevSchemaSync.Tests.csproj -v minimal`
  - 结果：通过，`23/23` 用例通过
- `dotnet test tests/Modeling.Tests/Modeling.Tests.csproj -v minimal`
  - 结果：通过，`16/16` 用例通过
- `dotnet run --project server/tools/dev-schema-sync -- --json`
  - 结果：通过
  - 摘要：输出结构化计划 JSON，包含 `summary`、`managedObjects`、`operations`、`unmanagedTables`
- `dotnet run --project server/tools/dev-schema-sync -- --sql`
  - 结果：通过
  - 摘要：输出文本计划四个区块与 SQL Preview
- `@('y','n') | dotnet run --project server/tools/dev-schema-sync -- --sql --apply`
  - 结果：按预期安全取消，进程退出码为 `1`
  - 摘要：先出现第一次整体确认，再出现第二次高风险确认；第二次输入 `n` 后输出“已取消本次同步，未执行任何数据库变更。”
- `git diff -- server/platform-persistence-efcore.migrations.sqlserver server/platform-persistence-efcore.migrations.mysql server/platform-persistence-efcore.migrations.oracle`
  - 结果：无输出
- `git diff -- apps/host-api`
  - 结果：无输出

## 3. 本次结果

- 工具已能基于全量 `model` 文件与当前 SQL Server 数据库生成结构化计划
- `--json` 已输出结构化计划 JSON
- `--sql` 已输出文本计划与 SQL Preview
- `--apply` 已接入第一次整体确认与第二次高风险确认；本次通过拒绝第二次确认验证了“拒绝后不落库”
- 当前真实开发库计划结果为 `3` 个高风险操作：
  - `ConfigItem.Description` 类型从 `nvarchar` 调整为 `nvarchar(max)`
  - `ConfigItem.UpdatedAt` 可空性调整为 `NULL`
  - `ConfigItem.Value` 类型从 `nvarchar` 调整为 `nvarchar(max)`

## 4. 已知限制

- 第一版只支持 SQL Server
- 只接管 `model` 管理对象，不兼容手写 `ModelDefinition`
- 当前 SQL 生成对泛型 store type 仅做了 SQL Server 最小必要映射，足以覆盖当前真实计划和常见新增表/列场景，但还不是完整类型系统
- 第一版不自动删表；数据库多出的表只进入 `Unmanaged Objects Summary`
- 工具不生成 migration、不修改 `ModelSnapshot`、不写 `__EFMigrationsHistory`

## 5. 正式链路隔离验证结论

- migration 目录 diff 为空，未新增或修改正式迁移文件
- `apps/host-api` diff 为空，未改动正式 Host API 启动链路
- 本次 `--sql --apply` 通过第二次确认拒绝路径结束，未执行任何数据库变更，因此也未触碰 `__EFMigrationsHistory`
- 结论：本次交付保持了与正式 migration 发布链路的隔离要求

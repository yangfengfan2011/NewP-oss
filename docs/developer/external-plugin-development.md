# 外部插件开发

当前公开外部插件能力只支持声明式 `standard-data.v1`。它从 Standard Plugin Blueprint 派生标准 CRUD、页面、菜单、权限、模型和 SQL Server migration bundle；不允许外仓注入任意 Controller、DI callback、程序集入口、React 页面或生命周期代码。

## 唯一可复制样本

使用 `tests/integration/plugin-api/baseline/1.0.0` 作为可执行基线。外部项目必须：

- 精确锁定同版 `ICP.Platform.Plugin.Contracts`、`ICP.Platform.Plugin.Sdk`、`@icp-platform/plugin-sdk` 和 `ICP.Platform.Plugin.Tool`；
- 提交 NuGet 与 pnpm 锁文件；
- 只依赖公开发布包，不使用 ProjectReference、workspace/file/path/source link 或 NewP 源码路径；
- 以 `newp.plugin.json` 和 `metadata/**` 作为 authoring 真源；
- 只携带声明式前端投影和标准 migration bundle，不携带外部可执行前后端代码。

`scripts/new-module.ps1 -ExternalPlugin` 与 `server/templates/external-plugin-template` 属于旧代码型插件实验，不是当前入口。

## 组合与模型声明

Blueprint 可声明 `dependencies: ["Part"]` 与 `composition: { "members": ["Part"] }`，省略时分别为空依赖和无成员。依赖表达实际能力消费，成员表达归组；归组不接管成员的身份、版本、模型或 migration。选定包版的声明进入与内建 Capsule 相同的 Canonical Module Plan；未选中的 installed 包不改变组合图，停用组件仍参加图完整性检查。平台基础集合来自正式发行 role，外部包不能自行声明 required 或 distribution role；集合外的实际业务依赖必须显式声明。

Model metadata 的单个业务根使用全局技术名，例如 `Asset`，与 Blueprint 资源 key 一致；模型 owner 仍为 `Partner.AssetCatalog`。根名不拼接 owner 或父级路径，字段、索引和迁移目标应与该声明对齐。完整规则见 [Module Contract](../architecture/module-contract.md) 与 [ADR-0055](../adr/0055-component-composition-separates-model-identity-and-deployment.md)。

存储声明由 `metadata/models/*.model.json` 拥有，Host 正式 PersistencePlan 是唯一存储消费入口。`explain` 中现有 `schemaName`、`tableName`、`uniqueIndexes` 是既有的旧物理提示，当前未用于建库、EF 或 CRUD 寻址，不能替代模型声明；本次没有修正这些旧提示或改变已有物理 schema。

## 工具闭环

取得与公共包同版的 Platform API release manifest 后，在外部项目根目录执行：

```powershell
dotnet newp plugin validate --root . --release-manifest <platform-api-release.json>
dotnet newp plugin explain --root . --release-manifest <platform-api-release.json> --output <explain.json>
dotnet newp plugin pack --root . --release-manifest <platform-api-release.json> --output <plugin-package>
dotnet newp plugin verify --package <plugin-package> --release-manifest <platform-api-release.json>
```

真实 migration bundle 从基线中的独立 EF 项目构建，固定 deterministic、PathMap 和禁止 source revision 注入。构建并复制所声明的 Release DLL 后，用同版公开工具完成 manifest 尾节点：

```powershell
dotnet build src/Partner.AssetCatalog.Migrations/Partner.AssetCatalog.Migrations.csproj -c Release --no-restore
Copy-Item src/Partner.AssetCatalog.Migrations/bin/Release/net10.0/Partner.AssetCatalog.Migrations.dll migrations/sqlserver/
dotnet newp plugin finalize-migration --root . --release-manifest <platform-api-release.json> --migration 20261010000100_AssetInitial --source src/Partner.AssetCatalog.Migrations/Migrations/20261010000100_AssetInitial.cs --compatibility safe-expand
```

首次 finalize 前，将基线空 manifest 提交到外仓 Git HEAD；后续已发布节点及其 canonical source 必须保持。命令只静态读取模型与 DLL 的 MigrationAttribute 集合，复用平台 projector/digest/manifest integrity 后原子写 manifest，不连接 SQL 或执行 Up。只有一个新尾节点可追加；重放同一尾节点保持相同 SHA。新节点显式选择 `safe-expand`、`data-backfill`、`contract` 或 `backup-restore-only`，其他值、历史改写或 DLL 类型集合不匹配都会拒绝。完成后提交 manifest、migration source 与锁文件，再按上面的 validate/pack/verify 交付。普通 build/pack 不代替 finalize；Model 修改仍需要配套真实 migration。

仓库内验证完整公开发布闭环：

```powershell
pwsh -NoProfile -File .\tests\integration\plugin-api\Run-PlatformApiReleaseSmoke.ps1
```

命令输出是结构化结果；失败时按 diagnostic code、stage 和 source path 修正 authoring，不绕过 release digest、依赖审计或包完整性检查。

release smoke 同时输出同一 owner 的 `1.0.0`、`1.1.0`（包含真实 Part）、`1.2.0`（解除该引用）三个独立 `.npp`，它们保持 Model 与 migration 字节不变，仅用于正式选版与组合生命周期验证。取得该轮 evidence 目录后可复跑真实 Host/API/SQL 消费：

```powershell
$env:NEWP_RUN_EXTERNAL_PLUGIN_SQLSERVER_INTEGRATION = "1"
$env:NEWP_EXTERNAL_PLUGIN_PACKAGE = "<evidence>/Partner.AssetCatalog@1.0.0.npp"
try {
    dotnet test tests/HostApi.Tests/HostApi.Tests.csproj -c Release --filter FullyQualifiedName~StandardDataCompositionSqlServerTests
}
finally {
    Remove-Item Env:NEWP_RUN_EXTERNAL_PLUGIN_SQLSERVER_INTEGRATION,Env:NEWP_EXTERNAL_PLUGIN_PACKAGE -ErrorAction SilentlyContinue
}
```

## 生命周期边界

安装、升级、启用、停用、卸载和回滚只允许在 Host 停止期间通过离线工具改变已安装库存或期望状态；运行中的 Host 写入口稳定不支持。活动版本在随后一次 Host 启动中完成整体验证、迁移和投影后生效，Shell 只装载该次启动的静态集合。migration 成功不等于插件已经激活，禁用也不等于删除数据。

完整术语和失败语义见[Plugin Platform Context](../contexts/plugin-platform/CONTEXT.md)、[ADR-0032](../adr/0032-plugins-change-only-while-host-is-stopped.md)与[Platform Persistence Context](../contexts/platform-persistence/CONTEXT.md)。需要代码型外部插件或运行时插件运维时必须另行设计，不能扩张 `standard-data.v1` 或把离线工具暴露为在线入口。

## 停服部署入口

正式入口使用已经构建的发行 `HostApi.dll`；保持与正常启动相同的环境、配置及 `Platform:ExternalPluginsEnabled`，并明确指定正常 Host 使用的安装根：

先将经 verify 的 `.npp` 解包到独立包目录，再把该目录传给 install；包内容仍由正式 installer 完整性与 profile 检查裁决。

```powershell
pwsh -NoProfile -File scripts/manage-unified-plugin.ps1 -Action install -PackagePath <包目录> -HostPath <HostApi.dll> -InstallRoot <安装根>
pwsh -NoProfile -File scripts/manage-unified-plugin.ps1 -Action select -ModuleId Part -Version 1.0.0 -HostPath <HostApi.dll> -InstallRoot <安装根>
pwsh -NoProfile -File scripts/manage-unified-plugin.ps1 -Action desired -CandidatePath <选择.json> -HostPath <HostApi.dll> -InstallRoot <安装根>
```

`select` 省略 `-Enabled` 时保留组件自身原选择；显式传 `-Enabled false` 可使移组或解除归组后的成员继续停用。`desired` 文件是 `{ "moduleId", "version", "enabled" }` 对象数组，三个字段都必须提供；一次保存列出的多项选择，其余保持原选择。例如同时停用 Workspace 和 Part：

```json
[
  { "moduleId": "Workspace", "version": "1.0.0", "enabled": false },
  { "moduleId": "Part", "version": "1.0.0", "enabled": false }
]
```

移组和解除归组在发行物所选版本的 Capsule `composition.members` 中声明，不通过工具创建平行关系表。`uninstall -ModuleId <id>` 删除该组件的内建发行声明和 installed 包；`prune -ModuleId <id> -Version <未选版本>` 仅裁剪指定 installed 包版本。两者保留安装根下独立的持久化归档，且不连接数据库或删除数据；必需成员卸载在离线入口拒绝。卸载后仍有组合引用，即使父级停用，下一次启动也会拒绝；解除引用后仍有活动依赖同样拒绝。必须在同一次停服部署中处理完整声明和选择，随后完整重启 Host 和 Shell。

成功输出 `PendingRestart` 只表示离线文件已保存。离线选择允许保存尚未闭合的组合或依赖，下一次正式 Host 在 DDL 和活动提交前验证整份候选；失败时保持 Not Ready，修正同一候选后重启，不把保存视为已部署。安装未选包不会发布活动视图。Host 与离线进程共享开关无关的安装根租约，运行期间拒绝离线写入，在线 HTTP 写入口继续返回 `501 / PLUGIN_RUNTIME_OPERATIONS_UNSUPPORTED`。中断的离线包删除可重新安装对应发行物；不要删除持久化归档或数据库对象来修复候选。

旧 `tools/PluginOps` CLI 已改为该 Host 命令的薄转发，必须增加 `--host-path <HostApi.dll>`；原 `--install-root` 和生命周期参数继续使用，但不再离线生成 runtime，也不直接改 legacy 启用字段。普通部署工具不签发 builtin 信任，信任仍来自正在执行的正式 Host 发行物。

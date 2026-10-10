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

## 工具闭环

取得与公共包同版的 Platform API release manifest 后，在外部项目根目录执行：

```powershell
dotnet newp plugin validate --root . --release-manifest <platform-api-release.json>
dotnet newp plugin explain --root . --release-manifest <platform-api-release.json> --output <explain.json>
dotnet newp plugin pack --root . --release-manifest <platform-api-release.json> --output <plugin-package>
dotnet newp plugin verify --package <plugin-package> --release-manifest <platform-api-release.json>
```

仓库内验证完整公开发布闭环：

```powershell
pwsh -NoProfile -File .\tests\integration\plugin-api\Run-PlatformApiReleaseSmoke.ps1
```

命令输出是结构化结果；失败时按 diagnostic code、stage 和 source path 修正 authoring，不绕过 release digest、依赖审计或包完整性检查。

## 生命周期边界

安装、升级、启用、停用、卸载和回滚只允许在 Host 停止期间通过离线工具改变已安装库存或期望状态；运行中的 Host 写入口稳定不支持。活动版本在随后一次 Host 启动中完成整体验证、迁移和投影后生效，Shell 只装载该次启动的静态集合。migration 成功不等于插件已经激活，禁用也不等于删除数据。

完整术语和失败语义见[Plugin Platform Context](../contexts/plugin-platform/CONTEXT.md)、[ADR-0032](../adr/0032-plugins-change-only-while-host-is-stopped.md)与[Platform Persistence Context](../contexts/platform-persistence/CONTEXT.md)。需要代码型外部插件或运行时插件运维时必须另行设计，不能扩张 `standard-data.v1` 或把离线工具暴露为在线入口。

## 停服部署入口

正式入口使用已经构建的发行 `HostApi.dll`；保持与正常启动相同的环境、配置及 `Platform:ExternalPluginsEnabled`，并明确指定正常 Host 使用的安装根：

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

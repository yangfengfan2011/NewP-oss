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

安装只改变已安装库存或期望状态；活动版本在 Host 启动期完成整体验证、迁移和投影后原子提交。migration 成功不等于插件已经激活，禁用也不等于删除数据。

完整术语和失败语义见[Plugin Platform Context](../contexts/plugin-platform/CONTEXT.md)与[Platform Persistence Context](../contexts/platform-persistence/CONTEXT.md)。需要代码型外部插件时必须另行设计 profile，不能扩张 `standard-data.v1`。

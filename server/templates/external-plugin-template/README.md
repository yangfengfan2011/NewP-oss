# 旧外部代码插件模板（legacy）

这个目录保留历史仓内代码插件骨架，只用于识别和迁移旧入口。它依赖 `server/sdk`、`ModuleBase`、Controller 与 `IServiceCollection`，不是公开 Platform API，也不得用于新的仓外插件。

新的仓外标准数据插件使用 `standard-data.v1`，以 `newp.plugin.json` 为唯一 authoring 真源；可执行基线位于 `tests/integration/plugin-api/baseline/1.0.0`。

## 迁移目标

- 删除 `ProjectReference`、`__SDK_PROJECT_PATH__` 与 NewP 源码路径绑定。
- 精确引用 `ICP.Platform.Plugin.Contracts@[1.0.0]` 与 `ICP.Platform.Plugin.Sdk@[1.0.0]`，并提交 `packages.lock.json`。
- 精确使用 `@icp-platform/plugin-sdk@1.0.0`、`ICP.Platform.Plugin.Tool@1.0.0` 及对应锁文件。
- 把 Controller、DI callback、`ModuleBase` 和自定义前端 runtime 改写为 Standard Plugin Blueprint；SQL Server 只携带受控 EF migration bundle。

## 当前目录为何仍保留

- 旧 `server/sdk` 和现有代码型加载路径尚作为仓内 legacy adapter 存在。
- B10 不把这套宽 runtime surface 冒充或发布成新的公共 interface。
- 后续 code-bearing profile 必须有独立设计和真实采用者，不能扩张 `standard-data.v1`。

## 新流程

按 `tests/integration/plugin-api/Run-PlatformApiReleaseSmoke.ps1` 的顺序执行 locked restore、build/test、`newp plugin validate/explain/pack/verify`。任何 ProjectReference、workspace/file/path/source link、平台实现依赖或 backend/frontend runtime 入口都必须稳定失败。

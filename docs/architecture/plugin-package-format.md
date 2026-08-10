# 插件包格式

本文档定义运行层插件平台当前采用的后端插件包最小格式。

## 目标

- 让宿主不再通过 `*Module.dll` 文件名扫描或模块名特判装配模块。
- 让宿主只依赖插件目录和插件清单发现可加载模块。
- 为后续外部打包、安装、校验和回滚保留稳定格式基础。

## 当前最小包结构

```text
<plugin-root>/
  plugin.json
  <entry-assembly>.dll
```

当前仓库内建模块的可提交 authoring 真源是：

- `modules/<ModuleId>/newp.plugin.json`
- `modules/<ModuleId>/metadata/**`

`apps/host-api/HostApi.csproj` 在开发期把根 Capsule 链接为输出目录中的 `modules/<ModuleId>/plugin.json`，并通过 `ProjectReference` 把模块 DLL 产出到宿主输出目录。`entryAssembly` 允许使用相对路径指向该 DLL。

`apps/host-api/modules/<ModuleId>/plugin.json` 只保留给尚未完成 Capsule cutover 的历史兼容模块，不是新增模块的 authoring 入口。

这意味着：

- 正式装配协议已经切到“根 Capsule authoring + 插件目录运行时投影”
- `ProjectReference` 与 MSBuild `Content` 链接只负责开发期投影，不拥有模块声明

## `plugin.json` 字段

当前已落地的字段如下：

```json
{
  "moduleId": "Audit",
  "displayName": "审计日志",
  "version": "1.0.0",
  "description": "开发期内建插件清单",
  "entryAssembly": "../../AuditModule.dll",
  "dependencies": [],
  "hostVersion": {
    "min": "1.0.0",
    "max": "2.0.0"
  },
  "entryAssemblySha256": "可选的入口程序集 SHA-256"
}
```

字段说明：

- `moduleId`
  插件对应的模块唯一标识，应与模块实现中的 `ModuleId` 一致。
- `displayName`
  插件显示名称，用于日志与治理信息展示。
- `version`
  插件版本。
- `description`
  可选说明信息。
- `entryAssembly`
  入口程序集路径，当前按插件目录相对路径解析。
- `dependencies`
  插件声明依赖的其他模块 ID 列表，当前先完成解析，依赖装配策略后续补齐。
- `hostVersion`
  宿主兼容约束，当前支持 `min` / `max`。
- `entryAssemblySha256`
  可选。若声明，则宿主会在运行时装载前校验入口程序集 SHA-256；若未声明，则跳过该校验。

## 当前约束

- 一个插件目录当前只承载一个后端模块入口程序集。
- 清单必须命名为 `plugin.json`。
- 入口程序集必须能被宿主解析为实际文件路径。
- 宿主兼容失败、被禁用或哈希校验失败时，插件都会被跳过并记录日志。

## Phase 6.6 统一插件包补充

在当前后端插件目录格式之外，Phase 6.6 已补上“统一插件包”作为交付单元的最小闭环。

当前统一插件包目录：

```text
<plugin-package>/
  plugin.json
  backend/
    plugin.backend.json
    <entry-assembly>.dll
  frontend/
    plugin.frontend.json
    manifest.json
    assets/
      ...
  checksums/
    sha256sums.json
```

当前安装目录布局：

```text
<install-root>/
  packages/<ModuleId>/<Version>/...
  state/<ModuleId>.json
  runtime/backend-load-view/<ModuleId>/plugin.json
  runtime/frontend-plugin-manifest.json
  public/<ModuleId>/<Version>/...
```

这意味着：

- 统一插件包是交付单元，不是直接部署单元
- 安装器负责把包内容拆分成版本目录、状态目录和运行时投影视图
- 后端宿主仍然只对 `runtime/backend-load-view` 下的最小插件目录执行装载
- 前端宿主静态资源入口来自安装后投影出的 `public/` 目录
- `PluginOps` / `manage-unified-plugin` 负责 install / enable / disable / rollback 这些运维动作
- `install` 不会切换启用版本，但若重装当前已启用版本，会立即刷新 `runtime/` 与 `public/` 投影视图

当前仓库内对应入口：

- `scripts/pack-unified-plugin.*`
- `scripts/verify-unified-plugin-package.*`
- `scripts/manage-unified-plugin.*`
- `tools/PluginOps`

## 后续演进

本轮尚未完成以下内容：

- 签名校验
- 外部插件打包脚本产物规范
- 依赖冲突与安装顺序治理
- 卸载与更完整的运维治理入口

说明：

- 可选的 `entryAssemblySha256` 字段已经进入正式运行时校验
- 当前统一插件包已经包含前端资源清单、安装状态和运行时投影视图
- 当前仍未实现签名体系，也未由单侧后端打包脚本自动生成哈希字段

这些能力后续在插件打包、治理与前端插件化阶段补齐。

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

当前仓库内建模块在开发期采用过渡形态：

- 清单文件位于 `apps/host-api/modules/<ModuleId>/plugin.json`
- 构建后复制到 `apps/host-api/bin/<Configuration>/<TFM>/modules/<ModuleId>/plugin.json`
- 清单中的 `entryAssembly` 允许使用相对路径回指开发期输出目录中的模块 DLL

这意味着：

- 正式装配协议已经切到“插件目录 + 清单”
- 当前 `ProjectReference` 仅用于开发期将内建模块 DLL 产出到宿主输出目录

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

## 后续演进

本轮尚未完成以下内容：

- 签名校验
- 前端资源清单
- 外部插件打包脚本产物规范
- 依赖冲突与安装顺序治理

说明：

- 可选的 `entryAssemblySha256` 字段已经进入正式运行时校验
- 当前仍未实现签名体系，也未由打包脚本自动生成哈希字段

这些能力后续在插件打包、治理与前端插件化阶段补齐。

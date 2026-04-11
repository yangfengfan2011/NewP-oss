# Phase 6.6 运行层插件平台验证记录

## 1. 目标

本文档用于为 `Phase 6.6` 提供独立验证入口，收拢运行层插件平台最小闭环的既有验证证据。

本记录聚焦 `plugin.json` 驱动的发现、装载、打包、校验、安装与运行时投影视图，不重复覆盖后续 `WP1` 插件治理增强中的更细粒度治理测试。

## 2. 覆盖范围

- 后端插件目录扫描与 `plugin.json` 解析
- 宿主版本兼容校验与入口程序集加载
- 内建模块通过插件目录装载
- 标准插件目录打包与校验
- 统一插件包打包、校验、安装与运行时投影
- 外部插件模板生成、构建、打包与校验链路

## 3. 既有验证入口

基础构建与测试：

- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj -v minimal`
- `dotnet build .\apps\host-api\HostApi.csproj -v minimal`

标准插件目录打包与校验：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\pack-plugin.ps1 -ModuleName Audit -Force`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-plugin-package.ps1 -PackagePath .\artifacts\plugin-packages\Audit`

统一插件包打包与校验：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\pack-unified-plugin.ps1 -ModuleName Audit -Configuration Release -Force`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-unified-plugin-package.ps1 -PackagePath .\artifacts\plugin-packages\Audit`

统一插件包生命周期验证：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\manage-unified-plugin.ps1 -Action install -PackagePath .\artifacts\plugin-packages\Audit -InstallRoot .\artifacts\installed-plugins`
- 后续继续对同一安装根执行 `enable / disable / uninstall / rollback`，并检查 `runtime/`、`state/` 与 `public/` 目录是否同步变化

外部插件模板链路：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\new-module.ps1 -ModuleName PartnerSample -DisplayName "合作方示例插件" -ExternalPlugin -OutputPath external-plugins/PartnerSample`
- `dotnet build .\external-plugins\PartnerSample\PartnerSample.csproj`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\pack-plugin.ps1 -ProjectPath .\external-plugins\PartnerSample\PartnerSample.csproj -Force`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-plugin-package.ps1 -PackagePath .\artifacts\plugin-packages\PartnerSample`

## 4. 已确认结果

- `Kernel.Tests` 与 `HostApi` 构建入口已通过
- 宿主启动日志已确认通过插件目录发现并装载 `6` 个内建后端模块
- `pack-plugin` / `verify-plugin-package` 已对现有模块完成真实打包校验
- `pack-unified-plugin` / `verify-unified-plugin-package` 已对 `Audit` 完成真实打包校验
- `manage-unified-plugin` 已对 `Audit` 完成真实 `install / enable / disable / uninstall / rollback` 操作，并验证运行时投影视图与状态目录同步变化
- 宿主已在 `Platform__PluginInstallRoot=artifacts/installed-plugins` 下完成安装结果驱动的烟测启动
- 外部插件样例已通过脚手架生成、构建、打包与校验链路验证

## 5. 环境与观察点

运行时投影检查的关键路径：

- `artifacts/installed-plugins/state/<ModuleId>.json`
- `artifacts/installed-plugins/runtime/backend-load-view/<ModuleId>/plugin.json`
- `artifacts/installed-plugins/runtime/frontend-plugin-manifest.json`
- `artifacts/installed-plugins/public/<ModuleId>/<Version>/manifest.json`

宿主运行观察点：

- `Platform__PluginInstallRoot`
- 宿主启动日志中的已装载模块数与被跳过插件日志

## 6. 当前边界

- `ProjectReference` 仍保留在 `HostApi.csproj` 中，作为开发期 DLL 产出过渡机制
- 统一插件包打包脚本当前仍以 `Audit` 为 Phase 1 验证对象，不代表所有模块都已收口
- 哈希/签名校验、更完整的前端插件治理与版本感知依赖仍未纳入本阶段
- 外部插件开发当前仍使用仓库内 `server/sdk/Sdk.csproj` 作为开发期 SDK 引用

## 7. 与后续治理增强的关系

`Phase 6.6` 负责证明“插件平台最小闭环已跑通”；
后续 `WP1` 负责把该闭环收成“依赖可治理、生命周期可约束、rollback 可恢复”的增强治理。

因此：

- 本文档是 `Phase 6.6` 的独立验证入口
- 更细粒度的依赖治理、版本清理与失败恢复验证继续参考 `docs/verification/plugin-governance-enhancement-verification.md`

## 8. 结论

`Phase 6.6` 已具备独立、可复用的验证入口，不再只依赖状态摘要来表达“插件平台最小闭环已完成”。

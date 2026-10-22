# 前端模块活动目录加载

本文档说明 Shell 当前唯一受支持的前端模块发现与装配路径。模块所有权与投影决策以 GitHub Issue #5 为决策源，本文只记录已经落地的运行时事实。

## 唯一发现入口

Shell 启动时只读取：

```http
GET /plugins/active-module-runtime.json
```

该活动目录由 Host 本次启动的 `Canonical Module Plan` 生成，包含模块身份、版本、依赖、前端入口和 behavior binding 信息。frontend、metadata、localization compatibility projection 也由同一启动期 plan 生成，并记录 `snapshotId` 与 `projectionHash` 供诊断；当前支持边界不允许 Host 运行期间切换这些投影。

对应实现：

- 计划编译：`server/kernel/ModulePlanning/CanonicalModulePlanCompiler.cs`
- 活动目录生成：`server/kernel/ModulePlanning/ActiveModuleRuntimeCatalogBuilder.cs`
- 运行时投影：`server/kernel/PluginPackages/RuntimeViewMaterializer.cs`
- Shell 加载：`frontend/packages/shell/src/active-module-runtime-loader.ts`
- 原子注册：`frontend/packages/shell/src/registerModules.ts`
- fatal / retry：`frontend/packages/shell/src/bootstrap.ts`

旧的 `frontend-plugin-manifest.json`、metadata manifest 和 localization manifest 是同一计划生成的 compatibility projection，不再承担独立模块发现职责。

## 启动与失败语义

Shell 按以下顺序启动：

1. 获取并校验 Active Module Runtime Catalog 的基本结构。
2. 按目录中的 required frontend entries 导入全部模块。
3. behavior binding 模式校验目录声明的 behavior key 与模块导出一致。
4. 所有入口都成功后，调用 `ModuleRegistry.replaceAll` 一次注册本次启动的完整静态集合。
5. 初始化全部模块并渲染正常 Shell。

任一目录读取、入口导入或 behavior key 校验失败时：

- 不提交部分模块、菜单或路由；
- 不回退到旧 manifest 或手工业务模块列表；
- 不补注册所谓 host-managed 模块；
- Shell 只显示非模块的 fatal / retry 页面；
- retry 会重新读取同一次 Host 启动公开的静态集合；它不是运行时插件切换入口。

## 内建与外部入口

随平台交付的内建模块由 Host 构建输出中的 capsule transport 声明进入候选集；本机专用原生接纳入口登记的库存复用同一可信候选与 Canonical Plan。可信来源与前端交付方式相互独立，当前两类入口都由活动目录选择：

- `moduleId`：指向 Vite 管理的内建源码入口；Shell 的 import registry 只提供可导入实现，不决定活动模块集合。
- `url`：指向验证安装资产物化后的入口；原生发行使用此方式而保留可信来源。默认公共根是 `/plugin-assets`，由 `Platform:FrontendPluginBaseUrl` 配置；正式目标可使用 `/plugins/runtime/assets/<ModuleId>/<Version>/frontend/entry.js`。

原生 URL 只公开包内 `frontend` 子树，包括已验证的懒加载 chunk 与资源；后端、metadata 和迁移由各自消费者读取。Shell 构建提供 `/shared-runtime` 与 HTML import map，使原生入口消费同一 SDK、Core、React 与 React DOM 模块实例。Shell 与正式组件 builder 使用同一受控平台运行时发行声明，构建时核对实际解析依赖的精确版本；普通外部 authoring 仍保持既有公开 profile 边界。

新增内建模块不修改 Shell 业务列表。迁移期 legacy 模块在 `apps/host-api/frontend/modules/<module-slug>/plugin.json` 提供 transport 声明；完成 capsule cutover 的模块以 `<组件源码根>/newp.plugin.json` 为根 authoring owner。Organization 是首个 behavior-binding adopter。

## 所有权边界

- module identity、version、dependencies：Module Capsule 根声明。
- route、menu、permission reference、model/view key：authoring metadata / security 声明。
- React page、renderer、action handler：TypeScript behavior bindings。
- runtime URL、projection references、snapshot/hash：ModuleProjectionCompiler 生成。
- Shell：只加载活动目录、原子提交 registry，并承载 fatal/retry；不维护业务模块 ID 特判。

legacy frontend modules 仍由兼容 adapter 装载，但不得作为新增 canonical interface 的模板。新 adopter 应逐步把 TS 中的 identity、route、menu 和 permission 声明迁回其唯一 authoring owner。

## 当前边界

当前只支持“停服部署 -> Host 启动期编译与装载 -> Shell 一次注册”的静态流程。运行中的安装、升级、启用、停用、卸载和回滚入口稳定不支持；插件运维前端不进入活动目录。跨快照 identity gate、运行中原子切换和回滚必须由未来插件运维设计重新建立，不能从现存 desired/active/LKG 数据结构推断为已支持能力；见 ADR-0032。

公开外部 authoring 仅支持声明式 `standard-data.v1`；它不向外仓开放任意后端程序集或前端运行时代码。公开包与工具链可用不等于运行时插件运维能力。

代码型外部 profile 必须另行设计进程隔离、受控 DI、前端共享依赖和失败域，不得在 Shell 中提前复制这些职责。

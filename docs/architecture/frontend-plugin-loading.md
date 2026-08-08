# 前端模块活动目录加载

本文档说明 Shell 当前唯一受支持的前端模块发现与装配路径。模块所有权与投影决策以 GitHub Issue #5 为决策源，本文只记录已经落地的运行时事实。

## 唯一发现入口

Shell 启动时只读取：

```http
GET /plugins/active-module-runtime.json
```

该活动目录由 `Canonical Module Plan` 生成，包含同一 `snapshotId` / `planHash` 下的模块身份、版本、依赖、前端入口和 behavior binding 信息。frontend、metadata、localization compatibility projection 也由同一 plan 生成，并携带相同的 `snapshotId` 与 `projectionHash`。

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
4. 所有入口都成功后，调用 `ModuleRegistry.replaceAll` 一次提交完整快照。
5. 初始化全部模块并渲染正常 Shell。

任一目录读取、入口导入或 behavior key 校验失败时：

- 不提交部分模块、菜单或路由；
- 不回退到旧 manifest 或手工业务模块列表；
- 不补注册所谓 host-managed 模块；
- Shell 只显示非模块的 fatal / retry 页面；
- retry 会重新读取并重新装载整份快照。

## 内建与外部入口

内建模块由 Host 构建输出中的 capsule transport 声明进入候选集。当前两类入口都由活动目录选择：

- `moduleId`：指向 Vite 管理的内建源码入口；Shell 的 import registry 只提供可导入实现，不决定活动模块集合。
- `url`：指向安装包物化到 `/plugin-assets/<ModuleId>/<Version>/...` 的外部入口。

新增内建模块不修改 Shell 业务列表。迁移期 legacy 模块在 `apps/host-api/modules/<ModuleId>/plugin.json` 提供 transport 声明；完成 capsule cutover 的模块以 `modules/<ModuleId>/newp.plugin.json` 为根 authoring owner。Organization 是首个 behavior-binding adopter。

## 所有权边界

- module identity、version、dependencies：Module Capsule 根声明。
- route、menu、permission reference、model/view key：authoring metadata / security 声明。
- React page、renderer、action handler：TypeScript behavior bindings。
- runtime URL、projection references、snapshot/hash：ModuleProjectionCompiler 生成。
- Shell：只加载活动目录、原子提交 registry，并承载 fatal/retry；不维护业务模块 ID 特判。

legacy frontend modules 仍由兼容 adapter 装载，但不得作为新增 canonical interface 的模板。新 adopter 应逐步把 TS 中的 identity、route、menu 和 permission 声明迁回其唯一 authoring owner。

## 当前边界

本阶段已经实现确定性 Canonical Module Plan、共同快照投影、Organization behavior binding 和 Shell all-or-nothing 注册。desired/active/LKG 生命周期、外仓公开 SDK 与完整外部插件闸门由后续实施 Issue 承担；不得在 Shell 中提前复制这些职责。

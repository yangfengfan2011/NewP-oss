# Phase 6.7 插件运维中心验证记录

## 1. 目标

本文档用于为 `Phase 6.7` 提供独立验证入口，收拢插件运维中心 V1 的既有验证证据。

本记录关注“网站入口 + HostApi 运维接口 + 统一插件包内核复用”的首版闭环，不重复覆盖后续 `WP1` 中新增的治理增强测试细节。

## 2. 覆盖范围

- `HostApi` 中的 `PluginOperationsController`
- `PluginOperationsAdminService` / `PluginUploadService` / `PluginOperationsLifecycleService`
- `frontend/modules/plugin-ops`
- `/plugin-ops` 页面
- `/api/plugin-ops/*` 接口
- 上传校验、安装、启用、禁用、卸载、回滚的站内运维闭环

## 3. 既有验证入口

基础验证入口：

- `pnpm --dir .\frontend\packages\shell test`
- `pnpm --dir .\frontend build`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter "FullyQualifiedName~PluginOperationsAdminServiceTests|FullyQualifiedName~PluginOperationsControllerTests|FullyQualifiedName~HostApiPluginManifestRouteTests" -nr:false -v minimal`
- `dotnet build .\apps\host-api\HostApi.csproj`

补充治理增强入口：

- `docs/verification/plugin-governance-enhancement-verification.md`

该补充入口用于承接 `uninstall`、warning tone 与失败恢复等后续增强，不替代本阶段的独立验证文档。

## 4. 已确认结果

- `frontend/packages/shell` 测试入口已通过
- 前端工作区构建已通过
- `PluginOperationsAdminServiceTests`、`PluginOperationsControllerTests`、`HostApiPluginManifestRouteTests` 已通过
- `HostApi` 构建已通过
- 页面已接入真实 API，可完成：
  - 模块状态查看
  - 统一插件包上传校验
  - `install / enable / disable / uninstall / rollback`
- 前端侧不再依赖未实际发放的额外权限点做显示拦截，而是由后端 `AdminOnly` 继续兜底

## 5. 当前边界

- 当前网页入口聚焦统一插件包已存在的运维动作，不包含源码一键打包、签名校验或更细粒度的多角色治理
- 前端静态资源与运行时清单会随 install / enable / disable / uninstall / rollback 更新
- 后端程序集若已在运行，仍可能需要重启宿主才能按新视图重载

## 6. 关联实现文档

- `docs/modules/plugin-ops-implementation.md`
- `docs/guides/unified-plugin-package-operations.md`
- `docs/superpowers/specs/2026-04-08-plugin-operations-center-v1-design.md`

## 7. 结论

`Phase 6.7` 已具备独立实现说明与独立验证入口，插件运维中心不再只依赖 `docs/current-status.md` 中的摘要段落表达其实现与验证结果。

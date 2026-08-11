# PluginOps 实现概览

## 概述

`PluginOps` 是 `Phase 6.7` 的站内插件运维入口，用于把统一插件包的既有脚本和 CLI 能力收敛到宿主网站中。

它不是另一套平行插件系统，而是对现有统一插件包内核的管理面封装。

## 实现目标

- 提供 `/plugin-ops` 页面，查看模块状态并执行管理员运维动作
- 提供 `HostApi` 管理接口，承接上传校验、安装、启用、禁用、卸载和回滚
- 复用既有统一插件包内核，而不是重新实现安装器、状态机或 runtime materializer

## 后端结构

后端入口位于 `apps/host-api`：

- `Controllers/PluginOperationsController.cs`
  - 提供 `GET /api/plugin-ops/modules`
  - 提供 `GET /api/plugin-ops/modules/{moduleId}`
  - 提供 `POST /api/plugin-ops/uploads/verify`
  - 提供 `POST /api/plugin-ops/uploads/{uploadId}/install`
  - 提供 `POST /api/plugin-ops/modules/{moduleId}/enable`
  - 提供 `POST /api/plugin-ops/modules/{moduleId}/disable`
  - 提供 `DELETE /api/plugin-ops/modules/{moduleId}`
  - 提供 `POST /api/plugin-ops/modules/{moduleId}/rollback`
- `PluginOperations/PluginOperationsAdminService.cs`
  - 从 `InstalledPluginStateStore` 读取状态
  - 将状态映射为前端消费的 `PluginModuleStatusDto`
- `PluginOperations/PluginUploadService.cs`
  - 接收 zip 上传流
  - 将包解压到 `<install-root>/uploads/<uploadId>/`
  - 调用 `UnifiedPluginPackageScanner` 校验统一插件包
  - 持久化 `upload-record.json`
- `PluginOperations/PluginOperationsLifecycleService.cs`
  - 承接 `install / enable / disable / uninstall / rollback`
  - 调用 `UnifiedPluginPackageInstaller`、`UnifiedPluginPackageOperations`
  - 在生命周期操作后刷新 `RuntimeViewMaterializer`
  - 对 `enable / disable / uninstall / rollback` 提供失败恢复保护

`Program.cs` 中负责：

- 注册 `PluginOperationsAdminService`
- 注册 `PluginUploadService`
- 注册 `PluginOperationsLifecycleService`
- 构造 `RuntimeViewMaterializer`
- 暴露 `/plugins/frontend-plugin-manifest.json`
- 将 `public/` 目录挂载到 `Platform:FrontendPluginBaseUrl`

## 前端结构

前端入口位于 `frontend/modules/plugin-ops`：

- `src/pages/PluginOperationsCenter.tsx`
  - 页面主体
  - 加载模块状态
  - 执行上传校验
  - 触发安装、启用、禁用、卸载、回滚
  - 展示最近反馈与状态摘要
- `src/api/pluginOperationsClient.ts`
  - 封装 `/api/plugin-ops/*` 请求
  - 统一处理 JSON 错误返回
- `src/types/pluginOperations.ts`
  - 定义模块状态、上传结果、生命周期动作结果等 DTO
- `src/index.ts`
  - 模块导出入口

页面当前提供三块能力：

1. 当前状态区
2. 上传与校验区
3. 模块生命周期操作区

## 核心边界

### 权限边界

- 页面是登录后可见入口
- 真正的管理员边界由后端 `AdminOnly` 策略控制

### 上传边界

- 客户端不能提交任意服务器路径
- 上传包必须先进入 `<install-root>/uploads/<uploadId>/`
- 安装动作只能从已验证通过的 `uploadId` 发起

### 运行时刷新边界

- 安装、启用、禁用、卸载、回滚会刷新：
  - `runtime/backend-load-view`
  - `runtime/frontend-plugin-manifest.json`
  - `public/<ModuleId>/<Version>/...`
- 如果后端程序集已经在运行，仍可能需要重启宿主才能按新视图重新装载程序集

## 失败恢复语义

- 上传校验失败时，不进入安装态
- `install` 失败时，不返回成功状态
- `enable / disable / uninstall / rollback` 失败时，不保留半更新运行视图
- `PluginOperationsLifecycleService` 在 materializer 失败时会尝试：
  - 恢复之前的 `InstalledPluginRecord`
  - 恢复模块文件快照
  - 重建之前的运行时投影

## 与既有脚本/CLI 的关系

`PluginOps` 网页入口复用了现有能力，而不是替代它们：

- `scripts/verify-unified-plugin-package.*`
- `scripts/manage-unified-plugin.*`
- `tools/PluginOps`
- `InstalledPluginStateStore`
- `RuntimeViewMaterializer`

脚本与 CLI 仍继续保留，用于：

- 非页面化运维
- 故障排查
- 自动化脚本

## 当前边界

- 页面聚焦统一插件包现有运维动作，不包含源码一键构建或签名校验
- 不提供多角色治理模型
- 不承诺后端热更新
- 运维页 warning tone 当前仍通过消息语义推断，后续如需统一 severity 字段，应归入后续体验增强任务

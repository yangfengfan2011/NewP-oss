# 插件运维中心 V1 设计

日期：2026-04-08

## 背景

当前仓库已经具备统一插件包 Phase 1 的最小闭环：

- 统一插件包打包与校验
- 安装目录、状态目录与版本化安装
- `install / enable / disable / rollback`
- 后端 `runtime/backend-load-view`
- 前端 `runtime/frontend-plugin-manifest.json`
- 前端静态资源投影目录 `public/`

但这些能力目前主要通过脚本和 `PluginOps` CLI 暴露：

- `scripts/pack-unified-plugin.*`
- `scripts/verify-unified-plugin-package.*`
- `scripts/manage-unified-plugin.*`
- `tools/PluginOps`

这使得统一插件包已经“能运维”，但还没有进入平台自己的 Web 管理入口。管理员仍需要登录服务器或使用命令行完成上传、校验、安装、启用、禁用和回滚，这不符合长期的平台化方向。

本设计定义一个稳妥版 V1：把统一插件包运维闭环收进网站，仅面向管理员开放，但不把“源码一键构建打包”一并纳入第一版。

## 目标

V1 目标：

- 在网站中提供统一插件包运维入口
- 支持管理员上传 unified plugin package
- 支持服务端校验上传包
- 支持管理员在网页内完成：
  - 安装
  - 启用
  - 禁用
  - 回滚
- 支持查看模块当前安装/启用状态
- 复用现有统一插件包 Phase 1 内核能力，不重写一套平行逻辑

## 非目标

本轮不包含：

- 从源码一键构建统一插件包
- 从构建产物自动打包
- 卸载
- 包签名与发布者信任链
- 多角色运维权限模型
- 多节点或多宿主分发
- 后端程序集热更新
- 审批流

## 用户与权限

唯一用户角色：

- 平台管理员

V1 权限规则：

- 整个“插件运维中心”页面仅管理员可见
- 所有插件运维 API 仅管理员可调用
- 不做更细粒度的运维角色拆分

## 核心原则

### 1. 网站是运维入口层，不是另一套插件系统

页面和 API 的职责是把已有统一插件包运维能力转成平台内管理入口，而不是重新实现一套安装器、状态机和 runtime materializer。

### 2. 上传、校验、安装必须显式分步

管理员上传包后，系统先做校验。只有校验成功，才允许执行安装。V1 不允许“上传即安装”或“上传即启用”。

### 3. 正式安装目录与上传临时目录必须隔离

上传包先保存到独立临时上传区；安装动作只能从“已校验通过的上传包”进入正式安装目录，不能允许前端直接提交任意服务器路径。

### 4. 运维动作必须复用现有内核

后台 API 应复用以下现有能力：

- `UnifiedPluginPackageScanner`
- `UnifiedPluginPackageInstaller`
- `UnifiedPluginPackageOperations`
- `InstalledPluginStateStore`
- `RuntimeViewMaterializer`

### 5. 后端运行时刷新边界必须明确

前端运行时清单与静态资源投影会随 install/enable/disable/rollback 更新；但后端程序集若已在运行，仍遵守当前 Phase 1 边界，必要时需要重启宿主以让后端按新视图重新装载。

## V1 总体方案

V1 采用“网站上传 + HostApi 运维 API + 统一插件包内核复用”的模式。

高层流程如下：

1. 管理员进入“插件运维中心”
2. 查看当前模块安装与启用状态
3. 上传 unified plugin package
4. 服务端保存到临时上传目录
5. 服务端执行统一插件包校验
6. 校验通过后，管理员点击安装
7. 服务端执行安装，并按当前规则更新状态与运行时投影
8. 管理员继续执行启用、禁用或回滚
9. 页面刷新显示当前状态和最近一次结果

## 页面设计

V1 页面建议为单页管理台，仅管理员可见，分为 3 个区域。

### 1. 当前状态区

展示每个模块的运维状态：

- `moduleId`
- `displayName`
- `installedVersions`
- `currentEnabledVersion`
- 状态摘要
- 最近一次操作结果
- 最近一次错误

状态摘要建议统一为：

- `NotInstalled`
- `Installed`
- `Enabled`
- `Failed`

此区域的目标是让管理员先知道：

- 当前有哪些插件模块已进入安装态
- 当前启用了哪个版本
- 是否存在失败状态
- 下一步可以执行哪些动作

### 2. 上传与校验区

管理员上传 unified plugin package 后，页面显示：

- 包基本信息
  - `moduleId`
  - `displayName`
  - `version`
- 校验结果
  - 成功 / 失败
  - 失败原因列表
- 若校验通过，显示 `Install` 按钮

上传与校验区只负责“进入系统并验证”，不负责默认启用。

### 3. 运维操作区

针对模块执行以下动作：

- `Install`
- `Enable`
- `Disable`
- `Rollback`

交互规则：

- `Install`
  - 仅对“当前已上传且校验成功的包”可执行
- `Enable`
  - 必须明确指定版本
- `Disable`
  - 仅当当前存在启用版本时可执行
- `Rollback`
  - 仅当当前存在可回滚的前一版本时可执行

每个操作执行后，页面应：

- 展示成功/失败反馈
- 展示失败详情
- 自动刷新模块状态

## 后端 API 设计

建议由 HostApi 提供一组新的管理员 API。

### 1. `GET /api/plugin-ops/modules`

用途：

- 获取模块运维列表页数据

返回字段建议包含：

- `moduleId`
- `displayName`
- `installedVersions`
- `currentEnabledVersion`
- `status`
- `lastOperation`
- `lastError`
- `canEnable`
- `canDisable`
- `canRollback`

### 2. `GET /api/plugin-ops/modules/{moduleId}`

用途：

- 获取单个模块详细状态

### 3. `POST /api/plugin-ops/uploads/verify`

用途：

- 上传并校验 unified plugin package

流程：

- 接收上传文件
- 保存到临时上传目录
- 触发统一插件包校验
- 返回校验结果与包元数据

返回字段建议包含：

- `uploadId`
- `moduleId`
- `displayName`
- `version`
- `isValid`
- `errors`

### 4. `POST /api/plugin-ops/uploads/{uploadId}/install`

用途：

- 对已校验通过的上传包执行安装

返回字段建议包含：

- `moduleId`
- `version`
- `status`
- `message`

### 5. `POST /api/plugin-ops/modules/{moduleId}/enable`

请求体：

```json
{
  "version": "1.0.0"
}
```

用途：

- 启用指定已安装版本

### 6. `POST /api/plugin-ops/modules/{moduleId}/disable`

用途：

- 禁用当前启用版本

### 7. `POST /api/plugin-ops/modules/{moduleId}/rollback`

用途：

- 回滚到上一已安装版本

## 服务端内部结构

建议新增一个应用层服务，例如：

- `PluginOperationsAdminService`

职责：

- 查询模块状态
- 管理上传目录
- 校验上传包
- 安装上传包
- 执行启用/禁用/回滚
- 统一封装错误语义与返回模型

控制器不应直接操作文件系统和状态目录；所有插件运维动作通过该服务进入现有内核能力。

## 上传与安装目录设计

### 上传临时目录

建议目录：

```text
<install-root>/uploads/<uploadId>/
```

规则：

- 上传目录与正式安装目录隔离
- 上传完成后先在此目录校验
- `install` 只能对“已校验通过的 uploadId”执行
- 上传目录可由后台定期清理

### 正式安装目录

继续沿用当前统一插件包安装布局：

```text
<install-root>/
  packages/
  state/
  runtime/
  public/
```

V1 不改变现有安装模型。

## 状态模型

前端列表建议统一消费一个简化 DTO：

```json
{
  "moduleId": "Audit",
  "displayName": "审计日志",
  "installedVersions": ["1.0.0", "1.1.0"],
  "currentEnabledVersion": "1.1.0",
  "status": "Enabled",
  "lastOperation": "Enable",
  "lastError": null,
  "canEnable": true,
  "canDisable": true,
  "canRollback": true
}
```

这样前端页面不需要重复推导运维逻辑。

## 安全边界

### 1. 路径安全

- 客户端不能提交任意服务器路径参与安装
- 所有上传包必须进入平台指定临时目录
- 安装动作只能从已登记的 `uploadId` 发起

### 2. 操作安全

- `verify`、`install`、`enable` 分步执行
- 所有高风险动作必须由管理员显式点击触发
- 不做隐式自动启用

### 3. 文件内容安全

- 上传后必须先校验统一插件包格式
- 校验失败不得进入安装态
- 继续沿用现有 scanner / installer 的 child manifest、引用文件与 unsafe path 校验

## 失败语义

V1 与现有 CLI / PluginOps 语义保持一致：

- `verify` 失败
  - 不产生安装记录
  - 返回明确错误列表
- `install` 失败
  - 不返回成功状态
  - 不污染已安装状态
- `enable / disable / rollback` 失败
  - 不保留半更新运行视图
  - 若 runtime view 重建失败，则尝试恢复上一个稳定状态

前端页面必须完整展示失败原因，不应只提示“操作失败”。

## 审计与日志

V1 至少要求：

- 应用日志记录
  - 管理员身份
  - 操作类型
  - 模块
  - 版本
  - 成功/失败
- 审计能力至少要能在现有审计基础上记录：
  - 上传校验
  - 安装
  - 启用
  - 禁用
  - 回滚

如果当前没有独立审计表，V1 可以先以结构化日志和现有审计链路落地，但不得完全无痕。

## 宿主行为边界

V1 页面与文档必须清楚说明：

- install / enable / disable / rollback 会更新安装状态与运行时视图
- 前端运行时 manifest 与静态资源投影会同步更新
- 后端程序集若已在运行，仍可能需要宿主重启才能按新视图完成重装载

这个边界是当前 Phase 1 的正式产品边界，不在 V1 中隐式承诺热更新。

## 实施切分

建议分 4 个实现块推进。

### 阶段 1：状态查询

包含：

- `GET /api/plugin-ops/modules`
- `GET /api/plugin-ops/modules/{moduleId}`

目标：

- 先把“当前状态可见”做稳
- 为前端页面骨架提供数据

### 阶段 2：上传与校验

包含：

- 上传统一插件包
- `POST /api/plugin-ops/uploads/verify`

目标：

- 打通网页上传能力
- 明确校验结果和失败反馈

### 阶段 3：安装与生命周期操作

包含：

- `POST /api/plugin-ops/uploads/{uploadId}/install`
- `POST /api/plugin-ops/modules/{moduleId}/enable`
- `POST /api/plugin-ops/modules/{moduleId}/disable`
- `POST /api/plugin-ops/modules/{moduleId}/rollback`

目标：

- 完成网页内的运维闭环

### 阶段 4：前端管理页收口

包含：

- 管理页面
- 上传面板
- 状态列表
- 操作按钮
- 结果反馈
- 管理员权限接入

目标：

- 让管理员不再需要进入命令行处理统一插件包运维

## 风险与控制

### 风险 1：把网页后台演进成构建系统

控制：

- V1 明确不做源码一键构建
- V1 仅覆盖上传、校验、安装和生命周期治理

### 风险 2：上传目录与正式安装目录混用

控制：

- 目录分离
- 通过 `uploadId` 驱动 install，而不是用户传路径

### 风险 3：失败信息不透明，管理员无法判断问题

控制：

- API 返回结构化错误
- 页面展示具体失败原因

### 风险 4：页面承诺了“后端热更新”

控制：

- 在页面和文档中明确 Phase 1 边界
- 将“需要重启宿主”作为正式说明的一部分

## 验收标准

V1 完成时，应满足：

- 管理员可在网站内上传 unified plugin package
- 上传包可在服务端完成校验并返回明确结果
- 管理员可在网页内完成 install / enable / disable / rollback
- 页面能正确显示安装版本与当前启用版本
- API 复用现有统一插件包内核，不形成平行实现
- 失败时页面可见明确原因
- 文档明确说明后端运行时刷新边界

## 与现有统一插件包能力的关系

本设计不替代现有脚本与 CLI，而是把以下能力产品化为网站入口：

- `verify-unified-plugin-package`
- `manage-unified-plugin`
- `InstalledPluginStateStore`
- `RuntimeViewMaterializer`

命令行入口在 V1 之后仍然保留，用于：

- 非页面化运维
- 故障排查
- 自动化脚本

网站入口是对现有统一插件包 Phase 1 能力的管理面封装，而不是另起一套插件治理体系。

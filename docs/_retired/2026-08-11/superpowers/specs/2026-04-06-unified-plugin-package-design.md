# 统一插件包设计

日期：2026-04-06

## 背景

当前仓库已经补齐了两条最小主线：

- 后端已具备基于 `plugin.json` 的插件目录发现和运行时装载能力
- 前端已具备基于 `runtime manifest` 的运行时装配能力，研发期可回退到 `builtin-runtime-plugins.ts`

这解决了“模块可运行时装配”的问题，但仍未解决最终目标需要的能力：

- 仓库外独立交付
- 第三方或内部团队单独发布插件
- 前后端版本一致性交付
- 来源校验、启停、升级、回滚、兼容治理
- 后续拆分部署、静态资源独立发布和负载均衡演进

本设计定义统一插件包的 V1 形态，优先支持“内部团队私有分发”。

## 目标

V1 目标：

- 支持“前后端统一插件包”的独立交付
- 支持宿主本机统一安装
- 支持安装、启用、禁用、升级、回滚的正式生命周期
- 支持宿主兼容、契约兼容、依赖兼容和哈希校验
- 前端与 Shell 共享运行时，但必须通过受控契约接入
- 不阻碍未来拆分部署、静态资源独立发布和更强隔离

## 非目标

本轮不包含：

- 公开插件市场
- 外部第三方开放生态
- 前端 iframe / 沙箱隔离运行
- 前端远程市场分发
- 完整签名信任链
- 多租户插件隔离

## 关键原则

### 1. 统一插件包是交付单元，不是部署单元

统一插件包解决的是：

- 版本对齐
- 校验与信任
- 安装与升级
- 回滚一致性

它不强制规定最终部署拓扑。

这意味着：

- 同一个插件包可以在安装阶段被拆分落地
- 后端产物可安装到宿主插件目录
- 前端产物可安装到宿主静态资源目录
- 后续也可以演进为“后端本机安装 + 前端独立静态资源服务”

### 2. 包内路径与运行时地址必须解耦

统一插件包内应描述两类信息：

- `artifactPath`
  包内产物路径
- `runtimeLocation`
  安装后的运行位置

V1 虽然先做本机统一安装，但不得让前端运行依赖“包内相对路径直接可访问”的假设。

### 3. 前端 V1 允许共享运行时，但必须契约隔离

V1 前端插件与 Shell 共享 React、宿主 SDK 和运行时容器。

但必须满足：

- 插件通过 `FrontendModule` 契约接入
- 插件只能使用宿主暴露的受控 API
- 插件不能直接依赖 Shell 私有实现
- 插件不能自行携带一套冲突的共享运行时

### 4. 统一插件包必须面向治理，而不是面向解压即跑

插件包不是一个给人手工解压后直接运行的目录。

插件包必须进入正式生命周期：

- 安装
- 校验
- 启用
- 禁用
- 升级
- 回滚

## V1 总体方案

V1 采用：

- 前后端统一插件包
- 宿主本机统一安装
- 前端共享 Shell 运行时
- 宿主通过安装结果生成后端加载视图和前端运行时 manifest

高层流程如下：

1. 内部团队构建统一插件包
2. 运维将插件包放入宿主指定安装入口
3. 宿主或安装器执行解包、校验、安装、状态登记
4. 后端产物安装到宿主插件目录
5. 前端产物安装到宿主静态资源目录
6. 宿主根据已启用插件重建后端装载视图和前端运行时 manifest
7. 宿主启动或刷新后按统一治理状态装载插件

## 统一插件包目录格式

推荐目录格式：

```text
<plugin-package>/
  plugin.json
  backend/
    plugin.backend.json
    <entry-assembly>.dll
    ...
  frontend/
    plugin.frontend.json
    manifest.json
    assets/
      ...
  checksums/
    sha256sums.json
```

约束如下：

- 一个统一插件包只承载一个业务模块
- 根级 `moduleId` 必须唯一
- 前后端默认共用同一版本号
- 包格式必须允许未来扩展签名和发布元数据

## 根级元数据

`plugin.json` 负责描述“这是什么插件包”。

建议字段：

```json
{
  "pluginId": "icp.module.audit",
  "moduleId": "Audit",
  "displayName": "审计日志",
  "version": "1.0.0",
  "description": "统一插件包示例",
  "distribution": {
    "channel": "internal",
    "publisher": "ICP Platform Team"
  },
  "hostVersion": {
    "min": "1.0.0",
    "max": "2.0.0"
  },
  "artifacts": {
    "backend": "backend/plugin.backend.json",
    "frontend": "frontend/plugin.frontend.json"
  },
  "checksums": {
    "sha256Manifest": "checksums/sha256sums.json"
  }
}
```

说明：

- `pluginId`
  插件包级别唯一标识，面向发布与治理
- `moduleId`
  业务模块标识，后端模块和前端模块必须一致
- `version`
  统一插件包版本号
- `distribution.channel`
  V1 固定为内部私有分发
- `hostVersion`
  宿主兼容范围
- `artifacts`
  指向前后端子元数据

## 后端元数据

`backend/plugin.backend.json` 描述后端安装和装载方式。

建议字段：

```json
{
  "moduleId": "Audit",
  "entryAssembly": "AuditModule.dll",
  "dependencies": [],
  "hostApiContractsVersion": "1.0.0",
  "install": {
    "targetKind": "host-modules-directory"
  }
}
```

V1 规则：

- 后端入口程序集必须位于插件包内
- 宿主按安装结果解析真实 DLL 路径
- 后端依赖缺失或不兼容时不可启用

## 前端元数据

`frontend/plugin.frontend.json` 描述前端静态资源和运行时装配信息。

建议字段：

```json
{
  "moduleId": "audit",
  "entryManifest": "manifest.json",
  "assetRoot": "assets",
  "runtimeBaseStrategy": "host-local-static",
  "publicBasePath": "/plugins/audit/1.0.0/",
  "sharedDependencies": [
    "react",
    "react-dom",
    "@icp-platform/sdk",
    "@icp-platform/core"
  ],
  "frontendContractsVersion": "1.0.0"
}
```

关键字段说明：

- `entryManifest`
  指向前端插件运行时入口描述
- `assetRoot`
  前端资源根目录
- `runtimeBaseStrategy`
  描述运行时资源基址策略
- `publicBasePath`
  当前安装后可访问的资源前缀
- `sharedDependencies`
  需要与 Shell 共享的运行时依赖

V1 约束：

- 前端默认共享宿主运行时
- 前端运行时 manifest 优先使用受控 `moduleId`
- 前端不得自带冲突版本的共享依赖副本

## 安装抽象

V1 先支持宿主本机统一安装，但安装器必须以“拆分落地”为模型。

安装后的典型形态：

```text
<host-root>/
  plugins/
    packages/
      Audit/
        1.0.0/
          ...
    runtime/
      backend/
        Audit/
          current -> 1.0.0
      frontend/
        Audit/
          1.0.0/
            assets/...
            manifest.json
  public/
    plugins/
      Audit/
        1.0.0/
          ...
```

核心要求：

- 包产物完整保留，用于回滚和审计
- 运行时启用版本通过元数据或指针表达
- 不能直接覆盖旧版本
- 宿主启动时读取“当前启用版本”视图，而不是扫描任意目录猜测

## 生命周期与状态机

建议状态：

- `Discovered`
- `Installed`
- `Resolved`
- `Enabled`
- `Disabled`
- `Failed`
- `RolledBack`

建议操作：

- 安装
  解包、校验、写入版本目录、登记状态
- 启用
  校验兼容性，切换当前版本，重建运行视图
- 禁用
  从运行视图移除，但保留安装产物
- 升级
  并行安装新版本，通过后切换当前版本
- 回滚
  切回上一稳定版本，重建运行视图
- 卸载
  仅在未启用或已禁用状态执行

关键要求：

- 所有操作都应尽量幂等
- 后端装载视图和前端 manifest 应由同一次启用操作重建
- 不允许出现“后端切到新版本、前端仍指向旧版本”的状态漂移

## 前端运行时装配

V1 前端运行时仍然沿用当前 Shell 的主线能力，但数据来源从“开发期受控清单”升级为“安装结果驱动”。

具体要求：

- 研发期仍允许继续使用 `builtin-runtime-plugins.ts`
- 统一插件包安装后，宿主应把已启用插件汇总为前端运行时 manifest
- 正式运行时优先读取安装结果生成的 manifest
- manifest 不可用时，开发环境才允许回退到 builtin 清单

这保证：

- 内建研发期接线能力不丢
- 正式交付路径不再依赖工作区源码
- 插件平台治理状态可以直接反映到前端装配结果

## 兼容治理

V1 至少需要三层兼容校验：

### 1. 宿主兼容

- 插件声明支持的宿主版本范围
- 宿主版本不满足时不可启用

### 2. 契约兼容

- 后端声明 `hostApiContractsVersion`
- 前端声明 `frontendContractsVersion`
- 宿主校验核心契约版本是否兼容

### 3. 依赖兼容

- 插件可声明依赖的模块及版本范围
- 缺依赖不可启用
- 版本不兼容不可启用

V1 不要求完整依赖求解器，但必须提供清晰错误信息。

## 来源校验

考虑 V1 优先支持内部团队私有分发，来源校验分两步推进：

### V1 必做

- 包级哈希校验
- 关键产物哈希校验
- 安装时校验失败则拒绝进入 `Installed`

### V2 预留

- 包级签名
- 发布者信任链
- 内部证书或签名白名单

V1 即使不做签名，也必须把校验字段设计进元数据模型，避免后续重做包格式。

## 版本治理

统一规则：

- 一个插件包一个版本号
- 前后端默认共用一个版本号
- 宿主只允许一个 `moduleId` 同时启用一个版本
- 允许同一插件保留多个已安装版本用于升级和回滚
- “当前启用版本”应由状态元数据表达，而不是通过覆盖文件实现

## 前端隔离边界

V1 前端虽然共享运行时，但必须满足契约隔离：

- 插件通过 `FrontendModule` 暴露能力
- 路由、菜单、权限、初始化通过声明式契约接入
- 插件不得直接修改 Shell 私有上下文和内部实现
- 样式污染范围应尽量约束在插件自身

这为 V2 升级到更强隔离预留空间。

V2 潜在升级方向：

- iframe / 沙箱容器挂载
- 受控 bridge API
- 跨边界宿主通信

V1 设计不得把插件写死在“必须直接 import Shell 私有实现”的模式上。

## 分阶段实施建议

### Phase 1：统一插件包最小闭环

- 定义统一插件包目录格式和元数据
- 实现打包脚本
- 实现安装目录、版本目录和状态记录
- 实现哈希校验
- 实现宿主按已启用插件重建前端 manifest 和后端装载视图

### Phase 2：治理闭环

- 启用、禁用、升级、回滚命令或管理入口
- 依赖与兼容错误可视化
- 插件状态审计记录

### Phase 3：前端分发与隔离增强

- 前端静态资源独立发布能力
- 前端运行时更强隔离
- 包级签名与来源信任体系

## 风险与控制

### 风险 1：统一包误被实现成统一部署

控制：

- 明确包内路径与运行时地址解耦
- 安装器负责拆分安装

### 风险 2：前端共享运行时导致插件耦合宿主内部

控制：

- 强制走 `FrontendModule` 契约
- 明确 shared dependencies 白名单

### 风险 3：升级时前后端版本漂移

控制：

- 启用操作必须同时重建后端装载视图和前端 manifest
- 当前版本切换为原子状态变更

### 风险 4：包格式过早锁死未来扩展

控制：

- 预留签名、分发、运行策略字段
- 不把“宿主本机统一安装”写死成唯一运行拓扑

## 验收标准

V1 完成时，应满足：

- 内部团队可在仓库外构建一个统一插件包
- 运维可把统一插件包放入宿主安装入口完成安装
- 宿主能校验、登记、启用和禁用插件
- 后端和前端能基于同一插件版本一起启用
- 前端不再依赖工作区源码才能参与正式装配
- 升级失败时可切回上一稳定版本
- 设计不阻碍未来拆分部署和负载均衡演进

## 与现有文档的关系

本设计是以下文档的下一阶段收口：

- `docs/architecture/plugin-package-format.md`
- `docs/architecture/plugin-runtime-loading.md`
- `docs/architecture/frontend-plugin-loading.md`
- `docs/guides/frontend-plugin-integration.md`

这些文档描述的是当前最小闭环或单侧能力；本设计定义的是统一插件包的终局演进方向和 V1 落地边界。

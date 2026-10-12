# 内建模块开发指南

本文是新增仓内模块的唯一开发入口。模板和脚手架是当前示例；`ModuleTemplateSample` 只保留为旧契约兼容样本。

## 所有权

一个模块由三个可独立核对的所有者组成：

1. `frontend/modules/<module-slug>/newp.plugin.json` 唯一拥有 module id、显示名、版本、依赖和 behavior key。
2. `frontend/modules/<module-slug>/metadata/**` 拥有路由、菜单、权限引用、模型与本地化等静态事实。
3. 后端和前端代码只把稳定 key 绑定到服务、endpoint、页面、renderer 或 action 实现。

Host、Shell、C# 与 TypeScript 不再维护平行的模块身份和静态声明。

## 推荐流程

### 1. 先确认边界

新模块、公共 Contracts/SDK、Host/Shell 入口、持久化或安全语义变化必须先有已确认的 issue/spec，并核对[组件化护栏](../architecture/componentization-guardrails.md)。

### 2. 使用脚手架

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块"
```

生成结果包括：

```text
frontend/modules/example-module/        Capsule、metadata 与前端 binding
server/modules/ExampleModule/           后端 binding 与实现
```

内建模块由通用构建入口发现，不再编辑 HostApi 或 Shell 接线。`-SkipIntegration` 保留命令兼容，不省略 Capsule 或两侧 binding。

### 3. 实现局部能力

- Controller 只处理协议、认证授权入口和应用服务调用。
- 稳定跨模块数据形状放入模块 Contracts；模块私有实现留在模块内。
- 前端 `src/index.ts` 只导出 `FrontendBehaviorBindings`。
- 模型与 migration 按[持久化开发流程](persistence-development-workflow.md)处理。
- 共享 UI 进入 `frontend/packages/ui`，普通模块不引用 Shell 内部源码。

### 4. 验证启动期静态集合

先执行模块级构建和定向测试，再构建 Host 与前端模块。接入完成必须证明：

- Capsule、metadata 和 behavior references 能编译为同一 Canonical Module Plan；
- 冲突或缺失引用会拒绝整份候选；
- 所有 runtime 投影都来自同一启动期计划；
- 前端模块全部装载成功后才一次注册本次启动的静态集合；
- 任务涉及的权限、持久化或启动 smoke 有可复跑验证。

插件变更只允许在 Host 停止期间通过离线部署工具完成，然后重启 Host / Shell。运行中的安装、升级、启用、停用、卸载和回滚不是开发或验收入口；只读 inventory 也不代表这些写能力可用。详见 ADR-0032。

仓库级验证使用 `scripts/verify.ps1 -Quick`；全量验证去掉 `-Quick`。

## 可移动组件源码根与构建

组件身份来自 `newp.plugin.json`，源码根可以平面存放，也可以移到组合组件目录内；后端实现与迁移工程始终位于 `server/modules/<ModuleId>`。`composition.members` 是唯一成员关系事实源。发现器按最近 Capsule 根核对嵌套 owner，不要求中间目录叫 `members`；开始嵌套后，父组件的全部直属成员必须位于该源码边界内。既有平面组合仍可开发；以组合根发起的完整集合检查要求其声明后代都实际嵌套。缺失、重复、自含、多父、环或声明与目录不一致直接拒绝。

`ComponentSourceCatalog` 复用正式 Canonical 编译器校验身份与组合图；模块资源嵌入、Host metadata/迁移资源链接、模型读取、DTO 和前端入口都消费按身份发现的当前根。MSBuild 通过项目依赖构建 `server/tools/component-source-build`，在短生命周期 CLI 中生成本轮 `obj` 映射，再用内建任务读取。映射只表示当前构建的路径，不是可提交的第二份组件或成员表。项目引用显式传递调用方配置，构建工具路径来自真实 `GetTargetPath` 输出。Host 发布目录保持 `modules/<ModuleId>`；父组件自有文件枚举在独立成员根处停止。

核对当前组件和完整归组集合：

```powershell
dotnet run --project server/tools/component-source-build/ComponentSource.Build.csproj -c Release -- describe . InforCenter
dotnet build apps/host-api/HostApi.csproj -c Release -p:NewpComponentRoot=InforCenter
```

上述 `InforCenter` 命令要求其全部声明成员已经嵌套，现有平面三成员布局会明确拒绝。前端 workspace 使用递归模块发现，Vite 与 `build-frontend-plugin.mjs` 通过同一 Catalog CLI 获取稳定 entry key 对应的真实源码入口；新增或调整测试配置时，从 workspace 根解析共享包，不按组件嵌套深度维护相对路径。

源码根和位置无关构建已落地。回退需同时恢复通用构建入口与工程资源配置，已实际归组的目录按其 JSON 关系还原；不改写模型、manifest、已提交迁移或数据库历史。

## 聚合发行构建与静态核验

正式入口 `server/tools/component-bundle` 按同一 `ComponentSourceCatalog` 取得完整归组，构建原位 owner 工程与现有前端 plugin builder，再从实际产物生成索引。纯组合可以只拥有声明，不补造后端、前端、模型或迁移入口。输出目录和 ZIP 必须是新的目标；所有命令均不写安装库存、desired、active 或 authoring。

```powershell
dotnet run --project server/tools/component-bundle/ComponentBundle.csproj -c Release -- build . InforCenter <新的暂存目录>
dotnet run --project server/tools/component-bundle/ComponentBundle.csproj -c Release -- pack <暂存目录> <新的ZIP路径>
dotnet run --project server/tools/component-bundle/ComponentBundle.csproj -c Release -- inspect <暂存目录或ZIP>
```

`build` 仍要求声明与成员目录完整对应；当前平面 InforCenter 三成员输入会被拒绝。后端 Release 参数只在正式发行入口排除调试路径与仓库提交号，不改变开发调试配置；产物路径来自 MSBuild `TargetPath`，目标框架及实际共享依赖必须匹配受控平台运行时发行。私有程序集按真实引用闭包从 owner 构建输出收集，受控平台依赖与 CLR 实际共享框架由目标发行提供；未知或版本不一致的引用在消费前拒绝。迁移沿用原项目的确定性设置，打包不执行 finalize。

包根包含 `component-bundle.json`、`platform-runtime-release.json` 与 `modules/<ModuleId>/`。每个组件根保存原始 `authoring/newp.plugin.json`、派生 `plugin.json`、自身 metadata/resources、实际后端 DLL、前端完整产物，以及声明的 migration manifest/DLL 与已发布 canonical source。原 Capsule 是唯一声明真源；派生 transport 只把后端映射为 `backend/<AssemblyName>.dll`、前端映射为 `frontend/entry.js`，其他声明严格保持。检查器逐项比对该映射及后端嵌入的 Capsule，不追随原声明的历史 `../../` 程序集路径。

成员索引记录精确版本、自有内容摘要、固定相对根和每个文件的 SHA-256。组件摘要从按相对路径排序的自有资产索引计算，排除成员；整包摘要从平台发行身份及全部组件索引计算，不增加人工包版本。ZIP 使用固定条目顺序和时间戳。同 ID 同版本相同内容可复用，不同内容通过 `ComponentBundleInspector.RequireReusable` 拒绝；库存 owner 在提交前消费此规则，检查器不维护库存。

平台发行方维护 `server/plugin-platform/ComponentBundles/platform-runtime-release.json`，它拥有公共行为/持久化契约、.NET 目标、共享程序集和前端依赖的精确发行版本，并固定既有存储类型映射摘要。该受控声明嵌入平台代码，业务 HEAD、组件根位置和父组件版本均不参与。正式构建核对真实 MSBuild 输出、CLR 引用版本、现有存储输入及 Node 实际解析的前端依赖；前端 builder 从 Rollup 输出生成 `frontend-artifacts.json`，记录全部 chunk/资源引用和共享运行时引用。共享契约或依赖发行变化时由平台发行方更新此声明，不能让业务包自行增加 Host 支持的发行身份。

`inspect` 与 `ExtractVerified` 在返回候选前检查整个资产集合、相对路径、大小写碰撞、链接、所有摘要、平台身份、声明映射和前端引用闭包，并通过正式 Capsule reader/Canonical 编译器读取候选。后端检查只读取 PE/CLR metadata，不加载包内代码；模型投影、manifest 前缀、bundleChecksum 和 canonical source 复用现有持久化完整性规则。检查通过及 checksum 只证明资产一致，不授予可信原生身份、平台必需角色或执行许可。可信接纳、独立运行、文件事务与选版由 [规格 #267](https://github.com/yangfengfan2011/NewP/issues/267) 的后续交付负责。

回退可撤销构建工具与发行协议变更，删除未安装的暂存产物；不能借此改写已发布 manifest 或迁移历史，也不能把源码/ZIP 回退当作数据库回退。
## 兼容代码边界

仓库仍有少量 `ModuleBase`、`FrontendModule`、Host 私有 `plugin.json` 和旧示例模块，它们是迁移兼容路径，不是新增模块模板。普通接入不得：

- 复制 `ModuleTemplateSample` 的 legacy 前端声明；
- 在 Shell 增加业务模块数组或 module id 特判；
- 在 Host 新建私有模块清单；
- 让 metadata 与代码分别声明同一条路由、菜单或权限；
- 在模块注册副作用中隐式迁移或导入数据。

底层契约见[module-contract.md](../architecture/module-contract.md)，模板结构见[module-template.md](../architecture/module-template.md)。

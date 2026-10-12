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

源码根和位置无关构建已落地；聚合发行、可信独立安装及库存事务仍由 [规格 #267](https://github.com/yangfengfan2011/NewP/issues/267) 的后续交付负责。回退需同时恢复通用构建入口与工程资源配置，已实际归组的目录按其 JSON 关系还原；不改写模型、manifest、已提交迁移或数据库历史。
## 兼容代码边界

仓库仍有少量 `ModuleBase`、`FrontendModule`、Host 私有 `plugin.json` 和旧示例模块，它们是迁移兼容路径，不是新增模块模板。普通接入不得：

- 复制 `ModuleTemplateSample` 的 legacy 前端声明；
- 在 Shell 增加业务模块数组或 module id 特判；
- 在 Host 新建私有模块清单；
- 让 metadata 与代码分别声明同一条路由、菜单或权限；
- 在模块注册副作用中隐式迁移或导入数据。

底层契约见[module-contract.md](../architecture/module-contract.md)，模板结构见[module-template.md](../architecture/module-template.md)。

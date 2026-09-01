# 内建模块开发指南

本文是新增仓内模块的唯一开发入口。模板和脚手架是当前示例；`ModuleTemplateSample` 只保留为旧契约兼容样本。

## 所有权

一个模块由三个可独立核对的所有者组成：

1. `modules/<ModuleId>/newp.plugin.json` 唯一拥有 module id、显示名、版本、依赖和 behavior key。
2. `modules/<ModuleId>/metadata/**` 拥有路由、菜单、权限引用、模型与本地化等静态事实。
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
modules/ExampleModule/                  Capsule 与 metadata
server/modules/ExampleModule/           后端 binding 与实现
frontend/modules/example-module/        前端 binding 与实现
```

`-SkipIntegration` 只跳过 HostApi 接线，不省略 Capsule 或两侧 binding。

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

## 兼容代码边界

仓库仍有少量 `ModuleBase`、`FrontendModule`、Host 私有 `plugin.json` 和旧示例模块，它们是迁移兼容路径，不是新增模块模板。普通接入不得：

- 复制 `ModuleTemplateSample` 的 legacy 前端声明；
- 在 Shell 增加业务模块数组或 module id 特判；
- 在 Host 新建私有模块清单；
- 让 metadata 与代码分别声明同一条路由、菜单或权限；
- 在模块注册副作用中隐式迁移或导入数据。

底层契约见[module-contract.md](../architecture/module-contract.md)，模板结构见[module-template.md](../architecture/module-template.md)。

# Runtime Layer Plugin Platform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把当前平台从“源码集成优先”演进到“运行层 + 模块包 + 外部扩展开发”的正式插件平台。

**Architecture:** 先冻结插件协议和装配模型，再把宿主切到正式插件加载，随后建立模块包交付、外部开发模板、插件治理和前端插件化收口。实施顺序遵循“先后端正式装配，再外部开发，再治理与前端”。

**Tech Stack:** ASP.NET Core, .NET assemblies, existing Kernel/SDK contracts, PowerShell/Bash tooling, frontend shell/module system, Markdown docs

---

## 文件结构与责任划分

### 需要新增或重点改造的区域

- `apps/host-api`
  宿主运行层、插件目录配置、插件装配入口、宿主级校验与日志。

- `server/kernel`
  插件清单模型、插件加载器、依赖与兼容检查、模块发现协议。

- `server/sdk`
  对外稳定 SDK、模块基类与公共扩展点、外部插件开发支持。

- `server/templates`
  内外一致的插件模板与打包模板。

- `scripts`
  打包、安装、验证、调试、外部插件开发辅助脚本。

- `docs/architecture`
  插件协议、目录结构、兼容策略、前后端装配规则。

- `docs/guides`
  外部开发者指南、打包与部署说明、插件调试说明。

### 需要避免的情况

- 不要把插件治理、远程分发、前端插件动态装配一次性揉进第一阶段实现。
- 不要在正式插件装配完成前继续扩张大量新模块。
- 不要长期保留“正式交付靠插件、开发永远只靠源码引用”的双轨不一致状态。

## 阶段计划

### Task 1: 冻结插件协议与交付边界

**Files:**
- Create: `D:\Github\ICP-X\NewP\docs\architecture\plugin-package-format.md`
- Create: `D:\Github\ICP-X\NewP\docs\architecture\plugin-runtime-loading.md`
- Create: `D:\Github\ICP-X\NewP\docs\architecture\plugin-compatibility-policy.md`
- Modify: `D:\Github\ICP-X\NewP\Plan.md`
- Modify: `D:\Github\ICP-X\NewP\docs\current-status.md`

- [ ] 明确插件包目录结构。
- [ ] 明确清单文件字段：模块 ID、版本、依赖、兼容宿主版本、入口程序集、可选前端资源声明。
- [ ] 明确宿主插件目录和发布目录约定。
- [ ] 明确开发期便利机制与正式交付机制的区别。
- [ ] 将 `方案 C` 主线写入计划与状态文档。

**验收标准**
- 宿主、模块包、SDK、客户扩展开发四者边界有统一书面定义。

### Task 2: 让宿主以后端正式插件装配为主

**Files:**
- Modify: `D:\Github\ICP-X\NewP\apps\host-api\Program.cs`
- Modify: `D:\Github\ICP-X\NewP\apps\host-api\HostApi.csproj`
- Create: `D:\Github\ICP-X\NewP\server\kernel\PluginModel\PluginManifest.cs`
- Create: `D:\Github\ICP-X\NewP\server\kernel\PluginModel\PluginCompatibility.cs`
- Create: `D:\Github\ICP-X\NewP\server\kernel\PluginLoader\PluginManifestLoader.cs`
- Create: `D:\Github\ICP-X\NewP\server\kernel\PluginLoader\PluginDirectoryScanner.cs`
- Create: `D:\Github\ICP-X\NewP\server\kernel\PluginLoader\PluginCompatibilityChecker.cs`
- Modify: `D:\Github\ICP-X\NewP\server\kernel\ModuleLoader\AssemblyModuleLoader.cs`

- [ ] 先让宿主能够读取插件目录和插件清单，而不是单纯扫描 `*Module.dll`。
- [ ] 把 `ConfigCenter.dll` 这类文件名特判替换成清单驱动装配。
- [ ] 将业务模块的 `ProjectReference` 从“正式装配依赖”降级为开发期过渡机制。
- [ ] 增加宿主版本兼容检查。
- [ ] 明确加载失败时的日志、阻断和降级策略。

**验收标准**
- 在无业务模块源码引用的条件下，宿主可基于插件目录和清单加载至少一个模块。

### Task 3: 建立模块包交付与验证链路

**Files:**
- Create: `D:\Github\ICP-X\NewP\scripts\pack-plugin.ps1`
- Create: `D:\Github\ICP-X\NewP\scripts\pack-plugin.sh`
- Create: `D:\Github\ICP-X\NewP\scripts\verify-plugin-package.ps1`
- Create: `D:\Github\ICP-X\NewP\scripts\verify-plugin-package.sh`
- Create: `D:\Github\ICP-X\NewP\docs\guides\plugin-packaging.md`
- Modify: `D:\Github\ICP-X\NewP\server\templates\backend-module-template\README.md`

- [ ] 让模块打包结果包含程序集、清单、版本信息和最小说明文件。
- [ ] 定义插件安装目录约定。
- [ ] 定义插件包完整性检查。
- [ ] 让至少一个现有模块具备标准化打包能力。

**验收标准**
- 至少一个模块能独立打包成插件包，并可被宿主装配。

### Task 4: 对外稳定 SDK 与外部模板

**Files:**
- Modify: `D:\Github\ICP-X\NewP\server\sdk\README.md`
- Create: `D:\Github\ICP-X\NewP\server\templates\external-plugin-template\...`
- Create: `D:\Github\ICP-X\NewP\docs\guides\external-plugin-development.md`
- Create: `D:\Github\ICP-X\NewP\docs\guides\external-plugin-debugging.md`
- Modify: `D:\Github\ICP-X\NewP\scripts\new-module.ps1`
- Modify: `D:\Github\ICP-X\NewP\scripts\new-module.sh`

- [ ] 区分“内部模块模板”和“对外插件模板”。
- [ ] 明确外部模板只依赖公开 SDK / Contracts。
- [ ] 提供一个最小本地调试宿主路径。
- [ ] 提供外部插件开发、打包、验证指南。

**验收标准**
- 不接触宿主源码的开发者，也能基于模板开发一个最小插件。

### Task 5: 插件治理与风险控制

**Files:**
- Create: `D:\Github\ICP-X\NewP\server\kernel\PluginLoader\PluginHashValidator.cs`
- Create: `D:\Github\ICP-X\NewP\server\kernel\PluginLoader\PluginDisablePolicy.cs`
- Create: `D:\Github\ICP-X\NewP\docs\architecture\plugin-governance.md`
- Create: `D:\Github\ICP-X\NewP\docs\guides\plugin-upgrade-and-rollback.md`

- [x] 增加模块版本与兼容失败提示。
- [x] 增加插件禁用机制。
- [x] 增加哈希或签名校验设计。
- [x] 增加升级和回滚路径说明。

**验收标准**
- 插件不兼容、校验失败或被禁用时，宿主行为可预测且可解释。

### Task 6: 前端插件化收口

**Files:**
- Modify: `D:\Github\ICP-X\NewP\frontend\packages\shell\src\registerModules.ts`
- Create: `D:\Github\ICP-X\NewP\frontend\packages\shell\src\plugin-manifest-loader.ts`
- Create: `D:\Github\ICP-X\NewP\docs\architecture\frontend-plugin-loading.md`
- Create: `D:\Github\ICP-X\NewP\docs\guides\frontend-plugin-integration.md`

- [x] 设计前端插件清单或资源清单格式。
- [x] 明确前端插件与后端插件如何对齐。
- [x] 逐步把 `registerModules.ts` 从研发期清单，过渡到正式插件装配入口。

**验收标准**
- 前端正式交付不再依赖工作区手工源码接线作为唯一方式。

## 里程碑与顺序建议

### 里程碑 1

- 完成 Task 1
- 完成 Task 2 的设计与最小装配闭环

目标：
- 平台正式承认插件装配是主线

### 里程碑 2

- 完成 Task 3
- 完成 Task 4

目标：
- 平台具备“运行包 + 模块包 + 外部最小开发”能力

### 里程碑 3

- 完成 Task 5
- 启动 Task 6

目标：
- 平台具备基本插件治理能力，并开始前后端交付模型收口

## 风险前置提醒

- 如果在 Task 2 之前继续大量新增模块，会加重后续去源码引用化的成本。
- 如果在 Task 4 之前对客户承诺开放开发能力，文档和 SDK 稳定性风险很高。
- 如果 Task 6 拖太久，最终交付会长期保持“后端插件化、前端源码接线”的不对称状态。

## 最终验收

- 宿主运行包不包含对业务模块源码工程的正式依赖。
- 模块以独立插件包方式部署。
- 客户可不拿源码完成部署。
- 外部开发者可基于公开模板开发最小插件。
- 插件兼容、加载失败、禁用和回滚具备正式规则。

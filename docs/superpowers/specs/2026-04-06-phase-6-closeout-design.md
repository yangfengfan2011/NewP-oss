# Phase 6 收口设计：端到端示例模块与可执行脚手架

## 1. 背景

截至当前节点，Phase 6 已完成后端模板、后端真实示例模块、前端模板骨架和基础验证，但仍未满足 `Plan.md` 中“可以端到端脚手架出一个示例模块”的验收口径。

当前缺口集中在两点：

- `ModuleTemplateSample` 只有后端示例，前端尚未形成端到端承接。
- 仓库中还没有可以直接执行的脚手架命令，无法把前后端模板自动生成成一个最小可运行模块。

因此，本轮收口不再新增新的平台抽象，而是把现有模板补齐成“默认就能打通前后端”的最小闭环，并提供可执行脚手架把这个闭环稳定复用。

## 2. 目标

- 为 `ModuleTemplateSample` 补齐前端示例模块，并接入 Shell。
- 让前端模板默认具备最小 API 联通能力，而不是只提供静态占位页面。
- 提供可执行脚手架，自动生成前后端模块目录、基础文件和宿主接入。
- 用真实命令完成构建与脚手架验证，并据此修正文档与阶段状态。

## 3. 非目标

- 不在本轮引入新的前端基础设施层或通用状态管理框架。
- 不在本轮实现前端自动测试框架扩展。
- 不在本轮补齐数据库持久化、迁移或缓存能力，这属于 Phase 6.5 之后范围。
- 不把脚手架扩展成复杂代码生成平台，只覆盖当前模板闭环所需的最小自动化。

## 4. 范围

本轮设计覆盖以下边界：

- `server/templates/backend-module-template`
- `server/modules/ModuleTemplateSample`
- `frontend/templates/frontend-module-template`
- `frontend/modules`
- `frontend/packages/shell/src/registerModules.ts`
- `scripts/`
- `docs/verification/`
- `docs/current-status.md`

## 5. 设计约束

- 代码注释、脚手架说明和新增文档全部使用中文。
- 脚手架优先复用仓库内模板，不重复维护第二套模板源。
- 脚手架输出必须遵循现有模块目录结构与命名习惯。
- 宿主接入必须保持显式，不做隐式扫描或运行时魔法注册。
- 新增前端示例模块必须直接调用后端示例 API，形成真实链路。

## 6. 总体方案

### 6.1 端到端示例模块

新增 `frontend/modules/module-template-sample`，作为 `server/modules/ModuleTemplateSample` 的前端承接模块：

- 在页面加载时调用 `/api/module-template-sample/status`。
- 展示加载中、成功、失败三种状态。
- 把后端返回的 `ModuleId`、`DisplayName`、`Message` 展示在 Shell 页面中。
- 在 `frontend/packages/shell/src/registerModules.ts` 中显式注册。

这样，仓库内会存在一个真实的最小全栈模块样板，而不是只有后端或只有模板。

### 6.2 前端模板升级

现有前端模板只提供静态页面，不足以支撑“端到端脚手架”的阶段目标，因此需要升级为：

- 新增最小服务文件，默认请求 `/api/template-module/status`。
- 页面展示请求状态、接口返回内容和扩展提示。
- 保留最小依赖，只继续依赖 `@icp-platform/sdk` 与 `react`。

升级后，脚手架生成的新前端模块天然具备最小联通验证价值。

### 6.3 后端模板补齐

后端模板当前缺少可直接生成项目的 `.csproj` 模板文件，不足以作为脚手架输入。因此需要补齐：

- 模板项目文件。
- 与当前示例模块一致的最小项目元数据。

补齐后，脚手架才能稳定生成新的后端模块并被 `HostApi` 引用。

### 6.4 可执行脚手架

新增脚手架脚本：

- `scripts/new-module.ps1`
- `scripts/new-module.sh`

脚手架职责限定为：

- 接收模块名称。
- 复制前后端模板目录。
- 替换模板中的模块名、路由前缀、包名和显示名。
- 更新 `apps/host-api/HostApi.csproj` 的 `ProjectReference`。
- 更新 `frontend/packages/shell/src/registerModules.ts` 的导入与注册列表。

脚手架默认生成最小状态接口闭环，不额外生成复杂业务文件。

## 7. 命名与生成规则

给定模块名 `ExampleModule`：

- 后端目录：`server/modules/ExampleModule`
- 后端项目：`ExampleModule.csproj`
- 前端目录：`frontend/modules/example-module`
- 前端包名：`@icp-modules/example-module`
- 前端路由：`/example-module`
- 后端 API 前缀：`/api/example-module`
- 宿主注册类：`ExampleModuleModule`

脚手架内部需要同时维护：

- PascalCase 模块名
- kebab-case 路由与前端目录名
- 中文显示名

默认显示名可使用“`<模块名> 模块`”。

## 8. 验证方案

### 8.1 模板与示例验证

- `dotnet build .\server\modules\ModuleTemplateSample\ModuleTemplateSample.csproj`
- `dotnet build .\apps\host-api\HostApi.csproj`
- `pnpm --filter @icp-modules/module-template-sample build`
- `pnpm build`

### 8.2 脚手架验证

至少执行一次 PowerShell 脚手架命令，生成一个新的最小模块样板，并验证：

- 生成目录存在。
- 生成后的后端项目可构建。
- 生成后的前端模块包可构建。

如果当前环境支持 `bash`，再补跑 `.sh` 版本；若不支持，需要在验证文档中明确说明。

## 9. 风险与控制

### 9.1 主要风险

- 脚手架字符串替换不完整，导致生成代码仍残留模板标识。
- 自动插入 `HostApi.csproj` 或 `registerModules.ts` 时破坏现有格式。
- 前端模板升级后，模板包自身构建失败。

### 9.2 控制方式

- 以 `TemplateModule`、`template-module`、模板包名为固定替换锚点。
- 优先做幂等检查，避免重复插入宿主注册项。
- 在提交完成结论前，必须 fresh 重跑构建与脚手架命令。

## 10. 完成标准

本轮只有在以下条件同时满足时，才视为 Phase 6 完成：

- `ModuleTemplateSample` 具备前后端端到端示例能力。
- 脚手架命令可生成一个新的最小前后端模块。
- 对应构建验证已 fresh 通过。
- `docs/verification/` 和 `docs/current-status.md` 已按真实结果同步。

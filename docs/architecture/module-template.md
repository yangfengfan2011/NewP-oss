# 后端模块模板落地指南

## 目标

- 让新增模块从第一天就遵守 Module Capsule 契约；
- 让 AI 与人工都能在小上下文中生成、验证和继续维护模块；
- 不通过模板引入身份重复、宿主特判或跨模块隐藏依赖。

## 标准结构

一个内建模块在前端与声明根和原位后端目录中共同组成：

```text
frontend/modules/<module-slug>/
├── newp.plugin.json
├── metadata/
│   ├── navigation/
│   └── dictionaries/
├── migrations/                 # 实际拥有的迁移清单
├── src/index.ts
├── src/pages/
├── package.json
└── README.md

server/modules/<ModuleId>/
├── <ModuleId>Module.cs
├── <ModuleId>.csproj
├── Controllers/
├── DTOs/
├── Services/
└── README.md

```

根 Capsule 是模块身份、版本、依赖、功能目录与 behavior key 的唯一 owner。后端和前端只实现对应 behavior binding。

## 推荐生成方式

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块"
```

只生成 authoring 与实现、暂不接入 HostApi：

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块" -SkipIntegration
```

`-SkipIntegration` 不会省略 Capsule 或 behavior binding。

## 模板目录

- 后端模板：`server/templates/backend-module-template/`
- 前端模板：`frontend/templates/frontend-module-template/`

后端模板中的 `capsule/` 让模板项目可独立构建；脚手架生成模块时会把它的实际资产合并到 `frontend/modules/<module-slug>/`，并更新嵌入资源路径。

## 必改项

- Capsule 中的模块身份、显示名、程序集名和 behavior key；
- navigation 的 route、`targetKey` 与 `featureKey` 引用；
- 后端命名空间、类名、路由和读取 behavior key；
- 前端包名、behavior key、page key 与页面实现。

脚手架会完成最小替换；业务模型、服务和页面仍需按已确认设计补齐。

## 禁止事项

- 在 C# 或 TypeScript 中重新声明 Capsule 身份；
- 在 `FrontendBehaviorBindings` 中维护 routes、menus 或 permissions；
- 控制器直接承载复杂业务规则；
- 模块依赖 Host / Shell 内部实现或其他模块私有基础设施；
- 在模块注册副作用中隐式执行迁移或导入；
- 新建 `apps/host-api/modules/<ModuleId>/plugin.json` 或 `legacy-module` 接入。

## 最小验证

```powershell
dotnet build .\server\modules\ExampleModule\ExampleModule.csproj
```

```bash
cd frontend
pnpm --filter @icp-modules/example-module build
```

接入 HostApi 后还应构建 Host，并确认生成的 Capsule 可以被 `ModuleCapsuleCandidateReader` 读取且通过 `CanonicalModulePlanCompiler`。

模板保留现有 `routeCapabilities` 静态页面及恢复 loader；导航 `featureKey` 指向 Capsule 的 `features` 导航声明，状态端点绑定同一读取行为。脚手架同步替换这些 key，不生成另一份业务 Permission 声明，也不为静态页面增加网络请求。

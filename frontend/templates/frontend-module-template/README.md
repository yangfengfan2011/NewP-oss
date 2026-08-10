# 前端模块模板

该模板提供一个最小、可复制、可构建的前端 behavior binding。

## 模板内容

- `src/index.ts`：导出 `FrontendBehaviorBindings`，只绑定稳定 behavior key 与 page key；
- `src/pages/TemplatePage.tsx`：最小页面实现；
- `package.json`：仅依赖前端 SDK。

模块身份、显示名、路由、菜单和权限不在 TypeScript 中声明，它们由 `modules/<ModuleId>/newp.plugin.json` 与 `modules/<ModuleId>/metadata/**` 拥有。

## 推荐方式

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块"
```

脚手架会同时生成前端 binding、后端 binding 和根 Capsule，避免手工维护三者之间的 key 对应关系。

## 按需扩展

只有出现真实需要时才新增：

- `src/components/`；
- `src/services/`；
- `src/assets/`；
- `@icp-platform/ui` 等显式依赖。

不得引用 `frontend/packages/shell/src/*`，也不得把 routes、menus、permissions 或 module metadata 复制到 `src/index.ts`。

## 验证

```bash
cd frontend
pnpm --filter @icp-templates/frontend-module-template build
pnpm --filter @icp-modules/<module-name> build
```

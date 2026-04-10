# 前端模块模板

这个模板用于快速创建一个最小、可复制、可构建的前端模块骨架。

## 模板包含

- `src/index.ts`
  负责声明模块元数据、菜单、路由和权限。
- `src/pages/TemplatePage.tsx`
  提供一个最小页面入口，用于继续拆分真实页面、组件和服务。
- `package.json`
  使用 `@icp-platform/sdk` 作为唯一平台依赖。

## 使用方式

把当前目录复制到 `frontend/modules/<你的模块名>`，然后至少修改以下内容：

- `package.json` 中的包名
- `src/index.ts` 中的模块 ID、显示名、路由和权限
- `src/pages/TemplatePage.tsx` 中的页面文案

如果模块确实需要服务层、内部组件或资源目录，再按需创建 `src/services/`、`src/components/`、`src/assets/`。

## 验证命令

```bash
cd frontend
pnpm --filter @icp-templates/frontend-module-template build
pnpm --filter @icp-modules/<你的模块名> build
pnpm build
```

## 约束

- 模块 ID 必须唯一。
- 路由路径不能与现有模块冲突。
- 权限编码建议使用 `<模块名>.View` 这种统一格式。
- 新增注释和说明统一使用中文。
- 模板本身不默认依赖 `@icp-platform/ui`，只有真实模块需要时再显式引入。

## Componentization Guardrails

- `src/index.ts` should only declare the frontend module.
- `src/pages/*` should contain page entry logic only.
- `src/services/*` should contain API access only.
- Normal module integration should only update `frontend/packages/shell/src/builtin-runtime-plugins.ts`.
- Do not modify other shell internals unless a separate design explicitly approves it.
- Do not embed live API verification or host page styling into the template unless the target module actually needs them.

## Minimum Verification

```bash
cd frontend
pnpm --filter @icp-modules/<your-module-name> build
pnpm build
```

# 前端模块模板

这个模板用于快速创建一个最小可联通的前端模块。

## 模板包含

- `src/index.ts`
  负责声明模块元数据、菜单、路由和权限。
- `src/pages/TemplatePage.tsx`
  提供一个最小页面，并展示接口加载中、成功、失败三种状态。
- `src/services/templateService.ts`
  默认请求 `/api/template-module/status`，用于验证前后端联通。
- `package.json`
  使用 `@icp-platform/sdk` 作为唯一平台依赖。

## 使用方式

### 方式一：使用脚手架

推荐直接使用仓库脚手架：

```powershell
.\scripts\new-module.ps1 -ModuleName ExampleModule
```

```bash
./scripts/new-module.sh --module-name ExampleModule
```

脚手架会同时生成：

- `server/modules/ExampleModule`
- `frontend/modules/example-module`

并在默认情况下自动更新：

- `apps/host-api/HostApi.csproj`
- `frontend/packages/shell/src/registerModules.ts`

### 方式二：手动复制

如果只想复制前端模板，可以把当前目录复制到 `frontend/modules/<你的模块名>`，然后至少修改以下内容：

- `package.json` 中的包名
- `src/index.ts` 中的模块 ID、显示名、路由和权限
- `src/services/templateService.ts` 中的接口地址
- `src/pages/TemplatePage.tsx` 中的页面文案

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
## Componentization Guardrails

- `src/index.ts` should only declare the frontend module.
- `src/pages/*` should contain page entry logic only.
- `src/services/*` should contain API access only.
- Normal module integration should only update `frontend/packages/shell/src/registerModules.ts`.
- Do not modify other shell internals unless a separate design explicitly approves it.

## Minimum Verification

```bash
cd frontend
pnpm --filter @icp-modules/<your-module-name> build
pnpm build
```

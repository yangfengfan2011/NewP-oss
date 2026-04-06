# module-template-sample

这是当前仓库里用于说明前端组件化开发方式的最小真实示例模块。

## 它演示什么

- 如何在 `src/index.ts` 中声明 `FrontendModule`
- 如何把页面放在 `src/pages/*`
- 如何把 API 访问放在 `src/services/*`
- 如何通过受控入口接入 Shell

## 主要文件

- `src/index.ts`
  模块声明入口，负责元数据、路由、菜单、权限和 i18n 声明。
- `src/pages/ModuleTemplateSamplePage.tsx`
  示例页面，展示加载、成功、失败三种状态。
- `src/services/moduleTemplateSampleService.ts`
  示例服务，请求后端 `/api/module-template-sample/status`。

## 这个模块怎么接入

研发期默认通过：

- `frontend/packages/shell/src/builtin-runtime-plugins.ts`

正式运行时方向则会通过：

- `frontend/packages/shell/src/plugin-manifest-loader.ts`
- `frontend-plugin-manifest.json`

## 推荐阅读

- 统一入口文档：
  [docs/guides/componentized-development.md](../../../docs/guides/componentized-development.md)
- 详细 Example 走读：
  [docs/guides/componentized-example-module.md](../../../docs/guides/componentized-example-module.md)

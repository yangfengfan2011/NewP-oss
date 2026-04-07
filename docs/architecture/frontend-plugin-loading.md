# 前端插件加载

本文档说明当前前端 Shell 的运行时插件加载主线，以及它和研发期源码联调之间的关系。

## 当前机制

Shell 启动时按下面顺序完成前端模块装配：

1. 读取 `import.meta.env.VITE_FRONTEND_PLUGIN_MANIFEST_URL`
2. 如果未配置，则使用默认地址 `/plugins/frontend-plugin-manifest.json`
3. 通过 `fetch` 请求运行时插件清单
4. 如果清单存在，则优先按清单逐项动态加载前端模块
5. 如果清单不存在或当前环境不可达，则回退到内建模块清单和本地源码加载路径
6. 将成功装载的 `FrontendModule` 注册到 `ModuleRegistry`
7. 在注册完成后执行 `ModuleRegistry.initializeAll()`
8. 最后再渲染 React Shell

对应实现位置：

- 清单加载：`frontend/packages/shell/src/plugin-manifest-loader.ts`
- 内建插件清单：`frontend/packages/shell/src/builtin-runtime-plugins.ts`
- 装配入口：`frontend/packages/shell/src/registerModules.ts`
- 启动顺序：`frontend/packages/shell/src/main.tsx`
- 清单生成：`frontend/packages/shell/vite.config.ts`

这里需要区分两个职责：

- `builtin-runtime-plugins.ts` 是内建业务模块的受控清单入口
- `registerModules.ts` 负责运行时装配、失败处理和研发期回退编排，不应再作为日常手工维护的模块列表

## 运行时清单来源

当前前端运行时清单有两类来源：

1. 宿主安装目录驱动的运行时清单
2. Shell 在 Vite 开发和构建阶段自动生成的开发期清单

对于统一插件包 Phase 1，HostApi 会把安装目录中的：

- `runtime/frontend-plugin-manifest.json`

映射到：

- `/plugins/frontend-plugin-manifest.json`

并把安装目录中的：

- `public/`

映射到：

- `/plugin-assets`

这使得已启用插件的前端入口 URL 可以直接写入运行时清单并由 Shell 动态导入。

如果当前没有统一插件包安装结果，默认运行时清单仍不依赖手工维护的 `public/plugins/frontend-plugin-manifest.json`，而是由 Shell 在 Vite 开发和构建阶段自动生成。默认包含的内建模块来自：

- `demo`
- `audit`
- `authorization`
- `config-center`
- `files`
- `module-template-sample`
- `sample-plugin`

生成后的清单格式示例：

```json
{
  "plugins": [
    { "moduleId": "demo" },
    { "moduleId": "audit" },
    { "moduleId": "authorization" },
    { "moduleId": "files" }
  ]
}
```

字段说明：

- `plugins`
  前端插件入口数组。
- `moduleId`
  Vite 管理的内建模块标识。Shell 会把它解析到受控源码入口。
- `url`
  可选。若声明，则按外部 URL 动态导入模块。统一插件包 Phase 1 当前主要使用该字段。
- `exportName`
  可选。若声明，则使用命名导出；若未声明，则默认读取模块的 `default` 导出。

默认推荐优先使用 `moduleId`。`url` 和 `exportName` 主要用于未来的外部前端插件交付场景。

## 打包与运行时加载关系

当前前端插件平台采用的是 “Vite-managed internal module + runtime manifest” 的方式：

- 业务模块源码入口来自 `frontend/modules/*/src/index.ts`
- Shell 通过 `import.meta.glob` 建立 internal registry
- 运行时清单优先声明 `moduleId`，不直接指向 `tsc` 产物
- Vite 在构建时为这些动态入口生成可加载 chunk，Shell 在运行时按清单再去装配它们

这样做的目的，是避免把 `tsc` 直接输出的原始 ESM 文件当作浏览器运行时插件入口使用。原始 `dist/index.js` 往往会保留无扩展名的相对导入，不适合作为稳定交付物。

对于统一插件包 Phase 1，流程进一步扩展为：

1. 统一插件包安装到标准安装目录
2. 启用操作把已启用版本重建为 `runtime/frontend-plugin-manifest.json`
3. 同一次启用操作把对应前端产物投影到 `public/<ModuleId>/<Version>/...`
4. HostApi 通过 `/plugins/frontend-plugin-manifest.json` 与 `/plugin-assets/...` 对外暴露运行时入口

补充说明：

- 常规 `install` 不会切换当前启用版本
- 如果重复安装的正好是当前已启用版本，PluginOps 仍会立即重建运行时清单和 `public/` 资源投影，保持前端入口与状态一致

## 研发期回退机制

研发期本地回退仍然保留，但不再依赖静态源码导入列表。当前回退机制同样通过受控的懒加载 registry 装配以下内建模块：

- `demo`
- `audit`
- `authorization`
- `config-center`
- `files`
- `module-template-sample`

这意味着：

- 正式交付入口优先走运行时清单
- 研发期在清单不可达时，仍可通过本地源码继续联调
- 运行时加载与研发期回退共用同一套受控模块标识

## 部分失败策略

当运行时清单存在时：

- Shell 不会因为单个插件加载失败而整体中断
- 成功加载的模块仍会继续注册
- 失败项会输出明确警告日志
- 不会因为某个运行时模块失败，就自动退回整份本地源码模块清单

这个策略的目的是避免生产环境把“运行时交付损坏”悄悄掩盖成“研发期源码回退仍可工作”。

## 当前边界

当前实现已经补齐了前端运行时装配、构建期 manifest 生成，以及统一插件包安装结果驱动的运行时清单，但仍未覆盖以下能力：

- 前端插件签名或来源校验
- 前端插件依赖冲突治理
- 热插拔、热更新或运行时卸载
- 更完整的宿主级隔离与恢复机制

因此，现阶段应把它理解为：

- Shell 已具备正式的前端运行时清单入口
- 业务模块已经能通过 Vite-managed internal module 参与运行时装配
- 统一插件包 Phase 1 已能把安装状态投影为运行时清单和静态资源目录
- 更完整的签名、依赖治理和宿主级隔离，仍属于后续阶段工作

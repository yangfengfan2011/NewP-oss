# 前端插件接入指南

本文档说明当前阶段前端插件如何接入 Shell，以及研发期接线与正式运行时交付之间的区别。

## 1. 当前接入目标

当前阶段的目标不是一次性完成前后端插件包完全统一，而是先让前端具备正式运行时入口：

- 研发期可以继续使用工作区源码模块开发
- 正式交付时可以切到运行时清单 + 动态导入
- 两条路径共用同一个 `FrontendModule` 合同

## 2. 前端模块导出要求

当前前端插件仍然需要对外暴露 `@icp-platform/sdk` 中定义的 `FrontendModule`。

最小示例：

```ts
import { FrontendModule } from '@icp-platform/sdk';
import DemoHome from './pages/DemoHome';

export const demoModule: FrontendModule = {
  metadata: {
    moduleId: 'demo',
    displayName: '示例模块',
    version: '1.0.0'
  },
  routes: [
    {
      path: '/demo',
      component: DemoHome
    }
  ]
};

export default demoModule;
```

建议：

- 保留 `default` 导出，便于运行时清单直接装配
- 如果确有需要，也可以同时提供命名导出，再由清单中的 `exportName` 指定

## 3. 运行时清单如何指向前端模块

当前 Shell 读取的前端运行时清单优先使用 `moduleId`：

```json
{
  "plugins": [
    { "moduleId": "demo" },
    { "moduleId": "files" }
  ]
}
```

对应关系如下：

- `moduleId`
  指向 `frontend/packages/shell/src/builtin-runtime-plugins.ts` 和 internal registry 中的受控模块标识。
- `url`
  可选。用于未来外部前端插件入口或自定义资源地址。
- `exportName`
  可选。仅在 `url` 对应的模块需要命名导出时使用；未声明时读取默认导出。

因此，正式运行时接入的关键不是改 Shell 内部，而是：

1. 对内建业务模块，确保模块已进入 `builtin-runtime-plugins.ts`
2. 让运行时清单中的 `moduleId` 指向该受控模块标识
3. 确保导出对象符合 `FrontendModule` 合同

如果未来接入独立前端插件包，再额外使用 `url` / `exportName` 描述外部入口。

## 4. 研发期路径与正式交付路径

### 研发期

当前研发期仍然允许通过 `builtin-runtime-plugins.ts` 中维护的内建模块清单接入：

- 适合仓库内建模块开发
- 适合快速联调和本地调试
- 不代表正式交付模型

### 正式交付期

当前正式交付路径应优先采用：

1. 构建前端插件产物
2. 将产物部署到 Shell 运行时可访问的位置
3. 提供 `frontend-plugin-manifest.json`
4. 让 Shell 在启动时通过清单动态导入

当前阶段，正式交付已经不再要求必须修改工作区源码，才能把前端模块接入到 Shell。

## 5. 推荐接入步骤

### 步骤 1：准备前端插件入口

- 确保构建后存在稳定入口文件
- 确保入口文件导出 `FrontendModule`

### 步骤 2：准备运行时清单

在 Shell 可访问的位置放置清单文件，例如：

```json
{
  "plugins": [
    { "moduleId": "demo" }
  ]
}
```

如需自定义地址，可设置：

```bash
VITE_FRONTEND_PLUGIN_MANIFEST_URL=/custom/frontend-plugin-manifest.json
```

### 步骤 3：启动并验证

启动 Shell 后确认：

- 模块已被注册
- 相关路由可访问
- 相关菜单已出现
- 控制台没有未处理的动态导入失败

## 6. 验证建议

当前至少执行以下验证：

1. `pnpm --filter @icp-platform/shell build`
2. 在无运行时清单时，确认仍能回退到内建模块清单
3. 在提供运行时清单时，确认 Shell 优先装配运行时模块
4. 在清单里故意放入一个错误入口时，确认：
   - 失败项有清晰警告
   - 成功项仍然会继续注册
   - 不会悄悄退回整份本地源码列表

## 7. 当前边界

当前实现仍然只是“前端插件化收口”的第一步，不应理解为已经完成：

- 前后端插件包一体化交付
- 后端清单自动生成前端清单
- 前端插件远程市场分发
- 版本矩阵、签名与来源治理

当前更准确的描述是：

- Shell 已拥有正式运行时清单入口
- 前端模块不再只能靠源码接线
- 后续仍需继续完成前后端插件交付模型对齐

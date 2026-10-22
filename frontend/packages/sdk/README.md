# Frontend SDK

`@icp-platform/sdk` 提供仓内前端 behavior binding、标准 CRUD/list 页面形状和 Shell 受控装配所需的稳定类型。

新增模块入口：

```typescript
import { type FrontendBehaviorBindings } from '@icp-platform/sdk';
import ExamplePage from './pages/ExamplePage';

export const exampleBindings: FrontendBehaviorBindings = {
  behaviorKey: 'example.frontend',
  customPages: [{ pageKey: 'example.page', component: ExamplePage }],
};

export default exampleBindings;
```

模块身份、路由、菜单和权限来自 Canonical Module Plan，不在 TypeScript 中重复声明。`FrontendModule` 与逐项 registry 注册只为尚未完成 cutover 的仓内兼容模块保留，不是公共新增接口。

外仓声明式插件使用 `frontend/packages/plugin-sdk` 对应的独立发布包，不依赖本包、React、Shell 或 workspace 路径。

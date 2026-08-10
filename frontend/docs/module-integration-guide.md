# 前端模块接入规范

本文定义新增前端模块的 authoring owner、behavior binding 和最小验证方式。

## 三个明确边界

1. `modules/<ModuleId>/newp.plugin.json` 拥有模块身份与 `frontend.behaviorKey`。
2. `modules/<ModuleId>/metadata/**` 拥有导航、路由、权限引用和本地化。
3. `frontend/modules/<module-id>/src/index.ts` 只把稳定 key 绑定到页面、renderer 或 action 实现。

Shell 只消费 Host 编译的 `active-module-runtime.json`，不从源码目录推断活动模块。

## 标准 binding

```typescript
import { type FrontendBehaviorBindings } from '@icp-platform/sdk';
import ExamplePage from './pages/ExamplePage';

export const exampleBindings: FrontendBehaviorBindings = {
  behaviorKey: 'example.frontend',
  customPages: [
    {
      pageKey: 'example.page',
      component: ExamplePage,
    },
  ],
};

export default exampleBindings;
```

Capsule 的 `frontend.bindings` 与 metadata 的 `targetKey` 必须包含同一个 `example.page`；behavior key 必须与 TypeScript 完全一致。

## 标准目录

```text
frontend/modules/<module-id>/
├── src/
│   ├── index.ts
│   └── pages/
├── package.json
├── tsconfig.json
└── README.md
```

`components/`、`services/` 和 `assets/` 只在有真实需求时新增。公共 UI 依赖必须在模块自己的 `package.json` 中显式声明。

## 禁止事项

- 使用旧 `FrontendModule` 在 TypeScript 中重复 module metadata、routes、menus 或 permissions；
- 直接引用 `frontend/packages/shell/src/*`；
- 为接入单个模块修改 Shell 静态列表或增加 module id 特判；
- binding key 与 Capsule / metadata 各自命名、靠运行时猜测匹配；
- 把 live API 联通或宿主页面样式变成模板必备内容。

## 推荐生成

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块"
```

脚手架会生成根 Capsule、metadata 与两侧 behavior binding，并按需增加 HostApi 的受控投影链接。

## 最小验证

```bash
cd frontend
pnpm --filter @icp-templates/frontend-module-template build
pnpm --filter @icp-modules/<module-id> build
```

完整接入还必须验证 Capsule 可被 Canonical Module Plan 编译，且 Shell 能从活动运行时目录解析并绑定全部声明入口。

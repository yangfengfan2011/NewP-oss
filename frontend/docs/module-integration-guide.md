# 前端模块接入规范

新增仓内前端模块只通过 Module Capsule、metadata 和 `FrontendBehaviorBindings` 接入。Shell 从 Host 的 Active Module Runtime Catalog 发现活动模块，不扫描 workspace，也不维护业务模块列表。

## 所有权

1. `modules/<ModuleId>/newp.plugin.json` 拥有 identity、version、dependencies 和 `frontend.behaviorKey`。
2. `modules/<ModuleId>/metadata/**` 拥有 route、menu、permission reference、model 和 localization。
3. `frontend/modules/<module-id>/src/index.ts` 只把稳定 key 绑定到页面、renderer 或 action 实现。

```typescript
import { type FrontendBehaviorBindings } from '@icp-platform/sdk';
import ExamplePage from './pages/ExamplePage';

const exampleBindings: FrontendBehaviorBindings = {
  behaviorKey: 'example.frontend',
  customPages: [{ pageKey: 'example.page', component: ExamplePage }],
};

export default exampleBindings;
```

Capsule 的公开 binding、metadata 的 `targetKey` 和 TypeScript 导出必须使用相同稳定 key；编译器会在活动快照提交前拒绝缺失、孤儿和冲突。

## 推荐生成

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块"
```

脚手架同时生成根 Capsule、metadata 和两侧 behavior binding。普通模块接入不手工编辑 Shell 注册表，也不新建 Host 私有模块清单。

## 运行时装配

```http
GET /plugins/active-module-runtime.json
```

Shell 会导入并校验整份目录，全部成功后通过 `ModuleRegistry.replaceAll` 原子提交。任一必需入口失败时只显示 fatal/retry，不保留部分菜单、路由或 registry 状态。

`FrontendModule`、逐项 registry 注册和 Host 私有 `plugin.json` 只服务于未完成 cutover 的既有模块。新增模块不得复制这些兼容接口。

## 标准 CRUD 动作输入

需要短确认的标准动作继续在 metadata 声明 `confirmMessageKey`。需要额外业务输入时，metadata 只声明稳定 `inputKey`，模块在对应 `StandardCrudResourceRegistration.actionInputRenderers` 注册受控 renderer，并在 `actionHandlers` 中从 `context.input` 读取提交值。renderer 只负责呈现输入并调用 `onChange`；Shell 统一负责权限投影、弹层、pending、成功刷新、错误反馈和关闭生命周期。不要在 Shell 中根据 module id、resource id 或业务字段名选择输入组件。

不声明 `inputKey` 的既有动作和 handler 保持兼容。声明了 `inputKey` 但缺少对应 renderer 时，活动标准 CRUD 注册会 fail closed，避免动作在缺失业务输入的情况下执行。

## 标准 CRUD 动作状态与结果

动作的静态定义仍由 metadata 拥有。需要根据当前列表行或详情快照决定可见性、可用性和原因时，模块在 `StandardCrudResourceRegistration.actionStateResolver` 注册一个仅返回通用 `visible` / `available` / `reason` 的 resolver。Shell 只解释这个通用结果；业务字段、组合规则和保护原因必须留在模块 binding，而且前端状态不能替代服务端 guard。

`actionHandlers` 可以返回不透明的 `StandardCrudOperationResult`。Shell 只把该结果交给模块注册的 `operationResultPresenter`，不检查、持久或重放 payload。模块 presenter 拥有具体结果形状、展示、复制与关闭销毁时机。动作发生并发冲突时，Shell 刷新最新快照并结束旧确认流程，不自动重放写入。

## 验证

- 模块构建证明 TypeScript binding 与依赖完整。
- Canonical Module Plan 测试证明 binding 缺失或 owner 冲突会拒绝整份候选。
- Shell 测试证明单入口失败不会产生部分提交。
- Host smoke 证明活动目录包含该模块，并与其他投影共享 `snapshotId` / `planHash`。

```powershell
corepack pnpm --dir frontend --filter @icp-modules/<module-id> build
corepack pnpm --dir frontend --filter @icp-platform/shell test
```

外仓 `standard-data.v1` 插件不携带任意前端运行时代码；它使用独立 build-only plugin SDK 生成声明式标准投影。代码型外部 profile 必须另行设计。

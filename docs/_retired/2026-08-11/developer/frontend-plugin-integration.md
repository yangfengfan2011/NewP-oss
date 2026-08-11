# 前端模块接入指南

Shell 当前只通过 Active Module Runtime Catalog 发现活动前端模块。新增模块不修改 Shell 业务列表，也不存在 manifest 不可用时的 workspace fallback。

## 1. 选择 authoring 模式

### Canonical behavior binding

新平台能力或正在完成 capsule cutover 的模块应以 Module Capsule 根声明拥有 identity / version / dependencies，并让 TypeScript 只导出稳定 behavior key 的实现：

```ts
import type { FrontendBehaviorBindings } from '@icp-platform/sdk';
import ExamplePage from './pages/ExamplePage';

const exampleBindings: FrontendBehaviorBindings = {
  behaviorKey: 'example.frontend',
  customPages: [
    { pageKey: 'example.page', component: ExamplePage },
  ],
};

export default exampleBindings;
```

route、menu 和 permission reference 应由 navigation / security authoring 拥有，不能在 TypeScript 中重复声明。`modules/<ModuleId>/newp.plugin.json` 引用 behavior key 与公开 bindings；compiler 会在活动快照提交前验证缺失、孤儿和冲突。

### Legacy compatibility adapter

尚未完成 cutover 的仓内模块可以暂时继续导出 `FrontendModule`，由 `apps/host-api/modules/<ModuleId>/plugin.json` 声明 `bindingMode: "legacy-module"` 与前端 entry。该模式只用于迁移，不是新增 canonical interface 的模板。

## 2. 运行时装配

Host 从 Canonical Module Plan 生成：

```http
GET /plugins/active-module-runtime.json
```

目录中的内建 `moduleId` 指向 Vite 管理的实现入口；外部 `url` 指向物化后的版本化资产。Shell 会先导入并校验整份目录，再调用 `ModuleRegistry.replaceAll` 一次提交。任一入口失败时只显示 fatal / retry，不保留部分菜单或路由。

`frontend-plugin-manifest.json` 仍可作为同一 plan 生成的 compatibility projection，但不再是 Shell 的发现入口。

## 3. 内建模块接入步骤

1. 在 `server/modules/<ModuleId>` 与 `frontend/modules/<module-slug>` 实现模块行为。
2. 把静态 route / menu / permission / model 事实放入对应 authoring owner。
3. 为 legacy adapter 提供 `apps/host-api/modules/<ModuleId>/plugin.json`，或为 canonical adopter 提供 `modules/<ModuleId>/newp.plugin.json`。
4. 后端项目需要编译进 Host 时，更新 `apps/host-api/HostApi.csproj` 的 ProjectReference。
5. 不修改 `frontend/packages/shell/src` 的业务注册逻辑。

## 4. 验证

- 编译器测试应证明 binding 缺失、route / permission / model owner 冲突会拒绝整份 candidate。
- Shell 测试应证明单入口失败时 `ModuleRegistry` 不产生部分提交。
- 启动 Host 后确认活动目录包含模块的 canonical identity、version、dependencies 与前端入口。
- 运行 `corepack pnpm --dir frontend --filter @icp-platform/shell test` 和受影响模块测试。

外仓发布物、desired/active/LKG 与远端插件完整生命周期由对应 Platform API / PluginGate 实施票负责，不通过修改 Shell 绕过。

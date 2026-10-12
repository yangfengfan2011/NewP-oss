# 前端模块接入规范

新增仓内前端模块只通过 Module Capsule、metadata 和 `FrontendBehaviorBindings` 接入。Shell 从 Host 的 Active Module Runtime Catalog 发现活动模块，不扫描 workspace，也不维护业务模块列表。

## 所有权

1. `frontend/modules/<module-slug>/newp.plugin.json` 拥有 identity、version、dependencies 和 `frontend.behaviorKey`。
2. `frontend/modules/<module-slug>/metadata/**` 拥有 route、menu、功能授权声明、model 和 localization。
3. `frontend/modules/<module-slug>/src/index.ts` 只把稳定 key 绑定到页面、renderer 或 action 实现。

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

需要短确认的标准动作继续在 metadata 声明 `confirmMessageKey`。需要额外业务输入时，metadata 只声明稳定 `inputKey`，模块在对应 `StandardCrudResourceRegistration.actionInputRenderers` 注册受控 renderer，并在 `actionHandlers` 中从 `context.input` 读取提交值。renderer 只负责呈现输入并调用 `onChange`；Shell 统一负责功能位置投影、弹层、pending、成功刷新、错误反馈和关闭生命周期。不要在 Shell 中根据 module id、resource id 或业务字段名选择输入组件。

不声明 `inputKey` 的既有动作和 handler 保持兼容。声明了 `inputKey` 但缺少对应 renderer 时，活动标准 CRUD 注册会 fail closed，避免动作在缺失业务输入的情况下执行。

## 标准 CRUD 动作状态与结果

动作的静态定义仍由 metadata 拥有。需要根据当前列表行或详情快照决定可见性、可用性和原因时，模块在 `StandardCrudResourceRegistration.actionStateResolver` 注册一个仅返回通用 `visible` / `available` / `reason` 的 resolver。Shell 只解释这个通用结果；业务字段、组合规则和保护原因必须留在模块 binding，而且前端状态不能替代服务端 guard。

`actionHandlers` 可以返回不透明的 `StandardCrudOperationResult`。Shell 只把该结果交给模块注册的 `operationResultPresenter`，不检查、持久或重放 payload。模块 presenter 拥有具体结果形状、展示、复制与关闭销毁时机。动作发生并发冲突时，Shell 刷新最新快照并结束旧确认流程，不自动重放写入。

## 标准路径的位置授权

标准功能声明与派生规则见 [Module Contract](../../docs/architecture/module-contract.md#功能位置授权)。Shell 消费活动目录中的完整 position，并按导航、Tab 和树的静态节点类型传递所属入口。独立详情使用声明的入口，重定向采用目标入口，Drawer 沿用发起动作的位置；模块不从 URL、Referer、点击历史或记录 ID 推导位置。

`StandardCrudFeatureRequestContext.featurePosition` 是每次请求的执行上下文。标准 client、data adapter、Lookup 和 action handler 沿调用链传递它，由请求写入 `X-NewP-Feature`；不把位置加入业务 payload、筛选值或查询 URL，也不使用可被并发操作覆盖的全局请求头。自定义 handler 调用模块服务时同样传递 `context.featurePosition`。位置只表明请求要执行的声明功能，服务端仍按当前可信主体判权。既有 Host policy 等额外约束继续生效，例如 Identity 账号管理仍要求 `AdminOnly`，不能仅凭功能点投影推断可执行。

需要输入的动作通过 `StandardCrudActionInputRenderContext.featurePosition` 将发起动作位置传给 renderer。列表及详情共用 ReviewDialog；renderer 调用准备端点时传递该位置，最终 handler 继续使用同一位置。例如账号角色弹窗的账号候选、角色分页目录、当前分配与保存由该动作拥有，不能要求用户再获得其他管理页面的资格。

标准列表按 metadata 顺序呈现新建及声明 handlerKey 的页动作。页/行动作的 handler 和输入 renderer 可读取当前页面的可选 queryScope；它只是定位输入，服务端必须重新校验空间、路径和对象资格。输入 renderer 的 submitting 表示共享执行器正在提交，可交给标准表单禁用输入；弹层、草稿保护、防重复提交与关闭仍由共享宿主持有。写入完成后异步列表刷新失败保留“已保存”事实，重试只读取；该事实按主体、页面实例和查询身份隔离，并在成功读取后清除。

快捷跳转动作声明 `navigationTargetKey`，并保留现有 handler 返回 `StandardCrudOperationResult.navigation`。宿主按目标导航资格显示，目标路由仍执行正常准入；它不新增可授予动作，也不借用 `standardAction: view` 来改变原查看动作的含义。

运行时动态列的 `StandardCrudColumnContext.featurePosition` 同样来自所属读取入口。column provider 与行 data adapter 各自沿请求传递该位置；例如 Example 动态工作量的列请求和行请求使用相同的入口位置，原有模块列上下文 token 仍负责列与行的业务一致性，不能用它替代授权位置。宿主的动态列缓存已经按读取位置隔离。

列表与详情读取、Lookup 的缓存按位置隔离。标准宿主只加载可用入口及动作所需读取，创建或编辑的准备步骤和提交使用同一动作位置；动作 Drawer 不加载底层列表查询的 Lookup。纯容器没有可用后代时隐藏，默认 Tab 不可用时进入首个可用 Tab，不预读未授权内容。

详情摘要 renderer 通过 `featureActions` 接收本 View 声明的专用动作映射，形状为 `Record<key, { position, allowed }>`。模块以 `allowed` 控制呈现、以 `position` 发起该动作请求，例如 Organization 的账号绑定；不要导入 Shell 的内部位置 Context，也不要把其他动作或入口资格用作写入授权。输入、确认、pending、错误及刷新仍沿用既有动作生命周期。摘要的内容读取使用所属详情的读取上下文；例如 Identity 安全事件由模块自己的 summary data 传递 `readContext.featurePosition`，查询缓存同时区分位置，不能借用写动作或其他页面的读取资格。

## 通用对象授权界面

已登记的数据范围资源可复用 `@icp-platform/ui` 的 `createObjectAuthorizationActions(resourceKey, getTarget)`。`getTarget` 从动作的当前 `row` 或 `detail` 返回 `{ id, name, title }`，不缓存跨对象目标。将返回的 `actionInputRenderers`、`actionEligibilityResolvers` 和 `actionHandlers` 合并进标准资源注册；Example 的 `exampleRecordStandardCrudResource` 是列表接入实例，Organization 同样消费这套通用界面。

View 的两个动作分别配置 `key: authorize` / `viewAuthorization`，共同使用 `handlerKey: authorize`，输入键分别为 `objectAuthorization` / `objectAuthorizationDetails`。两者都要求对象授权管理资格，分别派生功能位置并绑定 `<resourceKey>.authorize`；查看界面的只读设置是呈现约束，不另行授予较低权限的审计身份。模块只声明按钮和目标，完整授权保存、最小主体目录、用户选择及权限来源解释均由通用组件和 Authorization 端点负责。

不使用标准动作注册时，也可在已有宿主中渲染 `ObjectAuthorizationInput`，传入 SDK 动作输入上下文及 `target: { resourceKey, id, name, title }`；`action.inputKey` 决定编辑或查看模式。权限解释默认当前用户，可以选择用户，展示已保存配置的最终数据范围结果及规则路径，不替代功能位置判权或业务校验。

`StandardCrudActionInputRenderer` 可附带 `dialogWidth` 和 `readOnly` 呈现提示。宽度由共享 ReviewDialog 应用并保持窄屏约束；只读输入仅关闭，不执行 handler、写入或触发写入后的刷新。没有提示的现有输入保持原行为。

## 声明式标准插件的动作

`standard-data.v1` 生成页面消费同一候选目录和运行时 metadata，功能位置沿标准 CRUD client 传递；插件 SDK 与运行时投影不再提供旧 `permissionCodes`。刷新跟随所属读取入口，不登记独立业务动作。

生成的启用 handler 调用 `client.enable(id, context)`，停用调用对应 `disable`，均沿发起动作传递位置。不能把启用改写为通用 `update` 来借用其他执行绑定；编辑表单仍使用原已声明的字段范围，独立状态动作不引入字段级授权。

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

异构只读列表可用 Adapter.getDetailTarget 返回已登记的 detailViewKey 与真实实体主键；共享行操作只解析正式所属详情路由并重新执行目标准入，行选择与关系命令仍使用 getRowId。动作输入需要跨资源候选时，可从 navigationFeaturePosition(targetKey) 取得当前允许的正式导航位置；缺失位置不得拼接身份或以原动作位置冒充目标读取。候选继续使用所属模块标准 GET 的分页与字段协议。

## 从标准详情进入只读关联列表

模块可在自己的 List View authoring 声明 `detailContributions`，每项含唯一 `key`、正式 `targetViewKey`、本模块 `navigationKey` 及 `pageRef`。PageRef 只允许本 List View 的 `platform.list` 和本模块已登记的 inputResolver；参数 `anchorModelKey` 必须与目标详情的规范模型身份一致，不能声明 `recordId`。`WorkspaceReferencesList.view.json` 是三个真实详情消费者的接入实例。

准备阶段核对来源绑定/导航归属、目标详情能力及模型、唯一 key/目标、PageRef 和 resolver，任一缺失或歧义拒绝候选。运行时仅从通过详情契约的真实对象注入 `recordId`，由 resolver 形成该列表的 queryScope；标题来自来源 View 词典。入口按来源导航授权，目标详情读取位置保留所属身份；无来源资格时不显示入口或发起关联读取。服务器仍独立验证锚点与关联对象资格。

共享宿主在 Drawer 和独立详情使用同一入口，以 Modal/PageOutlet 装载标准列表；它不接受任意组件或 URL。每个 Modal 使用独立组合与列表状态，真实对象或资格变化释放旧读取，关闭回到原详情。多个纯内嵌列表可以复用同一 Drawer 详情 View；拥有独立详情路由的 capability 仍必须唯一，不因此生成额外路由。

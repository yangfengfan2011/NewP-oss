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

树与其右侧列表、动作 Drawer 共享当前 `StandardCrudActionContext.instance`，独立页面可省略该实例。模块用静态稳定 `Symbol` owner token 调用 `getOrCreate<T>` 持有自己的类型化内存会话，并用 `changed()` 通知动作状态重新读取；`actionStateResolver` 同时收到当前 `instance` 与 `queryScope`。容器归树/页面实例，切换目录保留，切换主体或销毁时执行模块 dispose；React effect 重挂载重新建立状态，旧异步响应不能复活。它不按 resourceKey 建立全局仓库，不持久化状态或代模块重放写入。`queryScope`、会话及动作定义只提供定位与呈现输入，服务端必须独立重验权限与关系。 树菜单仅为当前打开菜单实际引用的标准 list 装载同一注册及 schema，经纯 input resolver 提供定位上下文，复用相同 actionStateResolver 的可见性、禁用和原因，并在点击时重验；预览不执行 handler、input prepare 或对象读取。没有 row/detail 快照时不声明已完成异步对象资格裁决，最终 Drawer 与服务端仍各自重验。装载失败或过期结果保持不可执行。

`actionHandlers` 可以返回不透明的 `StandardCrudOperationResult`。Shell 只把该结果交给模块注册的 `operationResultPresenter`，不检查、持久或重放 payload。模块 presenter 拥有具体结果形状、展示、复制与关闭销毁时机。动作发生并发冲突时，Shell 刷新最新快照并结束旧确认流程，不自动重放写入。

已确定可由用户修正输入的409错误，所属 handler 可附带同步纯判断 `isCorrectableInputError(failure)`。只有该 handler 明确返回 `true` 时，共享宿主保留输入及原错误，等待用户修正后再次提交；它不改变 HTTP 状态、原错误事实或提交结果，不自动重放。未声明或未匹配的409继续刷新并结束旧确认流程，生命周期冲突不能作为普通字段错误忽略。

## 标准路径的位置授权

标准功能声明与派生规则见 [Module Contract](../../docs/architecture/module-contract.md#功能位置授权)。Shell 消费活动目录中的完整 position，并按导航、Tab 和树的静态节点类型传递所属入口。独立详情使用声明的入口，重定向采用目标入口，Drawer 沿用发起动作的位置；模块不从 URL、Referer、点击历史或记录 ID 推导位置。

`StandardCrudFeatureRequestContext.featurePosition` 是每次请求的执行上下文。标准 client、data adapter、Lookup 和 action handler 沿调用链传递它，由请求写入 `X-NewP-Feature`；不把位置加入业务 payload、筛选值或查询 URL，也不使用可被并发操作覆盖的全局请求头。自定义 handler 调用模块服务时同样传递 `context.featurePosition`。位置只表明请求要执行的声明功能，服务端仍按当前可信主体判权。既有 Host policy 等额外约束继续生效，例如 Identity 账号管理仍要求 `AdminOnly`，不能仅凭功能点投影推断可执行。

需要输入的动作通过 `StandardCrudActionInputRenderContext.featurePosition` 将发起动作位置传给 renderer。列表及详情共用 ReviewDialog；renderer 调用准备端点时传递该位置，最终 handler 继续使用同一位置。例如账号角色弹窗的账号候选、角色分页目录、当前分配与保存由该动作拥有，不能要求用户再获得其他管理页面的资格。

标准列表按 metadata 顺序呈现新建及声明 handlerKey 的页动作。页/行动作的 handler 和输入 renderer 可读取当前页面的可选 queryScope；它只是定位输入，服务端必须重新校验空间、路径和对象资格。输入 renderer 的 submitting 表示共享执行器正在提交，可交给标准表单禁用输入；弹层、草稿保护、防重复提交与关闭仍由共享宿主持有。写入完成后异步列表刷新失败保留“已保存”事实，重试只读取；该事实按主体、页面实例和查询身份隔离，并在成功读取后清除。

既有快捷跳转动作声明 `navigationTargetKey`，handler 返回 `StandardCrudOperationResult.navigation`，按目标导航资格显示并执行目标准入。普通 `view` 没有目标时仍打开原属性查看器。

另一个明确受限的组合是列表行 `standardAction: view` 与 `navigationTargetKey`：目标必须是已声明 `routeOnly: true`、`featureAuthorization: false` 的呈现页面，动作不能再有 handler/input，目标不能有 featureKey、featureOwner 或 redirect。它沿当前真实 read 位置显示并直接使用既有入口导航，不进入写执行器的 pending；真实未保存草稿的导航保护保持。模块可用纯同步 `dataAdapter.getReadNavigationParameters(row, { queryScope })` 投影明确的字符串定位字段，不能转储整行。目标仍由 metadata 决定，Shell 拒绝投影内的 `featurePosition` / `sourceFeaturePosition`，最后加入当前 read 位置作为 `sourceFeaturePosition`；没有投影的普通实体只传真实 recordId。缺失必要来源须拒绝，不回退裸对象ID。该位置是请求上下文，服务端仍每次判当前功能资格及完整对象访问；不产生新导航/Tab grant或全局权限继承。同一入口切Tab的search更新保留定位state，跨入口仍使用新参数。

标准 view 也可导航到已登记的独立功能入口；此时按目标导航的真实资格显示和分派，不继承来源读取资格。仍只同步投影完整定位参数并经既有入口导航执行，不调用异步业务 handler 或写执行器。目标页面使用该导航自己的功能位置，详情及输出可复用同一履历资格。

运行时动态列的 `StandardCrudColumnContext.featurePosition` 同样来自所属读取入口。column provider 与行 data adapter 各自沿请求传递该位置；例如 Example 动态工作量的列请求和行请求使用相同的入口位置，原有模块列上下文 token 仍负责列与行的业务一致性，不能用它替代授权位置。宿主的动态列缓存已经按读取位置隔离。

列表与详情读取、Lookup 的缓存按位置隔离。标准宿主只加载可用入口及动作所需读取，创建或编辑的准备步骤和提交使用同一动作位置；动作 Drawer 不加载底层列表查询的 Lookup。纯容器没有可用后代时隐藏，默认 Tab 不可用时进入首个可用 Tab，不预读未授权内容。

详情摘要 renderer 通过 `featureActions` 接收本 View 声明的专用动作映射，形状为 `Record<key, { position, allowed }>`。模块以 `allowed` 控制呈现、以 `position` 发起该动作请求，例如 Organization 的账号绑定；不要导入 Shell 的内部位置 Context，也不要把其他动作或入口资格用作写入授权。输入、确认、pending、错误及刷新仍沿用既有动作生命周期。摘要的内容读取使用所属详情的读取上下文；例如 Identity 安全事件由模块自己的 summary data 传递 `readContext.featurePosition`，查询缓存同时区分位置，不能借用写动作或其他页面的读取资格。

## 通用对象授权界面

已登记的数据范围资源可复用 `@icp-platform/ui` 的 `createObjectAuthorizationActions(resourceKey, getTarget)`。`getTarget` 从动作的当前 `row` 或 `detail` 返回 `{ id, name, title }`，不缓存跨对象目标。将返回的 `actionInputRenderers`、`actionEligibilityResolvers` 和 `actionHandlers` 合并进标准资源注册；Example 的 `exampleRecordStandardCrudResource` 是列表接入实例，Organization 同样消费这套通用界面。

旧通用 View 两个动作仍可配置 `key: authorize` / `viewAuthorization`，共同使用 `handlerKey: authorize`，输入键分别为 `objectAuthorization` / `objectAuthorizationDetails`。没有 owner basePath 的旧通用合同保持两者均需 `<resourceKey>.authorize`，不因新查询而放宽原API资格。

采用可浏览查询的 owner 可给 target 增加 `basePath`（含完整来源参数），复用 `ObjectAuthorizationPage` 的只读权限页及上述标准 read 导航组合；管理继续现有标准动作。共享请求在URL pathname上追加 catalog/explanation/subjects/eligibility 后缀并保留原查询；完整配置读写使用basePath本身，不拼接到问号之后。owner只读目录/账号选择/解释由本资源read入口保护，完整配置/管理主体候选/保存仍独立authorize。Workspace basePath里的LID与完整位置只用于来源校验，ACL身份始终是资源键+Entity EID；独立owner入口不要求LID。解释只消费server为实际命中主体投影的安全名称，不为浏览账号额外开放角色/公司/部门候选。选择被分析账号不切当前登录主体，数据动作结论不代表完整执行资格。

完整来源、目标及当前主体参与读取和资格身份；变化时取消并失效旧响应。新的owner保存必须有活动 action instance，未知提交结果在该实例内按完整来源冻结，跨关闭/重开不得重发；只能显式GET核对当前配置，失败、拒绝或不同当前值均不能推定原提交失败或解锁重放。SDK input context 的 `submissionFailure` 保留原结构化错误，submitting期间禁用主体/期限编辑。明确可修正错误保留草稿，204后自身失权仍保持已保存成功，不回读敏感配置。共享主体编辑器仅对象模式启用秒级UTC绝对期限，规则默认不接收期限或对象相对类型。

不使用标准动作注册时，也可在已有宿主中渲染 `ObjectAuthorizationInput`，传入 SDK 动作输入上下文及 `target: { resourceKey, id, name, title }`；`action.inputKey` 决定编辑或查看模式。权限解释默认当前用户，可以选择用户，展示已保存配置的最终数据范围结果及规则路径，不替代功能位置判权或业务校验。

`StandardCrudActionInputRenderer` 可附带 `dialogWidth` 和 `readOnly` 呈现提示。宽度由共享 ReviewDialog 应用并保持窄屏约束；只读输入仅关闭，不执行 handler、写入或触发写入后的刷新。没有提示的现有输入保持原行为。

## 声明式标准插件的动作

`standard-data.v1` 生成页面消费同一候选目录和运行时 metadata，功能位置沿标准 CRUD client 传递；插件 SDK 与运行时投影不再提供旧 `permissionCodes`。刷新跟随所属读取入口，不登记独立业务动作。

生成的启用 handler 调用 `client.enable(id, context)`，停用调用对应 `disable`，均沿发起动作传递位置。不能把启用改写为通用 `update` 来借用其他执行绑定；编辑表单仍使用原已声明的字段范围，独立状态动作不引入字段级授权。

## 验证

工作区对锁定的 `antd@5.29.3` 使用[默认语言依赖补丁](../patches/antd@5.29.3.patch)，让 Empty、ConfigProvider 的 Form 默认消息及 Modal 默认词条各自消费独立对象，避免认证语言选择器静态带入日期 locale。全量 `en_US` 继续引用同一组对象，ESM/CJS、词条内容、覆盖规则和控件呈现保持一致；模块不自行复制词条或替换控件。升级 Ant Design 时重新核对上游实现，复跑 Shell 的 `authentication-localization`、`antd-empty-locale`、`antd-default-locale`、`antd-locale-provider` 测试及原构建图检查；只有上游同样满足依赖边界和兼容行为后才移除补丁。

- 模块构建证明 TypeScript binding 与依赖完整。
- Canonical Module Plan 测试证明 binding 缺失或 owner 冲突会拒绝整份候选。
- Shell 测试证明单入口失败不会产生部分提交。
- Host smoke 证明活动目录包含该模块，并与其他投影共享 `snapshotId` / `planHash`。

```powershell
corepack pnpm --dir frontend --filter @icp-modules/<module-id> build
corepack pnpm --dir frontend --filter @icp-platform/shell test
```

外仓 `standard-data.v1` 插件不携带任意前端运行时代码；它使用独立 build-only plugin SDK 生成声明式标准投影。代码型外部 profile 必须另行设计。

异构只读列表原有 Adapter.getDetailTarget 路径保持：返回已登记的 detailViewKey 与真实实体主键，重新执行目标准入，行选择与关系命令仍使用 getRowId。

需要保留来源的属性页可在 `platform.detail` PageRef.parameters 显式声明 `detailReadSourceKey`。源由 `standardCrudDetailReadSources` 贡献 `{ key, resourceKeys, get(resourceKey, id, context) }`，key与页面/resolver等实现键一起进入现有Capsule.bindings；重复、未知key或资源不匹配拒绝装配。平台仍选择目标真实registration和已编译schema，只替换只读get；源核对服务器实际resourceKey/EID与目标一致，不接受任意URL/布局。来源属性页关闭编辑、摘要交互及按裸EID外跳的detailContributions，默认无source的详情行为保持。模块resolver负责明确记录/来源并保留read上下文，缺来源直接拒绝；独立owner可在原adapter.get里保留原featurePosition优先，承接本模块queryScope.sourceFeaturePosition，不导入Shell内部context或复制schema。自定义PageProps收到当前解析placement的featurePosition；没有placement时平台去除同名外来参数，呈现页的sourceFeaturePosition仍须由标准read导航提供并经服务端重新验证。动作输入需要跨资源候选时，可从 navigationFeaturePosition(targetKey) 取得当前允许的正式导航位置；缺失位置不得拼接身份或以原动作位置冒充目标读取。候选继续使用所属模块标准 GET 的分页与字段协议。

来源读取可额外声明可选 `navigationContributions: [{ key, labelKey, navigationKey, getParameters(resourceKey, id, context) }]`。这是来源注册拥有的固定导航目标，不是恢复普通详情贡献：Shell 仅在该来源详情读取成功、规范对象身份仍一致且目标导航真实存在并获独立资格时显示入口。投影必须同步且只返回完整字符串定位参数，不得包含 URL 定位键（`url` / `href` / `path`）或 `featurePosition` / `sourceFeaturePosition`。点击直接经既有入口导航及组合草稿保护，不进入写执行器；目标 PageEntry 提供自身真实功能位置，不能借来源 read 位置。模块必须校验 resourceKey/id 与完整来源一致；来源缺失或变化直接拒绝，不回退裸 EID。来源页的编辑、摘要及原 detailContributions 仍然关闭。

## 从标准详情进入只读关联列表

模块可在自己的 List View authoring 声明 `detailContributions`，每项含唯一 `key`、正式 `targetViewKey`、本模块 `navigationKey` 及 `pageRef`。PageRef 只允许本 List View 的 `platform.list` 和本模块已登记的 inputResolver；参数 `anchorModelKey` 必须与目标详情的规范模型身份一致，不能声明 `recordId`。`WorkspaceReferencesList.view.json` 是三个真实详情消费者的接入实例。

准备阶段核对来源绑定/导航归属、目标详情能力及模型、唯一 key/目标、PageRef 和 resolver，任一缺失或歧义拒绝候选。运行时仅从通过详情契约的真实对象注入 `recordId`，由 resolver 形成该列表的 queryScope；标题来自来源 View 词典。入口按来源导航授权，目标详情读取位置保留所属身份；无来源资格时不显示入口或发起关联读取。服务器仍独立验证锚点与关联对象资格。

共享宿主在 Drawer 和独立详情使用同一入口，以 Modal/PageOutlet 装载标准列表；它不接受任意组件或 URL。每个 Modal 使用独立组合与列表状态，真实对象或资格变化释放旧读取，关闭回到原详情。多个纯内嵌列表可以复用同一 Drawer 详情 View；拥有独立详情路由的 capability 仍必须唯一，不因此生成额外路由。

## 非呈现引用动作与成功回执

List View 已有 `featureActions` 保持非呈现功能目录。只有显式声明 `handlerKey` 与 `labelKey` 的项可被树等受控入口作为可执行动作引用；纯功能项保持原行为。可执行项的 `behaviorKey` 必须与资源和 handler 派生的行为一致，沿原注册点校验 handler/input，不能复制成列表页/行动作或贡献列表按钮。树引用独立编译自己的 contextMenu 功能位置，handler 获得当前已校验的可选 `StandardCrudActionContext.action`；客户端定义仍不是服务端权限凭证。

已确认成功且没有写入的动作可以返回 `StandardCrudOperationResult.noChange=true`，并用 `notice.reasonKey` 引用正式词典。共享成功交接仍处理原回执，只跳过变更发布和读取刷新；notice 由外层组合的现有消息 holder 显示，关闭子动作后仍可见，过期、已卸载或切换主体/组合的结果不发布到新上下文。该标记不能表示失败或未知结果；与实际 change 冲突时报告成功同步故障并保留实际变更刷新。普通成功动作保留既有 change/refresh 生命周期。

## 标准列表列宽拖动

List View 的 `columnResize: true` 开启数据列的表头拖动；省略或 `false` 不提供拖动入口。列的 `width` 是初始宽度，省略时沿用表格自动布局；单列 `resizable: false` 可禁止拖动。勾选框、序号和操作列由共享表格固定，不参与拖动。本契约不包含 `minWidth`、`maxWidth`。

```json
{
  "columnResize": true,
  "columns": [
    { "key": "name", "field": "Name", "source": "projection", "labelKey": "Name", "width": 180 },
    { "key": "type", "field": "ObjectType", "source": "projection", "labelKey": "ObjectType", "resizable": false }
  ]
}
```

宽度复用 `icp.standard-crud.columns` 的当前账号与 View 隔离偏好，保存于当前浏览器 localStorage；不跨设备同步，不写回元数据。拖动仅预览，松开提交；取消拖动丢弃预览。分隔线支持键盘左右方向键调整，且不触发表头排序。列设置的重置使用当前元数据默认值，恢复初始宽度（未声明宽度的列恢复自动布局）、显隐与顺序。关闭 `columnResize` 时忽略宽度覆盖并隐藏手柄，既有个人偏好保留。

# Module Contract

## Authoring owners

- Module Capsule 根声明唯一拥有 module id、display name、version、host contract range 与 dependencies。
- 后端 C# behavior binding 只注册 DI、endpoint 与服务实现，并从 runtime context 获取 canonical identity。
- navigation / view / model / security / localization authoring 分别拥有静态路由、菜单、模型、功能授权声明和文案事实。
- 前端 TypeScript behavior binding 只把稳定 key 绑定到 page、renderer 或 action implementation。

C#、TypeScript 和子清单不得重新声明 Capsule 已拥有的 identity，也不得与 authoring metadata 平行维护 route、menu 或功能授权声明。

## Runtime contract

`CanonicalModulePlanCompiler` 把候选 Capsule、metadata 与 behavior references 编译为一份确定的 Canonical Module Plan。Host 与 Shell 只消费该计划生成的活动投影：

- backend load view；
- `GET /plugins/active-module-runtime.json`；
- frontend / metadata / localization compatibility projections；
- feature catalog 与 model indexes。

所有投影由同一启动期计划生成并记录 `snapshotId` / `planHash`。冲突或引用缺失必须在该次启动中拒绝整份 candidate；不得依赖加载顺序覆盖、warning 后跳过或部分注册。当前不支持进程存活期间改变插件集合，因此跨请求投影 identity 核对、运行中原子切换与回滚不属于本契约；见 ADR-0032。

## 功能位置授权

标准功能入口在 navigation 声明 `featureAuthorization: true`。`StandardFeatureMetadataCompiler` 展开真实 View 和 PageRef，`FeatureCatalogCompiler` 与 Capsule 的自定义功能声明合并为同一目录。导航、Tab、动作是授权点；页面和静态类型展示组不是授权点。同一 View 在不同分支复用时分别生成位置，局部动作 Key 可以重复。

功能目录保留 `Title` / `StaticScopeTitle` 作为原诊断和旧声明的回退标题，同时公开可选 `TitleKey` / `StaticScopeTitleKey`（JSON 为 `titleKey` / `staticScopeTitleKey`），供呈现层按当前语言查词。标准适配器从导航完整 `titleKey`、View 的 `dicKey` 与 Tab、节点类型或动作标题键组合完整词条；节点组标题键与原静态范围标题同源。内建导出使用公共 `Platform.Export`。Capsule 自定义 `features` 可声明完整 `titleKey`，省略时保持原标题，不从中文或功能位置反推词条。译文事实不参与位置、行为、排序和入口依赖身份。

`FeatureCatalogCompiler.ValidateSelection` 的未知／失效位置和缺少本分支入口错误通过 `FeatureSelectionException` 保留稳定事实，该异常继承 `InvalidOperationException` 并保留原诊断。`Code` 分别为 `FeatureSelection.UnknownPosition`、`FeatureSelection.EntryDependencyMissing`；`Args.position` 保留原输入，缺入口另带依赖顺序中首个缺失的 `dependencyPosition`。模块负责将这些事实映射到自己的 API 错误及译文，内核不解析诊断或引用业务词典。

View 的 `resourceKey` 指定稳定资源；`lookupBindings` 在该 View 的资源范围内把模型字段或查询 override 所用 provider 绑定到只读 behavior。专用动作通过 `featureActions` 声明 `key`、`titleKey`、`behaviorKey` 及可选 `readBehaviors`。标准 `pageActions` / `rowActions` 也可声明 `readBehaviors`，补充动作输入所需的只读行为；它们与资源读取和实际表单 Lookup 一同随发起动作授权。这些绑定描述实际消费能力，不向管理员增加接口权限清单。

完整位置由声明引用及其祖先、静态节点类型和动作所在位置编译产生，不能使用 URL、文案、数组序号或记录 ID。导航的 `featureOwner` 为重定向别名和独立详情指定已有入口引用；别名不创建重复授权点。`ReferenceKey`、`StaticScope`、`ActionScope`、`StaticScopeTitle` 和 `Container` 是标准适配器的派生事实，不能据此推断 Capsule 自定义声明已经支持这些输入字段。

纯导航快捷动作以 `navigationTargetKey` 引用已有导航，沿用现有 handler 与 navigation-result 跳转；按目标导航的位置资格呈现，不生成独立业务动作点。目标必须是采用位置授权的真实导航，缺失或无效引用拒绝候选；不能借用当前资源的查看或保存资格。

Host 在开放请求前核对目录与真实端点的模块归属及执行绑定。`RequireFeature` 只允许当前位置声明的行为；附加读取必须绑定 GET/HEAD 端点，纯容器不获得后代读取。已有 Host policy 可作为同一端点的附加约束，与功能位置判权共同满足；它不能代替位置授权，也不与旧 Permission、匿名或拒绝规则组成另一条放行路径。缺失、冲突或无效绑定拒绝整份候选。运行时投影提供编译后的 position，前后端不得另外拼装授权身份。

Shell 在私有上下文解析器内把当前入口、静态范围和目录绑定为 `read`、`navigation`、`tab` 及具体 surface 的 `action`，consumer 只接收 `allowed(position)`、`denied(position)` 或 `unresolved` 三态，不读取原始目录点或自行匹配身份。目录定位独立于当前授权投影；授权刷新只按已绑定位置重投影当前三态，展示与执行消费同一轮结果。`unresolved` 不等于放行，各宿主按既有无位置契约显式处理；该解析器是 Shell 内部边界，不新增 SDK 或服务端契约。

当前 Organization 标准路径，以及 Authorization 的角色读取、创建、编辑、账号角色分配和工作台消费位置授权。角色页面未声明删除动作，遗留删除 HTTP 入口明确拒绝；旧角色 Permission 读取/保存及旧权限目录接口已退出。

Identity 账号管理入口也消费位置授权，并保留既有 `AdminOnly` Host policy；读取、创建、编辑、启用、停用、删除和重置凭据分别绑定声明行为。启用和停用使用独立的 `/status/enable`、`/status/disable`，校验请求的目标状态，旧 `/status` 入口明确拒绝。

Audit 列表和详情使用 `audit.change.read`，并继续由 `ILocalAdministratorRoleAssignment` 检查当前有效管理员分配；入口授权不替代该资格。Example 记录的读取、创建、编辑、删除和导出分别消费正式位置，动态工作量页面的列与行共用所属入口的只读位置。

正式业务消费者使用功能位置授权，不保留旧 Permission 放行路径。公开、仅认证、Host policy 及明确拒绝入口沿用原契约。旧存储清理与回退边界见 [ADR-0048](../adr/0048-functional-authorization-uses-entry-positions.md#切换与回退)。

## 声明式标准插件

`standard-data.v1` 由 `StandardDataMetadataProjection` 根据同一包内 blueprint、标准投影和 metadata 生成导航、列表与详情声明。候选编译使用该输出登记完整功能目录，运行时物化复用同一输出；Model 仍由原持久化/模型投影拥有。刷新是纯交互，不生成授权点；显式详情保留原结构并绑定所属入口。

C# 与 TypeScript 插件 SDK 不再提供旧 `PermissionCodes` / `permissionCodes` 形状。正式候选与生成页面消费功能声明及编译位置，目录与执行绑定仍必须完整校验。

标准启用和停用绑定独立行为。`POST /{id}/enable` 与 `/disable` 固定写入对应状态，不从请求 body 接收其他更新字段；编辑动作保留其正式字段白名单。`ByFeature` 按声明操作绑定功能位置，不从资源命名约定生成旧业务权限。

## Guardrails

- Host 只组合、装载和提供宿主级横切能力，不吸收模块私有业务规则。
- Shell 不维护业务模块 ID 列表；前端实现由 Active Module Runtime Catalog 选择并一次提交。
- 共享声明通过 Kernel / Contracts / SDK 的稳定契约协作，不复制进模块。
- migration/import 逻辑留在显式 persistence migration seam，不进入 Host 组合。
- compatibility adapter 必须显式标记并有退出条件，不得成为新的平行真源。
- Host 运行期间的插件安装、升级、启用、停用、卸载和回滚不属于受支持 module contract；只读 inventory 只观察停服部署结果。

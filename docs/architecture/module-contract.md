# Module Contract

## Authoring owners

- Module Capsule 根声明唯一拥有 module id、display name、version、host contract range 与 dependencies。
- 后端 C# behavior binding 只注册 DI、endpoint 与服务实现，并从 runtime context 获取 canonical identity。
- navigation / view / model / security / localization authoring 分别拥有静态路由、菜单、模型、功能授权声明和文案事实。
- 前端 TypeScript behavior binding 只把稳定 key 绑定到 page、renderer 或 action implementation。

C#、TypeScript 和子清单不得重新声明 Capsule 已拥有的 identity，也不得与 authoring metadata 平行维护 route、menu 或功能授权声明。

## 组件组合

Capsule 通过可选 `composition.members` 数组引用成员组件 ID。同一声明可嵌套组合，但每个成员只能有一个直接父组件。成员重复（含大小写重复）、缺失、自包含、多父或环均拒绝完整候选；错误数组形状不得被静默忽略。组合声明独立于 `dependencies`，归组或移组不增加能力依赖，也不改变成员身份、版本、模型 owner、物理映射或 migration stream。

组合图采用本次期望选定版本的声明，停用组件仍参与完整图校验；安装但未选中的版本不改变图。`CanonicalModulePlan.SelectedModules` 保留同一候选的选定声明，`CompositionGraph` 给出版本、自身启用选择、直接父组件和规范排序的成员。`Modules` 只包含自身选择启用且全部祖先启用的组件；业务装配、页面和功能目录只消费该活动集合。父级屏蔽不改写成员选择，恢复不会打开显式停用成员；活动依赖不闭合在持久化编译与 DDL 前拒绝整份候选。

`active-module-runtime.json` 从同一规范计划公开 `compositionGraph`，活动模块条目保留 `compositionMembers` 和 `parentModuleId`。没有 backend entry、且显式关闭 frontend 的纯组合组件不需要空实现程序集或页面。Host 只装载成员原有行为，Shell 仍以成员自己的身份绑定前端实现。

InforCenter 是无自有模型和迁移流的纯组合组件，直接成员为 Workspace、Part、Document。Workspace 的 Part、Document 能力依赖保持显式，工作空间内容 Link 与对象所属 API/页面不表示组件父子关系。模型身份采用下文全局技术名称，启停与保留持久化遵循 ADR-0055。

## 停用与保留部署

内建可选组件的选定版本与自身开关也可由受控 desired 声明；选定版本必须与可信内建发布物或已安装包一致，不补回被停用的内建组件，不静默回退版本。首次停用或屏蔽的组件不编译业务持久化贡献、建表或执行迁移；已有部署的组件则从最后成功部署资产保留旧模型与 migration stream。停用时选定新版只影响完整组合图，重新有效启用后才编译并迁移该版本。

同一次启动候选形成活动能力和数据库部署视图。`CompiledPersistencePlan.Models/Streams` 包含活动目标及保留目标，`RetainedModuleIds` 标识仅用于数据库验证的 owner。保留结构参加全局名称、物理目标、外键归属、字段、manifest、Provider Target 和实际 schema/history 的严格核对；保留外键不要求其业务能力活动。业务服务、控制器、CRUD 资源、Link 入口、页面及授权目录仍由活动 Canonical Plan 装配，保留 EF 结构不授予访问资格。仍有数据的模型技术名称和 owner 不得转移、改名或移除。

当前 SQL Server Host 的 `PersistenceDeploymentStore` 在 plugin install root 下的 `persistence` 目录保存按 provider、明确服务器及 catalog 绑定的不可变内容寻址资产。资产保留正式编译的 canonical 模型、owner/version/source、manifest 和已验证程序集字节；读取时重核内容摘要、模型摘要、manifest 链、程序集摘要及 frontier。不能从当前磁盘包重建缺失的保留资产，未知表、真实 drift 或资产缺失继续阻断。

本机独占部署租约串行发布引用，数据库写入仍使用原全局迁移锁与逐步事务。`PluginActivationGate.Prepare` 在静态编译和验证后、Host Build、ownership guard 和首个可能提交的 DDL 前，通过统一部署适配器 flush 并原子发布恢复资产与 `pending` 引用，再返回持有同一份 Module Plan 和 Persistence Plan 的会话；首次尚无 successful 记录时也保留完整可信输入。重启编译消费 pending 的模型、owner 和不可变步骤约束，同候选或前向候选必须保持原 owner stream 的完整不可变前缀。完整目标不代表已经应用：执行器在 SQL 锁内核对真实 EF 成员、NewP Position、摘要、Provider Target/Transition 及 owner 前缀，只补未提交步骤。被停用 owner 尚有未应用步骤时按真实 frontier 拒绝，不能把完整 pending 目标视为已部署结构或执行其剩余 Up。

SQL Server Host 和发行离线入口在读取 active/LKG 时，按同一 provider、服务器和 catalog 的成功资产核对 snapshot ID、module plan hash 与 persistence plan digest。`IPluginActivationDeployment` 把恢复编译、准备归档、最终确认与读取策略绑定到同一存储实例；生命周期读取方必须显式选择适配器，激活 Gate 从该读取方取得同一实例，不允许另外拼接编译或确认回调。Host 明确选择 SQL Server 部署确认或既有无部署确认分支，后者不扩大 provider 支持范围。Host Build、注册与端点校验后，`PluginActivationSession.CompleteAsync` 依次执行 migration 与只读检查、runtime staging、权限同步、raw active 提交、runtime 确认和最终部署确认；部署存储最后一次原子替换发布 `successful` 后，会话才完成。激活完成仍早于维护命令、缓存 warmup、HTTP 监听和 Host 整体 Ready。同时保留紧邻 `previousSuccessful`，使旧已确认 LKG 仍可读取和选择；未获最终确认的 raw snapshot 不进入活动集合。迁移后校验、活动提交、最终确认失败或进程中断均保留 pending 和既有成功事实；重试仍走正式编译与真实历史核对，不依赖 catch，不自动 Down、清 pending 或重写历史。

各 owner 的已应用步骤独立保持 manifest `Sequence` 的线性前缀。`dbo.__EFMigrationsHistory` 只保存全局唯一已应用 ID 集合，`dbo.__NewPMigrationHistory.Position/PlanDigest` 保存实际提交顺序及不可变前缀摘要。候选在 owner 队列头之间按 MigrationId 确定执行顺序，既有事实按数据库 Position 只读核对，新的 Position 和摘要在全局事务锁内绑定；ID 较早的延期或首次启用步骤可追加到其他 owner 已提交步骤之后。停用 owner 只核对保留流，不消费所选新版；完整目标/过渡、owner、所有摘要、未知对象、owner 跳步和数据库超前仍严格阻断。迁移与一致性成功之前不得公开新活动版本，见 [ADR-0027](../adr/0027-host-is-the-only-full-up-migration-entry.md)。

正式离线选版、移组、解除归组与卸载入口见[停服部署入口](../developer/external-plugin-development.md#停服部署入口)。工具只保存库存或期望选择；单组件与显式精确集合卸载在删除前先检查平台必需保护，再用同一 Canonical 编译器验证卸载后的完整组合图和有效活动依赖，阻塞时不修改集合、代码、库存或选择。完整 Host 重启仍在 DDL／活动提交前验证最终候选。停用父级不能豁免缺失成员，可信保留资产不代表成员已安装。移组只重新计算自身选择与祖先共同决定的有效资格，不改模型名称、owner、物理目标或迁移流。代码卸载与精确包裁剪不删除独立持久化校验资产。

## 全局模型身份

Model authoring 的技术名称是平台范围内唯一的引用身份，例如 `Part`、`Document`、`WorkspaceFolder` 和 `IdentityAccount`。Canonical Module Plan 的 `ModelKeys` 与运行时 `ModelDefinition.LogicalKey` 直接采用同一声明名称；名字按大小写不敏感规则拒绝重名，引用使用声明的规范大小写，不接受旧 `ModuleId/ModelName` 别名。组件归组不改变名称，当前不支持技术名改名或模型 owner 转移。

`OwnerModuleId` 独立声明唯一拥有者。EF shared-type entity、外键目标、Link 的 `linkedTypes`、固定端点、公共列映射、显示、CRUD、授权、缓存及 PageRef 输入共用全局名称；Workspace 的 `ChildModelKey` 也保存该名称；个人空间以 `SubjectId` 固定关联账号，不另存主体模型键。同一对象 ID 在不同模型中仍是不同对象，Link 的 LID 保持独立关系身份。资源键、behavior key、功能位置、路由和物理 schema/table 不由模型键派生，也不随身份切换改名。

归属从正式编译模型目录解析，资源 owner、真实主键、活动集合、平台基础或显式业务依赖及实际服务端授权继续独立验证。外键显式声明的 `principalModuleId` 保留为独立归属约束，不再拼入目标模型键。

发布的 ModelDigest v1 保持原 `owner/name` 的 Ordinal 序列化顺序；该组合只属于内部持久摘要排序，不能作为目录、EF 或 API 的寻址键。迁移 owner 和物理 target 保持独立，按正式 `migration-manifest check` 的真实 digest、Provider Target/Transition 结果决定是否需要 authoring 新迁移，不因逻辑名称切换改写已发布前缀或手改生成资产。

模型引用值的协议切换不提供存量旧 qualified 值的 alias 或回填兼容，即使物理 schema 没有变化。首次验证使用受支持的空 catalog；已有开发数据须按 ADR-0042 确认归属和可丢弃边界后显式重建，Host 不自动重建。回退需要配套旧代码与兼容数据库备份，或对已确认可丢弃的开发 catalog 显式重建；仅回退源码不能让旧 Host 正确消费新格式引用值。

## 平台必需基础集合

Host 发布物的 `module-composition.json` 只通过 `platformCompositionModuleId` 指定平台组合角色。当前角色引用既有 Platform Capsule；初始成员唯一声明于 `apps/host-api/modules/Platform/plugin.json` 的 `composition.members`，保留其既有语言与字典贡献。Host、migration-manifest preflight 和 runtime materializer 读取同一发布角色，Canonical 编译器由组合自身及完整成员后代派生 `RequiredModuleIds`；角色和派生结果都参与计划 hash，并由活动目录公开，不在 Host、Shell 或业务 Capsule 复制名单，也不按组件名称推断必需性。

必需组合或成员缺失、卸载后仍被引用、直接停用，或被外部祖先屏蔽时，完整候选在持久化编译与 DDL 前拒绝；不改写期望版本或成员自身启用选择。内建交付不等于必需，示例不在该集合；成员的模型 owner、版本和 migration stream 继续独立，`platform.foundation` 不变为 Platform Capsule 的自有迁移。Organization 已成为必需基础成员；Identity、Authentication 的可信来源、基础模型与 migration frontier 检查继续有效。

Link 端点与对象动作扩展通过 `ModuleDependencyPolicy` 消费同一活动计划：目标为必需平台基础或来源显式声明的业务依赖时可引用，两端必须活动；资源与目标 owner、主键、对象开放动作和用户授权仍独立校验。Workspace 仅声明 Part、Document 两项业务硬依赖，可以省略 Identity、Authentication、Authorization 基础依赖。停用或缺失业务目标仍拒绝，不用组合归属替代能力依赖。

PluginOps 参与基础候选，其 Capsule 继续关闭前端。ConfigCenter、MetadataConfigCenter 等既有冻结入口保持关闭；Files 的正式生命周期由 owner Contracts 提供，旧 raw upload/download 入口保持原拒绝契约。运行时插件写操作继续遵守 ADR-0032 的 `501 / PLUGIN_RUNTIME_OPERATIONS_UNSUPPORTED`。可选组件的祖先屏蔽和保留持久化生命周期见上文及 ADR-0055。

## Runtime contract

`CanonicalModulePlanCompiler` 把候选 Capsule、metadata 与 behavior references 编译为一份确定的 Canonical Module Plan。Host 与 Shell 只消费该计划生成的活动投影：

- backend load view；
- `GET /plugins/active-module-runtime.json`；
- frontend / metadata / localization compatibility projections；
- feature catalog 与 model indexes。

所有投影由同一启动期计划生成并记录 `snapshotId` / `planHash`。冲突或引用缺失必须在该次启动中拒绝整份 candidate；不得依赖加载顺序覆盖、warning 后跳过或部分注册。当前不支持进程存活期间改变插件集合，因此跨请求投影 identity 核对、运行中原子切换与回滚不属于本契约；见 ADR-0032。

普通内建模块在候选签发前，沿主体 PE 的精确程序集引用递归捕获 owner Contracts；文件只经可信发行来源读取，程序集身份必须匹配，循环引用去重，同名身份冲突拒绝。该闭包字节参与快照摘要，装载时不探测未签发邻文件。已接纳原生模块的入口同级 `backend/*.Contracts.dll` 继续只从本次活动 Canonical Plan 的已验证快照读取。重复副本及 Host 同名工件必须保持精确程序集身份、MVID 与完整字节一致，未签发旁文件不参与解析。新共享契约沿启动期可信装载登记器一次装入 Default；若 Default 已有登记器不能证明的预载体，该轮主体和新契约共同使用一个受控隔离上下文，保持跨 owner 接口的同一 Type 身份。Host 磁盘副本不能替代实际装载证明，已有 Host 稳定共享契约边界保持不变；不支持运行中增加契约或从任意包路径补齐依赖。

Host 与组件包采用同一正式发行构建形态，入口由[组件化开发指南](../developer/componentized-development.md#原生发行的离线接纳与独立消费)唯一说明。平台 API 发行身份相同仍须核对实际共享契约映像；不混用普通构建产物，不通过旁路复制程序集修复不一致。

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

公开 Blueprint 的可选 `dependencies` 和 `composition.members` 原样进入同一规范候选，由完整组合图、必需集合及实际依赖校验统一裁决；平台发布角色不能由外部作者自行授予。标准资源额外声明既有通用对象授权端点消费的 `authorize` 功能，它不自动给主体授予权限，也不新增页面行操作。Model 使用全局技术名，物理存储继续由其 metadata 和正式 PersistencePlan 拥有；旧标准投影中的物理提示不是第二份存储真源。外部作者的静态 migration finalize、构建及正式停服部署入口见[外部插件开发](../developer/external-plugin-development.md)。

C# 与 TypeScript 插件 SDK 不再提供旧 `PermissionCodes` / `permissionCodes` 形状。正式候选与生成页面消费功能声明及编译位置，目录与执行绑定仍必须完整校验。

标准启用和停用绑定独立行为。`POST /{id}/enable` 与 `/disable` 固定写入对应状态，不从请求 body 接收其他更新字段；编辑动作保留其正式字段白名单。`ByFeature` 按声明操作绑定功能位置，不从资源命名约定生成旧业务权限。

## Guardrails

- Host 只组合、装载和提供宿主级横切能力，不吸收模块私有业务规则。
- Shell 不维护业务模块 ID 列表；前端实现由 Active Module Runtime Catalog 选择并一次提交。
- 共享声明通过 Kernel / Contracts / SDK 的稳定契约协作，不复制进模块。
- migration/import 逻辑留在显式 persistence migration seam，不进入 Host 组合。
- compatibility adapter 必须显式标记并有退出条件，不得成为新的平行真源。
- Host 运行期间的插件安装、升级、启用、停用、卸载和回滚不属于受支持 module contract；只读 inventory 只观察停服部署结果。

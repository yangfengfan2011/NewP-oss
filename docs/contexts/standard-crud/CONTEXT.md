# Standard CRUD

资源可显式要求创建或更新业务记录。创建／更新参与者仅登记共享上下文变化；已有平台事务执行器统一保存与提交，缺失必需参与者或 recorder 时失败关闭。更新参与者在该事务内保护读取待替换行，所有原有候选校验及规范差异登记都在保护内执行。当前 EF 实现通过主键恒等更新取得写锁并持有至事务结束，不引入版本字段；普通未声明资源保留原更新协议。具体事件及白名单由模块 recorder 持有，共享内核不推断业务字段。

Standard CRUD 上下文定义平台共享宿主用于组合资源列表、创建、编辑和详情体验的公共语言。模块拥有业务数据与行为，Standard CRUD 拥有一致的 metadata 解释和呈现契约。

动作结果的 `notice` 由模块提供正式词典键 `reasonKey` 及可选模板参数 `args`。共享动作宿主在普通页面及组合页面中完整传递参数，并在当前语言下解析提示；组合页面继续拥有提示生命周期，不因提示参数改变业务提交或刷新行为。

List View 可用 `pageActionGroups: [{ key, labelKey, variant?, actionKeys }]` 声明父按钮，按 `actionKeys` 的顺序把已有页级动作显示为下拉子按钮。分组只拥有呈现，不拥有 handler 或权限；子动作的 key、功能位置和执行生命周期不变。每个子动作只能属于一个组，未知或重复引用在 View 解析时拒绝；父按钮位于首个可见子动作的位置，没有可见子动作时不显示。未分组动作保持原呈现，移除分组配置即可回退。

动作输入渲染器可声明 `presentation: "drawer"`，由共享动作宿主使用同一 `CrudFormDrawer` 承载业务表单；新建输入采用右侧抽屉及“保存并关闭”。默认 Modal 继续用于既有确认与审阅输入。两种容器共享权限失效、草稿关闭保护、pending 与提交交接，模块不自行创建容器；Drawer 随动作按需装载，不扩大 Shell 启动依赖。该选项不改变命令、权限或持久化协议，移除声明即可回退到 Modal。

显式 Link 查询采用 List View 的 `querySource: { kind: "link", model, resourceKey }`。form 的 Model 继续拥有创建字段，查询列只能是 Link 正式公共目录中的 projection 字段，默认排序及可排序列也按该目录校验。共享宿主收集 LID 而非默认 EID；Adapter 的 `getRowId` 仍拥有关系行身份，可选 `getDetailId` 单独提供当前目标资源的真实 Entity 主键。第一片采用者仅登记文件夹详情；异构对象的资源/页面选择继续由对象拥有模块登记，不能绑定虚假的混合 Entity Detail View。

通用 Link 查询的端点目录、条件与执行入口见[接入指南](../../developer/link-query-configuration.md)。公开请求始终分页；受信任树适配只能读取当前一层，不能通过 HTTP 关闭分页。读取失败不自动修复关系或扩大授权。

`LinkQueryReader.QuerySelectedAsync` 供真实owner按明确LID集合复用同一Link查询链；所选关系与真实source policy、anchor、端点read和固定投影相交后才Count/排序/分页。空集合保持空结果，旧无选择入口不变；它不新增HTTP筛选协议、全层读取开关或任意字段投影。批量授权的候选收集按该链逐层分页遍历当前可读Folder，按规范EID去重；选中来源与最终授权对象分开，隐藏路径不补造名称或数量。

ENTITY 的可选归属字段由公共 Model projector 提供：`OwnerId` 引用稳定账号，`OwnerDepartmentId` 引用一个 Department；只有 `builtinFields` 选用的模型才得到字段、存储与创建要求。字段由服务端拥有，create/edit form 隐藏普通输入，Detail 可按需只读显示 raw/display companion。创建候选在判权前从 `IAccountOwnershipDirectory` 与同一业务审计作用域取得可信账号和当前部门；普通更新不重算它们。Organization 实现目录，CRUD 不读取其私有持久化。选用者须显式声明相应模块依赖，DTO 同步仅在自身及依赖闭包中解析 display 目标。

显式转交由对象拥有模块验证真实来源，公共归属核心在同一事务内锁定规范资源及 EID、重验当前所属人和 edit，并从事务内目录读取接收账号的当前部门。管理员不获得无条件代转资格；接收者读取对象的资格不是前置条件，也不保证转交后可读。归属变化、事务内接收引用、业务记录及站内成功通知一起提交或回滚，手工 ACL 及 CreatedBy／CreatedAt 保持，UpdatedBy／UpdatedAt 按转交的可信审计 stamp 更新；迁移本身保留原四个审计字段。所属人及部门均未变化时返回无变化，不创建目录、引用、通知或成功事件；普通保存继续保留已转交归属。

归属字段的规范标记只在选中时序列化；未选用字段保持既有 JSON 形状。旧部署资产继续按原内容摘要与 canonical 模型严格校验，不通过重写旧资产或放宽校验接纳升级。

受控归属补齐在真实 owner 的入口重验来源及 read/edit，公共内核只对选中模型的缺失列更新，不更新四个旧审计字段、不更改完整归属。实际 diff 与对象变化在同一事务登记到 Audit；未解析、无变化、失败或提交前取消不生成成功记录。停用维护入口可以停止新补齐，但回退不得删除新增列、已提交归属或历史；已升级数据库仍需兼容该前沿的 Host。

中央固定提供者 `entityOwnership.transfer` 只接受资源短引用，准备与提交均在原所属人检查之后消费 `target/edit`；`edit.RequiresRead` 的既有完整读取依赖不能被消费者关闭，也不另加一轮 read。`entityOwnership.initialize` 固定待补齐 query/read，以及单项和批次的 visibility/read、target/edit，沿原 404/403 时点执行。资格与专属账号候选仍保留原管理员或当前所属人守卫，不能把准备结果缓存为提交许可。各业务 owner 的位置解析和独立读取仍由自己履行；不能选择中央合同后跳过必需角色。

共享归属输入从正式动作透传声明引用；草稿、在途请求和提交身份包含该声明 ID。切换来源取消旧读取并丢弃旧选择，声明不进入业务载荷，缺失来源也不能回退为内部或旧执行。

Part、Document 的运行时字段策略和复制字段分类消费同一 `CompiledPersistencePlan`：持久化编译器保留本候选已验证的 authoring 投影供字段策略使用，分类读取该计划的模型。authoring 投影只在内存中存在，不进入存储计划摘要、部署状态或迁移历史；缺失或 owner 不匹配时拒绝注册，不向工作树、Host 同名目录或安装路径回退读取。资源工厂在候选计划可解析后构造 descriptor，仍执行统一校验及资源/路由唯一性检查；无参 metadata provider 只用于显式设计时输入。

## Language

**标准 CRUD 资源（Standard CRUD Resource）**：
通过平台共享宿主提供列表、创建、编辑或详情能力的资源。它可以只启用其中一部分能力，不等于模块的全部业务页面。
_Avoid_: 通用页面、业务专用页面

**List View（列表视图）**：
标准 CRUD 列表静态展示结构的唯一 authoring 事实源，拥有 metadata 列、查询字段、高级筛选分区、可选业务页级操作和行级操作；runtime 列模式只把完整列集委托给 Runtime Column Provider。搜索、重置和刷新由 Shell/UI 作为标准固定交互提供，不进入 List View `pageActions`。模块 binding 不通过 React 回调覆盖标准列表区域，需要完全自定义结构时使用自定义页面 capability。
_Avoid_: renderFilters、renderHeaderActions、renderRowActions、列表 slot

**Standard CRUD 数据 Adapter（Standard CRUD Data Adapter）**：
把资源拥有者的读取与普通写入结果转换成共享宿主可消费的规范列表、详情和操作结果的运行时角色。它不解释 Model 或 View，不拥有业务专用命令，也不产生新的 authoring 或候选事实。
_Avoid_: Resource compiler、metadata 解释器、业务命令处理器

**列来源（Column Source）**：
List View 对具体列表列集所有权的选择。metadata 来源由 List View 拥有静态列集；runtime 来源只声明委托，由模块在运行时提供完整列集。
_Avoid_: 动态列回调、混合列覆盖

**运行时列 Provider（Runtime Column Provider）**：
由资源模块拥有、按当前列表上下文异步解析完整运行时列集的角色。它只返回可序列化列描述，不返回 UI 行为，也不向 metadata 暴露模块 API 地址。
_Avoid_: buildColumns、Shell 列计算器、远程 renderer

**运行时列集（Runtime Column Set）**：
运行时列 Provider 在一个加载代际内返回的有序完整列描述及可选列上下文令牌。列的稳定语义标识不随标题中的日期或其他展示文本变化。
_Avoid_: 列补丁、React 列节点

**列上下文令牌（Column Context Token）**：
把一次运行时列解析与后续列表行读取绑定到同一模块业务快照的不透明值。共享宿主只在内存中转交它，不解释、持久化或写入 URL。
_Avoid_: 查询筛选条件、列偏好键、Shell 快照标识

**计算型行数据（Computed Row Data）**：
模块经过业务逻辑计算或组合后，通过 Standard CRUD 数据 Adapter 的既有列表入口返回的行数据。它与列来源相互独立，不形成第二套列表读取入口。
_Avoid_: 动态行 Provider、第二列表 API seam

**Standard CRUD 动作执行（Standard CRUD Action Execution）**：
共享宿主对单条模块业务动作的确认、输入、互斥执行、结果交接和数据刷新过程。模块拥有业务副作用与输入语义，但不拥有呈现面刷新或冲突重放策略。

资源动作状态可用可选 `labelKey`（完整词典键）覆盖当前显示标签。列表页动作、行菜单、树上下文菜单和通用动作运行时一致消费；覆盖仅改变本地化呈现，不改变 action key、handler、正式功能位置、授权或提交输入。未提供时保留声明标签。
_Avoid_: 模块刷新回调、Operation artifact、业务命令编排器

**Standard CRUD 变更执行（Standard CRUD Mutation Execution）**：
共享宿主对创建和普通更新的值规范化、能力选择、按资源已声明策略执行的可选并发令牌注入、字段错误映射、冲突恢复、结果交接和单次刷新过程。列表 Drawer 与完整详情页必须共用这条生命周期；模块只声明普通写入 Adapter 与可选值构造，不声明宿主内的校验回调或自动重放策略。
_Avoid_: validate callback、冲突自动重放、呈现面自选刷新

普通创建与更新在提交后独立判断当前请求能否读取结果。可读时保留完整详情响应；不可读或提交后的只读呈现失败时返回明确的 `StandardCrudMutationReceipt` 成功回执，不返回对象字段或标识。共享 REST Adapter 将回执作为已提交变更交接：创建的标识未知，编辑沿用调用方原标识；组合视图按未知位置重新读取，刷新故障不把成功写入改报为失败或触发重放。创建前使用规范化、补齐服务端字段后的完整候选判权；编辑和删除使用动作前事实及独立读取依赖。

标准 options 在数据库分页前组合规范资源的读取计划。表单 Lookup 的当前值在受控请求完成前只显示加载状态，失败显示错误和不可用状态；服务端当前值响应决定可读名称或字面 `***`，详情字段中的旧名称和原始 ID 不作为可见标签。字段原值仍保留在编辑提交中。

**Model（模型）**：
资源字段目录、数据源以及创建和编辑表单结构的事实源。它不拥有详情展示结构。
_Avoid_: 查看表单、详情布局

**存储必填（Storage Requiredness）**：
字段成功持久化后不得为空的数据不变量。它不表示调用方必须提交该字段，也不等于创建表单必填。
_Avoid_: 表单 required、客户端必填、数据库默认值

**创建输入必填（Create Input Requirement）**：
由调用方拥有的字段在创建资源时必须提交非空值的写入约束。服务端拥有的必填值不属于创建输入。
_Avoid_: 存储非空、服务端生成、编辑必填

**更新非空不变量（Update Non-empty Invariant）**：
普通更新可以省略字段并保留旧值，但显式提交空值必须拒绝，合并后的候选仍须满足字段的存储不变量。
_Avoid_: 每次更新必须携带、PUT 全量替换、UpdateOnly required

**服务端拥有的必填值（Server-owned Required Value）**：
由平台或模块在持久化前生成、成功写入后不得为空的字段值。客户端和表单不能提交或覆盖它。
_Avoid_: 隐藏的客户端必填、数据库永久 DEFAULT、可编辑默认值

**创建默认值（Create Default Value）**：
新建交互或应用写入为尚未提交的字段建议或补充的初始值。它不是旧数据回填，也不能保证数据库绕过应用写入时得到值。
_Avoid_: migration backfill、数据库永久 DEFAULT、历史事实

**解析后字段目录（Resolved Field Catalog）**：
后端 Model projector 从 authoring 显式字段、Entity 或 Link 固定字段和选择的 builtin 派生出的完整 runtime `model.fields`。List、Detail 和 Form 直接消费该目录；它约束静态字段名称与字段语义，不声称每次响应都提供其中全部字段。
_Avoid_: DTO 属性列表、前端映射字段

**Detail View（详情视图）**：
一个命名且完整的详情展示 authoring 定义，拥有字段选择、顺序、分区和 renderer override。不同 Detail View 之间不继承、组合或增量覆盖。
_Avoid_: form.view、查看表单、详情字段补丁

**Resolved Detail Schema（解析后详情模式）**：
Detail View 经过字段语义、引用和 renderer binding 校验后形成的原子运行时投影。查看 Drawer 与完整详情页消费同一份投影，不能回退到创建或编辑表单结构。
_Avoid_: Model fallback、合并详情模式

**Detail Surface（详情呈现面）**：
呈现同一份 Resolved Detail Schema 的查看 Drawer、完整详情页或视图组合中的只读详情区域。各呈现面共享普通字段结构；完整详情页可以承载 Detail View 显式声明的 summary block 和 page action。
_Avoid_: 两套详情表单

**详情数据可用性（Detail Data Availability）**：
一次详情响应相对于 Resolved Detail Schema 的运行时可用状态，区分 present-null、missing field、unavailable section、critical id failure 和 action dependency unavailable；它不改变 Detail View 结构，也不参与候选注册。
_Avoid_: 响应字段白名单、Model 推断、候选字段校验

**原始字段值（Raw Field Value）**：
模型字段用于持久化、排序、筛选、写回和系统集成的规范值；请求其显示值时仍必须同时保留，不能被可读文本覆盖。
_Avoid_: 显示文本、格式化值、前端别名

**显示值（Display Value）**：
与原始字段值成对返回、供读取界面直接呈现的字符串或空值；它可以表达引用名称、枚举文本或按请求展示上下文生成的时间文本，但不是独立 Model 字段。
_Avoid_: 原始值替代、DTO alias、页面临时格式

**字段显示规则（Field Display Rule）**：
由 Model 拥有、声明一个字段如何从原始字段值形成显示值的静态语义；资源页面和数据 Adapter 不重复声明同一转换。
_Avoid_: 页面 formatter、资源别名表、renderer 数据查询

**标准时间显示格式（Standard Temporal Display Format）**：
时间字段显示规则允许选择的有限格式，只包含“日期”和“日期时间到秒”；时间点与本地日期时间可以显式选择其中一种，它只控制显示值，不改变字段的原始时间语义。
_Avoid_: 任意 formatter、metadata 格式串、值类型替代

**展示投影（Display Projection）**：
依据字段显示规则，从原始字段值统一形成显示值的读取投影；列表、单条读取、写入成功结果、选项和用户可读导出不得各自定义转换规则。
_Avoid_: 页面 renderer 特判、资源手工别名、第二套读取结果

**用户可读导出（Human-readable Export）**：
面向用户查看和继续处理的导出结果，默认使用与交互读取一致的显示值；面向系统交换的原始导出是另一项显式能力。
_Avoid_: 机器交换文件、隐式 raw export

**日历日期（Calendar Date）**：
只表示年月日且不对应时间线上的唯一时刻的字段值；展示时不应用用户时区转换。
_Avoid_: UTC 日期、零点时间点

**时间点（Instant）**：
表示时间线上唯一时刻的字段值，规范原始值使用 UTC；显示值可以按请求展示上下文转换，Entity 审计时间属于此类。
_Avoid_: 本地日期时间、无时区 datetime

**本地日期时间（Local Date Time）**：
表示某个业务所在地墙上时间、但本身不绑定查看者时区的字段值；展示时不得自动转换为当前用户时区。
_Avoid_: UTC 时间点、浏览器本地时间

**标准宿主环境（Standard CRUD Host Environment）**：
共享宿主从外层获得的地址所有权、页面实例生命周期、写入同步与呈现模式的显式接口；独立路由与视图组合各提供一个 adapter，宿主不解释自己被谁装载。地址为空表示宿主不拥有地址，不得读写外层 URL；呈现模式区分完整工作区与只承载一个动作入口的 Drawer，后者携带完整动作参数。
_Avoid_: embedded 标志、宿主特判、第二套页面身份

页动作可声明 `requiresSelection: true`，共享列表据此开启当前页多选、无选择时禁用动作，向动作与输入 renderer 传递 `selectedRows` 快照。分页、筛选、位置、主体或列表数据变化使勾选失效；确认期间锁定勾选。是否允许删除、整批事务及关系清理由模块处理，Shell 不解释对象类型。详见 ADR-0054。

List View 可选 `rowContextMenu: ["edit", "remove"]` 只按顺序引用本视图既有 `rowActions` 的唯一 key。候选和宿主解析拒绝缺失、重复、非行动作及内联定义；不派生第二份权限、handler、输入或确认。鼠标右键与行上的 `ContextMenu`／`Shift+F10` 打开共享菜单，执行原行操作链，仅提供所指行的 `id/row/queryScope`，不隐式使用当前勾选。视图未声明或声明空数组时保留浏览器菜单。主体、地址、查询位置、数据快照或页面活动状态变化使打开的菜单失效；保留的隐藏菜单 DOM 也须在执行时拒绝旧上下文。编辑控件及链接保留原生菜单；Escape 关闭后恢复目标焦点。普通“更多”入口与右键入口使用相同执行、资格、错误及成功交接。

组合树动作的详情读取按既有 `StandardCrudDetailReadContext.queryScope` 交接模块定位输入，并把范围纳入请求身份与取消生命周期；同一 recordId 在不同范围下不复用旧动作行。模块仍负责解释标识并由服务端重新核对写入资格。

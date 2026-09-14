# Authorization

Authorization 上下文把已认证主体、活动权限目录与 endpoint access rule 解析为服务端访问决策。它不拥有登录凭据、Cookie、业务数据或前端可见性状态。

## Language

**认证（Authentication）**：
验证请求由哪个主体发起，并建立服务器可信的主体标识。认证成功不表示主体有权执行任意业务操作。
_Avoid_: 授权、角色检查、登录后全放行

**已认证主体（Authenticated Subject）**：
由认证机制建立、具有稳定 subject ID 的请求主体；权限判定只使用服务器可信标识，不接受请求体或查询参数提供的用户 ID。
_Avoid_: 当前用户参数、前端用户对象、角色名

**权限（Permission）**：
模块拥有并声明的稳定业务能力代码，例如查看人员或编辑资产。权限表达能力，不表达页面可见性或持久化角色关系。
_Avoid_: 菜单项、角色、按钮、policy 实现名

**有效权限目录（Effective Permission Catalog）**：
当前活动模块快照中所有合法权限声明的确定集合；endpoint 只能引用该目录中的权限。
_Avoid_: 数据库全部历史权限、前端权限数组、未启用插件权限

**Endpoint Access Rule**：
一个服务端 endpoint 的终态访问分类：公开、仅认证、Host policy、显式业务权限或拒绝。模块业务 endpoint 和 Standard CRUD 不允许缺少终态规则。
_Avoid_: 前端路由 guard、Controller 内手写 if、默认登录即可

**权限前缀（Permission Stem）**：
Standard CRUD 资源显式声明的稳定权限命名根；内核从它派生 View、Create、Update 和 Delete，不从可随意重命名的显示文案猜测。
_Avoid_: ModelName 自动猜测、路由字符串、权限分组名

**授权决策（Authorization Decision）**：
PermissionGate 对一次主体、权限和 endpoint 的结果：Granted、Denied 或 Indeterminate。Indeterminate 表示依赖故障，必须 fail closed，但不伪装成业务拒绝。
_Avoid_: bool 默认 true、前端 hasPermission、异常即放行

**本地管理员（Local Administrator）**：
LocalAuth 开启时由开发 bootstrap 创建、精确获得当前活动权限全集的本地主体。它不享有绕过 PermissionGate 的超级管理员特例，也不定义生产管理员语义。
_Avoid_: root、权限通配符、角色名硬编码放行

**当前主体权限投影（Current Subject Permission Projection）**：
返回给 Shell 的当前已认证主体有效权限代码，用于菜单和按钮体验；它不替代服务端 endpoint 授权。
_Avoid_: 任意用户权限查询、授权事实源、Cookie 权限快照

### 已确认目标术语（尚未实施）

以下术语记录功能授权树的目标模型；上文 Permission 与 Endpoint Access Rule 等仍描述当前实现，不能据此认定功能授权树已经落地。

**功能授权点（Feature Authorization Point）**：
角色直接授予的功能单位，包括导航、Tab 和动作；页面承载内容，不是单独勾选的功能授权点。
_Avoid_: 独立业务权限代码、页面权限、接口权限

**功能授权树（Feature Authorization Tree）**：
按导航、Tab 与动作的声明位置组织功能授权点的树；标准页面和自定义页面的功能采用同一授权模型。
_Avoid_: 页面实现注册表、接口调用树、角色继承树

**授权位置（Authorization Placement）**：
功能授权点在所属入口及声明结构中的位置；不同位置的动作分别授予，即使名称或业务实现相同。
_Avoid_: 按钮显示名称、屏幕坐标、共享实现身份

**入口依赖（Entry Dependency）**：
一个功能授权点所依赖的本分支入口授权；角色授权集合必须满足该依赖，其他分支的入口不能替代。
_Avoid_: 隐式连带授权、任意可达路径、跨角色依赖补齐

**入口归属（Entry Ownership）**：
页面内容及其读取所属的导航或 Tab 授权位置；没有独立可见入口的详情直链也有明确归属，弹窗沿用发起位置。
_Avoid_: 来源页、浏览器访问历史、独立页面权限

**节点类型分组（Node Type Group）**：
功能授权树内用于区分不同树节点类型操作的展示分组，本身不单独授权；具体业务记录不是该分组的子授权点。
_Avoid_: 记录授权、每条记录的权限节点

**授权管理员（Authorization Administrator）**：
通过角色显式获得授权管理动作的可信主体，按获授动作在全局范围内管理角色功能或用户角色关系；该资格不专属于 LocalAdmin，也不自动包含未授予的其他管理动作。
_Avoid_: 角色名特判、只能授予自身已有功能、自动获得全部管理权限

**功能点索引（Feature Point Index）**：
正式功能目录的持久化索引投影，用于关联角色授权和识别目录变化；它不拥有独立的业务权限含义或可编辑的功能定义。
_Avoid_: 第二份权限目录、功能声明真源、完整页面结构副本

**功能授权工作台（Feature Authorization Workbench）**：
供授权管理员选择角色、查看完整功能树并配置该角色功能授权的独立入口；只读访问与保存授权分别授予。
_Avoid_: 角色详情授权弹窗、角色基础信息编辑、用户角色分配

已确认的目标边界及切换方式见 [ADR-0048](../../adr/0048-functional-authorization-uses-entry-positions.md)。

## 普通角色管理与事务性权限配置

- 对外角色、权限目录及既有用户角色管理入口额外要求可信主体当前有效的 LocalAdmin 分配。普通主体即使得到 Authorization.* 也不能管理；当前主体权限读取语义保持不变，任意主体权限查询和判权端点仍冻结。
- 普通角色代码和内建标记不能进入初始化路径；LocalAdmin 定义、启停、删除和权限全集受保护。启动期通过独立 LocalAdministratorCatalog 收敛管理员定义及活动权限全集，不依赖 Audit 登记，也不以操作者字符串绕过普通保护。
- PermissionService 在正式启动同步成功后发布不可变活动目录。权限目录、角色权限外部读取和有效权限并集只消费该目录；历史权限行不能成为授权源。停用角色关系可继续配置，但不进入有效权限并集。
- RolePermissionWriter 通过平台事务执行器，在角色主键写锁内读取实际旧集合、完整校验新集合、登记关系与 role.permissions.changed，再统一保存与提交。规范权限代码分别记录 Boolean 前后成员资格，相同集合或排列变化不产生记录。角色新建、改名和说明修改本期不产生分配事件。
- Authorization 只引用 Audit.Contracts，由 Audit 登记稳定 authorization.role 目标和字段差异；不形成反向模块加载依赖。缺少 recorder 或登记、写入、保存、确定未提交的提交失败都不会返回成功。当前角色名称由 Authorization 批量提供，删除目标后记录保留 ID。
- 角色列表与详情只读资源复用 StandardCrudService 的 Entity display 计划：用户字段一次批量解析，时间按请求 locale/timeZone 显示，原始审计值保持不变。只读 descriptor 不映射通用 HTTP 端点，既有 Controller 继续拥有 LocalAdmin 准入。
- 前端通过 Authorization capsule 和 metadata 进入标准列表、Drawer 表单和详情；模块只拥有权限选择输入，确认、pending、错误和刷新由共享动作宿主负责。网络结果未知时提示先核对实际结果，不自动重放写入。

回退时先停用普通权限配置入口，再回退本票代码与 authoring；保留已提交角色关系和 Audit 历史，不改写迁移历史。用户角色整组事务保存与成员保护由后续独立交付接入。

## 普通账号角色分配

- 普通管理通过 Identity 稳定账号目录校验全部目标；Disabled 可引用，Deleted 和受保护账号拒绝。现有 LocalAdmin 角色成员独立受保护，整组越界整体失败；内部初始化路径保持。
- Authorization 用户角色替换使用事务级账号应用锁，覆盖首次空集合。锁内读取实际旧关系、完整校验全部角色并登记 user.roles.changed；关系和记录由平台事务统一提交，异常不自动重放。停用角色允许配置但不产生有效权限。
- 差异以规范角色 EID 的 reference 前后值表示增加或移除，目标为 identity.account EID。Identity 和角色目录批量解析当前名称，缺失时保留历史 ID。集合相同或顺序变化不登记。
- 正式角色详情及行操作中的账号角色动作复用共享弹层；账号有界搜索、角色数据库分页，页外已配置选择保留到完整集合保存。

## 当前权限消费与界面同步

- 每次正式判权读取当前有效角色及活动目录权限的并集；移除全部来源才算撤权。同一认证 Cookie 的后续请求使用新权限，已经通过判权的在途请求允许提交，不添加提交前复判或服务端权限版本。
- Shell 页面导航重取当前主体；浏览器刷新沿用启动认证读取。共享请求收到明确 401/403 后触发同一认证 owner 刷新，菜单与按钮只消费返回投影。权限集合变化重建主体查询 scope；写拒绝同时清空旧查询，读拒绝清空自身失败结果，避免重复的读拒绝/刷新循环。本地 query scope 计数只用于缓存失效，不是授权事实或跨请求版本。
- 认证/授权依赖故障保持原始失败及现有认证不可用边界，不将 503 等故障解释成撤权。明确业务拒绝不重放，网络异常或成功写响应无法读取时提示先查询实际结果；只有业务执行前稳定防伪失败仍允许一次令牌恢复。

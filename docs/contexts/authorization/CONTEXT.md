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

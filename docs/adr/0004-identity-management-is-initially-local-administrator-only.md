# Identity 管理入口首阶段仅限本地管理员

Identity 作为独立内建模块接入活动模块目录，通过受控模块注册提供“账号管理”菜单与 `/identity/accounts` 路由，不在 Shell 中维护私有模块清单；入口只对当前主体投影中的本地管理员可见。账号查询、创建和 ENAME 编辑使用受限资源契约，账号状态切换、逻辑删除与凭据重置使用 Identity 自己的专用命令，不暴露可修改任意字段的通用 CRUD 更新入口。账号列表使用服务端分页，默认每页二十条且最大一百条，支持 ECODE 或 ENAME 搜索，以及账号状态、人员绑定标记和本地管理员标记筛选；默认只显示 Enabled 与 Disabled 并按 ECODE 排序，Deleted 只能显式筛选，首阶段不提供导出。由于本阶段明确暂缓新增角色与权限，服务端暂时复用现有 `AdminOnly` policy，当前主体投影增加 `isLocalAdministrator` 供 Shell 控制管理入口可见性；这不是 Permission 或 PermissionGate 的扩展。

本地管理员必须通过服务器本机的 `identity bootstrap-local-admin` 命令初始化。该命令作为 Host 可执行程序的离线模式运行，复用正式 DI、持久化和 schema migration 基础设施但不监听 HTTP；它获取数据库级互斥锁，只允许 User 表为空时在一个事务中创建本地管理员、LocalCredential、安全事件，并通过 Authorization 的稳定契约为新 User EID 分配现有 `LocalAdmin` 角色，任一环节失败都整体回滚。现有 `AdminOnly = RequireRole("LocalAdmin")` 保持不变，登录时继续读取现有角色写入主体，不通过本地管理员标记绕过服务端策略。bootstrap 与本地管理员凭据重置都只允许在交互式终端执行并展示一次秘密；检测到标准输出被重定向时必须在写入任何状态前拒绝执行，本阶段不提供非交互式秘密导出参数。配置只提供 ECODE 和 ENAME，不能保存密码，正常 Host 启动发现 Identity 为空时返回 `IDENTITY_BOOTSTRAP_REQUIRED`，发现本地管理员缺失或不唯一时返回 `IDENTITY_LOCAL_ADMIN_INVARIANT`，不得自行修复。管理员 ECODE 不可修改，维护命令按数据库中的唯一受保护标记定位账号，只生成高强度随机临时凭据并在控制台展示一次，不接受明文密码参数也不写入日志，同时撤销全部旧会话。

现有 `LocalAuth` 配置中的管理员标识、旧登录账号和明文密码不自动导入 Identity，也不能作为 bootstrap 输入；升级必须显式执行 bootstrap 并接收新的随机临时凭据，然后移除 `LocalAuth:Password`、`LocalAuth:AdminUserId` 等旧账号与凭据配置。Development 的共享临时凭据使用新的隔离配置表达，不继续借用旧 LocalAuth 密码配置。

当前 `LocalAuthSeeder` 中的权限目录与 `LocalAdmin` 角色目录同步迁回 Authorization 自有初始化器；Identity bootstrap 不复制权限目录规则，只调用窄角色分配契约。该职责拆分保留现有 Authorization 行为，但不新增角色、权限、分配页面或 PermissionGate 语义。

Identity 不为本阶段新增通用请求重放存储；启停、删除和人员联动通过目标状态收敛实现幂等，创建通过 NormalizedECODE 唯一约束识别冲突。一次性秘密永不在重试时重放；若结果丢失，只能发起新的显式重置。后续恢复 Authorization 能力设计时，再单独决定是否用模块权限替换当前管理边界。

# Authentication

Authentication 是 Host 的必需内建上下文，管理服务端认证会话及其有效性。它不拥有用户账号、本地凭据、人员档案、角色或业务权限。

## Language

**认证会话（Authentication Session）**：
一次由服务器确认并以 EID 标识的登录会话；它只属于一个用户账号，同一账号可以同时拥有多个认证会话。首阶段只保留会话类型及创建、到期、撤销时间和撤销原因等生命周期字段，不采集客户端画像或活动心跳。
_Avoid_: 用户账号、Cookie、在线用户

**活动会话（Active Session）**：
尚未撤销或过期且所属账号仍允许登录的认证会话；受保护请求必须解析到一个活动会话。
_Avoid_: 永久登录、浏览器在线状态、账号启用状态

**会话注册表（Session Registry）**：
Authentication 持有的认证会话权威集合，用于建立、验证和撤销会话。
_Avoid_: Cookie 列表、账号目录、客户端缓存

**会话令牌（Session Token）**：
为单次认证会话随机生成的 256-bit 不透明秘密；客户端 Cookie 携带经 Data Protection 保护的原始令牌，服务端注册表只持久化令牌哈希，Session EID 不作为认证凭证。
_Avoid_: Session EID、用户标识、可读 Cookie 载荷

**会话撤销（Session Revocation）**：
使一个或某账号全部认证会话永久失效的生命周期转换；账号恢复不会重新激活已撤销会话。
_Avoid_: 临时隐藏、账号停用、Cookie 删除

**会话轮换（Session Rotation）**：
用户成功修改凭据时撤销全部旧会话并为当前客户端建立一个新会话的原子转换。
_Avoid_: 延长旧会话、恢复已撤销会话、管理员重置

**会话有效期（Session Lifetime）**：
认证会话自建立起可被服务器接受的固定期限；到期会话不能通过后续活动延长。
_Avoid_: 滑动续期、永久登录、记住我

**会话保留（Session Retention）**：
已到期或已撤销的会话记录继续保留三十天，再由 Authentication 的内部清理能力分批移除；清理失败可以重试，不改变当前认证请求的判定。
_Avoid_: 会话有效期、同步请求清理、永久会话历史

**认证密钥环（Authentication Key Ring）**：
Host 用于保护和解析认证 Cookie 的持久化 Data Protection 密钥集合；首阶段由单个活动 Host 使用，稳定应用名与受保护存储保证正常重启后仍能解析既有 Cookie。生产配置无效时必须阻止 Host 启动，不能退回易失密钥。
_Avoid_: 会话注册表、临时部署目录、多实例共享契约

**受限认证会话（Restricted Authentication Session）**：
使用临时凭据建立、只允许修改凭据或退出的认证会话；完成凭据修改前不能进入 Shell 或业务页面。
_Avoid_: 正常会话、匿名会话、权限不足

**在线会话视图（Online Session View）**：
从活动会话形成的管理查询投影；它表示服务器仍接受的会话，不承诺浏览器或设备此刻仍保持连接。
_Avoid_: 在线用户实体、实时在线状态、用户账号目录

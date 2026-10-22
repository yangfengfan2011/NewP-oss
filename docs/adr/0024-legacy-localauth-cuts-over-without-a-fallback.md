# 旧 LocalAuth 原位切换且不保留密码后门

对外继续保留 `/api/auth/login`、`/api/auth/logout` 和 `/api/auth/current-user` 路径，以避免前端与验收调用方无意义改址；实现则在同一切换中改为使用 Identity User、LocalCredential、AuthenticationSession 和现有 Authorization 角色读取。旧 `LocalAuthOptions` 的配置明文密码验证、固定 AdminUserId、七天持久 Cookie 与 `LocalAuthSeeder` 的混合职责全部移除，不保留应急回退或并行登录路径。

Authorization 继续拥有并同步既有权限及 `LocalAdmin` 角色目录；离线 bootstrap 通过稳定契约把新本地管理员 User EID 分配给该角色，登录后将角色写入主体，因此 `AdminOnly` policy 无需改变。若新认证链路、角色分配或会话存储不可用，系统应失败关闭并保持 Not Ready，而不是重新启用旧配置密码。

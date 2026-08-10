# Identity 拥有账号，LocalAuth 作为认证适配

NewP 将 Identity 建立为独立上下文，由其拥有用户账号与账号状态；LocalAuth 只管理与账号一对一关联、拥有独立 EID 的本地凭据记录，并使用框架密码哈希器验证和按需升级哈希，Host 不拥有账号或凭据数据。Identity 通过 `Identity.Contracts` 暴露账号目录和生命周期接口，Organization 只依赖该稳定契约，Identity 不反向依赖 Organization。Authorization 的角色、权限与 PermissionGate 本轮保持不变。这样既避免把持久化账号数据放进 Host，也避免只建设无法真实登录的孤立账号目录。

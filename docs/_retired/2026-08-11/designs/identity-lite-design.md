# Identity Lite / LocalAuth 详细设计

## 1. 背景

第二阶段已经要求平台形成最小登录闭环：后端可启动、前端可启动、默认管理员可登录、登录后可进入主界面。当前仓库已经具备 `LocalAuthOptions`、`LocalAuthSeeder`、`AuthController` 和前端 `AuthContext`，但之前缺少一份把认证边界、状态流转、与 Authorization 的衔接关系讲清楚的详细设计文档。

这份设计的目标是把 V1 的最小认证能力固化下来，避免后续在实现新模块时继续把“认证”和“授权”混写在一起，也避免 LocalAuth 被误扩张成完整身份平台。

## 2. 目标

- 为新平台 V1 提供本地用户名密码登录能力。
- 提供 `login / logout / current-user` 三个最小认证接口。
- 使用 Cookie 持久化登录态，并支持页面刷新后的登录态恢复。
- 通过固定种子逻辑生成默认管理员，并把全部已声明权限赋给该管理员。
- 为后续真实 Identity 模块预留替换点，而不改变前端 Shell 的基本使用方式。

## 3. 非目标

- 不实现完整用户中心、注册、找回密码、账号锁定等用户生命周期能力。
- 不引入 LDAP、OAuth、OIDC、单点登录等外部身份集成。
- 不在本轮引入数据库用户表、密码策略、验证码、多因子认证。
- 不承担授权决策；LocalAuth 只负责“当前是谁”，不负责“能做什么”。

## 4. 范围

涉及范围：

- 后端宿主：`apps/host-api/Authentication/`、`apps/host-api/Controllers/AuthController.cs`
- 授权衔接：`apps/host-api/Authentication/LocalAuthSeeder.cs`
- 前端 Shell：`frontend/packages/shell/src/contexts/AuthContext.tsx`
- 前端登录页与受保护路由：`frontend/packages/shell/src/pages/Login.tsx`、`frontend/packages/shell/src/App.tsx`

不直接覆盖：

- 独立 Identity 模块目录
- 用户管理后台
- 组织、租户、审计等后续模块

## 5. 设计约束

- 认证实现必须足够轻，服务第二阶段“最小可运行平台”目标。
- 必须与 `Authorization` 模块解耦：认证只产出 `userId / username / displayName` 与登录态。
- 宿主使用 ASP.NET Core Cookie Authentication，前端统一通过 `fetch(..., { credentials: 'include' })` 访问。
- 默认管理员必须通过种子逻辑自动建立，保证本地开发环境可重复验证。
- 接口契约应尽量稳定，后续即使替换为真实用户源，也不应迫使前端 Shell 全面重写。

## 6. 总体方案

采用“宿主内最小本地认证 + 授权模块协同赋权”的方案：

- `LocalAuthOptions` 提供默认管理员账号、角色和 Cookie 配置。
- `AuthController` 负责登录、退出、当前用户查询。
- 登录成功后由宿主写入 Cookie Claims：`NameIdentifier`、`Name`、`display_name`。
- `LocalAuthSeeder` 在启动时同步所有模块声明的权限，创建本地管理员角色，并把全部权限赋给默认管理员。
- 前端 `AuthContext` 负责登录态初始化、登录请求、退出请求和权限工具方法。

选择这个方案的原因：

- 复杂度最低，能支撑当前阶段闭环。
- 认证状态由浏览器 Cookie 承担，刷新页面后无需额外 token 管理。
- 默认管理员通过种子逻辑与模块权限声明自动联动，避免手工维护权限清单。

本轮不选这些方案：

- 不选 JWT + 刷新令牌：当前没有跨端和外部 API 网关需求，复杂度偏高。
- 不选独立 Identity 模块先行落地：当前阶段目标是先形成主干闭环，而不是先搭完整身份平台。
- 不选把管理员权限硬编码在 `AuthController`：这样会把授权逻辑绑死在宿主中，后续难以演进。

## 7. 关键模型

### 后端配置模型

- `LocalAuthOptions`
  - `AdminUserId`
  - `Username`
  - `Password`
  - `DisplayName`
  - `AdminRoleCode`
  - `AdminRoleName`
  - `CookieName`

### 认证接口模型

- `LoginRequest`
  - `Username`
  - `Password`
- `AuthUserResponse`
  - `Id`
  - `Username`
  - `DisplayName`
  - `Permissions`

### Claims 模型

- `ClaimTypes.NameIdentifier`：用户唯一标识
- `ClaimTypes.Name`：登录名
- `display_name`：显示名称

### 前端状态模型

- `AuthContext`
  - `isAuthenticated`
  - `isInitialized`
  - `user`
  - `login`
  - `logout`
  - `hasPermission / hasAnyPermission / hasAllPermissions`

## 8. 核心流程

### 主流程：登录

1. 用户在登录页输入用户名和密码。
2. 前端调用 `POST /api/auth/login`，并携带 `credentials: include`。
3. `AuthController` 校验账号密码是否匹配 `LocalAuthOptions`。
4. 校验通过后，通过 `IAuthorizationService.GetUserPermissionsAsync(AdminUserId)` 获取当前管理员权限。
5. 宿主写入 Cookie Authentication 登录态。
6. 前端随后调用 `GET /api/auth/current-user` 获取完整用户信息并写入 `AuthContext`。
7. 路由守卫放行，用户进入主界面。

### 主流程：页面刷新后恢复登录态

1. `AuthProvider` 首次挂载时调用 `GET /api/auth/current-user`。
2. 如果 Cookie 有效且后端返回 200，则恢复 `user` 和 `isAuthenticated`。
3. 如果返回 401 或请求失败，则清空用户状态，并完成初始化。

### 主流程：退出登录

1. 前端调用 `POST /api/auth/logout`。
2. 宿主清理 Cookie 登录态。
3. 前端无论后端响应是否异常，最终都清空本地登录状态。

### 异常流程

- 用户名或密码不匹配：返回 401。
- Cookie 存在但无法解析用户标识：`current-user` 返回 401。
- 授权服务暂时不可用：登录请求失败，前端停留在登录页。
- 前端初始化阶段请求失败：进入未登录状态，但应用不崩溃。

## 9. 接口与集成点

### 后端接口

- `POST /api/auth/login`
- `POST /api/auth/logout`
- `GET /api/auth/current-user`

### 与 Authorization 的集成

- `LocalAuthSeeder` 通过 `ModuleRegistry.GetAllPermissions()` 聚合所有模块声明的权限。
- 通过 `IPermissionService.SyncPermissionsAsync(...)` 同步权限定义。
- 通过 `IRoleService` 创建本地管理员角色、分配角色权限、分配用户角色。
- `AuthController` 在构造响应时调用 `IAuthorizationService.GetUserPermissionsAsync(...)` 返回当前权限集。

### 与前端 Shell 的集成

- `AuthProvider` 统一维护登录态。
- `ProtectedRoute` 依据 `isAuthenticated` 控制是否可进入主界面。
- `AuthorizationProvider` 通过 `auth.user?.id` 再向授权体系注入当前用户标识。

## 10. 数据与状态

- 当前账号信息来源于配置，不存储在独立用户表中。
- 登录态存储在认证 Cookie 中，默认持久化 7 天。
- 默认管理员的角色与权限关系存储在当前 Authorization 模块的仓储实现中。
- 前端不持久化 token，仅依赖浏览器 Cookie 和首次加载时的 `current-user` 恢复。
- 一致性要求：默认管理员角色与权限同步必须在本地种子执行后成立，否则登录后菜单与权限守卫会失真。

## 11. 错误处理与风险

- 当前密码以明文配置存在，仅适用于本地开发和最小验证环境。
- 当前用户源是单管理员模型，不适合直接扩展成生产身份系统。
- Cookie 方案依赖前后端同源或正确的跨域 Cookie 配置，部署形态变化时需要重新核查。
- 当前返回的权限集来自 Authorization 模块，若种子未执行，管理员可能登录成功但看不到应有菜单。

降级与回滚思路：

- 若后续替换为真实 Identity 模块，可保留现有三个接口契约不变，内部改为从用户存储和密码校验服务读取。
- 若种子逻辑失败，宿主应在启动验证或 smoke 环节尽早暴露，而不是在业务页面阶段才暴露。

## 12. 验证方案

- 后端构建通过。
- 前端构建通过。
- 本地默认管理员可使用配置中的账号密码登录。
- `GET /api/auth/current-user` 在已登录时返回用户与权限，在未登录时返回 401。
- 刷新页面后登录态仍能恢复。
- 退出登录后受保护页面不可访问。

## 13. 未决问题

- 未来真实 Identity 模块是继续保留 Cookie 方案，还是切换为更独立的票据/令牌体系。
- 默认管理员账号密码是否需要移到更安全的本地秘密存储方式。
- 是否需要在第三阶段后补一层“当前用户详情服务”，把认证返回体与未来用户中心模型解耦。

## 14. 实施拆分建议

1. 固化认证配置与 Cookie 中间件接入。
2. 固化 `login / logout / current-user` 三个接口。
3. 固化本地管理员种子逻辑，并与 Authorization 权限同步打通。
4. 固化前端 `AuthContext`、登录页、受保护路由与初始化恢复逻辑。
5. 用默认管理员完成一次从登录到进入受保护页面的 smoke。

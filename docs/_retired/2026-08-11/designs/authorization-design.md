# Authorization 详细设计

## 1. 背景

`Authorization` 已经不再是占位模块，当前仓库已经具备权限点声明、角色管理、用户角色分配、前端权限守卫和后端 API。但现有文档更多停留在模块边界和实现总结层，缺少一份把权限模型、授权流程、宿主集成方式、前后端一致性约束讲清楚的详细设计。

这份设计的作用是把 V1 授权方案固化下来，给后续模块提供统一接入方式：模块应该如何声明权限、前端应该如何做菜单和路由保护、宿主应该如何同步权限与执行授权检查。

## 2. 目标

- 提供统一的权限点声明机制。
- 提供角色、权限、用户角色三类最小授权模型。
- 支撑后端接口级权限检查和前端菜单/路由级权限控制。
- 让默认管理员能够自动获得全部已声明权限。
- 为未来组织、租户、数据权限扩展预留结构，而不在 V1 过度设计。

## 3. 非目标

- 不实现完整身份认证、用户生命周期管理。
- 不实现组织树、岗位、审批式授权、数据权限和多租户隔离。
- 不做旧平台权限体系的全量迁移。
- 不在本轮引入数据库持久化和复杂缓存策略。

## 4. 范围

涉及范围：

- 后端模块：`server/modules/Authorization/`
- 宿主种子与认证衔接：`apps/host-api/Authentication/LocalAuthSeeder.cs`
- 前端核心权限上下文：`frontend/packages/core/src/authorization/`
- 前端模块接入：`frontend/modules/authorization/src/index.ts`
- Shell 集成：`frontend/packages/shell/src/App.tsx`

## 5. 设计约束

- 授权模型必须保持最小闭环，优先支撑功能权限，不引入组织和数据权限。
- 权限声明必须由模块自己提供，宿主只负责聚合，不负责手写权限规则。
- 前后端权限代码必须共享同一套字符串编码语义，否则菜单守卫与接口守卫会失配。
- 当前实现允许内存仓储，但接口和服务层必须为后续持久化替换保留稳定边界。
- 当前必须兼容第二阶段 LocalAuth 单管理员模型。

## 6. 总体方案

采用“模块声明权限 + 授权模块聚合落库 + 服务层决策 + 前端按同编码守卫”的方案：

- 每个业务模块通过 `IPermissionProvider` 声明自己的权限点。
- `AuthorizationModule` 自身也声明权限管理相关权限和菜单。
- 宿主启动时通过 `ModuleRegistry.GetAllPermissions()` 聚合所有权限描述，并调用 `IPermissionService.SyncPermissionsAsync(...)` 同步到仓储。
- 用户和权限之间不直接关联，而是通过 `UserRole -> RolePermission -> Permission` 三段关系求得最终权限集合。
- 前端模块在注册路由、菜单时直接引用同一批权限代码，Shell 通过 `PermissionGuard` 和 `AuthorizationProvider` 决定是否展示或放行。

选择这个方案的原因：

- 模块自治清晰，新增模块时不需要去宿主中心文件手工补权限清单。
- 角色和权限解耦，足以支撑 V1 的功能级授权。
- 前后端都使用权限代码字符串，易于验证和扩展。

本轮不选这些方案：

- 不选“用户直接挂权限”：会让权限维护分散且不利于后续扩展角色管理。
- 不选“宿主集中写死全部权限”：会破坏模块边界。
- 不选“一开始就上组织/租户/数据权限”：当前阶段会显著扩大范围。

## 7. 关键模型

### 领域模型

- `Permission`
  - 权限点实体，核心字段为 `Code`、`Name/DisplayName`、`ModuleId`、`Group`
- `Role`
  - 角色实体，核心字段为 `Code`、`Name`、`IsEnabled`
- `RolePermission`
  - 角色与权限关联
- `UserRole`
  - 用户与角色关联

### 契约模型

- `PermissionDescriptor`
  - 模块对外声明的权限描述
- `MenuDescriptor`
  - 模块对外声明的菜单描述
- `CreateRoleRequest / UpdateRoleRequest / AssignRolePermissionsRequest / AssignUserRolesRequest`
- `RoleDto / PermissionDto / UserRoleDto / RolePermissionsDto / PermissionGroupDto`

### 前端模型

- `FrontendModule.permissions`
- `FrontendModule.routes[].permissions`
- `FrontendModule.menus[].permissions`
- `AuthorizationContext`
  - 以 `userId` 为输入，提供权限读取与判断能力

## 8. 核心流程

### 主流程：权限声明与同步

1. 模块实现 `IPermissionProvider` 返回权限描述列表。
2. 宿主聚合所有模块权限。
3. `IPermissionService.SyncPermissionsAsync(...)` 将不存在的权限同步到授权仓储。
4. `LocalAuthSeeder` 将所有权限分配给默认管理员角色。

### 主流程：用户权限求值

1. `IAuthorizationService.GetUserPermissionsAsync(userId)` 读取用户所有角色。
2. 过滤出启用状态角色。
3. 读取这些角色关联的权限标识。
4. 读取对应权限实体并返回权限代码列表。
5. `HasPermission / HasAnyPermission / HasAllPermissions` 在该权限集合上做判断。

### 主流程：前端路由和菜单守卫

1. 用户登录后，Shell 得到当前 `userId`。
2. `AuthorizationProvider` 以 `userId` 为输入装配权限上下文。
3. Shell 或模块侧通过 `PermissionGuard`、`usePermission` 等能力判断是否展示菜单、是否允许进入页面。
4. 前端模块声明的权限代码必须与后端权限代码保持一致。

### 异常流程

- 用户没有任何角色：返回空权限集合。
- 用户角色全部被禁用：返回空权限集合。
- 权限点已在前端声明但后端未同步：页面可能展示异常，启动种子应尽早发现。
- 删除内置角色或角色编码冲突：角色服务返回业务错误。

## 9. 接口与集成点

### 后端接口

- `GET /api/authorization/roles`
- `GET /api/authorization/roles/{roleId}`
- `POST /api/authorization/roles`
- `PUT /api/authorization/roles/{roleId}`
- `DELETE /api/authorization/roles/{roleId}`
- `GET /api/authorization/roles/{roleId}/permissions`
- `PUT /api/authorization/roles/{roleId}/permissions`
- `GET /api/authorization/permissions`
- `GET /api/authorization/permissions/grouped`
- `GET /api/authorization/users/{userId}/roles`
- `PUT /api/authorization/users/{userId}/roles`
- `GET /api/authorization/users/{userId}/permissions`
- `POST /api/authorization/users/{userId}/check-permission`

### 与宿主的集成

- 宿主通过模块装载获取权限声明与菜单声明。
- LocalAuth 通过授权服务获取当前用户权限，并通过种子逻辑自动建立管理员权限。

### 与前端的集成

- `frontend/modules/authorization/src/index.ts` 注册权限管理页面、菜单和权限声明。
- `frontend/packages/shell/src/App.tsx` 使用 `AuthorizationProvider` 与 `PermissionGuard`。
- `frontend/packages/shell/src/registerModules.ts` 统一注册授权模块与其他业务模块。

## 10. 数据与状态

- 当前后端仓储实现为内存仓储，生命周期与应用进程一致。
- 权限、角色、角色权限、用户角色是当前授权的唯一真相来源。
- 当前用户的权限集合按读取时实时计算，不做持久化快照。
- 前端不缓存授权模型主数据，只依赖当前用户及按需查询。
- 一致性要求：模块声明、权限同步、管理员种子、前端模块注册四者必须匹配，否则会出现“有页面无权限”或“有权限码无页面”的偏差。

## 11. 错误处理与风险

- 当前为内存仓储，重启后需重新依赖种子恢复基础权限数据。
- 还没有接口级 `[Authorize(Policy=...)]` 这一层统一声明式能力，当前更多是服务层和前端守卫能力。
- 角色和权限编码完全依赖字符串，一旦手写不一致就会出现隐藏问题。
- 与未来 Identity、Organization、Tenant 集成时，`userId` 语义和作用域需要重新收敛。

风险控制方式：

- 要求模块权限声明统一集中在模块入口。
- 通过测试覆盖权限求值、角色管理和角色分配等核心路径。
- 后续替换持久化时，只替换仓储层，不破坏服务契约。

## 12. 验证方案

- `tests/Authorization.Tests` 通过。
- 后端构建通过，授权模块能随宿主正常装载。
- 默认管理员登录后能看到授权模块菜单。
- 具备 `Authorization.Role.View` 权限的用户可访问角色管理页。
- 移除或禁用角色后，对应权限判断结果随之变化。

## 13. 未决问题

- 后续接口级授权是采用自定义 attribute/policy，还是继续由控制器/服务手工控制。
- 数据库存储落地时，是否引入角色缓存与权限缓存。
- 多租户接入时，角色与权限是否按租户分层，还是平台级/租户级并存。

## 14. 实施拆分建议

1. 固化权限点声明契约与同步逻辑。
2. 固化角色、权限、用户角色三类仓储与服务层。
3. 固化基础管理接口与默认管理员赋权流程。
4. 固化前端权限上下文、守卫组件、模块菜单和路由联动。
5. 在后续阶段再补持久化、接口级策略授权和更复杂授权模型。

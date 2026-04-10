---
description: "Use when modifying Host API startup, middleware pipeline, module loading, authentication setup, or persistence bootstrap."
applyTo: "apps/host-api/**"
---
# Host 修改约束

## Host 的职责范围

Host 只负责：

- 启动与中间件管道配置
- 认证/授权接入（Cookie Authentication + Policy-based Authorization）
- 模块发现与加载（`AddModuleSystem`）
- 持久化引导（`AddPlatformPersistence` + `ApplyPlatformPersistenceAsync`）
- 路由/菜单/权限/配置描述符聚合
- 本地认证 Seed（`LocalAuthSeeder`）
- 插件前端清单服务

## Host 不应该做的事

- 包含模块业务逻辑
- 直接操作模块仓储或数据库细节
- 硬编码权限规则
- 包含配置中心内部逻辑
- 包含审计存储细节

## 中间件管道顺序

```
Authentication → AuditMiddleware → Authorization → Controllers
```

修改中间件顺序前请评估影响。

## 模块加载

通过 `AddModuleSystem()` 注册，模块通过反射发现。不要在 `Program.cs` 中手动 `new` 模块实例。

## 认证配置

- Cookie 名称：`ICP.Platform.Auth`
- 未认证返回 401（不重定向）
- 无权限返回 403（不重定向）
- 可通过 `appsettings.json` 配置 Cookie 参数

## 修改 Host 后的验证

1. `dotnet build ICP-Platform.sln`
2. `dotnet test ICP-Platform.sln`
3. 确认 Host 可启动
4. 确认默认管理员可登录
5. 确认 `/api/auth/current-user` 可返回当前用户

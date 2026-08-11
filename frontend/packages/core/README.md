# Frontend Core

`@icp-platform/core` 提供 Shell 与模块共享的宿主级前端基础能力：

- HTTP 请求与统一错误处理；
- 认证上下文和当前用户状态；
- 权限判断与服务端授权结果消费；
- 本地化资源与 locale 基础能力。

具体导出以 `src/index.ts` 为事实源。Core 不拥有业务模块状态、菜单、路由或页面实现；共享视觉组件进入 `@icp-platform/ui`，模块业务逻辑留在对应模块。

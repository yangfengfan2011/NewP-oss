# 新身份能力使用统一 ProblemDetails 错误契约

Identity、Authentication 和人员账号绑定的新 HTTP 端点统一返回 `application/problem+json`，采用 ProblemDetails 标准字段，并增加稳定机器错误码 `code`、请求追踪标识 `traceId`、可选字段错误 `errors` 及该错误明确需要的安全元数据。Revision 冲突可以携带不含凭据和人员隐私的最新快照，限流响应同时通过标准 `Retry-After` 头表达等待时间；响应不得因调试便利泄露账号是否存在、密码、会话令牌或临时凭据。

`platformFetch` 只依据 HTTP 状态与稳定 `code` 映射客户端行为，不解析人类消息。该契约先覆盖本次新增的 Identity、Authentication 与人员绑定端点，不顺带迁移现有旧模块的错误响应，也不复用面向插件构建诊断的 `PlatformApiDiagnostic`；以后若平台决定统一旧端点，应作为独立兼容性工作处理。

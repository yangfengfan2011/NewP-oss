# Authentication 模块

Authentication 拥有服务端认证会话注册表及其 SQL Server migration stream。对外只通过 `Authentication.Contracts` 暴露摘要化会话登记、校验与撤销 seam；契约不泄漏原始 token、EF 类型、仓储或数据库实现。

`modules/Authentication/metadata/models` 是模型 authoring 真源，`Migrations.SqlServer` 只推进 Authentication 自有表。该模块依赖 Identity，并以显式外键引用 `Identity/User`；Identity 不反向依赖 Authentication。

验证：运行 `dotnet test tests/Persistence.Tests/Persistence.Tests.csproj --filter IdentityFoundation`，并在空 SQL Server catalog 上通过 Host persistence runner 应用迁移。迁移为 safe-expand，失败信号由 persistence/readiness 稳定代码报告；发布回退依赖迁移前数据库备份，不执行破坏性 Down。

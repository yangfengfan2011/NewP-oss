# Identity 模块

Identity 拥有平台用户账号、本地凭据、身份安全事件及其 SQL Server migration stream。对外只通过 `Identity.Contracts` 暴露账号查询与生命周期参与者 seam；调用方不能依赖本模块的 EF 模型、仓储实现、密码摘要或安全事件持久化细节。

`modules/Identity/metadata/models` 是模型 authoring 真源，`Migrations.SqlServer` 只推进 Identity 自有表。Authentication 可以通过稳定外键引用 `Identity/User`，Identity 不反向依赖 Authentication、Organization 或 Host 的具体实现。

验证：运行 `dotnet test tests/Persistence.Tests/Persistence.Tests.csproj --filter IdentityFoundation`，并在空 SQL Server catalog 上通过 Host persistence runner 应用迁移。迁移为 safe-expand，失败信号由 persistence/readiness 稳定代码报告；发布回退依赖迁移前数据库备份，不执行破坏性 Down。

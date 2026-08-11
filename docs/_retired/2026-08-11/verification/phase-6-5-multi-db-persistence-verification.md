# Phase 6.5 多数据库持久化验证记录

## 1. 目标

本文档用于为 `Phase 6.5` 提供独立验证入口，收拢多数据库持久化（混合架构）的既有验证证据。

本记录不代表本轮 `WP3` 重新全量执行三库落库验证；它负责把此前已经真实完成的验证口径、脚本入口与环境前提整理成可复用文档。

## 2. 覆盖范围

- `platform-modeling`
- `platform-persistence`
- `platform-persistence-efcore`
- `platform-persistence-composition`
- `ConfigCenter` / `Authorization` 的 EF 仓储实现
- 三套迁移项目：`SqlServer` / `Oracle` / `MySql`
- `tests/integration/persistence` 下的三库 smoke 脚本

## 3. 环境前提

- 本机可用 `SQL Server`、`Oracle`、`MySql` 容器或等价实例
- 默认端口与脚本约定一致：
  - `SQL Server`: `localhost:1433`
  - `MySql`: `localhost:3306`
  - `Oracle`: `localhost:1521/FREEPDB1`
- 已执行 `dotnet tool restore`
- 宿主与迁移项目均可在当前环境构建

脚本入口位于：

- `tests/integration/persistence/Run-SqlServerSmoke.ps1`
- `tests/integration/persistence/Run-MySqlSmoke.ps1`
- `tests/integration/persistence/Run-OracleSmoke.ps1`
- `tests/integration/persistence/Invoke-PersistenceSmoke.ps1`

## 4. 既有验证入口

受影响测试与构建入口：

- `dotnet build .\apps\host-api\HostApi.csproj -v minimal`
- `dotnet test .\tests\Persistence.Tests\Persistence.Tests.csproj -v minimal`
- `dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -v minimal`
- `dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -v minimal`
- `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj -v minimal`

三库 smoke 入口：

- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-SqlServerSmoke.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-MySqlSmoke.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\integration\persistence\Run-OracleSmoke.ps1`

`Invoke-PersistenceSmoke.ps1` 内部会执行：

1. `dotnet tool restore`
2. `dotnet build apps/host-api/HostApi.csproj`
3. `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj`
4. `dotnet test tests/Authorization.Tests/Authorization.Tests.csproj`
5. `dotnet tool run dotnet-ef database update` 到对应迁移项目
6. 启动 `HostApi`，轮询 `/swagger/v1/swagger.json` 直到健康或超时

## 5. 已确认结果

- `HostApi` 构建成功
- `Persistence.Tests`、`ConfigCenter.Tests`、`Authorization.Tests` 与 `Kernel.Tests` 中受影响验证已重新跑通
- `tests/integration/persistence` 下三套 smoke 脚本已真实通过
- 三个 Provider 的验证都包含：
  - 迁移项目可执行 `database update`
  - 宿主可在对应连接串下启动
  - `/swagger/v1/swagger.json` 健康检查返回 `200`

## 6. 当前边界

- 首批真实落库范围仍只覆盖 `ConfigCenter` 与 `Authorization`
- `Audit` 与 `Files` 不在本轮首批落库范围内
- 三库 smoke 依赖本机容器、连接串和端口保持可用；更换环境后需要重新执行脚本确认
- 本轮以 `EfCore` 作为当前适配器，未扩展到其他持久化适配实现

## 7. 结论

`Phase 6.5` 已具备独立、可引用的验证入口：

- 受影响测试项目入口已明确
- 三库 smoke 脚本入口已明确
- 环境前提、宿主健康检查与当前边界都已文档化

因此，`Phase 6.5` 不再只依赖 `docs/current-status.md` 中的摘要说明来表达验证结果。

# 2026-04-09 本地多数据库运行记录

本文档记录 `Phase 6.5` 开发阶段本机实际启动的多数据库容器信息，供后续 `Task 5` / `Task 6` 的宿主接入、迁移和 smoke 验证复用。

## Docker 环境

- Docker Desktop: `4.68.0`
- Docker Engine: `29.3.1`
- 当前上下文: `desktop-linux`
- 验证命令: `docker version`

## 容器清单

### MySQL

- 容器名: `icp-mysql`
- 镜像: `mysql:8.4`
- 端口映射: `3306 -> 3306`
- 状态: `running`
- 数据库: `icp_platform`
- 用户: `icp`
- 密码: `icp_123`
- root 密码: `icp_root_123`
- 连接串:

```text
Server=localhost;Port=3306;Database=icp_platform;User=icp;Password=REDACTED_FOR_OPEN_SOURCE;
```

### SQL Server

- 容器名: `icp-sqlserver`
- 镜像: `mcr.microsoft.com/mssql/server:2022-latest`
- 端口映射: `1433 -> 1433`
- 状态: `running`
- 管理员用户: `sa`
- 管理员密码: `Icp_sa_123456`
- 连接串:

```text
Server=localhost,1433;Database=ICP_Platform;User Id=sa;Password=REDACTED_FOR_OPEN_SOURCE;TrustServerCertificate=True;
```

### Oracle

- 容器名: `icp-oracle`
- 镜像: `container-registry.oracle.com/database/free:latest-lite`
- 端口映射: `1521 -> 1521`
- 状态: `running`
- 健康状态: `healthy`
- 用户: `system`
- 密码: `Icp_oracle_123`
- 服务名: `FREEPDB1`
- 连接串:

```text
User Id=system;Password=REDACTED_FOR_OPEN_SOURCE;Data Source=localhost:1521/FREEPDB1;
```

## 连通性检查

已通过以下端口连通性检查：

- `localhost:3306`
- `localhost:1433`
- `localhost:1521`

## 当前用途

- 为 `ConfigCenter` / `Authorization` 的 EF 仓储替换提供本地真实数据库承接环境
- 为宿主接入、初始化和 smoke 脚本提供固定容器名与固定端口
- 为后续 `appsettings` / 环境变量落地提供统一连接串来源

## Smoke 验证结果

已在当前容器环境下通过以下脚本完成真实验证：

- `tests/integration/persistence/Run-SqlServerSmoke.ps1`
- `tests/integration/persistence/Run-MySqlSmoke.ps1`
- `tests/integration/persistence/Run-OracleSmoke.ps1`

每套 smoke 均完成：

- `HostApi` 构建
- `ConfigCenter.Tests`
- `Authorization.Tests`
- 对应迁移项目执行 `dotnet-ef database update`
- `HostApi` 启动与 `swagger/v1/swagger.json` 探活

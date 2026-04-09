# 本地多数据库容器准备清单

本文档用于支持 `Phase 6.5` 多数据库持久化实现。

当前推荐策略不是一开始就安装全部数据库，而是：

- 先完成 `Task 1` 到 `Task 4`
- 在 **完成 `Task 4` 后** 安装并启动本地数据库容器
- 再进入 `Task 5` 和 `Task 6` 的真实数据库验证阶段

## 1. 最佳安装时机

推荐安装时机：

- **完成 `Phase 6.5` 实施计划中的 `Task 4` 后**

原因：

- `Task 1` 到 `Task 4` 主要是模型语义、持久化内核、EF Core 适配器和模块定义，不必立即依赖真实数据库。
- 从 `Task 5` 后半段到 `Task 6`，会逐步进入真实迁移、宿主启动、迁移程序集和 smoke 验证。
- 太早装容器会增加本地维护负担，太晚装容器会阻塞真实联调。

一句话建议：

- **实现到 Task 4 收口时就装好容器，不要等到开始跑 smoke 时才准备。**

## 2. 当前推荐组合

推荐最终准备三套容器：

- `MySQL`
- `SQL Server`
- `Oracle Database Free`

推荐安装顺序：

1. `MySQL`
2. `SQL Server`
3. `Oracle Database Free`

原因：

- `MySQL` 最轻，适合先验证整条链路。
- `SQL Server` 与当前 .NET/EF Core 配合也比较直接。
- `Oracle` 通常最重，启动最慢，适合最后接入。

## 3. 前置准备

开始前请先准备：

1. 安装 `Docker Desktop`
2. 确认 `docker version` 能正常返回
3. 本机最好具备：
   - 内存 `16GB+`
   - 磁盘剩余 `30GB+`
4. 优先使用 `amd64/x86-64` 环境

## 4. 容器命名与端口建议

建议统一命名：

- `icp-mysql`
- `icp-sqlserver`
- `icp-oracle`

建议统一端口：

- `MySQL: 3306`
- `SQL Server: 1433`
- `Oracle: 1521`

这样后续写 `appsettings`、脚本和 smoke 时不容易混乱。

## 5. MySQL 准备

### 推荐镜像

- 官方 `mysql`

### 推荐环境变量

- Root 密码：`icp_root_123`
- 业务数据库：`icp_platform`
- 业务用户：`icp`
- 业务密码：`icp_123`

### 示例启动命令

```powershell
docker run -d `
  --name icp-mysql `
  -p 3306:3306 `
  -e MYSQL_ROOT_PASSWORD=REDACTED_FOR_OPEN_SOURCE `
  -e MYSQL_DATABASE=icp_platform `
  -e MYSQL_USER=icp `
  -e MYSQL_PASSWORD=REDACTED_FOR_OPEN_SOURCE `
  mysql:8.4
```

### 连接串示例

```text
Server=localhost;Port=3306;Database=icp_platform;User=icp;Password=REDACTED_FOR_OPEN_SOURCE;
```

## 6. SQL Server 准备

### 推荐镜像

- Microsoft 官方 SQL Server 容器镜像

### 推荐环境变量

- `ACCEPT_EULA=Y`
- `MSSQL_SA_PASSWORD=REDACTED_FOR_OPEN_SOURCE`

### 示例启动命令

```powershell
docker run -d `
  --name icp-sqlserver `
  -p 1433:1433 `
  -e ACCEPT_EULA=Y `
  -e MSSQL_SA_PASSWORD=REDACTED_FOR_OPEN_SOURCE `
  mcr.microsoft.com/mssql/server:2022-latest
```

### 连接串示例

```text
Server=localhost,1433;Database=ICP_Platform;User Id=sa;Password=REDACTED_FOR_OPEN_SOURCE;TrustServerCertificate=True;
```

## 7. Oracle Database Free 准备

### 推荐镜像

- Oracle 官方 `database/free:latest-lite`

### 推荐说明

- 优先用 `latest-lite`
- 它更适合作为本地开发环境
- 不建议一开始就使用更重的免费数据库镜像

### 示例启动命令

```powershell
docker run -d `
  --name icp-oracle `
  -p 1521:1521 `
  -e ORACLE_PWD=REDACTED_FOR_OPEN_SOURCE `
  container-registry.oracle.com/database/free:latest-lite
```

### 连接串示例

```text
User Id=system;Password=REDACTED_FOR_OPEN_SOURCE;Data Source=localhost:1521/FREEPDB1;
```

说明：

- 首次启动 Oracle 通常较慢
- 需要等待数据库真正 ready 后再连接

## 8. 推荐落地顺序

### 第一阶段：不装容器

先做这些：

- `platform-modeling`
- `platform-persistence`
- 模块模型语义定义
- `platform-persistence-efcore` 的基础部分
- 单元测试和投影测试

### 第二阶段：装容器

在完成 `Task 4` 后安装：

- `MySQL`
- `SQL Server`
- `Oracle`

### 第三阶段：真实验证

然后推进：

- `Task 5` 中需要真实数据库承接的部分
- `Task 6` 的宿主接入、迁移程序集、真实 smoke

## 9. 容器健康检查清单

每个容器启动后至少检查：

1. `docker ps` 能看到容器运行中
2. 对应端口已监听
3. 可以用客户端工具或脚本连通
4. 连接串可以写入本地 `appsettings` 或环境变量

## 10. 风险提示

- Oracle 容器启动慢，不要刚启动就立刻判定失败。
- 三库同时开时资源占用明显增加，内存不足会导致启动异常。
- 如果端口被本机已有数据库占用，需要先统一端口规划。
- 如果只想先跑第一批真实验证，可以先起 `MySQL` 和 `SQL Server`，再补 `Oracle`。

## 11. 当前建议

当前项目的最低成本做法是：

1. 先推进实现到 `Task 4`
2. 再一次性装好三库容器
3. 然后进入 `Task 5` 和 `Task 6`

不要在项目一开始就花时间折腾数据库环境，也不要拖到 smoke 阶段才临时准备。

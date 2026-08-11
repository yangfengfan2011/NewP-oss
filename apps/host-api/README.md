# Host API

Host API 是平台后端宿主，负责启动、认证接入、模块候选编译与激活、持久化 readiness、endpoint 组合和宿主级横切能力。业务规则归模块应用或领域服务，不进入 Host。

## 推荐启动

从仓库根目录使用受控开发脚本：

```powershell
.\scripts\dev.ps1
```

需要单独运行 Host 时：

```powershell
dotnet run --project .\apps\host-api\HostApi.csproj --urls http://127.0.0.1:5000
```

- API：`http://localhost:5000`
- Swagger：`http://localhost:5000/swagger`
- 活动模块目录：`http://localhost:5000/plugins/active-module-runtime.json`

## 本地配置

开发连接优先读取 `NEWP_DEV_SQLSERVER_CONNECTION_STRING`，未设置时读取 `appsettings.Development.json` 的 `ConnectionStrings:SqlServer`。不要把连接值写入命令、日志或文档。

全新数据库的本地管理员通过离线命令初始化：

```powershell
dotnet run --project .\apps\host-api\HostApi.csproj -- identity bootstrap-local-admin
```

模块接入边界见 `docs/architecture/componentization-guardrails.md`；Host 不维护业务模块清单或模块私有迁移顺序。

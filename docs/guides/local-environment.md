# 本地开发环境

本页只记录本地开发约定。工具精确版本以项目文件为事实源：后端读取各 `.csproj`，Node.js 与 pnpm 读取 `frontend/package.json`；初始化脚本会校验这些约束。

## 初始化与启动

首次拉取或 lockfile 变化后运行：

```powershell
.\scripts\bootstrap.ps1
```

日常启动和停止统一使用受控脚本；它们只管理当前 NewP worktree 拥有的进程：

```powershell
.\scripts\dev.ps1
.\scripts\stop-dev.ps1
```

Host 与 Shell 默认地址分别为 `http://localhost:5000` 和 `http://localhost:3000`。启动失败时先读取 `.codex-temp/dev/runs/<run>/result.json`、`stages.jsonl` 和对应 stderr 日志。

## 本地 SQL Server

开发 Host 优先使用 `NEWP_DEV_SQLSERVER_CONNECTION_STRING`，未设置时由 Host 正常加载 `apps/host-api/appsettings.Development.json`。已经配置本地连接时无需为每次启动重复设置环境变量。

Acceptance 按以下顺序解析管理连接：

1. `NEWP_ACCEPTANCE_SQLSERVER_ADMIN_CONNECTION`
2. `NEWP_DEV_SQLSERVER_CONNECTION_STRING`
3. `apps/host-api/appsettings.Development.json` 的 `ConnectionStrings:SqlServer`

Acceptance 只复用服务器与登录身份，将 catalog 强制切换到 `master`，随后创建并清理本轮唯一的临时 catalog；配置身份必须具备建库和删库权限。连接值不进入命令参数、证据或日志。

## 本地管理员

全新数据库不会获得固定明文密码。先停止开发服务，再在交互式终端执行离线 bootstrap：

```powershell
dotnet run --project .\apps\host-api\HostApi.csproj -- identity bootstrap-local-admin
```

命令只显示一次随机临时凭据；首次登录后必须修改。已有数据库继续使用其中现存的本地管理员凭据。

## 验证入口

- 开发循环：运行当前票据涉及的项目或过滤测试。
- 仓库测试：`.\scripts\test.ps1`。
- 冻结候选：`.\scripts\verify.ps1`。
- 终局 Gate：在干净候选上运行 `.\scripts\verify.ps1 -AcceptanceGate IdentityFoundation,Personnel`，或显式调用 `.\scripts\accept.ps1 -Gate <Gate>`。

完整验证结果位于 `.codex-temp/verify/runs/<run>/result.json`，每个阶段有独立日志；不需要从截断的终端输出推断结果。

## 推荐阅读

- 仓库规则：[../../AGENTS.md](../../AGENTS.md)
- 连续交付门禁：[../agents/delivery-gates.md](../agents/delivery-gates.md)
- 架构总览：[../architecture/architecture.md](../architecture/architecture.md)

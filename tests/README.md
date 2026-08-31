# 测试与验收入口

测试项目以 `ICP-Platform.sln`、`tests/**/*.csproj` 和 `frontend/package.json` 为事实源，不在本文维护项目枚举副本。

## 开发循环

优先运行本次变更涉及的项目或过滤测试：

```powershell
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj --filter <测试名或类别>
corepack pnpm --dir frontend test -- <测试过滤条件>
```

## 仓库级入口

```powershell
.\scripts\verify.ps1 -Quick
.\scripts\verify.ps1
```

`verify.ps1` 是唯一的验证入口，没有 CI 兜底。`-Quick` 供开发循环使用，跳过只有冻结候选才需要的 diff-check、`dotnet restore` 和前端 build；其余门禁两种模式完全一致。每个阶段的结果与日志写入 `.codex-temp/verify/`。

## Acceptance

```powershell
.\scripts\verify.ps1 -AcceptanceGate IdentityFoundation,Personnel
# 或
.\scripts\accept.ps1 -Gate IdentityFoundation
```

Acceptance 使用唯一临时 SQL Server catalog，并从本地配置解析管理连接。闸门本身是 `Acceptance.Tests` 中的 xUnit 测试，默认跳过，设置 `NEWP_RUN_ACCEPTANCE=1` 后启用；`accept.ps1` 只是 `dotnet test --filter` 的薄封装。
